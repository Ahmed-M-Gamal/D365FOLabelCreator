using D365LabelCreator.Models;
using D365LabelCreator.Models;

namespace D365LabelCreator.ViewModels;

/// <summary>A wrapper around LabelGroup to support multi-select and bulk operations in the hardcoded labels list.</summary>
public sealed class LabelGroupViewModel : ObservableObject
{
    public LabelGroupViewModel(LabelGroup group)
    {
        Group = group;
    }

    public LabelGroup Group { get; }

    public string DisplayText => Group.DisplayText;

    public int PendingCount => Group.PendingCount;

    /// <summary>
    /// Suggested/default id for this label group (the first occurrence's computed id).
    /// Used for bulk-generate operations on the group level.
    /// </summary>
    private string _suggestedId = "";
    public string SuggestedId
    {
        get => _suggestedId;
        set => SetProperty(ref _suggestedId, value);
    }

    /// <summary>Whether this group is selected for bulk operations.</summary>
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
