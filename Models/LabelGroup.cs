using System.Collections.Generic;

namespace D365LabelCreator.Models;

/// <summary>
/// Hardcoded labels grouped by normalised text (lower-cased, all whitespace removed).
/// DisplayText is the raw text of the first occurrence found.
/// </summary>
public sealed class LabelGroup
{
    public required string Key { get; init; }

    public required string DisplayText { get; set; }

    public List<LabelOccurrence> Occurrences { get; } = new();

    /// <summary>Count of occurrences not yet treated.</summary>
    public int PendingCount
    {
        get
        {
            int n = 0;
            foreach (var o in Occurrences)
                if (!o.Treated)
                    n++;
            return n;
        }
    }

    /// <summary>Only occurrences not yet treated.</summary>
    public IEnumerable<LabelOccurrence> PendingOccurrences
    {
        get
        {
            foreach (var o in Occurrences)
                if (!o.Treated)
                    yield return o;
        }
    }

    /// <summary>
    /// Normalises text for grouping: not case sensitive, and leading/trailing whitespace trimmed.
    /// Whitespace *inside* the text is significant, so "Sales order" never groups with "Salesorder".
    /// </summary>
    public static string NormalizeKey(string text) => text.Trim().ToLowerInvariant();
}
