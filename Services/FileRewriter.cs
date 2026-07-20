using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace D365LabelCreator.Services;

/// <summary>Applies plain-text character-span replacements to files, preserving encoding.</summary>
public static class FileRewriter
{
    public readonly struct Edit
    {
        public int Start { get; init; }
        public int Length { get; init; }
        public string Replacement { get; init; }
    }

    /// <summary>True if the file exists and carries the read-only attribute.</summary>
    public static bool IsReadOnly(string path)
    {
        try
        {
            return File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Clears the read-only attribute if set. Returns true if it was read-only.</summary>
    public static bool ClearReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            var attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                return true;
            }
        }
        catch
        {
            // Ignore; the write will fail later and be surfaced.
        }
        return false;
    }

    /// <summary>Reads a file as text, reporting whether it carried a UTF-8 BOM.</summary>
    public static string ReadAllText(string path, out bool hadBom)
    {
        var bytes = File.ReadAllBytes(path);
        hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        int start = hadBom ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>
    /// Applies the given edits to the file. Edits are applied from the highest offset to the
    /// lowest so that earlier offsets remain valid within this call. Encoding (UTF-8 with/without
    /// BOM) is preserved.
    /// </summary>
    public static void Apply(string path, IEnumerable<Edit> edits)
    {
        string text = ReadAllText(path, out bool hadBom);

        var ordered = new List<Edit>(edits);
        ordered.Sort((a, b) => b.Start.CompareTo(a.Start)); // descending

        var sb = new StringBuilder(text);
        foreach (var e in ordered)
        {
            sb.Remove(e.Start, e.Length);
            sb.Insert(e.Start, e.Replacement);
        }

        ClearReadOnly(path);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hadBom);
        File.WriteAllText(path, sb.ToString(), encoding);
    }
}
