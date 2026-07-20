namespace D365LabelCreator.Models;

/// <summary>
/// An AOT metadata object living in a type folder under the model, e.g. AxTable\SOG_Sandbox.xml.
/// Deliberately abstract (type + path) so behaviour can specialise per type later.
/// </summary>
public sealed class MetadataItem
{
    /// <summary>The type folder name, e.g. "AxTable", "AxForm", "AxEnum", "AxEnumExtension".</summary>
    public required string ElementType { get; init; }

    /// <summary>The object name = file name without ".xml", e.g. "SOG_Sandbox".</summary>
    public required string Name { get; init; }

    public required string FilePath { get; init; }

    /// <summary>Set when the source file is read-only on disk (surfaced in red in the UI).</summary>
    public bool IsReadOnly { get; set; }

    public override string ToString() => $"{ElementType}/{Name}";
}
