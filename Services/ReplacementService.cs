using System.Collections.Generic;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>
/// Applies label references to selected occurrences and keeps the rest of the working set's
/// offsets consistent as files shift underneath them.
/// </summary>
public static class ReplacementService
{
    public static string BuildReference(string labelFileId, string id) => $"@{labelFileId}:{id}";

    /// <summary>
    /// Overwrites each selected occurrence's span with <paramref name="reference"/>, then shifts the
    /// Start of every other still-pending occurrence in the same file by the net length delta of the
    /// edits that preceded it. Selected occurrences are marked Treated.
    /// </summary>
    public static void ApplySelection(
        IReadOnlyList<LabelOccurrence> allOccurrences,
        IReadOnlyList<LabelOccurrence> selected,
        string reference)
    {
        // Group the selected occurrences by file.
        var byFile = new Dictionary<string, List<LabelOccurrence>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var occ in selected)
        {
            if (!byFile.TryGetValue(occ.Item.FilePath, out var list))
                byFile[occ.Item.FilePath] = list = new List<LabelOccurrence>();
            list.Add(occ);
        }

        foreach (var (filePath, occs) in byFile)
        {
            var edits = new List<FileRewriter.Edit>(occs.Count);
            foreach (var o in occs)
                edits.Add(new FileRewriter.Edit { Start = o.Start, Length = o.Length, Replacement = reference });

            FileRewriter.Apply(filePath, edits);

            // Adjust the offsets of the remaining pending occurrences in this file.
            var selectedSet = new HashSet<LabelOccurrence>(occs);
            foreach (var other in allOccurrences)
            {
                if (other.Treated || selectedSet.Contains(other))
                    continue;
                if (!string.Equals(other.Item.FilePath, filePath, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                int shift = 0;
                foreach (var e in edits)
                    if (e.Start < other.Start)
                        shift += reference.Length - e.Length;
                other.Start += shift;
            }
        }

        foreach (var o in selected)
            o.Treated = true;
    }

    /// <summary>
    /// Rewrites the quotes around code-string occurrences from double to single, leaving the
    /// content untouched. Single-quoted spans are ignored by the lexer, so the string stops being
    /// reported as a hardcoded label. Each edit swaps one character for another, so the change is
    /// length-neutral and no other occurrence's offsets move.
    /// Returns the number of occurrences converted.
    /// </summary>
    public static int ConvertToSingleQuotes(IReadOnlyList<LabelOccurrence> selected)
    {
        var byFile = new Dictionary<string, List<LabelOccurrence>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var occ in selected)
        {
            if (occ.Treated || occ.Kind != OccurrenceKind.CodeString)
                continue;
            if (!byFile.TryGetValue(occ.Item.FilePath, out var list))
                byFile[occ.Item.FilePath] = list = new List<LabelOccurrence>();
            list.Add(occ);
        }

        int converted = 0;
        foreach (var (filePath, occs) in byFile)
        {
            string text = FileRewriter.ReadAllText(filePath, out _);
            var edits = new List<FileRewriter.Edit>();
            var applied = new List<LabelOccurrence>();

            foreach (var o in occs)
            {
                int open = o.Start - 1;              // the opening quote sits just before the content
                int close = o.Start + o.Length;      // the closing quote just after it
                if (open < 0 || close >= text.Length)
                    continue;
                if (text[open] != '"' || text[close] != '"')
                    continue;                        // not what we recorded — leave it alone

                edits.Add(new FileRewriter.Edit { Start = open, Length = 1, Replacement = "'" });
                edits.Add(new FileRewriter.Edit { Start = close, Length = 1, Replacement = "'" });
                applied.Add(o);
            }

            if (edits.Count == 0)
                continue;

            FileRewriter.Apply(filePath, edits);
            foreach (var o in applied)
                o.Treated = true;
            converted += applied.Count;
        }

        return converted;
    }

    /// <summary>
    /// Produces the post-edit text of a file for the dry-run preview, without writing to disk.
    /// </summary>
    public static string BuildPreview(string filePath, IReadOnlyList<LabelOccurrence> occs, string reference)
    {
        string text = FileRewriter.ReadAllText(filePath, out _);
        var edits = new List<FileRewriter.Edit>(occs.Count);
        foreach (var o in occs)
            edits.Add(new FileRewriter.Edit { Start = o.Start, Length = o.Length, Replacement = reference });
        edits.Sort((a, b) => b.Start.CompareTo(a.Start));

        var sb = new System.Text.StringBuilder(text);
        foreach (var e in edits)
        {
            sb.Remove(e.Start, e.Length);
            sb.Insert(e.Start, e.Replacement);
        }
        return sb.ToString();
    }
}
