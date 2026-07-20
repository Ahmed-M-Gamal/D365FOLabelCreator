namespace D365LabelCreator.Models;

/// <summary>One entry in a .label.txt resource file: an "Id=Text" line plus a " ;Description" line.</summary>
public sealed class LabelFileEntry
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string Description { get; init; }
}
