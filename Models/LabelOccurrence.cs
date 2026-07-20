namespace D365LabelCreator.Models;

public enum OccurrenceKind
{
    /// <summary>Inner text of an XML property element (Label/Caption/HelpText/Text).</summary>
    XmlProperty,

    /// <summary>A double-quoted string literal inside X++ source (CDATA).</summary>
    CodeString,
}

/// <summary>
/// One physical location of a hardcoded (not "@...") label, with the exact character span
/// to overwrite. Spans are relative to the file read as plain UTF-8 text.
/// </summary>
public sealed class LabelOccurrence
{
    public required MetadataItem Item { get; init; }

    public required OccurrenceKind Kind { get; init; }

    /// <summary>Property element name (Label/Caption/HelpText/Text) or "code".</summary>
    public required string PropertyName { get; init; }

    /// <summary>Effective type of the owning node, e.g. "AxTableFieldDate", "AxFormButtonGroupControl", "AxTable".</summary>
    public required string ParentType { get; init; }

    /// <summary>Decoded, display/edit text (XML entities resolved for XmlProperty).</summary>
    public required string Text { get; init; }

    /// <summary>Char offset of the replaceable span start in the file text. Mutated as earlier edits shift the file.</summary>
    public int Start { get; set; }

    /// <summary>Length of the replaceable span (the inner text / the content between the quotes).</summary>
    public required int Length { get; init; }

    /// <summary>For AxEnumValue labels: the enum value's Name (used for id defaulting). Otherwise null.</summary>
    public string? EnumValueName { get; init; }

    /// <summary>
    /// The &lt;Name&gt; of the element owning this label — the form control, table field, enum value, …
    /// Null when the owner has no Name (e.g. a form &lt;Design&gt; caption).
    /// </summary>
    public string? OwnerName { get; init; }

    /// <summary>Set true once its replacement has been written; removed from the working set.</summary>
    public bool Treated { get; set; }
}
