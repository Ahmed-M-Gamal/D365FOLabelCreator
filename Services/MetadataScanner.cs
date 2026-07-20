using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Scans a model's metadata files for hardcoded labels and records exact replace spans.</summary>
public static class MetadataScanner
{
    private const string XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly HashSet<string> PropertyTags =
        new(StringComparer.Ordinal) { "Label", "Caption", "HelpText", "Text" };

    /// <summary>Metadata type folders we never scan (reports are out of scope).</summary>
    private static readonly HashSet<string> IgnoredMetadataTypes =
        new(StringComparer.OrdinalIgnoreCase) { "AxReport" };

    private sealed class Frame
    {
        public string Local = "";
        public string EffectiveType = "";
        public string? Name;
    }

    public sealed class ScanResult
    {
        public List<MetadataItem> Items { get; } = new();
        public List<LabelOccurrence> Occurrences { get; } = new();
    }

    /// <summary>Scans every metadata file under the model, skipping build/resource folders.</summary>
    public static ScanResult ScanModel(string modelDir)
    {
        var result = new ScanResult();
        if (!Directory.Exists(modelDir))
            return result;

        foreach (var typeDir in Directory.EnumerateDirectories(modelDir))
        {
            string typeName = Path.GetFileName(typeDir);
            if (PackageScanner.IgnoredModelFolders.Contains(typeName) || IgnoredMetadataTypes.Contains(typeName))
                continue;

            foreach (var file in Directory.EnumerateFiles(typeDir, "*.xml", SearchOption.AllDirectories))
            {
                var item = new MetadataItem
                {
                    ElementType = typeName,
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    IsReadOnly = FileRewriter.IsReadOnly(file),
                };
                result.Items.Add(item);
                try
                {
                    ScanFile(item, result.Occurrences);
                }
                catch
                {
                    // A malformed file is skipped rather than aborting the whole scan.
                }
            }
        }

        return result;
    }

    private static void ScanFile(MetadataItem item, List<LabelOccurrence> sink)
    {
        string text = FileRewriter.ReadAllText(item.FilePath, out _);
        int[] lineStarts = BuildLineStarts(text);

        var stack = new Stack<Frame>();
        var settings = new XmlReaderSettings { IgnoreComments = false, DtdProcessing = DtdProcessing.Prohibit };

        using var sr = new StringReader(text);
        using var reader = XmlReader.Create(sr, settings);
        var li = (IXmlLineInfo)reader;

        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    string local = reader.LocalName;
                    string effType = reader.GetAttribute("type", XsiNs) ?? local;
                    bool isEmpty = reader.IsEmptyElement;
                    int elemOffset = ToOffset(lineStarts, li.LineNumber, li.LinePosition);

                    // XML property (leaf text): parent node type identifies the owning item.
                    if (!isEmpty && PropertyTags.Contains(local) && stack.Count > 0)
                    {
                        var parent = stack.Peek();
                        TryAddProperty(item, sink, text, elemOffset, local, parent);
                    }

                    // X++ code lives in <Declaration> / <Source> CDATA under <SourceCode>.
                    if (!isEmpty && (local == "Declaration" || local == "Source") && StackHas(stack, "SourceCode"))
                    {
                        TryAddCode(item, sink, text, elemOffset);
                    }

                    if (!isEmpty)
                        stack.Push(new Frame { Local = local, EffectiveType = effType });
                    break;
                }

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                {
                    // Capture a direct <Name>value</Name> onto its parent frame (for enum-value ids).
                    if (stack.Count >= 2 && stack.Peek().Local == "Name")
                    {
                        var nameFrame = stack.Peek();
                        // assign to the parent (frame just below Name)
                        Frame? parent = SecondFromTop(stack);
                        if (parent != null && parent.Name == null)
                            parent.Name = reader.Value;
                    }
                    break;
                }

                case XmlNodeType.EndElement:
                    if (stack.Count > 0)
                        stack.Pop();
                    break;
            }
        }
    }

    private static void TryAddProperty(MetadataItem item, List<LabelOccurrence> sink, string text,
        int elemOffset, string propertyName, Frame parent)
    {
        int gt = text.IndexOf('>', Math.Max(0, elemOffset - 1));
        if (gt < 0 || text[gt - 1] == '/')
            return; // malformed or self-closing

        int contentStart = gt + 1;
        int contentEnd = text.IndexOf('<', contentStart);
        if (contentEnd < 0 || contentEnd <= contentStart)
            return; // empty content

        string raw = text.Substring(contentStart, contentEnd - contentStart);

        // A real caption/label is a short single line. Multi-line values are embedded blobs
        // (e.g. an AxReport <Text> holding a whole RDL definition), never labels.
        if (raw.IndexOf('\n') >= 0)
            return;

        // <Text> is generic; only treat it as a caption when it belongs to a form control.
        if (propertyName == "Text" && !parent.EffectiveType.StartsWith("AxForm", StringComparison.Ordinal))
            return;

        string decoded = XmlDecode(raw);
        if (decoded.TrimStart().StartsWith("@", StringComparison.Ordinal))
            return; // already a defined label reference

        string? enumValueName = parent.Local == "AxEnumValue" ? parent.Name : null;

        sink.Add(new LabelOccurrence
        {
            Item = item,
            Kind = OccurrenceKind.XmlProperty,
            PropertyName = propertyName,
            ParentType = parent.EffectiveType,
            Text = decoded,
            Start = contentStart,
            Length = contentEnd - contentStart,
            EnumValueName = enumValueName,
            OwnerName = parent.Name,
        });
    }

    private static void TryAddCode(MetadataItem item, List<LabelOccurrence> sink, string text, int elemOffset)
    {
        int gt = text.IndexOf('>', Math.Max(0, elemOffset - 1));
        if (gt < 0)
            return;

        const string open = "<![CDATA[";
        const string close = "]]>";
        int cdataStart = text.IndexOf(open, gt, StringComparison.Ordinal);
        if (cdataStart < 0)
            return;
        int contentStart = cdataStart + open.Length;
        int contentEnd = text.IndexOf(close, contentStart, StringComparison.Ordinal);
        if (contentEnd < 0)
            return;

        string code = text.Substring(contentStart, contentEnd - contentStart);
        foreach (var lit in XppLexer.FindDoubleQuotedStrings(code))
        {
            if (lit.ContentLength == 0)
                continue;
            if (lit.Content.TrimStart().StartsWith("@", StringComparison.Ordinal))
                continue; // defined label reference in code

            sink.Add(new LabelOccurrence
            {
                Item = item,
                Kind = OccurrenceKind.CodeString,
                PropertyName = "code",
                ParentType = "code",
                Text = lit.Content,
                Start = contentStart + lit.ContentStart,
                Length = lit.ContentLength,
            });
        }
    }

    private static bool StackHas(Stack<Frame> stack, string local)
    {
        foreach (var f in stack)
            if (f.Local == local)
                return true;
        return false;
    }

    private static Frame? SecondFromTop(Stack<Frame> stack)
    {
        int idx = 0;
        foreach (var f in stack)
        {
            if (idx == 1)
                return f;
            idx++;
        }
        return null;
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);
        return starts.ToArray();
    }

    private static int ToOffset(int[] lineStarts, int line, int pos)
    {
        if (line < 1) line = 1;
        if (line > lineStarts.Length) line = lineStarts.Length;
        return lineStarts[line - 1] + (pos - 1);
    }

    private static string XmlDecode(string s)
    {
        if (s.IndexOf('&') < 0)
            return s;
        return s.Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&amp;", "&");
    }
}
