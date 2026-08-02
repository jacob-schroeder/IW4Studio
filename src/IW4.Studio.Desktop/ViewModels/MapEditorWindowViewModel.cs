using IW4.Studio.MapEditor;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.Import;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.Rendering.WorldViewport;

namespace IW4.Studio.Desktop.ViewModels;

public sealed record MapEditorObjectKindFilter(
    string Label,
    MapObjectKind? Kind);

public sealed record MapEditorSourceBindingRow(
    string Asset,
    string FieldPath,
    string OwnerRow,
    string Provenance,
    string BaselineDigest);

public sealed record MapEditorPendingEditRow(
    string Description,
    string Direction,
    string Classification,
    string SaveBlocker);

public enum MapEditorSidePane
{
    None,
    ObjectBrowser,
    CollisionBrowser,
    CompiledData
}

public enum MapEditorWorkspace
{
    Scene,
    Collision
}

public enum MapEditorViewportInteractionMode
{
    Select,
    Translate
}

public sealed class MapEditorWindowViewModel : ObservableObject, IDisposable
{
    private readonly ExistingMapImportResult _session;
    private readonly EditorMapDocument _document;
    private readonly StaticModelCorrespondenceCatalog
        _staticModelCorrespondenceCatalog;
    private readonly IReadOnlyDictionary<
        IW4.Studio.MapEditor.Editing.Identity.SourceBindingId,
        CompiledSourceBinding> _bindings;
    private readonly MapEditorEditingContext _editingContext;
    private readonly bool _ownsEditingContext;
    private readonly bool _ownsLivePreview;
    private string _searchText = string.Empty;
    private MapEditorObjectKindFilter _selectedKindFilter;
    private IReadOnlyList<EditorMapObject> _visibleObjects;
    private EditorMapObject? _selectedObject;
    private PrimaryLightEditorViewModel? _primaryLightEditor;
    private FxGlassDefinitionEditorViewModel?
        _fxGlassDefinitionEditor;
    private StaticModelTransformEditorViewModel?
        _staticModelTransformEditor;
    private AuthoredCollisionTransformEditorViewModel?
        _authoredCollisionTransformEditor;
    private StaticModelSuppressionEditorViewModel?
        _staticModelSuppressionEditor;
    private StaticModelRemovalEditorViewModel?
        _staticModelRemovalEditor;
    private StaticModelDuplicationEditorViewModel?
        _staticModelDuplicationEditor;
    private MapEntityInspectorViewModel? _entityInspector;
    private MapEditorCollisionInspectorViewModel? _collisionInspector;
    private MapEditorSidePane _activeSidePane =
        MapEditorSidePane.ObjectBrowser;
    private MapEditorWorkspace _activeWorkspace =
        MapEditorWorkspace.Scene;
    private MapEditorViewportInteractionMode _viewportInteractionMode =
        MapEditorViewportInteractionMode.Select;
    private bool _isCollisionOverlayVisible;
    private bool _isCollisionIsolateActive;
    private bool _synchronizingSelection;
    private bool _disposed;

    public MapEditorWindowViewModel(ExistingMapImportResult session)
        : this(
            session,
            session?.Audit.Diagnostics,
            livePreview: null,
            editingContext: null)
    {
    }

    public MapEditorWindowViewModel(
        ExistingMapImportResult session,
        MapEditorEditingContext editingContext)
        : this(
            session,
            session?.Audit.Diagnostics,
            livePreview: null,
            editingContext)
    {
    }

    public MapEditorWindowViewModel(MapEditorOpenResult result)
        : this(
            result?.Session ??
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result)),
            result.Diagnostics,
            livePreview: null,
            editingContext: null)
    {
        if (!result.Succeeded)
        {
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        }
    }

    public MapEditorWindowViewModel(
        MapEditorOpenResult result,
        MapEditorLivePreviewBridge livePreview)
        : this(
            result?.Session ??
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result)),
            result.Diagnostics,
            livePreview,
            editingContext: null)
    {
        if (!result.Succeeded)
        {
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        }
    }

    public MapEditorWindowViewModel(
        MapEditorOpenResult result,
        MapEditorLivePreviewBridge livePreview,
        MapEditorEditingContext editingContext)
        : this(
            result?.Session ??
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result)),
            result.Diagnostics,
            livePreview,
            editingContext)
    {
        if (!result.Succeeded)
        {
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        }
    }

    private MapEditorWindowViewModel(
        ExistingMapImportResult session,
        IEnumerable<string>? diagnostics,
        MapEditorLivePreviewBridge? livePreview,
        MapEditorEditingContext? editingContext)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = session.Document;
        _staticModelCorrespondenceCatalog =
            session.StaticModelCorrespondences;
        _bindings = session.SourceBindings.ToDictionary(value => value.Id);
        if (editingContext is not null &&
            !ReferenceEquals(editingContext.Document, _document))
        {
            throw new ArgumentException(
                "The editing context belongs to another map document.",
                nameof(editingContext));
        }
        if (livePreview is not null &&
            !ReferenceEquals(livePreview.Document, _document))
        {
            throw new ArgumentException(
                "The Live Preview bridge belongs to another map document.",
                nameof(livePreview));
        }

        _ownsEditingContext = editingContext is null;
        _editingContext = editingContext ??
            new MapEditorEditingContext(_document);
        _ownsLivePreview = livePreview is null;
        LivePreview = livePreview ??
            new MapEditorLivePreviewBridge(_document);
        CollisionBrowser = new MapEditorCollisionBrowserViewModel(
            _document,
            value => SelectedObject = value);
        CollisionBoxAuthoring = new CollisionBoxAuthoringViewModel(
            _document,
            CollisionAuthoringMaterialCatalog.Create(_session.Bundle),
            ResolveSelectedBounds,
            () => AreEditorsEnabled,
            ExecuteCommand,
            SelectObject);
        ScriptOriginCardinalityEditor =
            new ScriptOriginCardinalityEditorViewModel(
                _document,
                () => AreEditorsEnabled,
                ExecuteCommand,
                Undo,
                SelectObject);

        UndoCommand = new ViewModelCommand(
            Undo,
            () => CanUndo);
        RedoCommand = new ViewModelCommand(
            Redo,
            () => CanRedo);
        ToggleObjectsPaneCommand = new ViewModelCommand(
            ToggleObjectsPane);
        ToggleCollisionPaneCommand = new ViewModelCommand(
            ToggleCollisionPane);
        ToggleCompiledDataPaneCommand = new ViewModelCommand(
            ToggleCompiledDataPane);
        ActivateSelectModeCommand = new ViewModelCommand(
            ActivateSelectMode);
        ActivateTranslateModeCommand = new ViewModelCommand(
            ActivateTranslateMode,
            () => CanActivateTranslateMode);
        CloseInspectorCommand = new ViewModelCommand(
            () => SelectedObject = null,
            () => HasContextualInspector);
        KindFilters = Array.AsReadOnly(
        [
            new MapEditorObjectKindFilter("All scene objects", null),
            .. Enum.GetValues<MapObjectKind>()
                .Where(IsSceneObjectKind)
                .Select(kind => new MapEditorObjectKindFilter(
                    SplitWords(kind.ToString()),
                    kind))
        ]);
        _selectedKindFilter = KindFilters[0];
        _visibleObjects = _document.Objects
            .Where(IsSceneObject)
            .ToArray();
        SelectedObject =
            _document.PrimaryLights.FirstOrDefault(
                value => value.SourceOrdinal.Value > 0)
            ?? _document.PrimaryLights.FirstOrDefault()
            ?? _document.Objects.FirstOrDefault(IsSceneObject)
            ?? _document.Objects.FirstOrDefault();

        SemanticCounts = session.Audit.SemanticCounts;
        ProvenanceCounts = session.Audit.ProvenanceCounts;
        UnresolvedJoins = session.Audit.UnresolvedCrossAssetJoins;
        Diagnostics = Array.AsReadOnly(
            (diagnostics ?? session.Audit.Diagnostics)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        EnvironmentValues = session.Document.Environment.Values;
        Assets = session.Bundle.Assets;
        _document.Changed += Document_Changed;
        _editingContext.StateChanged += EditingContext_StateChanged;
        LivePreview.SelectionChanged += LivePreview_SelectionChanged;
    }

    public string MapIdentity => _session.Bundle.MapIdentity;
    public string DocumentId => _session.Document.Id.ToString();
    public string BaselineDigest => _session.Bundle.BaselineDigest;
    public string RevisionText => $"Revision {_document.Revision}";
    public string SafetyText =>
        "Semantic command document · loaded assets stay immutable · Save As validates every pending edit";
    public string DirtyStateText =>
        _document.RequiresReopen
            ? "REOPEN SAVED OUTPUT"
            : _editingContext.IsCompiledSaveInProgress
                ? "SAVING COMPILED MAP"
                : IsDirty
                ? "UNSAVED CHANGES"
                : "IMPORTED BASELINE";
    public string LiveProjectionText =>
        $"LIVE PROJECTION R{LivePreview.CurrentProjection.Revision}";
    public bool IsDirty =>
        _editingContext.HasUnsavedChanges;
    public bool CanUndo =>
        AreEditorsEnabled &&
        !HasTransformDraft &&
        _document.History.CanUndo;
    public bool CanRedo =>
        AreEditorsEnabled &&
        !HasTransformDraft &&
        _document.History.CanRedo;
    public bool AreEditorsEnabled =>
        _editingContext.AreMutationsAllowed;
    public bool IsCompiledSaveInProgress =>
        _editingContext.IsCompiledSaveInProgress;
    public string EditorAvailabilityText =>
        _document.RequiresReopen
            ? "Reopen the saved output before further editing."
            : IsCompiledSaveInProgress
                ? "Editing and undo/redo are locked while Save As validates the compiled output."
                : "Edits remain available for semantic preview and undo/redo.";
    public bool HasPropertyDrafts =>
        _editingContext.HasPropertyDrafts;
    public bool HasTransformDraft =>
        _editingContext.HasTranslationDraft;
    public int PropertyDraftCount =>
        _editingContext.PropertyDraftCount;
    public string PendingEditCountText =>
        $"{_editingContext.UnsavedChangeCount:N0} pending edit" +
        (_editingContext.UnsavedChangeCount == 1
            ? string.Empty
            : "s");
    public ViewModelCommand UndoCommand { get; }
    public ViewModelCommand RedoCommand { get; }
    public ViewModelCommand ToggleObjectsPaneCommand { get; }
    public ViewModelCommand ToggleCollisionPaneCommand { get; }
    public ViewModelCommand ToggleCompiledDataPaneCommand { get; }
    public ViewModelCommand ActivateSelectModeCommand { get; }
    public ViewModelCommand ActivateTranslateModeCommand { get; }
    public ViewModelCommand CloseInspectorCommand { get; }
    public ScriptOriginCardinalityEditorViewModel
        ScriptOriginCardinalityEditor { get; }
    public MapEditorCollisionBrowserViewModel CollisionBrowser { get; }
    public CollisionBoxAuthoringViewModel CollisionBoxAuthoring { get; }
    public MapEditorLivePreviewBridge LivePreview { get; }
    public MapEditorEditingContext EditingContext => _editingContext;
    public IReadOnlyList<MapEditorObjectKindFilter> KindFilters { get; }
    public IReadOnlyList<MapImportCount> SemanticCounts { get; }
    public IReadOnlyList<MapProvenanceCount> ProvenanceCounts { get; }
    public IReadOnlyList<string> UnresolvedJoins { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public IReadOnlyList<EditorEnvironmentValue> EnvironmentValues { get; }
    public IReadOnlyList<CompiledMapAssetDescriptor> Assets { get; }

    public MapEditorSidePane ActiveSidePane
    {
        get => _activeSidePane;
        private set
        {
            if (!SetProperty(ref _activeSidePane, value))
                return;

            OnPropertyChanged(nameof(IsObjectsPaneOpen));
            OnPropertyChanged(nameof(IsCollisionPaneOpen));
            OnPropertyChanged(nameof(IsCompiledDataPaneOpen));
        }
    }

    public bool IsObjectsPaneOpen =>
        ActiveSidePane == MapEditorSidePane.ObjectBrowser;

    public bool IsCollisionPaneOpen =>
        ActiveSidePane == MapEditorSidePane.CollisionBrowser;

    public bool IsCompiledDataPaneOpen =>
        ActiveSidePane == MapEditorSidePane.CompiledData;

    public MapEditorWorkspace ActiveWorkspace
    {
        get => _activeWorkspace;
        private set
        {
            if (!SetProperty(ref _activeWorkspace, value))
                return;

            OnPropertyChanged(nameof(IsSceneWorkspaceActive));
            OnPropertyChanged(nameof(IsCollisionWorkspaceActive));
            OnPropertyChanged(nameof(IsCollisionPickingActive));
        }
    }

    public bool IsSceneWorkspaceActive =>
        ActiveWorkspace == MapEditorWorkspace.Scene;

    public bool IsCollisionWorkspaceActive =>
        ActiveWorkspace == MapEditorWorkspace.Collision;

    public bool IsCollisionPickingActive =>
        IsCollisionWorkspaceActive &&
        IsCollisionOverlayVisible;

    /// <summary>
    /// Viewport-layer state. This is intentionally independent from the
    /// active workspace and interaction tool, and never enters semantic
    /// history.
    /// </summary>
    public bool IsCollisionOverlayVisible
    {
        get => _isCollisionOverlayVisible;
        set
        {
            if (!SetProperty(ref _isCollisionOverlayVisible, value))
                return;

            if (!value && IsCollisionIsolateActive)
                IsCollisionIsolateActive = false;
            OnPropertyChanged(nameof(CollisionDisplayStatusText));
            OnPropertyChanged(nameof(IsCollisionPickingActive));
        }
    }

    /// <summary>
    /// Isolates collision from rendered scene layers without changing map
    /// semantics. Isolation implies that the collision overlay is visible.
    /// </summary>
    public bool IsCollisionIsolateActive
    {
        get => _isCollisionIsolateActive;
        set
        {
            if (!SetProperty(ref _isCollisionIsolateActive, value))
                return;

            if (value && !IsCollisionOverlayVisible)
                IsCollisionOverlayVisible = true;
            OnPropertyChanged(nameof(CollisionDisplayStatusText));
        }
    }

    public string CollisionDisplayStatusText =>
        IsCollisionIsolateActive
            ? "COLLISION ISOLATED"
            : IsCollisionOverlayVisible
                ? "COLLISION OVERLAY"
                : "COLLISION HIDDEN";

    public MapEditorViewportInteractionMode ViewportInteractionMode
    {
        get => _viewportInteractionMode;
        private set
        {
            if (!SetProperty(ref _viewportInteractionMode, value))
                return;

            OnPropertyChanged(nameof(IsSelectModeActive));
            OnPropertyChanged(nameof(IsTranslateModeActive));
            OnPropertyChanged(nameof(ViewportInteractionHintText));
        }
    }

    public bool IsSelectModeActive =>
        ViewportInteractionMode ==
        MapEditorViewportInteractionMode.Select;

    public bool IsTranslateModeActive =>
        ViewportInteractionMode ==
        MapEditorViewportInteractionMode.Translate;

    public string ViewportInteractionHintText =>
        IsTranslateModeActive
            ? "Left-drag move · X/Y/Z constrain · Ctrl snap · Enter apply · Esc cancel"
            : "WASD navigate · right-drag look · left-click select · F frame";

    public bool CanActivateTranslateMode =>
        ActiveTranslationTool?.CanManipulate == true;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                RefreshObjects();
        }
    }

    public MapEditorObjectKindFilter SelectedKindFilter
    {
        get => _selectedKindFilter;
        set
        {
            if (value is not null &&
                SetProperty(ref _selectedKindFilter, value))
            {
                RefreshObjects();
            }
        }
    }

    public IReadOnlyList<EditorMapObject> VisibleObjects
    {
        get => _visibleObjects;
        private set
        {
            if (SetProperty(ref _visibleObjects, value))
                OnPropertyChanged(nameof(ObjectResultText));
        }
    }

    public EditorMapObject? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (ReferenceEquals(_selectedObject, value))
                return;

            StaticModelTransformEditor?.Dispose();
            AuthoredCollisionTransformEditor?.Dispose();
            if (!SetProperty(ref _selectedObject, value))
                return;

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            OnPropertyChanged(nameof(HasContextualInspector));
            OnPropertyChanged(nameof(HasWorldSurfaceInspector));
            OnPropertyChanged(nameof(HasStaticModelInspector));
            OnPropertyChanged(nameof(HasCollisionInspector));
            OnPropertyChanged(
                nameof(ShowsSelectionVisibilityControl));
            OnPropertyChanged(nameof(SelectedProperties));
            OnPropertyChanged(nameof(SelectedBindings));
            OnPropertyChanged(nameof(SelectedObjectId));
            OnPropertyChanged(nameof(IsSelectedObjectVisible));
            PrimaryLightEditor = value is EditorPrimaryLight light
                ? new PrimaryLightEditorViewModel(
                    light,
                    ExecuteCommand)
                : null;
            FxGlassDefinitionEditor =
                value is EditorGlassObject
                {
                    Representation: GlassRepresentation.FxDefinition
                } glassDefinition &&
                glassDefinition.HalfThickness.Value is not null &&
                glassDefinition.Color.Value is not null
                    ? new FxGlassDefinitionEditorViewModel(
                        _document,
                        glassDefinition,
                        ExecuteCommand)
                    : null;
            StaticModelTransformEditor =
                value is EditorStaticModel
                {
                    Representation: StaticModelRepresentation.Render,
                    IsImported: true
                } staticModel
                    ? new StaticModelTransformEditorViewModel(
                        _document,
                        _session.Bundle,
                        staticModel,
                        _staticModelCorrespondenceCatalog,
                        _editingContext,
                        () => AreEditorsEnabled,
                        ExecuteCommand)
                    : null;
            AuthoredCollisionTransformEditor =
                value is EditorAuthoredCollisionObject authoredCollision
                    ? new AuthoredCollisionTransformEditorViewModel(
                        _document,
                        authoredCollision,
                        _editingContext,
                        () => AreEditorsEnabled,
                        ExecuteCommand)
                    : null;
            StaticModelSuppressionEditor =
                value is EditorStaticModel
                {
                    Representation: StaticModelRepresentation.Render,
                    IsImported: true
                } suppressionModel
                    ? new StaticModelSuppressionEditorViewModel(
                        _document,
                        suppressionModel,
                        _staticModelCorrespondenceCatalog,
                        () => AreEditorsEnabled,
                        ExecuteCommand)
                    : null;
            StaticModelRemovalEditor =
                value is EditorStaticModel
                {
                    Representation: StaticModelRepresentation.Render,
                    IsImported: true
                } removalModel
                    ? new StaticModelRemovalEditorViewModel(
                        _document,
                        _session.Bundle,
                        removalModel,
                        _staticModelCorrespondenceCatalog,
                        () => AreEditorsEnabled,
                        ExecuteCommand)
                    : null;
            StaticModelDuplicationEditor =
                value is EditorStaticModel
                {
                    Representation: StaticModelRepresentation.Render,
                    IsImported: true
                } duplicationTemplate
                    ? new StaticModelDuplicationEditorViewModel(
                        _document,
                        _session.Bundle,
                        duplicationTemplate,
                        _staticModelCorrespondenceCatalog,
                        () => AreEditorsEnabled,
                        ExecuteCommand,
                        SelectObject)
                    : null;
            EntityInspector = value is EditorEntity entity
                ? new MapEntityInspectorViewModel(
                    _document,
                    entity,
                    _editingContext,
                    ExecuteCommand)
                    : null;
            CollisionInspector =
                value is not null &&
                MapEditorCollisionBrowserViewModel.IsCollisionObject(value)
                    ? new MapEditorCollisionInspectorViewModel(
                        value,
                        _staticModelCorrespondenceCatalog,
                        _staticModelCorrespondenceCatalog
                            .CollisionAssetKind)
                    : null;
            if (ActiveTranslationTool is null &&
                IsTranslateModeActive)
            {
                ViewportInteractionMode =
                    MapEditorViewportInteractionMode.Select;
            }
            OnPropertyChanged(nameof(CanActivateTranslateMode));
            ActivateTranslateModeCommand.RaiseCanExecuteChanged();
            CloseInspectorCommand.RaiseCanExecuteChanged();
            CollisionBrowser.SynchronizeSelection(value);
            CollisionBoxAuthoring.Refresh();
            if (!_synchronizingSelection)
                LivePreview.SetSelection(value?.Id);
        }
    }

    public bool HasSelection => SelectedObject is not null;
    public bool HasNoSelection => !HasSelection;
    public bool HasContextualInspector =>
        HasWorldSurfaceInspector ||
        HasStaticModelInspector ||
        HasCollisionInspector;
    public bool HasWorldSurfaceInspector =>
        SelectedObject is EditorWorldSurface;
    public bool HasStaticModelInspector =>
        SelectedObject is EditorStaticModel
        {
            Representation: StaticModelRepresentation.Render
        };
    public bool HasCollisionInspector =>
        CollisionInspector is not null;
    public bool ShowsSelectionVisibilityControl =>
        !HasCollisionInspector;
    public string SelectedObjectId => SelectedObject?.Id.ToString() ?? string.Empty;
    public IReadOnlyList<EditorObjectProperty> SelectedProperties =>
        SelectedObject?.Properties ?? [];
    public bool IsSelectedObjectVisible
    {
        get => SelectedObject?.IsVisible == true;
        set
        {
            if (SelectedObject is not { } selected ||
                selected.IsVisible == value)
            {
                return;
            }

            ExecuteCommand(
                new SetEditorObjectVisibilityCommand(
                    selected.Id,
                    value
                        ? EditorObjectVisibility.Visible
                        : EditorObjectVisibility.Hidden));
        }
    }

    public PrimaryLightEditorViewModel? PrimaryLightEditor
    {
        get => _primaryLightEditor;
        private set
        {
            if (SetProperty(ref _primaryLightEditor, value))
            {
                OnPropertyChanged(nameof(HasPrimaryLightEditor));
                OnPropertyChanged(nameof(HasNoPrimaryLightEditor));
            }
        }
    }

    public bool HasPrimaryLightEditor => PrimaryLightEditor is not null;
    public bool HasNoPrimaryLightEditor => !HasPrimaryLightEditor;

    public FxGlassDefinitionEditorViewModel?
        FxGlassDefinitionEditor
    {
        get => _fxGlassDefinitionEditor;
        private set
        {
            if (SetProperty(
                    ref _fxGlassDefinitionEditor,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasFxGlassDefinitionEditor));
            }
        }
    }

    public bool HasFxGlassDefinitionEditor =>
        FxGlassDefinitionEditor is not null;

    public StaticModelTransformEditorViewModel? StaticModelTransformEditor
    {
        get => _staticModelTransformEditor;
        private set
        {
            if (SetProperty(ref _staticModelTransformEditor, value))
            {
                OnPropertyChanged(
                    nameof(HasStaticModelTransformEditor));
                OnPropertyChanged(nameof(ActiveTranslationTool));
            }
        }
    }

    public bool HasStaticModelTransformEditor =>
        StaticModelTransformEditor is not null;

    public AuthoredCollisionTransformEditorViewModel?
        AuthoredCollisionTransformEditor
    {
        get => _authoredCollisionTransformEditor;
        private set
        {
            if (SetProperty(
                    ref _authoredCollisionTransformEditor,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasAuthoredCollisionTransformEditor));
                OnPropertyChanged(nameof(ActiveTranslationTool));
            }
        }
    }

    public bool HasAuthoredCollisionTransformEditor =>
        AuthoredCollisionTransformEditor is not null;

    public IWorldViewportTranslationTool? ActiveTranslationTool =>
        (IWorldViewportTranslationTool?)StaticModelTransformEditor ??
        AuthoredCollisionTransformEditor;

    public StaticModelSuppressionEditorViewModel?
        StaticModelSuppressionEditor
    {
        get => _staticModelSuppressionEditor;
        private set
        {
            if (SetProperty(
                    ref _staticModelSuppressionEditor,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasStaticModelSuppressionEditor));
            }
        }
    }

    public bool HasStaticModelSuppressionEditor =>
        StaticModelSuppressionEditor is not null;

    public StaticModelRemovalEditorViewModel?
        StaticModelRemovalEditor
    {
        get => _staticModelRemovalEditor;
        private set
        {
            if (SetProperty(
                    ref _staticModelRemovalEditor,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasStaticModelRemovalEditor));
            }
        }
    }

    public bool HasStaticModelRemovalEditor =>
        StaticModelRemovalEditor is not null;

    public StaticModelDuplicationEditorViewModel?
        StaticModelDuplicationEditor
    {
        get => _staticModelDuplicationEditor;
        private set
        {
            if (SetProperty(
                    ref _staticModelDuplicationEditor,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasStaticModelDuplicationEditor));
            }
        }
    }

    public bool HasStaticModelDuplicationEditor =>
        StaticModelDuplicationEditor is not null;

    public MapEntityInspectorViewModel? EntityInspector
    {
        get => _entityInspector;
        private set
        {
            if (SetProperty(ref _entityInspector, value))
                OnPropertyChanged(nameof(HasEntityInspector));
        }
    }

    public bool HasEntityInspector => EntityInspector is not null;

    public MapEditorCollisionInspectorViewModel? CollisionInspector
    {
        get => _collisionInspector;
        private set
        {
            if (SetProperty(ref _collisionInspector, value))
            {
                OnPropertyChanged(nameof(HasCollisionInspector));
                OnPropertyChanged(nameof(HasContextualInspector));
                OnPropertyChanged(
                    nameof(ShowsSelectionVisibilityControl));
            }
        }
    }

    public IReadOnlyList<MapEditorPendingEditRow> PendingEdits =>
        _document.History.PendingJournal
            .Select(entry => new MapEditorPendingEditRow(
                entry.Command.Description,
                entry.Direction == MapCommandJournalDirection.Apply
                    ? "Apply"
                    : "Revert",
                SplitWords(entry.Command.Impact.Classification.ToString()),
                entry.Command.Impact.SaveBlocker ?? "No save blocker"))
            .ToArray();

    public IReadOnlyList<MapEditorSourceBindingRow> SelectedBindings =>
        SelectedObject is null
            ? []
            : SelectedObject.SourceBindings
                .Select(id => _bindings.TryGetValue(
                    id,
                    out CompiledSourceBinding? binding)
                        ? new MapEditorSourceBindingRow(
                            binding.AssetType.ToString(),
                            binding.FieldPath,
                            $"Target row #{binding.OwnerRow.SerializedIndex}",
                            binding.Provenance.ToString(),
                            binding.BaselineDigest)
                        : CreateUncataloguedBindingRow(id))
                .ToArray();

    public string ObjectResultText =>
        $"{VisibleObjects.Count:N0} of " +
        $"{_session.Document.Objects.Count(IsSceneObject):N0} scene objects";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StaticModelTransformEditor?.Dispose();
        AuthoredCollisionTransformEditor?.Dispose();
        _document.Changed -= Document_Changed;
        _editingContext.StateChanged -= EditingContext_StateChanged;
        LivePreview.SelectionChanged -= LivePreview_SelectionChanged;
        if (_ownsEditingContext)
            _editingContext.Dispose();
        if (_ownsLivePreview)
            LivePreview.Dispose();
    }

    private void Document_Changed(
        object? sender,
        MapDocumentChangedEventArgs e)
    {
        if (_disposed)
            return;

        bool changesEntityCardinality =
            e.Command?.Kind == MapEditKind.MapEntityCardinality;
        bool changesStaticModelCardinality =
            e.Command?.Kind == MapEditKind.StaticModelDuplication;
        bool changesCollisionCardinality =
            e.Command?.Kind == MapEditKind.CollisionCardinality;
        if (changesEntityCardinality ||
            changesStaticModelCardinality ||
            changesCollisionCardinality)
        {
            RefreshObjects();
            CollisionBrowser.Refresh();
        }

        PrimaryLightEditor?.Refresh();
        FxGlassDefinitionEditor?.Refresh();
        StaticModelTransformEditor?.Refresh();
        AuthoredCollisionTransformEditor?.Refresh();
        StaticModelSuppressionEditor?.Refresh();
        StaticModelRemovalEditor?.Refresh();
        StaticModelDuplicationEditor?.Refresh();
        EntityInspector?.Refresh();
        CollisionInspector?.Refresh();
        CollisionBoxAuthoring.Refresh();
        ScriptOriginCardinalityEditor.Refresh();
        if (e.Command?.Kind is
            MapEditKind.MapEntityKeyValue or
            MapEditKind.MapEntityCardinality)
        {
            OnPropertyChanged(nameof(SelectedObject));
            if (!changesEntityCardinality)
                RefreshObjects();
        }
        OnPropertyChanged(nameof(SelectedProperties));
        OnPropertyChanged(nameof(IsSelectedObjectVisible));
        OnPropertyChanged(nameof(RevisionText));
        OnPropertyChanged(nameof(LiveProjectionText));
        OnPropertyChanged(nameof(DirtyStateText));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(AreEditorsEnabled));
        OnPropertyChanged(nameof(EditorAvailabilityText));
        OnPropertyChanged(nameof(CanActivateTranslateMode));
        OnPropertyChanged(nameof(PendingEdits));
        OnPropertyChanged(nameof(PendingEditCountText));
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        ActivateTranslateModeCommand.RaiseCanExecuteChanged();
    }

    private void EditingContext_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (_disposed)
            return;

        if (SelectedObject is not { } selected ||
            (_document.TryGetObject(
                 selected.Id,
                 out EditorMapObject? owned) &&
             ReferenceEquals(selected, owned)))
        {
            EntityInspector?.Refresh();
        }
        StaticModelTransformEditor?.Refresh();
        AuthoredCollisionTransformEditor?.Refresh();
        StaticModelSuppressionEditor?.Refresh();
        StaticModelRemovalEditor?.Refresh();
        StaticModelDuplicationEditor?.Refresh();
        ScriptOriginCardinalityEditor.Refresh();
        CollisionBoxAuthoring.Refresh();
        OnPropertyChanged(nameof(DirtyStateText));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(AreEditorsEnabled));
        OnPropertyChanged(nameof(IsCompiledSaveInProgress));
        OnPropertyChanged(nameof(EditorAvailabilityText));
        OnPropertyChanged(nameof(HasPropertyDrafts));
        OnPropertyChanged(nameof(PropertyDraftCount));
        OnPropertyChanged(nameof(HasTransformDraft));
        OnPropertyChanged(nameof(CanActivateTranslateMode));
        OnPropertyChanged(nameof(PendingEditCountText));
        if (!CanActivateTranslateMode && IsTranslateModeActive)
        {
            ViewportInteractionMode =
                MapEditorViewportInteractionMode.Select;
        }
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        ActivateTranslateModeCommand.RaiseCanExecuteChanged();
    }

    private void LivePreview_SelectionChanged(
        object? sender,
        MapEditorLivePreviewSelectionChangedEventArgs e)
    {
        if (_disposed)
            return;

        EditorMapObject? selected = null;
        if (e.Selection is { } objectId &&
            !_document.TryGetObject(objectId, out selected))
        {
            return;
        }

        _synchronizingSelection = true;
        try
        {
            SelectedObject = selected;
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void RefreshObjects()
    {
        IEnumerable<EditorMapObject> query =
            _session.Document.Objects.Where(IsSceneObject);
        if (SelectedKindFilter.Kind is { } kind)
            query = query.Where(value => value.Kind == kind);
        string search = SearchText.Trim();
        if (search.Length != 0)
        {
            query = query.Where(value =>
                value.DisplayName.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ||
                value.Id.ToString().Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase));
        }

        VisibleObjects = query.ToArray();
        if (SelectedObject is not null &&
            !VisibleObjects.Contains(SelectedObject))
        {
            SelectedObject = VisibleObjects.FirstOrDefault();
        }
    }

    private void ToggleObjectsPane()
    {
        ActiveWorkspace = MapEditorWorkspace.Scene;
        ActiveSidePane = IsObjectsPaneOpen
            ? MapEditorSidePane.None
            : MapEditorSidePane.ObjectBrowser;
        OnPropertyChanged(nameof(IsSceneWorkspaceActive));
    }

    private void ToggleCollisionPane()
    {
        bool enteringCollision =
            !IsCollisionWorkspaceActive;
        ActiveWorkspace = MapEditorWorkspace.Collision;
        if (enteringCollision)
            IsCollisionOverlayVisible = true;
        CollisionBrowser.Activate();
        ActiveSidePane = IsCollisionPaneOpen
            ? MapEditorSidePane.None
            : MapEditorSidePane.CollisionBrowser;
        OnPropertyChanged(nameof(IsCollisionWorkspaceActive));
    }

    private void ToggleCompiledDataPane() =>
        ActiveSidePane = IsCompiledDataPaneOpen
            ? MapEditorSidePane.None
            : MapEditorSidePane.CompiledData;

    private void ActivateSelectMode() =>
        ViewportInteractionMode =
            MapEditorViewportInteractionMode.Select;

    private void ActivateTranslateMode()
    {
        if (CanActivateTranslateMode)
        {
            ViewportInteractionMode =
                MapEditorViewportInteractionMode.Translate;
        }
    }

    private void SelectObject(MapObjectId? objectId)
    {
        EditorMapObject? selected = null;
        if (objectId is { } id &&
            !_document.TryGetObject(id, out selected))
        {
            return;
        }

        SelectedObject = selected;
    }

    private MapBounds? ResolveSelectedBounds() =>
        SelectedObject switch
        {
            EditorWorldSurface surface => surface.Bounds.Value,
            EditorStaticModel model => model.Bounds.Value,
            EditorCollisionObject collision => collision.Bounds.Value,
            EditorAuthoredCollisionObject authored =>
                AuthoredCollisionSourceTransforms.GetBounds(authored.Source),
            EditorSpatialObject spatial => spatial.Bounds.Value,
            _ => null
        };

    private MapEditorSourceBindingRow CreateUncataloguedBindingRow(
        SourceBindingId id)
    {
        if (SelectedObject is EditorEntity entity &&
            TryResolveAuthoredEntityBinding(
                entity,
                id,
                out string fieldPath))
        {
            return new MapEditorSourceBindingRow(
                "MapEnts",
                fieldPath,
                $"Authored physical entity #{entity.SyntaxOrdinal.Value}",
                MapValueProvenance.Authored.ToString(),
                _document.EntitySource?.BaselineDigest ?? string.Empty);
        }
        if (SelectedObject is EditorStaticModel
            {
                IsImported: false,
                AuthoredDuplicatePair: { } pair
            } staticModel &&
            TryResolveAuthoredStaticModelBinding(
                staticModel,
                id,
                out string staticModelFieldPath))
        {
            return new MapEditorSourceBindingRow(
                staticModel.Representation ==
                    StaticModelRepresentation.Render
                    ? "GfxMap"
                    : pair.CollisionAssetKind.ToString(),
                staticModelFieldPath,
                $"Authored pending row #{staticModel.SourceOrdinal.Value}",
                MapValueProvenance.Authored.ToString(),
                pair.BundleBaselineDigest);
        }
        if (SelectedObject is EditorAuthoredCollisionObject authored &&
            authored.EditorProvenanceBinding == id)
        {
            return new MapEditorSourceBindingRow(
                "Editor source",
                $"authoredCollision[{authored.Id}].source",
                "Unallocated M3 candidate",
                MapValueProvenance.Authored.ToString(),
                string.Empty);
        }

        return new MapEditorSourceBindingRow(
            "Unresolved",
            id.ToString(),
            "Unknown",
            "Unknown",
            string.Empty);
    }

    private static bool TryResolveAuthoredEntityBinding(
        EditorEntity entity,
        SourceBindingId id,
        out string fieldPath)
    {
        string entityPath =
            $"entityStringBytes.entities[{entity.SyntaxOrdinal.Value}]";
        if (IsAuthoredBinding(entity.SourceOrdinal, id))
        {
            fieldPath = $"{entityPath}.ordinal";
            return true;
        }
        if (IsAuthoredBinding(entity.SourceByteOffset, id))
        {
            fieldPath = $"{entityPath}.span.offset";
            return true;
        }
        if (IsAuthoredBinding(entity.SourceByteLength, id))
        {
            fieldPath = $"{entityPath}.span.length";
            return true;
        }

        foreach (EditorEntityProperty property in entity.KeyValues)
        {
            string propertyPath =
                $"{entityPath}.properties[{property.Ordinal.Value}]";
            if (IsAuthoredBinding(property.KeyValue, id))
            {
                fieldPath = $"{propertyPath}.key";
                return true;
            }
            if (IsAuthoredBinding(property.PropertyValue, id))
            {
                fieldPath = $"{propertyPath}.value";
                return true;
            }
        }

        fieldPath = string.Empty;
        return false;
    }

    private static bool TryResolveAuthoredStaticModelBinding(
        EditorStaticModel model,
        SourceBindingId id,
        out string fieldPath)
    {
        string rowPath =
            $"authoredStaticModelPairs[" +
            $"{model.AuthoredDuplicatePair!.OperationId}]." +
            $"{model.Representation.ToString().ToLowerInvariant()}";
        if (IsAuthoredBinding(model.SourceOrdinal, id))
        {
            fieldPath = $"{rowPath}.projectedOrdinal";
            return true;
        }
        if (IsAuthoredBinding(model.ModelName, id))
        {
            fieldPath = $"{rowPath}.model";
            return true;
        }
        if (IsAuthoredBinding(model.Origin, id))
        {
            fieldPath = $"{rowPath}.origin";
            return true;
        }
        if (IsAuthoredBinding(model.Scale, id))
        {
            fieldPath = $"{rowPath}.scale";
            return true;
        }
        if (IsAuthoredBinding(model.Bounds, id))
        {
            fieldPath = $"{rowPath}.bounds";
            return true;
        }

        fieldPath = string.Empty;
        return false;
    }

    private static bool IsAuthoredBinding<T>(
        MapValue<T> value,
        SourceBindingId id) =>
        value.SourceBinding == id &&
        value.Provenance == MapValueProvenance.Authored;

    private void ExecuteCommand(IMapEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureDocumentIsEditable();
        MapObjectId? selectedObjectId = SelectedObject?.Id;
        _document.History.Execute(command);
        SynchronizeSelectedObjectInstance(selectedObjectId);
    }

    private void Undo()
    {
        EnsureDocumentIsEditable();
        MapObjectId? selectedObjectId = SelectedObject?.Id;
        _document.History.Undo();
        SynchronizeSelectedObjectInstance(selectedObjectId);
    }

    private void Redo()
    {
        EnsureDocumentIsEditable();
        MapObjectId? selectedObjectId = SelectedObject?.Id;
        _document.History.Redo();
        SynchronizeSelectedObjectInstance(selectedObjectId);
    }

    private void SynchronizeSelectedObjectInstance(
        MapObjectId? selectedObjectId)
    {
        if (selectedObjectId is not { } id)
            return;

        if (_document.TryGetObject(id, out EditorMapObject? current))
        {
            if (!ReferenceEquals(SelectedObject, current))
                SelectedObject = current;
            return;
        }

        SelectedObject = null;
    }

    private void EnsureDocumentIsEditable()
        => _editingContext.EnsureMutationsAllowed();

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));

    private static bool IsSceneObject(EditorMapObject value) =>
        !MapEditorCollisionBrowserViewModel.IsCollisionObject(value);

    private static bool IsSceneObjectKind(MapObjectKind kind) =>
        kind is not (
            MapObjectKind.CollisionStaticModel or
            MapObjectKind.CollisionBrush or
            MapObjectKind.CollisionTriangle);
}
