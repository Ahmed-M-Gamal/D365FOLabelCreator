using System;
using System.Collections.Generic;
using System.Text;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Builds and validates label ids.</summary>
public static class LabelIdHelper
{
    // Internal tracker to remember generated IDs and apply number sequences automatically
    private static readonly HashSet<string> _usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default id generated using the context (object/control name) AND the actual label text.
    /// Automatically applies a number sequence (_1, _2) if the ID already exists.
    /// </summary>
    public static string DefaultId(LabelOccurrence occ, string? prefix = null)
    {
        // 1. Determine the context prefix (using your original fallback logic)
        string objectBase = occ.Item.Name;
        if (occ.Item.ElementType == "AxEnumExtension")
        {
            int dot = objectBase.IndexOf('.');
            if (dot > 0)
                objectBase = objectBase[..dot];
        }

        string contextPrefix;
        if (occ.ParentType == "AxEnumValue" && !string.IsNullOrEmpty(occ.EnumValueName))
            contextPrefix = objectBase + "_" + occ.EnumValueName;       // keep the value's own underscore verbatim
        else if (IsFormControl(occ.ParentType) && !string.IsNullOrEmpty(occ.OwnerName))
            contextPrefix = occ.OwnerName;                             // form control -> its <Name>
        else if (IsTableField(occ.ParentType) && !string.IsNullOrEmpty(occ.OwnerName))
            contextPrefix = occ.OwnerName;                             // table field / field group -> its <Name>
        else
            contextPrefix = objectBase;                                // form Design caption, table label, EDT, menu item, …

        // 2. Sanitize the context prefix and the actual label text
        string cleanContext = Sanitize(contextPrefix);
        string sanitizedText = Sanitize(occ.Text);

        if (string.IsNullOrEmpty(sanitizedText))
            sanitizedText = "Label";

        // 3. Combine them to create a descriptive ID (e.g. "CustTable_CustomerBalance")
        string baseId = string.IsNullOrEmpty(cleanContext)
            ? sanitizedText
            : $"{cleanContext}_{sanitizedText}";

        // 4. Apply optional global prefix
        baseId = ApplyPrefix(baseId, prefix);

        // 5. A help text gets a "_HelpText" suffix.
        if (occ.PropertyName == "HelpText")
            baseId += "_HelpText";

        // 6. Ensure Uniqueness (Number Sequence Fallback)
        string finalId = baseId;
        int sequence = 1;

        while (_usedIds.Contains(finalId))
        {
            finalId = $"{baseId}_{sequence}";
            sequence++;
        }

        // 7. Register the ID so future calls don't reuse it
        _usedIds.Add(finalId);

        return finalId;
    }

    /// <summary>
    /// Generates a label id directly from text by sanitizing it and optionally applying a prefix.
    /// Note: Does not track uniqueness on its own unless you manually add that logic here.
    /// </summary>
    public static string GenerateIdFromText(string labelText, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(labelText))
            return prefix ?? "Label";

        string sanitized = Sanitize(labelText);

        if (string.IsNullOrEmpty(sanitized))
            sanitized = "Label";

        return ApplyPrefix(sanitized, prefix);
    }

    /// <summary>A form control node, e.g. AxFormControl / AxFormButtonGroupControl / AxFormStringControl.</summary>
    private static bool IsFormControl(string parentType) =>
        parentType.StartsWith("AxForm", StringComparison.Ordinal) &&
        parentType.EndsWith("Control", StringComparison.Ordinal);

    /// <summary>A table field or field group node (AxTableFieldString, AxTableFieldGroup, …).</summary>
    private static bool IsTableField(string parentType) =>
        parentType.StartsWith("AxTableField", StringComparison.Ordinal);

    /// <summary>
    /// Prepends the prefix unless it already appears anywhere in the id (case-insensitive).
    /// </summary>
    public static string ApplyPrefix(string id, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return id;

        string clean = Sanitize(prefix);
        if (clean.Length == 0)
            return id;
        if (id.Contains(clean, StringComparison.OrdinalIgnoreCase))
            return id;
        return clean + id;
    }

    /// <summary>Removes whitespace and replaces any char outside [A-Za-z0-9_] with '_'.</summary>
    public static string Sanitize(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;

        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            if (char.IsWhiteSpace(c))
                continue;
            sb.Append(IsIdChar(c) ? c : '_');
        }
        return sb.ToString();
    }

    /// <summary>A valid label id is non-empty and consists only of [A-Za-z0-9_].</summary>
    public static bool IsValid(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        foreach (var c in id)
            if (!IsIdChar(c))
                return false;
        return true;
    }

    private static bool IsIdChar(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
}