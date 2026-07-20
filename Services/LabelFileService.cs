using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Reads label-file descriptors and reads/writes the .label.txt resource content.</summary>
public static class LabelFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Lists every label file of a model, all languages. Each descriptor maps to one resource file.
    /// en-US sorts first (it is the base language), so it stays the default pick.
    /// </summary>
    public static List<LabelFileInfo> GetLabelFiles(string modelDir)
    {
        var result = new List<LabelFileInfo>();
        var labelDir = Path.Combine(modelDir, "AxLabelFile");
        if (!Directory.Exists(labelDir))
            return result;

        foreach (var descriptorPath in Directory.EnumerateFiles(labelDir, "*.xml"))
        {
            try
            {
                var doc = XDocument.Load(descriptorPath);
                var root = doc.Root;
                if (root == null)
                    continue;

                string labelFileId = (string?)root.Element("LabelFileId") ?? string.Empty;
                string contentFileName = (string?)root.Element("LabelContentFileName") ?? string.Empty;
                if (labelFileId.Length == 0 || contentFileName.Length == 0)
                    continue;

                // Content file is named "<id>.<lang>.label.txt".
                string language = ExtractLanguage(contentFileName);
                if (language.Length == 0)
                    continue;

                string contentPath = Path.Combine(labelDir, "LabelResources", language, contentFileName);

                result.Add(new LabelFileInfo
                {
                    LabelFileId = labelFileId,
                    Language = language,
                    DescriptorName = (string?)root.Element("Name") ?? Path.GetFileNameWithoutExtension(descriptorPath),
                    DescriptorPath = descriptorPath,
                    ContentFilePath = contentPath,
                });
            }
            catch
            {
                // Skip unreadable descriptor.
            }
        }

        // Group by label file, en-US first within each so the base language is the default pick.
        result.Sort((a, b) =>
        {
            int byId = string.Compare(a.LabelFileId, b.LabelFileId, StringComparison.OrdinalIgnoreCase);
            if (byId != 0)
                return byId;
            bool aBase = string.Equals(a.Language, "en-US", StringComparison.OrdinalIgnoreCase);
            bool bBase = string.Equals(b.Language, "en-US", StringComparison.OrdinalIgnoreCase);
            if (aBase != bBase)
                return aBase ? -1 : 1;
            return string.Compare(a.Language, b.Language, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    /// <summary>Extracts "en-US" from "SOG_OPR.en-US.label.txt".</summary>
    private static string ExtractLanguage(string contentFileName)
    {
        const string suffix = ".label.txt";
        if (contentFileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            string stem = contentFileName[..^suffix.Length]; // "SOG_OPR.en-US"
            int dot = stem.LastIndexOf('.');
            if (dot >= 0 && dot < stem.Length - 1)
                return stem[(dot + 1)..];
        }
        return string.Empty;
    }

    /// <summary>Parses the content file into entries (used for display and lookups).</summary>
    public static List<LabelFileEntry> ReadEntries(string contentPath)
    {
        var entries = new List<LabelFileEntry>();
        if (!File.Exists(contentPath))
            return entries;

        var blocks = ParseBlocks(File.ReadAllText(contentPath));
        foreach (var b in blocks)
        {
            if (b.Id == null)
                continue;
            entries.Add(new LabelFileEntry
            {
                Id = b.Id,
                Text = b.Text ?? string.Empty,
                Description = b.Description ?? string.Empty,
            });
        }
        return entries;
    }

    /// <summary>Returns the set of existing label ids (for collision blocking).</summary>
    public static HashSet<string> GetExistingIds(string contentPath)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ReadEntries(contentPath))
            ids.Add(e.Id);
        return ids;
    }

    /// <summary>Returns existing entries keyed by id (case-insensitive), for collision handling.</summary>
    public static Dictionary<string, LabelFileEntry> GetEntriesById(string contentPath)
    {
        var map = new Dictionary<string, LabelFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in ReadEntries(contentPath))
            map[e.Id] = e; // last wins on duplicate ids
        return map;
    }

    /// <summary>
    /// Inserts a new entry at its case-insensitive alphabetical position, leaving all existing
    /// entries byte-for-byte untouched. Creates the file if missing.
    /// </summary>
    public static void InsertSorted(string contentPath, LabelFileEntry entry)
    {
        string existing = File.Exists(contentPath) ? File.ReadAllText(contentPath) : string.Empty;
        string newline = existing.Contains("\r\n") ? "\r\n" : (existing.Contains('\n') ? "\n" : Environment.NewLine);

        var blocks = ParseBlocks(existing);

        // Build the new block's raw text.
        string newBlock = $"{entry.Id}={entry.Text}{newline} ;{entry.Description}";

        // Find insertion index: first id-bearing block whose id sorts after the new id.
        int insertAt = blocks.Count;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Id == null)
                continue; // leading non-entry lines stay first
            if (string.Compare(entry.Id, blocks[i].Id, StringComparison.OrdinalIgnoreCase) < 0)
            {
                insertAt = i;
                break;
            }
        }

        var rawBlocks = new List<string>(blocks.Count + 1);
        for (int i = 0; i < blocks.Count; i++)
            rawBlocks.Add(blocks[i].Raw);
        rawBlocks.Insert(insertAt, newBlock);

        var sb = new StringBuilder();
        for (int i = 0; i < rawBlocks.Count; i++)
        {
            if (rawBlocks[i].Length == 0)
                continue;
            sb.Append(rawBlocks[i]);
            sb.Append(newline);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
        FileRewriter.ClearReadOnly(contentPath);
        File.WriteAllText(contentPath, sb.ToString(), Utf8NoBom);
    }

    private sealed class Block
    {
        public string? Id;
        public string? Text;
        public string? Description;
        public string Raw = string.Empty;
    }

    /// <summary>
    /// Splits content into blocks. An entry block starts at an "Id=Text" line (no leading space,
    /// contains '=') and absorbs the following indented/comment lines. Any leading lines before the
    /// first entry are kept as a single id-less block that always stays first.
    /// </summary>
    private static List<Block> ParseBlocks(string content)
    {
        var blocks = new List<Block>();
        if (content.Length == 0)
            return blocks;

        // Normalise line splitting but remember raw content per block.
        var lines = content.Replace("\r\n", "\n").Split('\n');
        // A trailing empty element appears when the file ends with a newline; drop it.
        int lineCount = lines.Length;
        if (lineCount > 0 && lines[lineCount - 1].Length == 0)
            lineCount--;

        Block? current = null;
        var rawLines = new List<string>();

        void Flush()
        {
            if (current != null)
            {
                current.Raw = string.Join("\n", rawLines);
                blocks.Add(current);
            }
            rawLines = new List<string>();
        }

        for (int i = 0; i < lineCount; i++)
        {
            string line = lines[i];
            bool isEntryLine = line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.Contains('=');

            if (isEntryLine)
            {
                Flush();
                int eq = line.IndexOf('=');
                current = new Block
                {
                    Id = line[..eq],
                    Text = line[(eq + 1)..],
                };
                rawLines.Add(line);
            }
            else
            {
                if (current == null)
                {
                    // Leading non-entry lines: keep as an id-less prefix block.
                    current = new Block { Id = null };
                }
                if (current.Id != null && line.TrimStart().StartsWith(';') && current.Description == null)
                    current.Description = line.TrimStart().TrimStart(';');
                rawLines.Add(line);
            }
        }
        Flush();

        return blocks;
    }
}
