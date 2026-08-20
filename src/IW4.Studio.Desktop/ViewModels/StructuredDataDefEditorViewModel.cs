using System.ComponentModel;
using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Editors.StructuredData;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class StructuredDataNavigationNodeViewModel
{
    internal StructuredDataNavigationNodeViewModel(
        StructuredDataSelection selection,
        string title,
        string detail,
        IEnumerable<StructuredDataNavigationNodeViewModel>? children = null,
        bool isExpanded = false)
    {
        Selection = selection;
        Title = title;
        Detail = detail;
        Children = Array.AsReadOnly(children?.ToArray() ?? []);
        IsExpanded = isExpanded;
    }

    internal StructuredDataSelection Selection { get; }
    public string Title { get; }
    public string Detail { get; }
    public IReadOnlyList<StructuredDataNavigationNodeViewModel> Children { get; }
    public bool IsExpanded { get; set; }
}

public sealed class StructuredDataMemberRowViewModel
{
    internal StructuredDataMemberRowViewModel(
        StructuredDataSelection selection,
        string indexText,
        string primaryText,
        string secondaryText,
        string tertiaryText,
        string kindText)
    {
        Selection = selection;
        IndexText = indexText;
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        TertiaryText = tertiaryText;
        KindText = kindText;
    }

    internal StructuredDataSelection Selection { get; }
    public string IndexText { get; }
    public string PrimaryText { get; }
    public string SecondaryText { get; }
    public string TertiaryText { get; }
    public string KindText { get; }
}

internal enum StructuredDataSelectionKind
{
    Definition,
    RootType,
    Enums,
    Enum,
    EnumEntry,
    Structs,
    Struct,
    StructProperty,
    IndexedArrays,
    IndexedArray,
    EnumedArrays,
    EnumedArray
}

internal readonly record struct StructuredDataSelection(
    StructuredDataSelectionKind Kind,
    int DefinitionIndex,
    int Index = -1,
    int ChildIndex = -1);

/// <summary>
/// Detached, index-preserving StructuredDataDef editor. The main surface owns
/// navigation and concise member summaries; the shared Properties tool owns
/// deliberate scalar edits for the current selection.
/// </summary>
public sealed class StructuredDataDefEditorViewModel : ObservableObject,
    IAssetEditorProperties,
    IAssetEditorInspectorSource,
    IAssetEditorPropertiesRevealSource,
    IAssetEditorDiagnostics,
    IAssetEditorStagingState
{
    private readonly AssetEditorSession _session;
    private readonly List<INotifyPropertyChanged> _stagedRows = [];
    private StructuredDataDraft _baseline;
    private StructuredDataDraft _workingDraft;
    private IReadOnlyList<StructuredDataNavigationNodeViewModel> _navigationRoots = [];
    private StructuredDataNavigationNodeViewModel? _selectedNavigationNode;
    private IReadOnlyList<StructuredDataMemberRowViewModel> _allRows = [];
    private IReadOnlyList<StructuredDataMemberRowViewModel> _visibleRows = [];
    private StructuredDataMemberRowViewModel? _selectedMember;
    private InspectorSelectionViewModel? _inspectorSelection;
    private IReadOnlyList<AssetValidationIssue> _candidateDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _searchText = string.Empty;
    private bool _isReplacingProjection;
    private bool _isCommittingRows;

    public StructuredDataDefEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.StructuredDataDef)
        {
            throw new InvalidDataException(
                "The StructuredDataDef view model can host only StructuredDataDef editor sessions.");
        }

        _baseline = session.OpenDraft<StructuredDataDraft>();
        _workingDraft = _baseline.Copy();
        RefreshValidation();
        RebuildProjection();
    }

    public event EventHandler? PropertiesRevealRequested;

    internal StructuredDataDraft WorkingDraft => _workingDraft;

    public WorkspaceAssetAccess Mode => _session.Mode;
    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable;
    public string Name => _workingDraft.Name
        ?? _session.Entry.OriginalName
        ?? "Unnamed StructuredDataDef";
    public string AccessText => Mode switch
    {
        WorkspaceAssetAccess.Editable => "EDITABLE TARGET DEFINITION",
        WorkspaceAssetAccess.ReadOnly => "READ-ONLY RESOLVED PROVIDER",
        WorkspaceAssetAccess.ContentUnavailable => "CONTENT UNAVAILABLE",
        _ => "UNKNOWN ACCESS"
    };
    public string SummaryText =>
        $"{_workingDraft.Definitions.Count:N0} {Pluralize(_workingDraft.Definitions.Count, "definition", "definitions")} · " +
        $"{TotalNodeCount():N0} schema nodes";
    public string StatusMessage => HasCandidateChanges
        ? "Review the stored format checksum before applying; IW4 Studio does not recalculate it."
        : "Indexed references, stored checksums, and serialized padding are preserved.";

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _searchText, value))
                return;

            FilterRows();
        }
    }

    public IReadOnlyList<StructuredDataNavigationNodeViewModel> NavigationRoots
    {
        get => _navigationRoots;
        private set => SetProperty(ref _navigationRoots, value);
    }

    public StructuredDataNavigationNodeViewModel? SelectedNavigationNode
    {
        get => _selectedNavigationNode;
        set
        {
            if (_isReplacingProjection || ReferenceEquals(value, _selectedNavigationNode))
                return;
            if (value is not null && !ContainsNavigationNode(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (!TryCommitInspectorRows())
            {
                OnPropertyChanged();
                RevealProperties();
                return;
            }

            _selectedNavigationNode = value;
            OnPropertyChanged();
            _selectedMember = null;
            OnPropertyChanged(nameof(SelectedMember));
            RebuildRows();
            RefreshInspector();
            NotifySelectionState();
            RevealProperties();
        }
    }

    public IReadOnlyList<StructuredDataMemberRowViewModel> VisibleRows
    {
        get => _visibleRows;
        private set => SetProperty(ref _visibleRows, value);
    }

    public StructuredDataMemberRowViewModel? SelectedMember
    {
        get => _selectedMember;
        set
        {
            if (_isReplacingProjection || ReferenceEquals(value, _selectedMember))
                return;
            if (value is not null && !VisibleRows.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (!TryCommitInspectorRows())
            {
                OnPropertyChanged();
                RevealProperties();
                return;
            }

            _selectedMember = value;
            OnPropertyChanged();
            RefreshInspector();
            NotifySelectionState();
            RevealProperties();
        }
    }

    public string SelectionTitle => SelectedMember?.PrimaryText
        ?? SelectedNavigationNode?.Title
        ?? "Schema";
    public string SelectionSubtitle => SelectedMember is { } member
        ? $"{member.KindText} · {member.SecondaryText}"
        : SelectedNavigationNode?.Detail
          ?? "Choose a definition or schema table.";
    public bool HasVisibleRows => VisibleRows.Count != 0;
    public string EmptySelectionMessage => SelectedNavigationNode is null
        ? "This definition set contains no schema definitions."
        : string.IsNullOrWhiteSpace(SearchText)
            ? "This schema node has no child rows. Its scalar values are available in Properties."
            : "No rows match the current filter.";

    public InspectorSelectionViewModel? InspectorSelection
    {
        get => _inspectorSelection;
        private set => SetProperty(ref _inspectorSelection, value);
    }

    public IReadOnlyList<AssetValidationIssue> Diagnostics
    {
        get => _diagnostics;
        private set => SetProperty(ref _diagnostics, value);
    }

    public bool HasErrors => Diagnostics.Any(
        issue => issue.Severity == AssetValidationSeverity.Error);
    public bool HasWarnings => Diagnostics.Any(
        issue => issue.Severity == AssetValidationSeverity.Warning);
    public bool HasOnlyWarnings => !HasErrors && HasWarnings;
    public bool HasNoDiagnostics => !HasErrors && !HasWarnings;
    public string ValidationSummary
    {
        get
        {
            int errors = Diagnostics.Count(
                issue => issue.Severity == AssetValidationSeverity.Error);
            int warnings = Diagnostics.Count - errors;
            if (errors != 0)
                return $"{errors:N0} {Pluralize(errors, "error", "errors")} must be resolved";
            if (warnings != 0)
                return $"Schema valid · {warnings:N0} {Pluralize(warnings, "warning", "warnings")}";
            return $"Schema valid · {ReferenceCount():N0} indexed references resolved";
        }
    }

    public bool HasUnappliedChanges =>
        IsEditable && (StagedRowsHaveInput || HasCandidateChanges);
    public bool CanApply =>
        IsEditable && HasUnappliedChanges && !HasStagedErrors &&
        !_candidateDiagnostics.Any(issue =>
            issue.Severity == AssetValidationSeverity.Error);
    public bool CanRevert => IsEditable && HasUnappliedChanges;

    public string PropertySectionName => "StructuredDataDef";
    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Name", Name),
        new("Access", AccessText),
        new("Definitions", _workingDraft.Definitions.Count.ToString("N0")),
        new("Selection", SelectionTitle),
        new("References", ReferenceCount().ToString("N0")),
        new("Errors", Diagnostics.Count(issue =>
            issue.Severity == AssetValidationSeverity.Error).ToString("N0")),
        new("Warnings", Diagnostics.Count(issue =>
            issue.Severity == AssetValidationSeverity.Warning).ToString("N0"))
    ];

    public void ApplyDraft()
    {
        if (!IsEditable || !TryCommitInspectorRows())
            return;

        RefreshValidation();
        if (_candidateDiagnostics.Any(issue =>
                issue.Severity == AssetValidationSeverity.Error))
        {
            RevealProperties();
            return;
        }

        try
        {
            _ = _session.Apply<StructuredDataDraft>(
                draft => draft.ReplaceWith(_workingDraft));
            _baseline = _session.OpenDraft<StructuredDataDraft>();
            _workingDraft = _baseline.Copy();
            RefreshValidation();
            RebuildProjection();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            InvalidDataException or
            ArgumentException or
            OverflowException)
        {
            _candidateDiagnostics =
            [
                new AssetValidationIssue(
                    "StructuredDataDefSet",
                    exception.Message,
                    AssetValidationSeverity.Error)
            ];
            RebuildDiagnostics();
            NotifyState();
            RevealProperties();
        }
    }

    public void RevertDraft()
    {
        if (!IsEditable)
            return;

        foreach (IInspectorStagedPropertyRow row in
                 _stagedRows.OfType<IInspectorStagedPropertyRow>())
        {
            row.ResetInput();
        }

        _workingDraft = _baseline.Copy();
        RefreshValidation();
        RebuildProjection();
    }

    internal void Mutate(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (!IsEditable)
            throw new InvalidOperationException("This StructuredDataDef provider is read-only.");
        if (!_isCommittingRows && !TryCommitInspectorRows())
        {
            RevealProperties();
            throw new InvalidOperationException(
                "Resolve the invalid property value before changing another field.");
        }

        mutation();
        RefreshValidation();
        RebuildProjection(rebuildInspector: !_isCommittingRows);
    }

    private bool HasCandidateChanges =>
        !_session.CandidateMatchesCurrent(_workingDraft);
    private bool StagedRowsHaveInput => _stagedRows
        .OfType<IInspectorStagedPropertyRow>()
        .Any(row => row.HasStagedValue);
    private bool HasStagedErrors => _stagedRows
        .OfType<InspectorPropertyRowViewModel>()
        .Any(row => row.HasValidationError);

    private void RefreshValidation()
    {
        _candidateDiagnostics = _session.ValidateCandidate(_workingDraft).Issues;
        RebuildDiagnostics();
    }

    private void RebuildDiagnostics()
    {
        var diagnostics = new List<AssetValidationIssue>(_candidateDiagnostics);
        if (HasCandidateChanges)
        {
            diagnostics.Add(new AssetValidationIssue(
                "StructuredDataDefSet.Defs.FormatChecksum",
                "The schema has unapplied changes. IW4 Studio preserves format checksums but cannot calculate a replacement; verify the stored checksum before applying.",
                AssetValidationSeverity.Warning));
        }

        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    private bool TryCommitInspectorRows()
    {
        if (_isCommittingRows)
            return true;

        _isCommittingRows = true;
        try
        {
            foreach (IInspectorStagedPropertyRow row in
                     _stagedRows.OfType<IInspectorStagedPropertyRow>())
            {
                if (row.HasStagedValue && !row.CommitInput())
                    return false;
            }
        }
        finally
        {
            _isCommittingRows = false;
        }

        RefreshInspector();
        NotifyState();
        return true;
    }

    private void RefreshInspector()
    {
        foreach (INotifyPropertyChanged row in _stagedRows)
            row.PropertyChanged -= StagedRow_PropertyChanged;
        _stagedRows.Clear();

        StructuredDataSelection? selection = SelectedMember?.Selection
            ?? SelectedNavigationNode?.Selection;
        InspectorSelection = selection is { } current
            ? StructuredDataDefInspectorProjection.Create(this, current)
            : new InspectorSelectionViewModel(
                Name,
                "STRUCTURED DATA",
                [
                    new InspectorSectionViewModel(
                        "Definition set",
                        [
                            new InspectorReadOnlyPropertyRowViewModel(
                                "Name",
                                "StructuredDataDefSet.Name",
                                Name),
                            new InspectorReadOnlyPropertyRowViewModel(
                                "Definitions",
                                "StructuredDataDefSet.DefCount",
                                _workingDraft.Definitions.Count.ToString())
                        ])
                ],
                "Select a schema node or member to inspect its serialized values.");

        foreach (INotifyPropertyChanged row in InspectorSelection.Sections
                     .SelectMany(section => section.Rows)
                     .OfType<INotifyPropertyChanged>())
        {
            if (row is not IInspectorStagedPropertyRow)
                continue;
            _stagedRows.Add(row);
            row.PropertyChanged += StagedRow_PropertyChanged;
        }
    }

    private void StagedRow_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IInspectorStagedPropertyRow.HasStagedValue) or
            nameof(InspectorPropertyRowViewModel.HasValidationError))
        {
            NotifyState();
        }
    }

    private void RebuildProjection(bool rebuildInspector = true)
    {
        StructuredDataSelection? navigationSelection =
            SelectedNavigationNode?.Selection;
        StructuredDataSelection? memberSelection = SelectedMember?.Selection;
        IReadOnlySet<StructuredDataSelection>? expandedSelections =
            NavigationRoots.Count == 0 ? null : CaptureExpandedSelections();

        _isReplacingProjection = true;
        try
        {
            NavigationRoots = BuildNavigationRoots(expandedSelections);
            _selectedNavigationNode = navigationSelection is { } requested
                ? FindNavigationNode(requested)
                : NavigationRoots.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedNavigationNode));
            RebuildRows();
            _selectedMember = memberSelection is { } selected
                ? VisibleRows.FirstOrDefault(row => row.Selection == selected)
                : null;
            OnPropertyChanged(nameof(SelectedMember));
        }
        finally
        {
            _isReplacingProjection = false;
        }

        if (rebuildInspector)
            RefreshInspector();
        NotifySelectionState();
        NotifyState();
    }

    private IReadOnlyList<StructuredDataNavigationNodeViewModel> BuildNavigationRoots(
        IReadOnlySet<StructuredDataSelection>? expandedSelections = null)
    {
        var result = new StructuredDataNavigationNodeViewModel[
            _workingDraft.Definitions.Count];
        for (int definitionIndex = 0;
             definitionIndex < _workingDraft.Definitions.Count;
             definitionIndex++)
        {
            StructuredDataDefinitionDraft definition =
                _workingDraft.Definitions[definitionIndex];
            var enumChildren = definition.Enums.Select((value, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Enum,
                        definitionIndex,
                        index),
                    $"Enum {index}",
                    $"{value.Entries.Count:N0} entries · capacity {value.ReservedEntryCount:N0}"));
            var structChildren = definition.Structs.Select((value, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Struct,
                        definitionIndex,
                        index),
                    $"Struct {index}",
                    $"{value.Properties.Count:N0} properties · {ReferenceDetail(definition, StructuredDataTypeCategory.DataStruct, index)}"));
            var indexedChildren = definition.IndexedArrays.Select((value, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.IndexedArray,
                        definitionIndex,
                        index),
                    $"Indexed array {index}",
                    $"{value.ArraySize:N0} elements · {FormatType(definition, value.ElementType)}"));
            var enumedChildren = definition.EnumedArrays.Select((value, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.EnumedArray,
                        definitionIndex,
                        index),
                    $"Enumed array {index}",
                    $"Enum {value.EnumIndex} · {FormatType(definition, value.ElementType)}"));

            result[definitionIndex] = new StructuredDataNavigationNodeViewModel(
                new StructuredDataSelection(
                    StructuredDataSelectionKind.Definition,
                    definitionIndex),
                $"Definition {definitionIndex}",
                $"Version {definition.Version} · {definition.Size:N0} bytes",
                [
                    new StructuredDataNavigationNodeViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.RootType,
                            definitionIndex),
                        "Root type",
                        FormatType(definition, definition.RootType)),
                    new StructuredDataNavigationNodeViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.Structs,
                            definitionIndex),
                        "Structs",
                        $"{definition.Structs.Count:N0}",
                        structChildren,
                        isExpanded: expandedSelections?.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.Structs,
                                definitionIndex)) == true),
                    new StructuredDataNavigationNodeViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.Enums,
                            definitionIndex),
                        "Enums",
                        $"{definition.Enums.Count:N0}",
                        enumChildren,
                        isExpanded: expandedSelections?.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.Enums,
                                definitionIndex)) == true),
                    new StructuredDataNavigationNodeViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.IndexedArrays,
                            definitionIndex),
                        "Indexed arrays",
                        $"{definition.IndexedArrays.Count:N0}",
                        indexedChildren,
                        isExpanded: expandedSelections?.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.IndexedArrays,
                                definitionIndex)) == true),
                    new StructuredDataNavigationNodeViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.EnumedArrays,
                            definitionIndex),
                        "Enumed arrays",
                        $"{definition.EnumedArrays.Count:N0}",
                        enumedChildren,
                        isExpanded: expandedSelections?.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.EnumedArrays,
                                definitionIndex)) == true)
                ],
                isExpanded: expandedSelections is null
                    ? definitionIndex == 0
                    : expandedSelections.Contains(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.Definition,
                            definitionIndex)));
        }

        return Array.AsReadOnly(result);
    }

    private void RebuildRows()
    {
        _allRows = SelectedNavigationNode is { } selected
            ? BuildRows(selected.Selection)
            : [];
        FilterRows();
    }

    private void FilterRows()
    {
        StructuredDataSelection? selected = SelectedMember?.Selection;
        string filter = SearchText.Trim();
        VisibleRows = string.IsNullOrEmpty(filter)
            ? _allRows
            : Array.AsReadOnly(_allRows.Where(row =>
                    Contains(row.IndexText, filter) ||
                    Contains(row.PrimaryText, filter) ||
                    Contains(row.SecondaryText, filter) ||
                    Contains(row.TertiaryText, filter) ||
                    Contains(row.KindText, filter))
                .ToArray());
        if (selected is { } selection)
        {
            _selectedMember = VisibleRows.FirstOrDefault(
                    row => row.Selection == selection)
                ?? _selectedMember;
        }
        else
        {
            _selectedMember = null;
        }
        OnPropertyChanged(nameof(SelectedMember));
        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(EmptySelectionMessage));
        NotifySelectionState();
    }

    private IReadOnlyList<StructuredDataMemberRowViewModel> BuildRows(
        StructuredDataSelection selection)
    {
        if (selection.DefinitionIndex < 0 ||
            selection.DefinitionIndex >= _workingDraft.Definitions.Count)
        {
            return [];
        }

        StructuredDataDefinitionDraft definition =
            _workingDraft.Definitions[selection.DefinitionIndex];
        IEnumerable<StructuredDataMemberRowViewModel> rows = selection.Kind switch
        {
            StructuredDataSelectionKind.Definition => BuildDefinitionRows(
                definition,
                selection.DefinitionIndex),
            StructuredDataSelectionKind.RootType =>
            [
                TypeRow(
                    selection,
                    "ROOT",
                    "Root type",
                    definition,
                    definition.RootType,
                    $"{definition.Size:N0} bytes")
            ],
            StructuredDataSelectionKind.Enums => definition.Enums.Select(
                (value, index) => new StructuredDataMemberRowViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Enum,
                        selection.DefinitionIndex,
                        index),
                    $"#{index}",
                    $"Enum {index}",
                    $"{value.Entries.Count:N0} entries",
                    $"Capacity {value.ReservedEntryCount:N0}",
                    "ENUM")),
            StructuredDataSelectionKind.Enum => BuildEnumRows(
                definition,
                selection),
            StructuredDataSelectionKind.Structs => definition.Structs.Select(
                (value, index) => new StructuredDataMemberRowViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Struct,
                        selection.DefinitionIndex,
                        index),
                    $"#{index}",
                    $"Struct {index}",
                    $"{value.Properties.Count:N0} properties",
                    $"{value.Size:N0} bytes · bit {value.BitOffset:N0}",
                    "STRUCT")),
            StructuredDataSelectionKind.Struct => BuildStructRows(
                definition,
                selection),
            StructuredDataSelectionKind.IndexedArrays =>
                definition.IndexedArrays.Select((value, index) =>
                    new StructuredDataMemberRowViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.IndexedArray,
                            selection.DefinitionIndex,
                            index),
                        $"#{index}",
                        $"Indexed array {index}",
                        FormatType(definition, value.ElementType),
                        $"{value.ArraySize:N0} × {value.ElementSize:N0} bytes",
                        "INDEXED ARRAY")),
            StructuredDataSelectionKind.IndexedArray =>
            [
                BuildIndexedArrayRow(definition, selection)
            ],
            StructuredDataSelectionKind.EnumedArrays =>
                definition.EnumedArrays.Select((value, index) =>
                    new StructuredDataMemberRowViewModel(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.EnumedArray,
                            selection.DefinitionIndex,
                            index),
                        $"#{index}",
                        $"Enumed array {index}",
                        $"Enum {value.EnumIndex} → {FormatType(definition, value.ElementType)}",
                        $"{value.ElementSize:N0} bytes per element",
                        "ENUMED ARRAY")),
            StructuredDataSelectionKind.EnumedArray =>
            [
                BuildEnumedArrayRow(definition, selection)
            ],
            _ => []
        };

        return Array.AsReadOnly(rows.ToArray());
    }

    private static IEnumerable<StructuredDataMemberRowViewModel> BuildDefinitionRows(
        StructuredDataDefinitionDraft definition,
        int definitionIndex)
    {
        yield return TypeRow(
            new StructuredDataSelection(
                StructuredDataSelectionKind.RootType,
                definitionIndex),
            "ROOT",
            "Root type",
            definition,
            definition.RootType,
            $"{definition.Size:N0} bytes");
        yield return GroupRow(
            StructuredDataSelectionKind.Structs,
            definitionIndex,
            "Structs",
            definition.Structs.Count);
        yield return GroupRow(
            StructuredDataSelectionKind.Enums,
            definitionIndex,
            "Enums",
            definition.Enums.Count);
        yield return GroupRow(
            StructuredDataSelectionKind.IndexedArrays,
            definitionIndex,
            "Indexed arrays",
            definition.IndexedArrays.Count);
        yield return GroupRow(
            StructuredDataSelectionKind.EnumedArrays,
            definitionIndex,
            "Enumed arrays",
            definition.EnumedArrays.Count);
    }

    private static IEnumerable<StructuredDataMemberRowViewModel> BuildEnumRows(
        StructuredDataDefinitionDraft definition,
        StructuredDataSelection selection)
    {
        if (selection.Index < 0 || selection.Index >= definition.Enums.Count)
            yield break;
        StructuredDataEnumDraft value = definition.Enums[selection.Index];
        for (int index = 0; index < value.Entries.Count; index++)
        {
            StructuredDataEnumEntryDraft entry = value.Entries[index];
            yield return new StructuredDataMemberRowViewModel(
                new StructuredDataSelection(
                    StructuredDataSelectionKind.EnumEntry,
                    selection.DefinitionIndex,
                    selection.Index,
                    index),
                entry.Index.ToString(),
                entry.String ?? "NULL",
                $"Entry {index}",
                entry.Padding == 0 ? "No padding" : $"Padding 0x{entry.Padding:X4}",
                "ENUM VALUE");
        }
    }

    private static IEnumerable<StructuredDataMemberRowViewModel> BuildStructRows(
        StructuredDataDefinitionDraft definition,
        StructuredDataSelection selection)
    {
        if (selection.Index < 0 || selection.Index >= definition.Structs.Count)
            yield break;
        StructuredDataStructDraft value = definition.Structs[selection.Index];
        for (int index = 0; index < value.Properties.Count; index++)
        {
            StructuredDataStructPropertyDraft property = value.Properties[index];
            yield return new StructuredDataMemberRowViewModel(
                new StructuredDataSelection(
                    StructuredDataSelectionKind.StructProperty,
                    selection.DefinitionIndex,
                    selection.Index,
                    index),
                $"0x{property.Offset:X8}",
                property.Name ?? "NULL",
                FormatType(definition, property.Type),
                $"Property {index}",
                "PROPERTY");
        }
    }

    private static StructuredDataMemberRowViewModel BuildIndexedArrayRow(
        StructuredDataDefinitionDraft definition,
        StructuredDataSelection selection)
    {
        StructuredDataIndexedArrayDraft value =
            definition.IndexedArrays[selection.Index];
        return new StructuredDataMemberRowViewModel(
            selection,
            $"#{selection.Index}",
            $"Indexed array {selection.Index}",
            FormatType(definition, value.ElementType),
            $"{value.ArraySize:N0} × {value.ElementSize:N0} bytes",
            "INDEXED ARRAY");
    }

    private static StructuredDataMemberRowViewModel BuildEnumedArrayRow(
        StructuredDataDefinitionDraft definition,
        StructuredDataSelection selection)
    {
        StructuredDataEnumedArrayDraft value =
            definition.EnumedArrays[selection.Index];
        return new StructuredDataMemberRowViewModel(
            selection,
            $"#{selection.Index}",
            $"Enumed array {selection.Index}",
            $"Enum {value.EnumIndex} → {FormatType(definition, value.ElementType)}",
            $"{value.ElementSize:N0} bytes per element",
            "ENUMED ARRAY");
    }

    private static StructuredDataMemberRowViewModel TypeRow(
        StructuredDataSelection selection,
        string index,
        string name,
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft type,
        string layout) => new(
            selection,
            index,
            name,
            FormatType(definition, type),
            layout,
            "TYPE");

    private static StructuredDataMemberRowViewModel GroupRow(
        StructuredDataSelectionKind kind,
        int definitionIndex,
        string title,
        int count) => new(
            new StructuredDataSelection(kind, definitionIndex),
            "—",
            title,
            $"{count:N0} {Pluralize(count, "item", "items")}",
            "Indexed table",
            "TABLE");

    internal static string FormatType(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value)
    {
        string category = value.Type switch
        {
            StructuredDataTypeCategory.DataInt => "int",
            StructuredDataTypeCategory.DataByte => "byte",
            StructuredDataTypeCategory.DataBool => "bool",
            StructuredDataTypeCategory.DataString => "string",
            StructuredDataTypeCategory.DataEnum => "enum",
            StructuredDataTypeCategory.DataStruct => "struct",
            StructuredDataTypeCategory.DataIndexedArray => "indexed array",
            StructuredDataTypeCategory.DataEnumArray => "enumed array",
            StructuredDataTypeCategory.DataFloat => "float",
            StructuredDataTypeCategory.DataShort => "short",
            _ => value.Type.ToString()
        };
        return value.Type switch
        {
            StructuredDataTypeCategory.DataEnum =>
                $"{category} #{value.UnionValue}",
            StructuredDataTypeCategory.DataStruct =>
                $"{category} #{value.UnionValue}" +
                FormatAliasSuffix(
                    ReferenceAlias(
                        definition,
                        StructuredDataTypeCategory.DataStruct,
                        value.UnionValue)),
            StructuredDataTypeCategory.DataIndexedArray =>
                $"{category} #{value.UnionValue}",
            StructuredDataTypeCategory.DataEnumArray =>
                $"{category} #{value.UnionValue}",
            _ => value.UnionValue == 0
                ? category
                : $"{category} · raw {value.UnionValue}"
        };
    }

    private static string ReferenceDetail(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index)
    {
        string? alias = ReferenceAlias(definition, category, index);
        return alias is null ? "No named usage" : $"used as {alias}";
    }

    private static string? ReferenceAlias(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index) => definition.Structs
        .SelectMany(value => value.Properties)
        .Where(property =>
            property.Type.Type == category &&
            property.Type.UnionValue == index &&
            !string.IsNullOrWhiteSpace(property.Name))
        .Select(property => property.Name)
        .Distinct(StringComparer.Ordinal)
        .FirstOrDefault();

    private static string FormatAliasSuffix(string? alias) =>
        alias is null ? string.Empty : $" · {alias}";

    private int TotalNodeCount() => _workingDraft.Definitions.Sum(definition =>
        1 +
        definition.Enums.Count +
        definition.Enums.Sum(value => value.Entries.Count) +
        definition.Structs.Count +
        definition.Structs.Sum(value => value.Properties.Count) +
        definition.IndexedArrays.Count +
        definition.EnumedArrays.Count);

    private int ReferenceCount() => _workingDraft.Definitions.Sum(definition =>
        (IsIndexedReference(definition.RootType.Type) ? 1 : 0) +
        definition.Structs.Sum(value => value.Properties.Count(property =>
            IsIndexedReference(property.Type.Type))) +
        definition.IndexedArrays.Count(value =>
            IsIndexedReference(value.ElementType.Type)) +
        definition.EnumedArrays.Count +
        definition.EnumedArrays.Count(value =>
            IsIndexedReference(value.ElementType.Type)));

    private static bool IsIndexedReference(StructuredDataTypeCategory category) =>
        category is StructuredDataTypeCategory.DataEnum or
            StructuredDataTypeCategory.DataStruct or
            StructuredDataTypeCategory.DataIndexedArray or
            StructuredDataTypeCategory.DataEnumArray;

    private bool ContainsNavigationNode(
        StructuredDataNavigationNodeViewModel value) => NavigationRoots.Any(
        root => ContainsNavigationNode(root, value));

    private HashSet<StructuredDataSelection> CaptureExpandedSelections()
    {
        var result = new HashSet<StructuredDataSelection>();
        foreach (StructuredDataNavigationNodeViewModel root in NavigationRoots)
            CaptureExpandedSelections(root, result);
        return result;
    }

    private static void CaptureExpandedSelections(
        StructuredDataNavigationNodeViewModel node,
        ISet<StructuredDataSelection> result)
    {
        if (node.IsExpanded)
            result.Add(node.Selection);
        foreach (StructuredDataNavigationNodeViewModel child in node.Children)
            CaptureExpandedSelections(child, result);
    }

    private static bool ContainsNavigationNode(
        StructuredDataNavigationNodeViewModel root,
        StructuredDataNavigationNodeViewModel value) =>
        ReferenceEquals(root, value) || root.Children.Any(
            child => ContainsNavigationNode(child, value));

    private StructuredDataNavigationNodeViewModel? FindNavigationNode(
        StructuredDataSelection selection)
    {
        foreach (StructuredDataNavigationNodeViewModel root in NavigationRoots)
        {
            StructuredDataNavigationNodeViewModel? result =
                FindNavigationNode(root, selection);
            if (result is not null)
                return result;
        }
        return null;
    }

    private static StructuredDataNavigationNodeViewModel? FindNavigationNode(
        StructuredDataNavigationNodeViewModel root,
        StructuredDataSelection selection)
    {
        if (root.Selection == selection)
            return root;
        foreach (StructuredDataNavigationNodeViewModel child in root.Children)
        {
            StructuredDataNavigationNodeViewModel? result =
                FindNavigationNode(child, selection);
            if (result is not null)
                return result;
        }
        return null;
    }

    private void NotifySelectionState()
    {
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectionSubtitle));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void NotifyState()
    {
        RebuildDiagnostics();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasOnlyWarnings));
        OnPropertyChanged(nameof(HasNoDiagnostics));
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void RevealProperties() =>
        PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);

    private static bool Contains(string value, string filter) =>
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;
}
