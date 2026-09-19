using System.ComponentModel;
using System.Text;
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
        IEnumerable<StructuredDataNavigationNodeViewModel>? children = null,
        bool isExpanded = false)
    {
        Selection = selection;
        Title = title;
        Children = Array.AsReadOnly(children?.ToArray() ?? []);
        IsExpanded = isExpanded;
    }

    internal StructuredDataSelection Selection { get; }
    public string Title { get; }
    public IReadOnlyList<StructuredDataNavigationNodeViewModel> Children { get; }
    public bool IsExpanded { get; set; }
}

public sealed class StructuredDataMemberRowViewModel
{
    internal StructuredDataMemberRowViewModel(
        StructuredDataSelection selection,
        string primaryText,
        string schemaTypeText,
        string schemaCardinalityText)
    {
        Selection = selection;
        PrimaryText = primaryText;
        SchemaTypeText = schemaTypeText;
        SchemaCardinalityText = schemaCardinalityText;
    }

    internal StructuredDataSelection Selection { get; }
    public string PrimaryText { get; }
    public string SchemaTypeText { get; }
    public string SchemaCardinalityText { get; }
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
    private IReadOnlyList<StructuredDataMemberRowViewModel> _visibleRows = [];
    private StructuredDataMemberRowViewModel? _selectedMember;
    private InspectorSelectionViewModel? _inspectorSelection;
    private IReadOnlyList<AssetValidationIssue> _candidateDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
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
    public string DisplayName => FormatDisplayName(Name);
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
    public string SelectionBreadcrumb => FormatSelectionBreadcrumb();
    public string SelectionKindText => FormatSelectionKind(
        SelectedMember?.Selection.Kind ??
        SelectedNavigationNode?.Selection.Kind);
    public string SchemaFirstColumnTitle =>
        SelectedNavigationNode?.Selection.Kind switch
        {
            StructuredDataSelectionKind.Enum => "VALUE",
            StructuredDataSelectionKind.Struct => "FIELD",
            _ => "NAME"
        };
    public string SchemaSecondColumnTitle =>
        SelectedNavigationNode?.Selection.Kind switch
        {
            StructuredDataSelectionKind.Enum => "INDEX",
            StructuredDataSelectionKind.Struct => "TYPE",
            _ => "KIND"
        };
    public string SchemaThirdColumnTitle =>
        SelectedNavigationNode?.Selection.Kind switch
        {
            StructuredDataSelectionKind.Enum => string.Empty,
            StructuredDataSelectionKind.Struct => "CARDINALITY",
            _ => "CONTENTS"
        };
    public string SchemaHelpText => IsEditable
        ? "Fields point to types; cardinality shows fixed counts or enum keys. Friendly names are inferred. Select a row to edit in Properties."
        : "Fields point to types; cardinality shows fixed counts or enum keys. Friendly names are inferred. Select a row to inspect in Properties.";
    public bool HasVisibleRows => VisibleRows.Count != 0;
    public string EmptySelectionMessage => SelectedNavigationNode is null
        ? "This definition set contains no schema definitions."
        : "This item has no members. Its stored values are available in Properties.";

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
        if (HasStagedErrors)
            return false;

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
                : FindInitialNavigationNode();
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
        var result = new List<StructuredDataNavigationNodeViewModel>();
        for (int definitionIndex = 0;
             definitionIndex < _workingDraft.Definitions.Count;
             definitionIndex++)
        {
            StructuredDataDefinitionDraft definition =
                _workingDraft.Definitions[definitionIndex];
            int rootStructIndex = definition.RootType.UnionValue;
            bool hasNavigableRootStruct = IsRootStruct(
                definition,
                rootStructIndex);
            var enumChildren = definition.Enums.Select((_, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Enum,
                        definitionIndex,
                        index),
                    ReferenceDisplayName(
                        definition,
                        StructuredDataTypeCategory.DataEnum,
                        index)));
            var structChildren = Enumerable.Range(0, definition.Structs.Count)
                .Where(index => !hasNavigableRootStruct || index != rootStructIndex)
                .Select(index => new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Struct,
                        definitionIndex,
                        index),
                    ReferenceDisplayName(
                        definition,
                        StructuredDataTypeCategory.DataStruct,
                        index)));
            var indexedChildren = definition.IndexedArrays.Select((_, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.IndexedArray,
                        definitionIndex,
                        index),
                    ReferenceDisplayName(
                        definition,
                        StructuredDataTypeCategory.DataIndexedArray,
                        index)));
            var enumedChildren = definition.EnumedArrays.Select((_, index) =>
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.EnumedArray,
                        definitionIndex,
                        index),
                    ReferenceDisplayName(
                        definition,
                        StructuredDataTypeCategory.DataEnumArray,
                        index)));

            var definitionChildren = new List<StructuredDataNavigationNodeViewModel>();
            definitionChildren.Add(new StructuredDataNavigationNodeViewModel(
                new StructuredDataSelection(
                    StructuredDataSelectionKind.RootType,
                    definitionIndex),
                "Root"));

            definitionChildren.AddRange(
            [
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Structs,
                        definitionIndex),
                    "Structs",
                    structChildren,
                    isExpanded: expandedSelections is null ||
                        expandedSelections.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.Structs,
                                definitionIndex))),
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Enums,
                        definitionIndex),
                    "Enums",
                    enumChildren,
                    isExpanded: expandedSelections is null ||
                        expandedSelections.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.Enums,
                                definitionIndex))),
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.IndexedArrays,
                        definitionIndex),
                    "Fixed arrays",
                    indexedChildren,
                    isExpanded: expandedSelections is null ||
                        expandedSelections.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.IndexedArrays,
                                definitionIndex))),
                new StructuredDataNavigationNodeViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.EnumedArrays,
                        definitionIndex),
                    "Keyed arrays",
                    enumedChildren,
                    isExpanded: expandedSelections is null ||
                        expandedSelections.Contains(
                            new StructuredDataSelection(
                                StructuredDataSelectionKind.EnumedArrays,
                                definitionIndex)))
            ]);

            if (_workingDraft.Definitions.Count == 1)
                return Array.AsReadOnly(definitionChildren.ToArray());

            result.Add(new StructuredDataNavigationNodeViewModel(
                new StructuredDataSelection(
                    StructuredDataSelectionKind.Definition,
                    definitionIndex),
                $"Definition {definitionIndex}",
                definitionChildren,
                isExpanded: expandedSelections is null
                    ? definitionIndex == 0
                    : expandedSelections.Contains(
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.Definition,
                            definitionIndex))));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private void RebuildRows()
    {
        VisibleRows = SelectedNavigationNode is { } selected
            ? BuildRows(selected.Selection)
            : [];
        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(EmptySelectionMessage));
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
            StructuredDataSelectionKind.RootType => IsRootStruct(
                definition,
                definition.RootType.UnionValue)
                    ? BuildStructRows(
                        definition,
                        new StructuredDataSelection(
                            StructuredDataSelectionKind.Struct,
                            selection.DefinitionIndex,
                            definition.RootType.UnionValue))
                    :
                    [
                        TypeRow(
                            selection,
                            "Root",
                            definition,
                            definition.RootType)
                    ],
            StructuredDataSelectionKind.Enums => definition.Enums.Select(
                (value, index) => new StructuredDataMemberRowViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Enum,
                        selection.DefinitionIndex,
                        index),
                    ReferenceDisplayName(
                        definition,
                        StructuredDataTypeCategory.DataEnum,
                        index),
                    "Enum",
                    FormatEnumCardinality(value))),
            StructuredDataSelectionKind.Enum => BuildEnumRows(
                definition,
                selection),
            StructuredDataSelectionKind.Structs => Enumerable
                .Range(0, definition.Structs.Count)
                .Where(index => !IsRootStruct(definition, index))
                .Select(index => new StructuredDataMemberRowViewModel(
                    new StructuredDataSelection(
                        StructuredDataSelectionKind.Struct,
                        selection.DefinitionIndex,
                        index),
                    ReferenceDisplayName(
                        definition,
                        StructuredDataTypeCategory.DataStruct,
                        index),
                    "Struct",
                    $"{definition.Structs[index].Properties.Count:N0} fields")),
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
                        ReferenceDisplayName(
                            definition,
                            StructuredDataTypeCategory.DataIndexedArray,
                            index),
                        FormatSemanticType(definition, value.ElementType),
                        FormatIndexedArrayCardinality(definition, value))),
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
                        ReferenceDisplayName(
                            definition,
                            StructuredDataTypeCategory.DataEnumArray,
                            index),
                        FormatSemanticType(definition, value.ElementType),
                        FormatEnumedArrayCardinality(definition, value))),
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
        bool hasRootStruct = IsRootStruct(
            definition,
            definition.RootType.UnionValue);
        yield return TypeRow(
            new StructuredDataSelection(
                StructuredDataSelectionKind.RootType,
                definitionIndex),
            "Root",
            definition,
            definition.RootType);
        yield return GroupRow(
            StructuredDataSelectionKind.Structs,
            definitionIndex,
            "Structs",
            definition.Structs.Count - (hasRootStruct ? 1 : 0));
        yield return GroupRow(
            StructuredDataSelectionKind.Enums,
            definitionIndex,
            "Enums",
            definition.Enums.Count);
        yield return GroupRow(
            StructuredDataSelectionKind.IndexedArrays,
            definitionIndex,
            "Fixed arrays",
            definition.IndexedArrays.Count);
        yield return GroupRow(
            StructuredDataSelectionKind.EnumedArrays,
            definitionIndex,
            "Keyed arrays",
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
                entry.String ?? "NULL",
                $"#{entry.Index}",
                string.Empty);
        }
    }

    private static string FormatEnumCardinality(StructuredDataEnumDraft value) =>
        value.ReservedEntryCount > value.Entries.Count
            ? $"{value.Entries.Count:N0} values · capacity {value.ReservedEntryCount:N0}"
            : $"{value.Entries.Count:N0} values";

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
                property.Name ?? "NULL",
                FormatSemanticType(definition, property.Type),
                FormatSemanticCardinality(definition, property.Type));
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
            ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataIndexedArray,
                selection.Index),
            FormatSemanticType(definition, value.ElementType),
            FormatIndexedArrayCardinality(definition, value));
    }

    private static StructuredDataMemberRowViewModel BuildEnumedArrayRow(
        StructuredDataDefinitionDraft definition,
        StructuredDataSelection selection)
    {
        StructuredDataEnumedArrayDraft value =
            definition.EnumedArrays[selection.Index];
        return new StructuredDataMemberRowViewModel(
            selection,
            ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataEnumArray,
                selection.Index),
            FormatSemanticType(definition, value.ElementType),
            FormatEnumedArrayCardinality(definition, value));
    }

    internal static bool IsBitPackedBoolean(
        StructuredDataTypeDraft elementType,
        uint elementSize) =>
        elementType.Type == StructuredDataTypeCategory.DataBool &&
        elementSize == 1;

    private static StructuredDataMemberRowViewModel TypeRow(
        StructuredDataSelection selection,
        string name,
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft type) => new(
            selection,
            name,
            FormatSemanticType(definition, type),
            FormatSemanticCardinality(definition, type));

    private static StructuredDataMemberRowViewModel GroupRow(
        StructuredDataSelectionKind kind,
        int definitionIndex,
        string title,
        int count) => new(
            new StructuredDataSelection(kind, definitionIndex),
            title,
            "Schema group",
            $"{count:N0} {Pluralize(count, "item", "items")}");

    internal static string FormatSemanticType(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value) =>
        FormatSemanticType(definition, value, 0);

    private static string FormatSemanticType(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value,
        int depth)
    {
        if (depth >= 8)
            return FormatType(definition, value);

        return value.Type switch
        {
            StructuredDataTypeCategory.DataInt or
            StructuredDataTypeCategory.DataByte or
            StructuredDataTypeCategory.DataBool or
            StructuredDataTypeCategory.DataString or
            StructuredDataTypeCategory.DataFloat or
            StructuredDataTypeCategory.DataShort =>
                FormatSemanticScalar(value),
            StructuredDataTypeCategory.DataEnum =>
                ReferenceDisplayName(
                    definition,
                    StructuredDataTypeCategory.DataEnum,
                    value.UnionValue),
            StructuredDataTypeCategory.DataStruct =>
                ReferenceDisplayName(
                    definition,
                    StructuredDataTypeCategory.DataStruct,
                    value.UnionValue),
            StructuredDataTypeCategory.DataIndexedArray
                when IsValidIndex(value.UnionValue, definition.IndexedArrays.Count) =>
                FormatSemanticType(
                    definition,
                    definition.IndexedArrays[value.UnionValue].ElementType,
                    depth + 1),
            StructuredDataTypeCategory.DataEnumArray
                when IsValidIndex(value.UnionValue, definition.EnumedArrays.Count) =>
                FormatSemanticType(
                    definition,
                    definition.EnumedArrays[value.UnionValue].ElementType,
                    depth + 1),
            _ => FormatType(definition, value)
        };
    }

    private static string FormatSemanticCardinality(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value) =>
        FormatSemanticCardinality(definition, value, 0);

    private static string FormatSemanticCardinality(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value,
        int depth)
    {
        if (depth >= 8)
            return "Nested reference";

        if (value.Type == StructuredDataTypeCategory.DataIndexedArray)
        {
            if (!IsValidIndex(value.UnionValue, definition.IndexedArrays.Count))
                return $"Unresolved fixed array #{value.UnionValue}";
            StructuredDataIndexedArrayDraft array =
                definition.IndexedArrays[value.UnionValue];
            string cardinality = $"Fixed [{array.ArraySize:N0}]";
            if (IsBitPackedBoolean(array.ElementType, array.ElementSize))
                cardinality += " · bit-packed";
            return JoinCardinality(
                cardinality,
                FormatSemanticCardinality(
                    definition,
                    array.ElementType,
                    depth + 1));
        }

        if (value.Type == StructuredDataTypeCategory.DataEnumArray)
        {
            if (!IsValidIndex(value.UnionValue, definition.EnumedArrays.Count))
                return $"Unresolved keyed array #{value.UnionValue}";
            StructuredDataEnumedArrayDraft array =
                definition.EnumedArrays[value.UnionValue];
            string cardinality = $"Keyed by {ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataEnum,
                array.EnumIndex)}";
            if (IsBitPackedBoolean(array.ElementType, array.ElementSize))
                cardinality += " · bit-packed";
            return JoinCardinality(
                cardinality,
                FormatSemanticCardinality(
                    definition,
                    array.ElementType,
                    depth + 1));
        }

        return "Single";
    }

    private static string FormatIndexedArrayCardinality(
        StructuredDataDefinitionDraft definition,
        StructuredDataIndexedArrayDraft value)
    {
        string cardinality = $"Fixed [{value.ArraySize:N0}]";
        if (IsBitPackedBoolean(value.ElementType, value.ElementSize))
            cardinality += " · bit-packed";
        return JoinCardinality(
            cardinality,
            FormatSemanticCardinality(definition, value.ElementType));
    }

    private static string FormatEnumedArrayCardinality(
        StructuredDataDefinitionDraft definition,
        StructuredDataEnumedArrayDraft value)
    {
        string cardinality = $"Keyed by {ReferenceDisplayName(
            definition,
            StructuredDataTypeCategory.DataEnum,
            value.EnumIndex)}";
        if (IsBitPackedBoolean(value.ElementType, value.ElementSize))
            cardinality += " · bit-packed";
        return JoinCardinality(
            cardinality,
            FormatSemanticCardinality(definition, value.ElementType));
    }

    private static string JoinCardinality(string current, string nested) =>
        string.Equals(nested, "Single", StringComparison.Ordinal)
            ? current
            : $"{current} · {nested}";

    internal static string ReferenceDisplayName(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index)
    {
        string fallback = ReferenceFallbackName(category, index);
        if (!IsValidReference(definition, category, index))
            return fallback;

        string candidate = ReferenceDisplayCandidate(
            definition,
            category,
            index);
        int count = ReferenceTableCount(definition, category);
        bool isAmbiguous = Enumerable.Range(0, count).Count(otherIndex =>
            string.Equals(
                ReferenceDisplayCandidate(definition, category, otherIndex),
                candidate,
                StringComparison.Ordinal)) > 1;
        return isAmbiguous ? $"{candidate} · #{index}" : candidate;
    }

    private static string ReferenceDisplayCandidate(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index)
    {
        if (category == StructuredDataTypeCategory.DataStruct &&
            IsRootStruct(definition, index))
        {
            return "Root";
        }

        return BestReferenceAlias(definition, category, index) ??
            ReferenceFallbackName(category, index);
    }

    private static string? BestReferenceAlias(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index) => definition.Structs
        .SelectMany(value => value.Properties)
        .Where(property => !string.IsNullOrWhiteSpace(property.Name))
        .Select(property => (
            property.Name,
            ArrayDepth: ReferenceArrayDepth(
                definition,
                property.Type,
                category,
                index,
                0)))
        .Where(candidate => candidate.ArrayDepth.HasValue)
        .Select(candidate => ToTypeIdentifier(
            candidate.Name!,
            candidate.ArrayDepth.GetValueOrDefault() > 0))
        .Where(value => value.Length != 0)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value.Length)
        .ThenBy(value => value, StringComparer.Ordinal)
        .FirstOrDefault();

    private static int? ReferenceArrayDepth(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value,
        StructuredDataTypeCategory category,
        int index,
        int depth)
    {
        if (value.Type == category && value.UnionValue == index)
            return depth;
        if (depth >= 8)
            return null;
        if (value.Type == StructuredDataTypeCategory.DataIndexedArray &&
            IsValidIndex(value.UnionValue, definition.IndexedArrays.Count))
        {
            return ReferenceArrayDepth(
                definition,
                definition.IndexedArrays[value.UnionValue].ElementType,
                category,
                index,
                depth + 1);
        }
        if (value.Type == StructuredDataTypeCategory.DataEnumArray &&
            IsValidIndex(value.UnionValue, definition.EnumedArrays.Count))
        {
            StructuredDataEnumedArrayDraft array =
                definition.EnumedArrays[value.UnionValue];
            if (category == StructuredDataTypeCategory.DataEnum &&
                array.EnumIndex == index)
            {
                return depth + 1;
            }

            return ReferenceArrayDepth(
                definition,
                array.ElementType,
                category,
                index,
                depth + 1);
        }
        return null;
    }

    private static string ReferenceFallbackName(
        StructuredDataTypeCategory category,
        int index) => category switch
    {
        StructuredDataTypeCategory.DataEnum => $"Enum {index}",
        StructuredDataTypeCategory.DataStruct => $"Struct {index}",
        StructuredDataTypeCategory.DataIndexedArray => $"Fixed array {index}",
        StructuredDataTypeCategory.DataEnumArray => $"Keyed array {index}",
        _ => $"{category} {index}"
    };

    private static string ScalarTypeName(
        StructuredDataTypeCategory category) => category switch
    {
        StructuredDataTypeCategory.DataInt => "int",
        StructuredDataTypeCategory.DataByte => "byte",
        StructuredDataTypeCategory.DataBool => "bool",
        StructuredDataTypeCategory.DataString => "string",
        StructuredDataTypeCategory.DataFloat => "float",
        StructuredDataTypeCategory.DataShort => "short",
        _ => category.ToString()
    };

    private static string FormatSemanticScalar(StructuredDataTypeDraft value)
    {
        string scalar = ScalarTypeName(value.Type);
        return value.Type == StructuredDataTypeCategory.DataString &&
            value.UnionValue > 0
                ? $"{scalar} ({value.UnionValue:N0})"
                : scalar;
    }

    private static string ToTypeIdentifier(string value, bool singularize)
    {
        var result = new StringBuilder(value.Length);
        bool capitalize = true;
        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                capitalize = true;
                continue;
            }
            result.Append(capitalize
                ? char.ToUpperInvariant(character)
                : character);
            capitalize = false;
        }

        string identifier = result.ToString();
        if (identifier.Length != 0 && !char.IsAsciiLetter(identifier[0]))
            identifier = $"Value{identifier}";
        if (!singularize)
            return identifier;
        if (string.Equals(identifier, "Lives", StringComparison.OrdinalIgnoreCase))
            return "Life";
        if (identifier.EndsWith("us", StringComparison.OrdinalIgnoreCase) ||
            identifier.EndsWith("is", StringComparison.OrdinalIgnoreCase) ||
            identifier.EndsWith("pos", StringComparison.OrdinalIgnoreCase) ||
            identifier.EndsWith("series", StringComparison.OrdinalIgnoreCase))
        {
            return identifier;
        }
        if (identifier.EndsWith("ies", StringComparison.OrdinalIgnoreCase) &&
            identifier.Length > 3)
        {
            return identifier[..^3] + "y";
        }
        if (identifier.EndsWith("classes", StringComparison.OrdinalIgnoreCase))
            return identifier[..^2];
        if ((identifier.EndsWith("ches", StringComparison.OrdinalIgnoreCase) ||
             identifier.EndsWith("shes", StringComparison.OrdinalIgnoreCase) ||
             identifier.EndsWith("xes", StringComparison.OrdinalIgnoreCase) ||
             identifier.EndsWith("zes", StringComparison.OrdinalIgnoreCase) ||
             identifier.EndsWith("ses", StringComparison.OrdinalIgnoreCase)) &&
            identifier.Length > 2)
        {
            return identifier[..^2];
        }
        if (identifier.EndsWith('s') &&
            !identifier.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            identifier.Length > 1)
        {
            return identifier[..^1];
        }
        return identifier;
    }

    private static bool IsValidReference(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index) => IsValidIndex(index, ReferenceTableCount(definition, category));

    private static int ReferenceTableCount(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category) => category switch
    {
        StructuredDataTypeCategory.DataEnum => definition.Enums.Count,
        StructuredDataTypeCategory.DataStruct => definition.Structs.Count,
        StructuredDataTypeCategory.DataIndexedArray => definition.IndexedArrays.Count,
        StructuredDataTypeCategory.DataEnumArray => definition.EnumedArrays.Count,
        _ => 0
    };

    private static bool IsValidIndex(int index, int count) =>
        index >= 0 && index < count;

    internal static bool IsRootStruct(
        StructuredDataDefinitionDraft definition,
        int index) =>
        IsValidIndex(index, definition.Structs.Count) &&
        definition.RootType.Type == StructuredDataTypeCategory.DataStruct &&
        definition.RootType.UnionValue == index;

    internal static string FormatReferenceLabel(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index,
        string noun)
    {
        string identity = $"{noun} #{index}";
        if (!IsValidReference(definition, category, index))
            return identity;
        if (category == StructuredDataTypeCategory.DataStruct &&
            IsRootStruct(definition, index))
        {
            return $"{identity} · Root";
        }

        string? alias = BestReferenceAlias(definition, category, index);
        return alias is null
            ? identity
            : $"{identity} · inferred as {alias}";
    }

    internal static string FormatReferenceDescription(
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeCategory category,
        int index,
        string identity)
    {
        if (category == StructuredDataTypeCategory.DataStruct &&
            IsRootStruct(definition, index))
        {
            return $"Root schema type. Serialized identity remains {identity}.";
        }

        return BestReferenceAlias(definition, category, index) is null
            ? $"No friendly name is inferred. Serialized identity remains {identity}."
            : $"Display name is inferred from schema usage. Serialized identity remains {identity}.";
    }

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
        return IsIndexedReference(value.Type)
            ? FormatReferenceLabel(
                definition,
                value.Type,
                value.UnionValue,
                category)
            : value.UnionValue == 0
                ? category
                : $"{category} · raw {value.UnionValue}";
    }

    private static string FormatDisplayName(string value)
    {
        int separatorIndex = Math.Max(
            value.LastIndexOf('/'),
            value.LastIndexOf('\\'));
        string fileName = separatorIndex >= 0
            ? value[(separatorIndex + 1)..]
            : value;
        int extensionIndex = fileName.LastIndexOf('.');
        string stem = extensionIndex > 0
            ? fileName[..extensionIndex]
            : fileName;

        return stem.ToLowerInvariant() switch
        {
            "defaultstructureddata" => "Default Structured Data",
            "playerdata" => "Player Data",
            "prestigedata" => "Prestige Data",
            "clientmatchdata" => "Client Match Data",
            "matchdata" => "Match Data",
            "playerconstantdata" => "Player Constant Data",
            "playerprogressdata" => "Player Progress Data",
            _ => FormatFallbackDisplayName(stem)
        };
    }

    private static string FormatFallbackDisplayName(string value)
    {
        string words = MaterialTechsetViewerViewModel.FormatIdentifier(
            value.Replace('_', ' ').Replace('-', ' '));
        return words.Length == 0
            ? "Structured Data"
            : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private string FormatSelectionBreadcrumb()
    {
        if (SelectedNavigationNode is not { } node)
            return "Schema";

        StructuredDataSelection selection = node.Selection;
        string path = selection.Kind switch
        {
            StructuredDataSelectionKind.Definition =>
                $"Definition {selection.DefinitionIndex}",
            StructuredDataSelectionKind.RootType => "Root",
            StructuredDataSelectionKind.Structs => "Structs",
            StructuredDataSelectionKind.Struct => $"Structs / {node.Title}",
            StructuredDataSelectionKind.Enums => "Enums",
            StructuredDataSelectionKind.Enum => $"Enums / {node.Title}",
            StructuredDataSelectionKind.IndexedArrays => "Fixed arrays",
            StructuredDataSelectionKind.IndexedArray =>
                $"Fixed arrays / {node.Title}",
            StructuredDataSelectionKind.EnumedArrays => "Keyed arrays",
            StructuredDataSelectionKind.EnumedArray =>
                $"Keyed arrays / {node.Title}",
            _ => node.Title
        };

        if (_workingDraft.Definitions.Count > 1 &&
            selection.Kind != StructuredDataSelectionKind.Definition)
        {
            path = $"Definition {selection.DefinitionIndex} / {path}";
        }

        if (SelectedMember is { } member &&
            !string.Equals(
                member.PrimaryText,
                node.Title,
                StringComparison.Ordinal))
        {
            path += $" / {member.PrimaryText}";
        }

        return path;
    }

    private static string FormatSelectionKind(
        StructuredDataSelectionKind? kind) => kind switch
    {
        StructuredDataSelectionKind.Definition => "DEFINITION",
        StructuredDataSelectionKind.RootType => "ROOT",
        StructuredDataSelectionKind.Enums or
        StructuredDataSelectionKind.Structs or
        StructuredDataSelectionKind.IndexedArrays or
        StructuredDataSelectionKind.EnumedArrays => "GROUP",
        StructuredDataSelectionKind.Enum => "ENUM",
        StructuredDataSelectionKind.EnumEntry => "ENUM VALUE",
        StructuredDataSelectionKind.Struct => "STRUCT",
        StructuredDataSelectionKind.StructProperty => "FIELD",
        StructuredDataSelectionKind.IndexedArray => "FIXED ARRAY",
        StructuredDataSelectionKind.EnumedArray => "KEYED ARRAY",
        _ => "SCHEMA"
    };

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

    private StructuredDataNavigationNodeViewModel? FindInitialNavigationNode()
    {
        StructuredDataNavigationNodeViewModel? root = FindNavigationNode(
            new StructuredDataSelection(
                StructuredDataSelectionKind.RootType,
                0));
        if (root is not null)
            return root;

        return NavigationRoots.FirstOrDefault();
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
        OnPropertyChanged(nameof(SelectionBreadcrumb));
        OnPropertyChanged(nameof(SelectionKindText));
        OnPropertyChanged(nameof(SchemaFirstColumnTitle));
        OnPropertyChanged(nameof(SchemaSecondColumnTitle));
        OnPropertyChanged(nameof(SchemaThirdColumnTitle));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void NotifyState()
    {
        RebuildDiagnostics();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SummaryText));
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

    private static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;
}
