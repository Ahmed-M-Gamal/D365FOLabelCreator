using D365LabelCreator.Models;

namespace D365LabelCreator.ViewModels;

/// <summary>A single metadata item occurrence shown in the item list.</summary>
public sealed class OccurrenceViewModel : ObservableObject
{
    public OccurrenceViewModel(LabelOccurrence occurrence)
    {
        Occurrence = occurrence;
    }

    public LabelOccurrence Occurrence { get; }

    public string ObjectName => $"{Occurrence.Item.ElementType} / {Occurrence.Item.Name}";

    public string Location => Occurrence.Kind == OccurrenceKind.CodeString
        ? "code (X++ string)"
        : $"{Occurrence.PropertyName} on {Occurrence.ParentType}"
          + (Occurrence.EnumValueName != null ? $" [{Occurrence.EnumValueName}]" : "");

    public bool IsReadOnly => Occurrence.Item.IsReadOnly;

    public string FilePath => Occurrence.Item.FilePath;
}
