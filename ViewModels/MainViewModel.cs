using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using D365LabelCreator.Models;
using D365LabelCreator.Services;

namespace D365LabelCreator.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private List<LabelOccurrence> _allOccurrences = new();
    private Dictionary<string, LabelFileEntry> _existingLabels = new(StringComparer.OrdinalIgnoreCase);
    private List<OccurrenceViewModel> _selected = new();
    private OccurrenceViewModel? _focused;
    private bool _suppressFilter;

    public MainViewModel()
    {
        _config = ConfigService.Load();

        BrowseCommand = new RelayCommand(Browse);
        BrowseProjectsCommand = new RelayCommand(BrowseProjects);
        ClearSolutionCommand = new RelayCommand(() => SelectedSolution = null);
        ClearProjectCommand = new RelayCommand(() => SelectedProject = null);
        ClearObjectTypeCommand = new RelayCommand(() => SelectedObjectType = null);
        ClearItemFilterCommand = new RelayCommand(() => SelectedItemFilter = null);
        ScanCommand = new RelayCommand(Scan, () => SelectedLabelFile != null);
        ValidateCommand = new RelayCommand(Validate, CanValidate);
        ConvertToSingleQuotesCommand = new RelayCommand(ConvertToSingleQuotes, CanConvertToSingleQuotes);

        // Restore saved path, else auto-detect on first launch.
        var path = _config.PackagesLocalDirectory;
        if (string.IsNullOrWhiteSpace(path) || !PackageScanner.LooksValid(path))
            path = PackageScanner.AutoDetect();
        if (!string.IsNullOrWhiteSpace(path))
            PackagesPath = path;

        if (!string.IsNullOrWhiteSpace(_config.ProjectsDirectory))
            ProjectsPath = _config.ProjectsDirectory;

        if (!string.IsNullOrWhiteSpace(_config.IdPrefix))
            _idPrefix = _config.IdPrefix; // field, so restoring does not re-save
    }

    // ----- Step 1: PackagesLocalDirectory -----
    private string? _packagesPath;
    public string? PackagesPath
    {
        get => _packagesPath;
        set
        {
            if (SetProperty(ref _packagesPath, value))
                LoadModels();
        }
    }

    // ----- Projects directory + solution/project filter -----
    private string? _projectsPath;
    public string? ProjectsPath
    {
        get => _projectsPath;
        set
        {
            if (SetProperty(ref _projectsPath, value))
                LoadSolutions();
        }
    }

    public ObservableCollection<SolutionInfo> Solutions { get; } = new();

    private SolutionInfo? _selectedSolution;
    public SolutionInfo? SelectedSolution
    {
        get => _selectedSolution;
        set
        {
            if (SetProperty(ref _selectedSolution, value))
            {
                Projects.Clear();
                _suppressFilter = true;
                SelectedProject = null;
                if (value != null)
                    foreach (var p in value.Projects)
                        Projects.Add(p);

                // Preselect the first project of the solution.
                if (Projects.Count > 0)
                    SelectedProject = Projects[0];
                _suppressFilter = false;

                SyncModelFromProject();
                OnProjectFilterChanged();
            }
        }
    }

    public ObservableCollection<ProjectInfo> Projects { get; } = new();

    private ProjectInfo? _selectedProject;
    public ProjectInfo? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value) && !_suppressFilter)
            {
                SyncModelFromProject();
                OnProjectFilterChanged();
            }
        }
    }

    // ----- Type + item filters (populated from scanned occurrences) -----
    public ObservableCollection<TypeOption> ObjectTypes { get; } = new();

    private TypeOption? _selectedObjectType;
    public TypeOption? SelectedObjectType
    {
        get => _selectedObjectType;
        set
        {
            if (SetProperty(ref _selectedObjectType, value) && !_suppressFilter)
            {
                _suppressFilter = true;
                RecomputeItemOptions();
                _suppressFilter = false;
                BuildGroups();
            }
        }
    }

    public ObservableCollection<ItemOption> ItemFilters { get; } = new();

    private ItemOption? _selectedItemFilter;
    public ItemOption? SelectedItemFilter
    {
        get => _selectedItemFilter;
        set
        {
            if (SetProperty(ref _selectedItemFilter, value) && !_suppressFilter)
                BuildGroups();
        }
    }

    // ----- Step 2: Model -----
    public ObservableCollection<ModelInfo> Models { get; } = new();

    private ModelInfo? _selectedModel;
    public ModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (SetProperty(ref _selectedModel, value))
                LoadLabelFiles();
        }
    }

    // ----- Step 3: Label file -----
    public ObservableCollection<LabelFileInfo> LabelFiles { get; } = new();

    private LabelFileInfo? _selectedLabelFile;
    public LabelFileInfo? SelectedLabelFile
    {
        get => _selectedLabelFile;
        set
        {
            if (SetProperty(ref _selectedLabelFile, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                InvalidateScan();
            }
        }
    }

    // ----- Step 4: grouped labels -----
    public ObservableCollection<LabelGroup> Groups { get; } = new();

    private LabelGroup? _selectedGroup;
    public LabelGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
                LoadItems();
        }
    }

    // ----- Step 5: items in the selected group -----
    public ObservableCollection<OccurrenceViewModel> Items { get; } = new();

    // ----- Step 6: editable fields -----
    /// <summary>
    /// Prefix applied to every defaulted label id (added at the front unless already present
    /// anywhere in the value). Remembered across validations and sessions.
    /// </summary>
    private string _idPrefix = "";
    public string IdPrefix
    {
        get => _idPrefix;
        set
        {
            if (SetProperty(ref _idPrefix, value))
            {
                _config.IdPrefix = value;
                ConfigService.Save(_config);

                // Re-apply the default so the shown id reflects the new prefix.
                ApplyDefaultLabelId();
            }
        }
    }

    private string _labelId = "";
    public string LabelId
    {
        get => _labelId;
        set
        {
            if (SetProperty(ref _labelId, value))
            {
                UpdatePreview();
                UpdateExistingLabelInfo();
                ValidateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Default description: the selected solution's name if any, else the model name.</summary>
    private string DefaultDescription => SelectedSolution?.Name ?? SelectedModel?.Name ?? "";

    // ----- Live "id already exists" feedback (refreshed whenever the id changes) -----
    private string _existingLabelInfo = "";
    public string ExistingLabelInfo { get => _existingLabelInfo; private set => SetProperty(ref _existingLabelInfo, value); }

    private bool _hasExistingLabelInfo;
    public bool HasExistingLabelInfo { get => _hasExistingLabelInfo; private set => SetProperty(ref _hasExistingLabelInfo, value); }

    private void UpdateExistingLabelInfo()
    {
        if (!string.IsNullOrWhiteSpace(LabelId) && _existingLabels.TryGetValue(LabelId, out var existing))
        {
            ExistingLabelInfo = $"This label id already exists — current value: \"{existing.Text}\"";
            HasExistingLabelInfo = true;
        }
        else
        {
            ExistingLabelInfo = "";
            HasExistingLabelInfo = false;
        }
    }

    private string _labelText = "";
    public string LabelText
    {
        get => _labelText;
        set { if (SetProperty(ref _labelText, value)) ValidateCommand.RaiseCanExecuteChanged(); }
    }

    private string _labelDescription = "";
    public string LabelDescription
    {
        get => _labelDescription;
        set => SetProperty(ref _labelDescription, value);
    }

    // ----- Preview (inline diff parts; the view builds coloured runs) -----
    /// <summary>Context text before the change (shown in both panes, uncoloured).</summary>
    public string PreviewPrefix { get; private set; } = "";
    /// <summary>The removed span (shown red in the "before" pane).</summary>
    public string PreviewOldText { get; private set; } = "";
    /// <summary>The inserted reference (shown green in the "after" pane).</summary>
    public string PreviewNewText { get; private set; } = "";
    /// <summary>Context text after the change (shown in both panes, uncoloured).</summary>
    public string PreviewSuffix { get; private set; } = "";
    public bool HasPreview { get; private set; }

    /// <summary>Raised whenever the preview parts change so the view can rebuild its coloured runs.</summary>
    public event Action? PreviewUpdated;

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private int _selectedCount;
    public int SelectedCount { get => _selectedCount; set => SetProperty(ref _selectedCount, value); }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseProjectsCommand { get; }
    public RelayCommand ClearSolutionCommand { get; }
    public RelayCommand ClearProjectCommand { get; }
    public RelayCommand ClearObjectTypeCommand { get; }
    public RelayCommand ClearItemFilterCommand { get; }
    public RelayCommand ScanCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public RelayCommand ConvertToSingleQuotesCommand { get; }

    /// <summary>Raised after the item list is rebuilt so the view can select the first item.</summary>
    public event Action? ItemsReloaded;

    // ============================================================

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select PackagesLocalDirectory" };
        if (!string.IsNullOrWhiteSpace(PackagesPath) && Directory.Exists(PackagesPath))
            dlg.InitialDirectory = PackagesPath;
        if (dlg.ShowDialog() == true)
            PackagesPath = dlg.FolderName;
    }

    private void BrowseProjects()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select Projects directory" };
        if (!string.IsNullOrWhiteSpace(ProjectsPath) && Directory.Exists(ProjectsPath))
            dlg.InitialDirectory = ProjectsPath;
        if (dlg.ShowDialog() == true)
            ProjectsPath = dlg.FolderName;
    }

    private void LoadSolutions()
    {
        Solutions.Clear();
        SelectedSolution = null;
        if (string.IsNullOrWhiteSpace(ProjectsPath) || !Directory.Exists(ProjectsPath))
            return;

        _config.ProjectsDirectory = ProjectsPath;
        ConfigService.Save(_config);

        foreach (var s in ProjectScanner.GetSolutions(ProjectsPath))
            Solutions.Add(s);
        Status = $"{Solutions.Count} solution(s) found.";
    }

    /// <summary>The active solution/project item filter: project (if set), else solution (if set), else none.</summary>
    private HashSet<string>? ActiveItemFilter =>
        SelectedProject != null ? SelectedProject.ItemKeys
        : SelectedSolution != null ? SelectedSolution.AllItemKeys
        : null;

    /// <summary>Selects the project's &lt;Model&gt; in the model dropdown when it is available there.</summary>
    private void SyncModelFromProject()
    {
        var proj = SelectedProject;
        if (proj == null || string.IsNullOrWhiteSpace(proj.Model))
            return;

        var match = Models.FirstOrDefault(m => string.Equals(m.Name, proj.Model, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            SelectedModel = match; // no-op if already selected
    }

    private void OnProjectFilterChanged()
    {
        // Solution/project changed: refresh the dependent type/item options and re-filter.
        if (_allOccurrences.Count > 0)
            RefreshFiltersAndGroups();
    }

    /// <summary>Occurrences passing the model scan + solution/project filter (ignores type/item).</summary>
    private IEnumerable<LabelOccurrence> OccurrencesForOptions()
    {
        var filter = ActiveItemFilter;
        foreach (var o in _allOccurrences)
        {
            if (o.Treated)
                continue;
            if (filter != null && !filter.Contains(ProjectInfo.Key(o.Item.ElementType, o.Item.Name)))
                continue;
            yield return o;
        }
    }

    /// <summary>Recomputes the type + item dropdowns (resetting their selection) and rebuilds the groups.</summary>
    private void RefreshFiltersAndGroups()
    {
        _suppressFilter = true;
        RecomputeTypeOptions();
        RecomputeItemOptions();
        _suppressFilter = false;
        BuildGroups();
    }

    private void RecomputeTypeOptions()
    {
        var types = OccurrencesForOptions()
            .Select(o => o.Item.ElementType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ObjectTypes.Clear();
        foreach (var t in types)
            ObjectTypes.Add(new TypeOption { ElementType = t });
        SelectedObjectType = null;
    }

    private void RecomputeItemOptions()
    {
        string? type = SelectedObjectType?.ElementType;
        var items = OccurrencesForOptions()
            .Where(o => type == null || o.Item.ElementType == type)
            .Select(o => (o.Item.ElementType, o.Item.Name))
            .Distinct()
            .OrderBy(x => x.ElementType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ItemFilters.Clear();
        foreach (var (etype, name) in items)
            ItemFilters.Add(new ItemOption { ElementType = etype, Name = name });
        SelectedItemFilter = null;
    }

    private void LoadModels()
    {
        Models.Clear();
        SelectedModel = null;
        if (string.IsNullOrWhiteSpace(PackagesPath) || !Directory.Exists(PackagesPath))
        {
            Status = "Select a valid PackagesLocalDirectory.";
            return;
        }

        _config.PackagesLocalDirectory = PackagesPath;
        ConfigService.Save(_config);

        foreach (var m in PackageScanner.GetCustomizableModels(PackagesPath))
            Models.Add(m);
        Status = $"{Models.Count} customizable model(s) found.";
    }

    private void LoadLabelFiles()
    {
        LabelFiles.Clear();
        SelectedLabelFile = null;
        InvalidateScan();
        if (SelectedModel == null)
            return;

        foreach (var lf in LabelFileService.GetLabelFiles(SelectedModel.ModelDir))
            LabelFiles.Add(lf);

        // Preselect the first entry (en-US sorts first, so the base language stays the default).
        if (LabelFiles.Count > 0)
            SelectedLabelFile = LabelFiles[0];

        Status = LabelFiles.Count == 0
            ? "No label file found in this model."
            : $"{LabelFiles.Count} label file(s) found.";
    }

    private void Scan()
    {
        if (SelectedModel == null || SelectedLabelFile == null)
            return;

        if (!File.Exists(SelectedLabelFile.ContentFilePath))
        {
            MessageBox.Show(
                $"The {SelectedLabelFile.Language} resource file is missing:\n{SelectedLabelFile.ContentFilePath}",
                "Missing resource file", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Status = "Scanning…";
        var result = MetadataScanner.ScanModel(SelectedModel.ModelDir);
        _allOccurrences = result.Occurrences;
        _existingLabels = LabelFileService.GetEntriesById(SelectedLabelFile.ContentFilePath);
        UpdateExistingLabelInfo();
        RefreshFiltersAndGroups();
        Status = $"Scanned {result.Items.Count} items — {Groups.Count} distinct hardcoded label(s), " +
                 $"{_allOccurrences.Count} occurrence(s).";
    }

    private void BuildGroups()
    {
        ClearResults();
        var filter = ActiveItemFilter;
        string? typeFilter = SelectedObjectType?.ElementType;
        ItemOption? itemFilter = SelectedItemFilter;
        var map = new Dictionary<string, LabelGroup>(StringComparer.Ordinal);
        foreach (var occ in _allOccurrences)
        {
            if (occ.Treated)
                continue;
            if (filter != null && !filter.Contains(ProjectInfo.Key(occ.Item.ElementType, occ.Item.Name)))
                continue;
            if (typeFilter != null && occ.Item.ElementType != typeFilter)
                continue;
            if (itemFilter != null &&
                (occ.Item.ElementType != itemFilter.ElementType || occ.Item.Name != itemFilter.Name))
                continue;
            string key = LabelGroup.NormalizeKey(occ.Text);
            if (!map.TryGetValue(key, out var g))
            {
                g = new LabelGroup { Key = key, DisplayText = occ.Text };
                map[key] = g;
            }
            g.Occurrences.Add(occ);
        }

        foreach (var g in map.Values.OrderBy(g => g.DisplayText, StringComparer.OrdinalIgnoreCase))
            Groups.Add(g);
    }

    private void LoadItems()
    {
        Items.Clear();
        _selected = new List<OccurrenceViewModel>();
        _focused = null;
        SelectedCount = 0;
        ClearPreview();

        if (SelectedGroup == null)
            return;

        foreach (var occ in SelectedGroup.PendingOccurrences)
            Items.Add(new OccurrenceViewModel(occ));

        LabelText = SelectedGroup.DisplayText;
        LabelDescription = DefaultDescription;
        LabelId = "";
        ValidateCommand.RaiseCanExecuteChanged();

        // Let the view select the first metadata item automatically.
        ItemsReloaded?.Invoke();
    }

    /// <summary>Called by the view when the item selection changes (native Ctrl/Shift multiselect).</summary>
    public void UpdateSelection(IEnumerable<OccurrenceViewModel> selected, OccurrenceViewModel? focused)
    {
        _selected = selected.ToList();
        _focused = focused ?? _selected.LastOrDefault();
        SelectedCount = _selected.Count;

        ApplyDefaultLabelId();
        UpdatePreview();
        ValidateCommand.RaiseCanExecuteChanged();
        ConvertToSingleQuotesCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Fills the id with the selection's default. Every selected occurrence must derive the very
    /// same id — which happens often, e.g. a "Test Date" label on the DateField of two tables both
    /// default to SOG_DateField. If they disagree, or any code string is selected (those never
    /// derive an id), the field is left blank and required.
    /// </summary>
    private void ApplyDefaultLabelId()
    {
        string? common = null;
        foreach (var vm in _selected)
        {
            var occ = vm.Occurrence;
            if (occ.Kind == OccurrenceKind.CodeString)
            {
                common = null;
                break;
            }

            string id = LabelIdHelper.DefaultId(occ, IdPrefix);
            if (common == null)
            {
                common = id;
            }
            else if (!string.Equals(common, id, StringComparison.Ordinal))
            {
                common = null;
                break;
            }
        }

        LabelId = common ?? "";
    }

    private void UpdatePreview()
    {
        if (_focused == null || SelectedLabelFile == null)
        {
            ClearPreview();
            return;
        }

        string idForPreview = string.IsNullOrWhiteSpace(LabelId) ? "<id>" : LabelId;
        string reference = ReplacementService.BuildReference(SelectedLabelFile.LabelFileId, idForPreview);
        var occ = _focused.Occurrence;

        try
        {
            string text = FileRewriter.ReadAllText(occ.Item.FilePath, out _);
            int end = occ.Start + occ.Length;
            (int ctxStart, int ctxEnd) = LineContext(text, occ.Start, end, 10); // ~21 lines of context

            PreviewPrefix = text.Substring(ctxStart, occ.Start - ctxStart);
            PreviewOldText = text.Substring(occ.Start, occ.Length);
            PreviewNewText = reference;
            PreviewSuffix = text.Substring(end, ctxEnd - end);
            HasPreview = true;
        }
        catch
        {
            PreviewPrefix = PreviewSuffix = PreviewNewText = "";
            PreviewOldText = "(unable to load file)";
            HasPreview = true;
        }

        PreviewUpdated?.Invoke();
    }

    private void ClearPreview()
    {
        PreviewPrefix = PreviewOldText = PreviewNewText = PreviewSuffix = "";
        HasPreview = false;
        PreviewUpdated?.Invoke();
    }

    private static (int, int) LineContext(string text, int start, int end, int lines)
    {
        int LineStart(int pos)
        {
            int nl = text.LastIndexOf('\n', Math.Max(0, pos - 1));
            return nl < 0 ? 0 : nl + 1;
        }
        int LineEnd(int pos)
        {
            int nl = text.IndexOf('\n', Math.Min(pos, text.Length));
            return nl < 0 ? text.Length : nl + 1;
        }

        int ctxStart = LineStart(start);
        for (int i = 0; i < lines && ctxStart > 0; i++)
            ctxStart = LineStart(ctxStart - 1);

        int ctxEnd = LineEnd(end);
        for (int i = 0; i < lines && ctxEnd < text.Length; i++)
            ctxEnd = LineEnd(ctxEnd);

        return (ctxStart, ctxEnd);
    }

    private bool CanValidate()
    {
        return SelectedLabelFile != null
               && _selected.Count > 0
               && !string.IsNullOrWhiteSpace(LabelId)
               && LabelIdHelper.IsValid(LabelId)
               && !string.IsNullOrEmpty(LabelText);
    }

    private void Validate()
    {
        if (SelectedLabelFile == null || _selected.Count == 0)
            return;

        if (!LabelIdHelper.IsValid(LabelId))
        {
            MessageBox.Show("Label id must contain only letters, digits and underscores.",
                "Invalid id", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // If the id already exists, show its value and offer to reuse it.
        bool reuseExisting = false;
        if (_existingLabels.TryGetValue(LabelId, out var existing))
        {
            var choice = MessageBox.Show(
                $"The label id '{LabelId}' already exists with the text:\n\n\"{existing.Text}\"\n\n" +
                "Do you want to use this existing label for the selected item(s)?",
                "Label id already exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                MessageBox.Show("The label id has to be modified because it is already taken.",
                    "Change the id", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            reuseExisting = true;
        }

        var occs = _selected.Select(vm => vm.Occurrence).Where(o => !o.Treated).ToList();
        if (occs.Count == 0)
            return;

        string reference = ReplacementService.BuildReference(SelectedLabelFile.LabelFileId, LabelId);
        string description = string.IsNullOrEmpty(LabelDescription) ? DefaultDescription : LabelDescription;

        try
        {
            if (!reuseExisting)
            {
                var entry = new LabelFileEntry { Id = LabelId, Text = LabelText, Description = description };
                LabelFileService.InsertSorted(SelectedLabelFile.ContentFilePath, entry);
                _existingLabels[LabelId] = entry;
            }

            ReplacementService.ApplySelection(_allOccurrences, occs, reference);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Status = reuseExisting
            ? $"Reused existing '{reference}' for {occs.Count} item(s)."
            : $"Created '{reference}' and updated {occs.Count} item(s).";

        AdvanceAfterTreatment();
    }

    private bool CanConvertToSingleQuotes() =>
        _selected.Any(vm => vm.Occurrence.Kind == OccurrenceKind.CodeString && !vm.Occurrence.Treated);

    /// <summary>
    /// Swaps the double quotes around the selected code strings for single quotes, which takes them
    /// out of scope entirely instead of turning them into labels.
    /// </summary>
    private void ConvertToSingleQuotes()
    {
        var occs = _selected
            .Select(vm => vm.Occurrence)
            .Where(o => o.Kind == OccurrenceKind.CodeString && !o.Treated)
            .ToList();
        if (occs.Count == 0)
            return;

        var confirm = MessageBox.Show(
            $"Replace the double quotes with single quotes for {occs.Count} code string(s)?\n\n" +
            "The text itself is left unchanged, and single-quoted strings are no longer reported " +
            "as hardcoded labels.\n\nAre you sure?",
            "Replace with single quotes", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        int converted;
        try
        {
            converted = ReplacementService.ConvertToSingleQuotes(occs);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to replace the quotes:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Status = $"Replaced double quotes with single quotes on {converted} code string(s).";
        AdvanceAfterTreatment();
    }

    /// <summary>
    /// After occurrences have been treated: drop the group if it is now empty and move to the next
    /// one (or the previous, if it was last); otherwise stay and refresh the remaining items.
    /// </summary>
    private void AdvanceAfterTreatment()
    {
        var current = SelectedGroup;
        if (current != null && current.PendingCount == 0)
        {
            int idx = Groups.IndexOf(current);
            Groups.Remove(current);
            if (Groups.Count == 0)
                SelectedGroup = null;
            else
                SelectedGroup = Groups[idx < Groups.Count ? idx : Groups.Count - 1];
        }
        else
        {
            LoadItems();
        }
    }

    /// <summary>
    /// Drops the whole scan (occurrences, known labels, filter options and groups). Used when the
    /// model or label file changes, so nothing can be rebuilt from a previous model's data.
    /// </summary>
    private void InvalidateScan()
    {
        _allOccurrences = new List<LabelOccurrence>();
        _existingLabels = new Dictionary<string, LabelFileEntry>(StringComparer.OrdinalIgnoreCase);

        _suppressFilter = true;
        ObjectTypes.Clear();
        SelectedObjectType = null;
        ItemFilters.Clear();
        SelectedItemFilter = null;
        _suppressFilter = false;

        UpdateExistingLabelInfo();
        ClearResults();
    }

    private void ClearResults()
    {
        Groups.Clear();
        SelectedGroup = null;
        Items.Clear();
        ClearPreview();
        SelectedCount = 0;
    }
}
