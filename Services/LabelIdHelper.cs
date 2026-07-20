using System;
using System.Text;
using D365LabelCreator.Models;

namespace D365LabelCreator.Services;

/// <summary>Builds and validates label ids.</summary>
public static class LabelIdHelper
{
    /// <summary>
    /// Default id for a single occurrence, followed by the prefix rule:
    ///   - form control caption/label/text     -> the control's &lt;Name&gt;
    ///   - table field / field group           -> its &lt;Name&gt;
    ///   - enum / enum-extension value         -> "&lt;object&gt;_&lt;valueName&gt;"
    ///   - form &lt;Design&gt; caption and everything else -> the object (file) name
    /// Enum-extension objects drop their ".ModelName" suffix. A HelpText property finally gets a
    /// "_HelpText" suffix.
    /// </summary>
    public static string DefaultId(LabelOccurrence occ, string? prefix = null)
    {
        string objectBase = occ.Item.Name;
        if (occ.Item.ElementType == "AxEnumExtension")
        {
            int dot = objectBase.IndexOf('.');
            if (dot > 0)
                objectBase = objectBase[..dot];
        }

        string id;
        if (occ.ParentType == "AxEnumValue" && !string.IsNullOrEmpty(occ.EnumValueName))
            id = objectBase + "_" + occ.EnumValueName;       // keep the value's own underscore verbatim
        else if (IsFormControl(occ.ParentType) && !string.IsNullOrEmpty(occ.OwnerName))
            id = occ.OwnerName!;                             // form control -> its <Name>
        else if (IsTableField(occ.ParentType) && !string.IsNullOrEmpty(occ.OwnerName))
            id = occ.OwnerName!;                             // table field / field group -> its <Name>
        else
            id = objectBase;                                 // form Design caption, table label, EDT, menu item, …

        string result = ApplyPrefix(Sanitize(id), prefix);

        // A help text hangs off the same id as its label.
        if (occ.PropertyName == "HelpText")
            result += "_HelpText";

        return result;
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
