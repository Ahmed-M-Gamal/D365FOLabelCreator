namespace D365LabelCreator.Models;

/// <summary>
/// A single-language label file. The picker lists every language; en-US sorts first as the base.
/// </summary>
public sealed class LabelFileInfo
{
    /// <summary>Logical id used in references: @&lt;LabelFileId&gt;:&lt;labelId&gt;, e.g. "SOG_OPR".</summary>
    public required string LabelFileId { get; init; }

    /// <summary>Language code, e.g. "en-US".</summary>
    public required string Language { get; init; }

    /// <summary>Descriptor name, e.g. "SOG_OPR_en-US".</summary>
    public required string DescriptorName { get; init; }

    /// <summary>Full path of the AxLabelFile descriptor xml.</summary>
    public required string DescriptorPath { get; init; }

    /// <summary>Full path of the .label.txt resource file holding the actual strings.</summary>
    public required string ContentFilePath { get; init; }

    public override string ToString() => $"{LabelFileId} ({Language})";
}
