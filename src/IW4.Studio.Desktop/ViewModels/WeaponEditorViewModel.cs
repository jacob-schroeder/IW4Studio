using System.ComponentModel;
using System.Globalization;
using System.Text;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.OpenGl.XModel;
using IW4.Render.SceneBuilding;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Editors.Weapon;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class WeaponEditorViewModel : ObservableObject,
    IAssetEditorProperties, IAssetEditorInspectorSource,
    IAssetEditorPropertiesRevealSource, IAssetEditorDiagnostics,
    IAssetEditorStagingState, IDisposable
{
    private const string CamoTableAssetName = "mp/camotable.csv";
    private readonly AssetEditorSession _session;
    private readonly AssetReferencePickerService? _assetReferencePicker;
    private readonly XModelSceneBuilder _sceneBuilder = new();
    private readonly WorkspaceGfxImagePayloadResolver _imagePayloads;
    private readonly Dictionary<XModelAsset, XModelRenderScene> _sceneCache =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<INotifyPropertyChanged> _stagedRows = [];
    private readonly IReadOnlyDictionary<int, string> _modelVariantLabels;
    private WeaponDraft _baseline;
    private WeaponDraft _workingDraft;
    private WeaponCategoryItemViewModel _selectedCategory;
    private IReadOnlyList<WeaponIndexedRowItemViewModel> _indexedRows = [];
    private WeaponIndexedRowItemViewModel? _selectedIndexedRow;
    private IReadOnlyList<WeaponModelSlotItemViewModel> _modelSlots = [];
    private WeaponModelSlotItemViewModel? _selectedModelSlot;
    private XModelRenderScene? _scene;
    private IReadOnlyList<XModelLodItemViewModel> _lods = [];
    private XModelLodItemViewModel? _selectedLod;
    private InspectorSelectionViewModel? _inspectorSelection;
    private IReadOnlyList<AssetValidationIssue> _candidateDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _previewDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _rendererDiagnostics = [];
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];
    private string _previewStatus = string.Empty;
    private string _previewMessage = "Select a model slot";
    private WeaponPreviewState _previewState = WeaponPreviewState.NoSelection;
    private long _sceneCacheRevision = -1;
    private int _rendererLodIndex = -1;
    private XModelViewerUploadResult? _rendererUploadResult;
    private string? _rendererFailure;
    private bool _isStudioEnvironmentEnabled = true;
    private bool _isWireframeEnabled;
    private bool _isCollisionEnabled;
    private bool _showBoneTags;
    private bool _isReplacingProjection;
    private bool _isCommittingRows;
    private bool _disposed;

    public WeaponEditorViewModel(AssetEditorSession session, AssetReferencePickerService? assetReferencePicker = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.Weapon)
            throw new InvalidDataException("The Weapon view model can host only Weapon editor sessions.");
        _assetReferencePicker = assetReferencePicker;
        _imagePayloads = new WorkspaceGfxImagePayloadResolver(session.Workspace);
        _modelVariantLabels = CaptureModelVariantLabels(session);
        _baseline = session.OpenDraft<WeaponDraft>();
        _workingDraft = _baseline.Copy();
        Categories = Array.AsReadOnly(WeaponCategoryItemViewModel.CreateAll());
        _selectedCategory = Categories[0];
        RefreshAll();
    }

    public event EventHandler? PropertiesRevealRequested;
    public event EventHandler<AssetReferenceSelectionRequestedEventArgs>? AssetReferenceSelectionRequested;
    internal AssetEditorSession Session => _session;
    internal WeaponDraft WorkingDraft => _workingDraft;
    public WorkspaceAssetAccess Mode => _session.Mode;
    public bool IsEditable => Mode == WorkspaceAssetAccess.Editable && HasDefinition;
    public string Name => _workingDraft.Variant.InternalName ?? _session.Entry.OriginalName ?? "Unnamed Weapon";
    public string AccessText => Mode switch { WorkspaceAssetAccess.Editable => "Editable target", WorkspaceAssetAccess.ReadOnly => "Read-only provider", WorkspaceAssetAccess.ContentUnavailable => "Content unavailable", _ => "Unknown access" };
    public bool HasDefinition => _workingDraft.HasDefinition;
    public XModelRenderScene? Scene { get => _scene; private set => SetProperty(ref _scene, value); }
    public string PreviewStatus { get => _previewStatus; private set => SetProperty(ref _previewStatus, value); }
    public string PreviewMessage { get => _previewMessage; private set => SetProperty(ref _previewMessage, value); }
    public WeaponPreviewState PreviewState { get => _previewState; private set => SetProperty(ref _previewState, value); }
    public bool HasPreviewMessage => !string.IsNullOrEmpty(PreviewMessage);
    public bool HasPreviewDiagnostics => _previewDiagnostics.Count > 0 || _rendererDiagnostics.Count > 0;
    public IReadOnlyList<XModelLodItemViewModel> Lods { get => _lods; private set => SetProperty(ref _lods, value); }
    public int SelectedLodIndex => SelectedLod?.LodIndex ?? -1;
    public XModelLodItemViewModel? SelectedLod
    {
        get => _selectedLod;
        set
        {
            if (value is not null && !Lods.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (ReferenceEquals(value, _selectedLod)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); RevealProperties(); return; }
            _selectedLod = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedLodIndex)); ResetRendererStatus();
            if (Scene is not null && SelectedModelSlot?.ResolvedModel is { } model)
            {
                PreviewMessage = string.Empty;
                PreviewStatus = $"{model.Name} · LOD {SelectedLodIndex} · static selected-model preview";
            }
            RefreshCollisionCapability(); RebuildDiagnostics(); NotifyState();
        }
    }
    public bool IsStudioEnvironmentEnabled { get => _isStudioEnvironmentEnabled; set => SetProperty(ref _isStudioEnvironmentEnabled, value); }
    public bool IsWireframeEnabled { get => _isWireframeEnabled; set => SetProperty(ref _isWireframeEnabled, value); }
    public bool IsCollisionEnabled { get => _isCollisionEnabled; set => SetProperty(ref _isCollisionEnabled, value); }
    public bool ShowBoneTags { get => _showBoneTags; set => SetProperty(ref _showBoneTags, value); }
    public bool CanShowCollision => SelectedLod?.Lod.CollisionTriangleCount > 0;
    public IReadOnlyList<WeaponCategoryItemViewModel> Categories { get; }

    public WeaponCategoryItemViewModel SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_isReplacingProjection || !Categories.Contains(value) || ReferenceEquals(value, _selectedCategory)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); RevealProperties(); return; }
            _selectedCategory = value; OnPropertyChanged(); RebuildIndexedRows(null); RefreshInspector(); RevealProperties();
        }
    }
    public IReadOnlyList<WeaponIndexedRowItemViewModel> IndexedRows { get => _indexedRows; private set => SetProperty(ref _indexedRows, value); }
    public bool HasIndexedRows => IndexedRows.Count > 0;
    public WeaponIndexedRowItemViewModel? SelectedIndexedRow
    {
        get => _selectedIndexedRow;
        set
        {
            if (_isReplacingProjection || (value is not null && !IndexedRows.Contains(value)) || ReferenceEquals(value, _selectedIndexedRow)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); RevealProperties(); return; }
            _selectedIndexedRow = value; OnPropertyChanged(); SyncSelectedModelSlot(); RefreshInspector(); RevealProperties();
        }
    }
    public IReadOnlyList<WeaponModelSlotItemViewModel> ModelSlots { get => _modelSlots; private set => SetProperty(ref _modelSlots, value); }
    public WeaponModelSlotItemViewModel? SelectedModelSlot
    {
        get => _selectedModelSlot;
        set
        {
            if (_isReplacingProjection || (value is not null && !ModelSlots.Contains(value)) || ReferenceEquals(value, _selectedModelSlot)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); RevealProperties(); return; }
            _selectedModelSlot = value; OnPropertyChanged();
            if (value is not null)
            {
                WeaponCategoryItemViewModel modelsCategory = Categories.Single(category =>
                    category.Id == WeaponPropertyCategory.Models);
                if (!ReferenceEquals(_selectedCategory, modelsCategory))
                {
                    _selectedCategory = modelsCategory;
                    OnPropertyChanged(nameof(SelectedCategory));
                    RebuildIndexedRows(value.StableKey);
                }
                _selectedIndexedRow = IndexedRows.FirstOrDefault(row => row.Kind == value.Kind && row.Index == value.Index);
                OnPropertyChanged(nameof(SelectedIndexedRow)); RefreshInspector();
            }
            RebuildSelectedModelPreview(); RevealProperties();
        }
    }

    public InspectorSelectionViewModel? InspectorSelection { get => _inspectorSelection; private set => SetProperty(ref _inspectorSelection, value); }
    public IReadOnlyList<AssetValidationIssue> Diagnostics { get => _diagnostics; private set => SetProperty(ref _diagnostics, value); }
    public string PropertySectionName => "Weapon";
    public IReadOnlyList<AssetEditorProperty> EditorProperties
    {
        get
        {
            WeaponDef? definition = _workingDraft.Definition;
            return [new("Name", Name), new("Access", AccessText), new("Type", definition?.WeaponType.ToString() ?? "Unavailable"), new("Class", definition?.WeaponClass.ToString() ?? "Unavailable"), new("Category", SelectedCategory.Title), new("Selection", SelectedIndexedRow?.Title ?? "Category"), new("Model", SelectedModelSlot?.DisplayName ?? "None"), new("Candidate", CandidateState), new("Preview", PreviewStatus), new("LOD", SelectedLod?.DisplayName ?? "None"), new("Errors", Diagnostics.Count(issue => issue.Severity == AssetValidationSeverity.Error).ToString()), new("Warnings", Diagnostics.Count(issue => issue.Severity == AssetValidationSeverity.Warning).ToString())];
        }
    }
    public string CandidateState => !HasDefinition ? "Invalid" : !IsEditable ? "Read-only" : _candidateDiagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error) ? "Invalid" : HasUnappliedChanges ? "Modified" : "Unchanged";
    public bool HasErrors => Diagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error);
    public bool HasUnappliedChanges => StagedRowsHaveInput || !_session.CandidateMatchesCurrent(_workingDraft);
    public bool CanApply => IsEditable && HasUnappliedChanges && !HasStagedErrors && !_candidateDiagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error);
    public bool CanRevert => IsEditable && HasUnappliedChanges;

    public void ApplyDraft()
    {
        if (!IsEditable || !TryCommitInspectorRows()) return;
        RefreshValidation();
        if (_candidateDiagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error)) { RevealProperties(); return; }
        try
        {
            _ = _session.Apply<WeaponDraft>(draft => draft.ReplaceWith(_workingDraft));
            _baseline = _session.OpenDraft<WeaponDraft>(); _workingDraft = _baseline.Copy(); RefreshAll();
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException)
        {
            _candidateDiagnostics = [new AssetValidationIssue("weapon.apply", exception.Message, AssetValidationSeverity.Error)]; RebuildDiagnostics(); RevealProperties();
        }
    }
    public void RevertDraft()
    {
        if (!IsEditable) return;
        foreach (IInspectorStagedPropertyRow row in _stagedRows.OfType<IInspectorStagedPropertyRow>()) row.ResetInput();
        _workingDraft = _baseline.Copy(); RefreshAll();
    }
    internal void Mutate(Action mutation, bool rebuildInspector = true)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        mutation(); RefreshValidation(); RebuildModelSlots();
        if (!_isCommittingRows && rebuildInspector) RefreshInspector(); NotifyState();
    }
    internal void MutateModel(WeaponIndexedRowKind kind, int index, Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        string stableKey = $"{kind}:{index}";
        bool isSelected = string.Equals(SelectedModelSlot?.StableKey, stableKey, StringComparison.Ordinal);
        XModelAsset? previousModel = ModelSlots.FirstOrDefault(slot => slot.StableKey == stableKey)?.ResolvedModel;
        if (isSelected)
        {
            ClearPreviewProjection();
            CompletePreviewProjection();
        }
        try
        {
            mutation();
        }
        catch
        {
            if (isSelected) RebuildSelectedModelPreview();
            throw;
        }
        RefreshValidation(); RebuildModelSlots();
        if (previousModel is not null) _sceneCache.Remove(previousModel);
        if (ModelSlots.FirstOrDefault(slot => slot.StableKey == stableKey)?.ResolvedModel is { } currentModel) _sceneCache.Remove(currentModel);
        if (isSelected) RebuildSelectedModelPreview();
        if (!_isCommittingRows) RefreshInspector(); NotifyState();
    }
    internal void RequestAssetReferenceSelection(InspectorAssetReferencePropertyRowViewModel row) => AssetReferenceSelectionRequested?.Invoke(this, new AssetReferenceSelectionRequestedEventArgs(row));
    internal bool IsReferenceMissing(XAssetType type, string? name) => _assetReferencePicker is not null && !_assetReferencePicker.IsResolved(type, name);
    internal void RevealProperties() => PropertiesRevealRequested?.Invoke(this, EventArgs.Empty);
    internal void UpdateRendererStatus(int lodIndex, XModelViewerUploadResult? uploadResult, string? failure)
    {
        if (_disposed || (lodIndex >= 0 && lodIndex != SelectedLodIndex)) return;
        if (_rendererLodIndex == lodIndex &&
            string.Equals(_rendererFailure, failure, StringComparison.Ordinal) &&
            UploadResultsEqual(_rendererUploadResult, uploadResult)) return;
        _rendererLodIndex = lodIndex;
        _rendererUploadResult = uploadResult;
        _rendererFailure = failure;
        var issues = new List<AssetValidationIssue>();
        string context = $"{SelectedModelSlot?.RoleLabel ?? "Selected model"}, LOD {SelectedLodIndex}";
        if (!string.IsNullOrWhiteSpace(failure)) issues.Add(new AssetValidationIssue("weapon.preview.opengl", $"{context}: OpenGL execution: {failure}", AssetValidationSeverity.Error));
        if (uploadResult is not null) issues.AddRange(uploadResult.Diagnostics.Select(message => new AssetValidationIssue("weapon.preview.opengl", $"{context}: {message}", AssetValidationSeverity.Warning)));
        if (!string.IsNullOrWhiteSpace(failure))
        {
            PreviewMessage = "OpenGL preview is unavailable";
            PreviewStatus = Bounded($"OpenGL preview is unavailable: {failure}");
        }
        else if (SelectedModelSlot?.ResolvedModel is { } model && Scene is not null)
        {
            PreviewMessage = string.Empty;
            int totalGroups = (uploadResult?.ExecutableGroupCount ?? 0) + (uploadResult?.BlockedGroupCount ?? 0);
            PreviewStatus = uploadResult is null
                ? $"{model.Name} · LOD {SelectedLodIndex} · static selected-model preview"
                : $"{model.Name} · LOD {SelectedLodIndex} · {uploadResult.ExecutableGroupCount}/{totalGroups} render groups executable";
        }
        _rendererDiagnostics = Array.AsReadOnly(issues.ToArray()); RebuildDiagnostics(); NotifyState();
    }

    private bool TryCommitInspectorRows()
    {
        if (_isCommittingRows) return true;
        _isCommittingRows = true;
        try
        {
            foreach (IInspectorStagedPropertyRow row in _stagedRows.OfType<IInspectorStagedPropertyRow>()) if (row.HasStagedValue && !row.CommitInput()) return false;
            RefreshValidation(); return true;
        }
        finally { _isCommittingRows = false; }
    }
    private bool StagedRowsHaveInput => _stagedRows.OfType<IInspectorStagedPropertyRow>().Any(row => row.HasStagedValue);
    private bool HasStagedErrors => _stagedRows.OfType<InspectorPropertyRowViewModel>().Any(row => row.HasValidationError);
    private void StagedRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IInspectorStagedPropertyRow.HasStagedValue) or nameof(InspectorPropertyRowViewModel.HasValidationError)) NotifyState();
    }
    private void RefreshAll()
    {
        RefreshValidation(); RebuildModelSlots(); RebuildIndexedRows(SelectedIndexedRow?.StableKey); RebuildSelectedModelPreview(); RefreshInspector(); NotifyState();
    }
    private void RefreshValidation()
    {
        _candidateDiagnostics = _session.ValidateCandidate(_workingDraft).Issues;
        if (!HasDefinition && !_candidateDiagnostics.Any(issue => issue.FieldPath == "weapon.variant.definition")) _candidateDiagnostics = [.. _candidateDiagnostics, new AssetValidationIssue("weapon.variant.definition", "Weapon variant has no nested definition.", AssetValidationSeverity.Error)];
        RebuildDiagnostics();
    }
    private void RebuildModelSlots()
    {
        string? preferred = SelectedModelSlot?.StableKey;
        Dictionary<string, WeaponModelSlotItemViewModel> previousSlots = ModelSlots.ToDictionary(slot => slot.StableKey);
        var slots = new List<WeaponModelSlotItemViewModel>();
        if (_workingDraft.Definition is { } definition)
        {
            AddModels(slots, WeaponIndexedRowKind.GunModel, "View model", definition.GunModels, WeaponDef.GunModelCount, definition.GunModelsPointer.Type != PointerType.Null, definition.GunModelPointers.Count);
            slots.Add(CreateModelSlot(WeaponIndexedRowKind.HandModel, 0, "Hand model", definition.HandModel, true));
            AddModels(slots, WeaponIndexedRowKind.WorldGunModel, "World model", definition.WorldGunModels, WeaponDef.GunModelCount, definition.WorldGunModelsPointer.Type != PointerType.Null, definition.WorldGunModelPointers.Count);
            slots.Add(CreateModelSlot(WeaponIndexedRowKind.WorldClipModel, 0, "World clip model", definition.WorldClipModel, true));
            slots.Add(CreateModelSlot(WeaponIndexedRowKind.RocketModel, 0, "Rocket model", definition.RocketModel, true));
            slots.Add(CreateModelSlot(WeaponIndexedRowKind.KnifeModel, 0, "Knife model", definition.KnifeModel, true));
            slots.Add(CreateModelSlot(WeaponIndexedRowKind.WorldKnifeModel, 0, "World knife model", definition.WorldKnifeModel, true));
            slots.Add(CreateModelSlot(WeaponIndexedRowKind.ProjectileModel, 0, "Projectile model", definition.Projectile.Model, true));
        }
        foreach (WeaponModelSlotItemViewModel slot in slots)
        {
            if (previousSlots.TryGetValue(slot.StableKey, out WeaponModelSlotItemViewModel? previous) &&
                previous.State == WeaponModelSlotState.NonRenderable && slot.HasSameReference(previous))
                slot.SetState(WeaponModelSlotState.NonRenderable);
        }
        ModelSlots = Array.AsReadOnly(slots.ToArray());
        _selectedModelSlot = ModelSlots.FirstOrDefault(slot => slot.StableKey == preferred)
            ?? ModelSlots.FirstOrDefault(slot => slot.Kind == WeaponIndexedRowKind.GunModel && slot.State == WeaponModelSlotState.Resolved)
            ?? ModelSlots.FirstOrDefault(slot => slot.Kind == WeaponIndexedRowKind.HandModel)
            ?? ModelSlots.FirstOrDefault(slot => slot.State == WeaponModelSlotState.Resolved)
            ?? ModelSlots.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedModelSlot));
    }
    private void AddModels(List<WeaponModelSlotItemViewModel> target, WeaponIndexedRowKind kind, string family, IReadOnlyList<XModelAsset?> models, int conceptualCount, bool tablePresent, params int[] parallelCounts)
    {
        int[] counts = [models.Count, .. parallelCounts];
        bool absent = !tablePresent && counts.All(count => count == 0);
        bool malformed = !absent && (!tablePresent || counts.Any(count => count != conceptualCount));
        for (int index = 0; index < conceptualCount; index++)
        {
            bool positionPresent = tablePresent && index < models.Count;
            target.Add(CreateModelSlot(kind, index, family, positionPresent ? models[index] : null, positionPresent, malformed));
        }
    }
    private WeaponModelSlotItemViewModel CreateModelSlot(WeaponIndexedRowKind kind, int index, string family, XModelAsset? semanticModel, bool storagePositionPresent, bool malformedStorage = false)
    {
        string roleLabel = kind switch
        {
            WeaponIndexedRowKind.GunModel => WeaponIndexedRowItemViewModel.ModelVariantTitle("View model", index, _modelVariantLabels),
            WeaponIndexedRowKind.HandModel => "Hand model",
            WeaponIndexedRowKind.WorldGunModel => WeaponIndexedRowItemViewModel.ModelVariantTitle("World model", index, _modelVariantLabels),
            WeaponIndexedRowKind.WorldClipModel => "World clip model",
            WeaponIndexedRowKind.RocketModel => "Rocket model",
            WeaponIndexedRowKind.KnifeModel => "Knife model",
            WeaponIndexedRowKind.WorldKnifeModel => "World knife model",
            WeaponIndexedRowKind.ProjectileModel => "Projectile model",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (!storagePositionPresent)
            return new(kind, index, family, roleLabel, semanticModel, null, malformedStorage ? WeaponModelSlotState.Malformed : WeaponModelSlotState.TableAbsent, false, null, malformedStorage);
        if (semanticModel is null)
            return new(kind, index, family, roleLabel, null, null, WeaponModelSlotState.Empty, IsEditable && !malformedStorage, null, malformedStorage);
        string? name = semanticModel.Name;
        XModelAsset? resolved = null;
        string? sourceDetail = null;
        if (string.IsNullOrWhiteSpace(name) || !TryResolveModel(name, out resolved, out sourceDetail))
            return new(kind, index, family, roleLabel, semanticModel, null, WeaponModelSlotState.Unresolved, IsEditable && !malformedStorage, sourceDetail, malformedStorage);
        return new(kind, index, family, roleLabel, semanticModel, resolved, WeaponModelSlotState.Resolved, IsEditable && !malformedStorage, sourceDetail, malformedStorage);
    }
    private bool TryResolveModel(string name, out XModelAsset? resolved, out string? sourceDetail)
    {
        var pool = _session.Workspace.LoadedZone.Context.AssetPool;
        resolved = null;
        sourceDetail = null;
        if (!pool.TryResolve(XAssetType.XModel, name, out XModelAsset? current) || current is null ||
            current.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !pool.TryGetSlot(address, out var slot) || slot is null || slot.ActiveProvider.IsReferencePlaceholder)
            return false;
        resolved = current;
        WorkspaceZone? provider = _session.Workspace.LoadedZones.FirstOrDefault(zone =>
            zone.LoadResult.Context.ZoneOwner == slot.ActiveProvider.Owner);
        sourceDetail = provider is null ? "Resolved workspace provider" : $"Provider zone: {provider.LogicalZoneName}";
        return true;
    }
    private void RebuildIndexedRows(string? preferredKey)
    {
        _isReplacingProjection = true;
        try
        {
            IndexedRows = Array.AsReadOnly(WeaponIndexedRowItemViewModel.Create(_workingDraft, SelectedCategory.Id, _modelVariantLabels).ToArray()); _selectedIndexedRow = IndexedRows.FirstOrDefault(row => row.StableKey == preferredKey) ?? IndexedRows.FirstOrDefault(); OnPropertyChanged(nameof(SelectedIndexedRow)); OnPropertyChanged(nameof(HasIndexedRows)); SyncSelectedModelSlot();
        }
        finally { _isReplacingProjection = false; }
    }
    private void SyncSelectedModelSlot()
    {
        if (SelectedIndexedRow is not { } row || !row.Kind.IsModel()) return;
        _selectedModelSlot = ModelSlots.FirstOrDefault(slot => slot.Kind == row.Kind && slot.Index == row.Index); OnPropertyChanged(nameof(SelectedModelSlot));
        if (!_isReplacingProjection) RebuildSelectedModelPreview();
    }
    private void RebuildSelectedModelPreview()
    {
        int? previousLodIndex = SelectedLod?.LodIndex;
        string? selectedKey = SelectedModelSlot?.StableKey;
        long poolRevision = _session.Workspace.LoadedZone.Context.AssetPool.Revision;
        if (_sceneCacheRevision != poolRevision)
        {
            bool reprojectSlots = _sceneCacheRevision >= 0;
            _sceneCache.Clear();
            _sceneCacheRevision = poolRevision;
            if (reprojectSlots) RebuildModelSlots();
        }
        ClearPreviewProjection();
        CompletePreviewProjection();
        WeaponModelSlotItemViewModel? slot = ModelSlots.FirstOrDefault(candidate => candidate.StableKey == selectedKey);
        if (!HasDefinition || slot is null) { CompleteEmptyPreview(WeaponPreviewState.NoSelection, "Select a model slot", "No model selected"); return; }
        if (slot.State == WeaponModelSlotState.TableAbsent) { CompleteEmptyPreview(WeaponPreviewState.TableAbsent, "This native model table is absent", $"{slot.RoleLabel} · table absent"); return; }
        if (slot.State == WeaponModelSlotState.Malformed) { CompleteEmptyPreview(WeaponPreviewState.Malformed, "This native model table is malformed", $"{slot.RoleLabel} · malformed table"); return; }
        if (slot.State == WeaponModelSlotState.Empty) { CompleteEmptyPreview(WeaponPreviewState.Empty, "This model slot is empty", $"{slot.RoleLabel} · empty"); return; }
        if (slot.ResolvedModel is not { } model)
        {
            _previewDiagnostics = [new AssetValidationIssue("weapon.preview.model", $"{slot.RoleLabel}: model reference '{slot.SemanticName ?? "unnamed"}' is unresolved.", AssetValidationSeverity.Warning)];
            CompleteEmptyPreview(WeaponPreviewState.Unresolved, "The model reference is unresolved", $"{slot.RoleLabel} · unresolved"); return;
        }
        try
        {
            XModelRenderScene scene;
            if (!_sceneCache.TryGetValue(model, out scene!))
            {
                scene = _sceneBuilder.Build(
                    model,
                    WorkspaceRenderAssetSource.Create(_session.Workspace, "Weapon XModel material assets"),
                    _imagePayloads);
                if (_session.Workspace.LoadedZone.Context.AssetPool.Revision != poolRevision)
                    throw new InvalidOperationException("The asset pool changed while the selected Weapon model scene was being built.");
                if (scene.Lods.Count > 0) _sceneCache.Add(model, scene);
            }
            if (scene.Lods.Count == 0)
            {
                slot.SetState(WeaponModelSlotState.NonRenderable);
                _previewDiagnostics = [new AssetValidationIssue("weapon.preview.scene", $"{slot.RoleLabel} '{model.Name}': no complete loaded LOD geometry.", AssetValidationSeverity.Warning)];
                CompleteEmptyPreview(WeaponPreviewState.NonRenderable, "The XModel has no complete loaded LOD geometry", $"{model.Name} · no complete loaded LOD"); return;
            }
            _scene = scene;
            _lods = scene.Lods.Select(lod => new XModelLodItemViewModel(lod)).ToArray();
            int preferredLodIndex = previousLodIndex ?? scene.DefaultLodIndex;
            _selectedLod = _lods.FirstOrDefault(lod => lod.LodIndex == preferredLodIndex) ?? _lods.FirstOrDefault();
            _previewState = WeaponPreviewState.Ready;
            _previewMessage = string.Empty;
            _previewStatus = $"{model.Name} · LOD {_selectedLod?.LodIndex ?? -1} · static selected-model preview";
            _previewDiagnostics = Array.AsReadOnly(scene.Diagnostics.Select(message => new AssetValidationIssue("weapon.preview.scene", $"{slot.RoleLabel} '{model.Name}': {message}", AssetValidationSeverity.Warning)).GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity)).Select(group => group.First()).ToArray());
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException or OverflowException)
        {
            slot.SetState(WeaponModelSlotState.NonRenderable);
            _previewDiagnostics = [new AssetValidationIssue("weapon.preview.scene", $"{slot.RoleLabel}: {exception.Message}", AssetValidationSeverity.Warning)];
            CompleteEmptyPreview(WeaponPreviewState.Failed, Bounded($"Selected-model preview failed: {exception.Message}"), $"{slot.RoleLabel} · scene build failed"); return;
        }
        CompletePreviewProjection();
    }
    private void ClearPreviewProjection()
    {
        _scene = null; _lods = []; _selectedLod = null; _previewDiagnostics = []; _rendererDiagnostics = [];
        _previewState = WeaponPreviewState.NoSelection; _previewMessage = "Select a model slot"; _previewStatus = "No model selected";
        ResetRendererStatus();
    }
    private void CompleteEmptyPreview(WeaponPreviewState state, string message, string status)
    {
        _previewState = state; _previewMessage = message; _previewStatus = status; CompletePreviewProjection();
    }
    private void CompletePreviewProjection()
    {
        OnPropertyChanged(nameof(Scene)); OnPropertyChanged(nameof(Lods)); OnPropertyChanged(nameof(SelectedLod)); OnPropertyChanged(nameof(SelectedLodIndex));
        OnPropertyChanged(nameof(PreviewState)); OnPropertyChanged(nameof(PreviewMessage)); OnPropertyChanged(nameof(PreviewStatus));
        RefreshCollisionCapability(); RebuildDiagnostics(); NotifyState();
    }
    private void RefreshInspector()
    {
        foreach (INotifyPropertyChanged row in _stagedRows) row.PropertyChanged -= StagedRow_PropertyChanged; _stagedRows.Clear(); InspectorSelection = WeaponInspectorProjection.Create(this);
        if (InspectorSelection is not null) foreach (INotifyPropertyChanged row in InspectorSelection.Sections.SelectMany(section => section.Rows).OfType<INotifyPropertyChanged>()) if (row is IInspectorStagedPropertyRow) { _stagedRows.Add(row); row.PropertyChanged += StagedRow_PropertyChanged; }
        NotifyState();
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(EditorProperties)); OnPropertyChanged(nameof(HasUnappliedChanges)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(CanRevert)); OnPropertyChanged(nameof(CandidateState)); OnPropertyChanged(nameof(HasErrors)); OnPropertyChanged(nameof(HasPreviewMessage)); OnPropertyChanged(nameof(HasPreviewDiagnostics));
    }
    private void ResetRendererStatus()
    {
        _rendererLodIndex = -1; _rendererUploadResult = null; _rendererFailure = null; _rendererDiagnostics = [];
    }
    private void RefreshCollisionCapability()
    {
        if (!CanShowCollision && _isCollisionEnabled)
        {
            _isCollisionEnabled = false;
            OnPropertyChanged(nameof(IsCollisionEnabled));
        }
        OnPropertyChanged(nameof(CanShowCollision));
    }
    private static bool UploadResultsEqual(XModelViewerUploadResult? left, XModelViewerUploadResult? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null &&
        left.ExecutableGroupCount == right.ExecutableGroupCount &&
        left.BlockedGroupCount == right.BlockedGroupCount &&
        left.Diagnostics.SequenceEqual(right.Diagnostics, StringComparer.Ordinal);
    private static IReadOnlyDictionary<int, string> CaptureModelVariantLabels(AssetEditorSession session)
    {
        var labels = new Dictionary<int, string>();
        if (!session.Workspace.LoadedZone.Context.AssetPool.TryResolve(
                XAssetType.StringTable,
                CamoTableAssetName,
                out StringTableAsset? table) ||
            table is null || table.ColumnCount < 2 || table.RowCount <= 0)
        {
            return labels;
        }

        int cellCount;
        try
        {
            cellCount = checked(table.RowCount * table.ColumnCount);
        }
        catch (OverflowException)
        {
            return labels;
        }
        if (table.Cells.Count != cellCount)
            return labels;

        var duplicateIndices = new HashSet<int>();
        for (int row = 0; row < table.RowCount; row++)
        {
            int offset = checked(row * table.ColumnCount);
            string? labelText = table.Cells[offset + 1].String;
            if (!int.TryParse(table.Cells[offset].String, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int index) ||
                index < 0 || index >= WeaponDef.GunModelCount ||
                string.IsNullOrWhiteSpace(labelText) ||
                duplicateIndices.Contains(index))
            {
                continue;
            }

            string label = labelText.Trim();
            if (!labels.TryAdd(index, label))
            {
                labels.Remove(index);
                duplicateIndices.Add(index);
            }
        }
        return labels;
    }
    private static string Bounded(string message) => message.Length <= 180 ? message : message[..177] + "...";
    private void RebuildDiagnostics()
    {
        Diagnostics = Array.AsReadOnly(_candidateDiagnostics.Concat(_previewDiagnostics).Concat(_rendererDiagnostics).GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity)).Select(group => group.First()).ToArray());
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; foreach (INotifyPropertyChanged row in _stagedRows) row.PropertyChanged -= StagedRow_PropertyChanged; _stagedRows.Clear(); _sceneCache.Clear(); _scene = null; _lods = []; _selectedLod = null; AssetReferenceSelectionRequested = null; PropertiesRevealRequested = null;
    }
}

public enum WeaponPropertyCategory { Overview, Models, AnimationNames, HideTagsAndNoteTracks, ClassificationAndReticle, ViewAndPositionalMovement, HudIconsAndAmmo, Timing, AimAndMovementTuning, OverlayAdsAndSpread, PhysicsAndProjectile, KickRecoilAndAccuracy, DamageRangeAndAiTuning, EffectsAndMaterials, SoundsAndBounce, HintsAndRumble, TurretAndMissile, TailAndPreservedStorage }
public enum WeaponPreviewState { NoSelection, Empty, TableAbsent, Malformed, Unresolved, NonRenderable, Ready, Failed }
public enum WeaponModelSlotState { Resolved, Empty, Unresolved, NonRenderable, TableAbsent, Malformed }
public sealed class WeaponCategoryItemViewModel
{
    private WeaponCategoryItemViewModel(WeaponPropertyCategory id, string title) { Id = id; Title = title; }
    public WeaponPropertyCategory Id { get; } public string Title { get; }
    internal static WeaponCategoryItemViewModel[] CreateAll() => [new(WeaponPropertyCategory.Overview, "Overview and variant"), new(WeaponPropertyCategory.Models, "Models"), new(WeaponPropertyCategory.AnimationNames, "Animation names"), new(WeaponPropertyCategory.HideTagsAndNoteTracks, "Hide tags and note tracks"), new(WeaponPropertyCategory.ClassificationAndReticle, "Classification and reticle"), new(WeaponPropertyCategory.ViewAndPositionalMovement, "View and positional movement"), new(WeaponPropertyCategory.HudIconsAndAmmo, "HUD icons and ammo"), new(WeaponPropertyCategory.Timing, "Timing"), new(WeaponPropertyCategory.AimAndMovementTuning, "Aim and movement tuning"), new(WeaponPropertyCategory.OverlayAdsAndSpread, "Overlay, ADS, and spread"), new(WeaponPropertyCategory.PhysicsAndProjectile, "Physics and projectile"), new(WeaponPropertyCategory.KickRecoilAndAccuracy, "Kick, recoil, and accuracy"), new(WeaponPropertyCategory.DamageRangeAndAiTuning, "Damage, range, and AI tuning"), new(WeaponPropertyCategory.EffectsAndMaterials, "Flash and shell effects"), new(WeaponPropertyCategory.SoundsAndBounce, "Sounds and bounce response"), new(WeaponPropertyCategory.HintsAndRumble, "Hints and rumble"), new(WeaponPropertyCategory.TurretAndMissile, "Turret and missile-cone sound"), new(WeaponPropertyCategory.TailAndPreservedStorage, "Tail bytes and preserved storage")];
}

public enum WeaponIndexedRowKind { GunModel, HandModel, WorldGunModel, WorldClipModel, RocketModel, KnifeModel, WorldKnifeModel, ProjectileModel, VariantAnimation, RightAnimation, LeftAnimation, HideTag, SoundNoteMapping, RumbleNoteMapping, AiVsAiCurrentAccuracyGraph, AiVsPlayerCurrentAccuracyGraph, AiVsAiOriginalAccuracyGraph, AiVsPlayerOriginalAccuracyGraph, LocationDamage, ProjectileParallelBounce, ProjectilePerpendicularBounce, BounceSound, TurretSpinUpSound, TurretSpinDownSound }
internal static class WeaponIndexedRowKindExtensions { internal static bool IsModel(this WeaponIndexedRowKind value) => value is WeaponIndexedRowKind.GunModel or WeaponIndexedRowKind.HandModel or WeaponIndexedRowKind.WorldGunModel or WeaponIndexedRowKind.WorldClipModel or WeaponIndexedRowKind.RocketModel or WeaponIndexedRowKind.KnifeModel or WeaponIndexedRowKind.WorldKnifeModel or WeaponIndexedRowKind.ProjectileModel; }

internal static class WeaponSemanticLabels
{
    internal static string SlotTitle<T>(string family, int index) where T : struct, Enum
    {
        string? name = Enum.GetName(typeof(T), index);
        return name is null || string.Equals(name, "Count", StringComparison.Ordinal)
            ? $"{family} — Unknown slot [{index:00}]"
            : $"{family} — {HumanizeIdentifier(name)}";
    }

    internal static string HumanizeIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return identifier;
        var result = new StringBuilder(identifier.Length + 8);
        for (int index = 0; index < identifier.Length; index++)
        {
            char current = identifier[index];
            if (index > 0 && (char.IsDigit(current) && !char.IsDigit(identifier[index - 1]) ||
                char.IsUpper(current) && (char.IsLower(identifier[index - 1]) ||
                    index + 1 < identifier.Length && char.IsLower(identifier[index + 1]))))
            {
                result.Append(' ');
            }
            result.Append(current);
        }

        return string.Join(' ', result.ToString().Split(' ').Select(word => word switch
        {
            "Ads" => "ADS",
            "Ai" => "AI",
            "Emp" => "EMP",
            "Hud" => "HUD",
            "Dof" => "DOF",
            "Fov" => "FOV",
            "Vs" => "vs.",
            _ => word
        }));
    }
}

public sealed class WeaponIndexedRowItemViewModel
{
    internal WeaponIndexedRowItemViewModel(WeaponIndexedRowKind kind, int index, string title, bool tablePresent = true, bool hasValue = true, bool isMalformed = false) { Kind = kind; Index = index; Title = title; IsTablePresent = tablePresent; HasValue = hasValue; IsMalformed = isMalformed; }
    public WeaponIndexedRowKind Kind { get; } public int Index { get; } public string Title { get; } public bool IsTablePresent { get; } public bool HasValue { get; } public bool IsMalformed { get; } public bool IsTableAbsent => !IsTablePresent && !IsMalformed; public bool CanEditValue => IsTablePresent && HasValue && !IsMalformed; public string Detail => IsTableAbsent ? "Table absent" : IsMalformed ? HasValue ? $"Index {Index} · Malformed table" : "Malformed table" : $"Index {Index}"; public string StableKey => $"{Kind}:{Index}";
    internal static IEnumerable<WeaponIndexedRowItemViewModel> Create(WeaponDraft draft, WeaponPropertyCategory category, IReadOnlyDictionary<int, string> modelVariantLabels)
    {
        ArgumentNullException.ThrowIfNull(modelVariantLabels);
        if (draft.Definition is not { } definition) yield break;
        IEnumerable<WeaponIndexedRowItemViewModel> BuildRows(WeaponIndexedRowKind kind, Func<int, string> title, int actual, int expected, bool tablePresent, params int[] parallelCounts)
        {
            int[] counts = [actual, .. parallelCounts];
            bool absent = !tablePresent && counts.All(count => count == 0);
            bool malformed = !absent && (!tablePresent || counts.Any(count => count != expected));
            int count = absent ? expected : Math.Max(expected, counts.Max());
            return Enumerable.Range(0, count).Select(index => new WeaponIndexedRowItemViewModel(
                kind, index, title(index), tablePresent, index < actual, malformed));
        }
        IEnumerable<WeaponIndexedRowItemViewModel> Rows(WeaponIndexedRowKind kind, string label, int actual, int expected, bool tablePresent, params int[] parallelCounts) =>
            BuildRows(kind, index => kind is WeaponIndexedRowKind.GunModel or WeaponIndexedRowKind.WorldGunModel
                ? ModelVariantTitle(label, index, modelVariantLabels)
                : $"{label} [{index:00}]", actual, expected, tablePresent, parallelCounts);
        IEnumerable<WeaponIndexedRowItemViewModel> SemanticRows<T>(WeaponIndexedRowKind kind, string label, int actual, int expected, bool tablePresent, params int[] parallelCounts) where T : struct, Enum =>
            BuildRows(kind, index => WeaponSemanticLabels.SlotTitle<T>(label, index), actual, expected, tablePresent, parallelCounts);
        IEnumerable<WeaponIndexedRowItemViewModel> ExactSemanticRows<T>(WeaponIndexedRowKind kind, string label, int actual, int expected, params int[] parallelCounts) where T : struct, Enum =>
            SemanticRows<T>(kind, label, actual, expected, true, parallelCounts);
        bool Present(PointerType type) => type != PointerType.Null;
        IEnumerable<WeaponIndexedRowItemViewModel> selected = category switch
        {
            WeaponPropertyCategory.Models => Rows(WeaponIndexedRowKind.GunModel, "View model", definition.GunModels.Count, WeaponDef.GunModelCount, Present(definition.GunModelsPointer.Type), definition.GunModelPointers.Count).Concat([new(WeaponIndexedRowKind.HandModel, 0, "Hand model")]).Concat(Rows(WeaponIndexedRowKind.WorldGunModel, "World model", definition.WorldGunModels.Count, WeaponDef.GunModelCount, Present(definition.WorldGunModelsPointer.Type), definition.WorldGunModelPointers.Count)).Concat([new(WeaponIndexedRowKind.WorldClipModel, 0, "World clip model"), new(WeaponIndexedRowKind.RocketModel, 0, "Rocket model"), new(WeaponIndexedRowKind.KnifeModel, 0, "Knife model"), new(WeaponIndexedRowKind.WorldKnifeModel, 0, "World knife model"), new(WeaponIndexedRowKind.ProjectileModel, 0, "Projectile model")]),
            WeaponPropertyCategory.AnimationNames => SemanticRows<WeaponAnimationSlot>(WeaponIndexedRowKind.VariantAnimation, "Variant animation", draft.Variant.AnimationNames.Count, (int)WeaponAnimationSlot.Count, Present(draft.Variant.AnimationNamesPointer.Type), draft.Variant.AnimationNamePointers.Count).Concat(SemanticRows<WeaponAnimationSlot>(WeaponIndexedRowKind.RightAnimation, "Right-hand animation", definition.RightHandAnimationNames.Count, (int)WeaponAnimationSlot.Count, Present(definition.RightHandAnimationNamesPointer.Type), definition.RightHandAnimationNamePointers.Count)).Concat(SemanticRows<WeaponAnimationSlot>(WeaponIndexedRowKind.LeftAnimation, "Left-hand animation", definition.LeftHandAnimationNames.Count, (int)WeaponAnimationSlot.Count, Present(definition.LeftHandAnimationNamesPointer.Type), definition.LeftHandAnimationNamePointers.Count)),
            WeaponPropertyCategory.HideTagsAndNoteTracks => Rows(WeaponIndexedRowKind.HideTag, "Hide tag", draft.Variant.HideTags.Count, WeaponVariantDef.HideTagCount, Present(draft.Variant.HideTagsPointer.Type)).Concat(Rows(WeaponIndexedRowKind.SoundNoteMapping, "Sound note mapping", definition.NoteTrackMaps.SoundMappings.Count, WeaponDef.NoteTrackMapCount, Present(definition.NoteTrackMaps.SoundMapKeysPointer.Type) && Present(definition.NoteTrackMaps.SoundMapValuesPointer.Type))).Concat(Rows(WeaponIndexedRowKind.RumbleNoteMapping, "Rumble note mapping", definition.NoteTrackMaps.RumbleMappings.Count, WeaponDef.NoteTrackMapCount, Present(definition.NoteTrackMaps.RumbleMapKeysPointer.Type) && Present(definition.NoteTrackMaps.RumbleMapValuesPointer.Type))),
            WeaponPropertyCategory.KickRecoilAndAccuracy => Rows(WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph, "AI vs. AI current graph", draft.Variant.AiVsAiAccuracyGraphKnots.Count, draft.Variant.AiVsAiAccuracyGraphKnotCount, Present(draft.Variant.AiVsAiAccuracyGraphKnotsPointer.Type)).Concat(Rows(WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph, "AI vs. player current graph", draft.Variant.AiVsPlayerAccuracyGraphKnots.Count, draft.Variant.AiVsPlayerAccuracyGraphKnotCount, Present(draft.Variant.AiVsPlayerAccuracyGraphKnotsPointer.Type))).Concat(Rows(WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph, "AI vs. AI original graph", definition.Accuracy.OriginalAiVsAiGraphKnots.Count, definition.Accuracy.OriginalAiVsAiGraphKnotCount, Present(definition.Accuracy.OriginalAiVsAiGraphKnotsPointer.Type))).Concat(Rows(WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph, "AI vs. player original graph", definition.Accuracy.OriginalAiVsPlayerGraphKnots.Count, definition.Accuracy.OriginalAiVsPlayerGraphKnotCount, Present(definition.Accuracy.OriginalAiVsPlayerGraphKnotsPointer.Type))),
            WeaponPropertyCategory.DamageRangeAndAiTuning => SemanticRows<HitLocation>(WeaponIndexedRowKind.LocationDamage, "Location damage", definition.LocationDamageMultipliers.Count, (int)HitLocation.Count, Present(definition.LocationDamageMultipliersPointer.Type)),
            WeaponPropertyCategory.PhysicsAndProjectile => SemanticRows<MaterialSurfaceType>(WeaponIndexedRowKind.ProjectileParallelBounce, "Parallel bounce", definition.Projectile.ParallelBounce.Count, (int)MaterialSurfaceType.Count, Present(definition.Projectile.ParallelBouncePointer.Type)).Concat(SemanticRows<MaterialSurfaceType>(WeaponIndexedRowKind.ProjectilePerpendicularBounce, "Perpendicular bounce", definition.Projectile.PerpendicularBounce.Count, (int)MaterialSurfaceType.Count, Present(definition.Projectile.PerpendicularBouncePointer.Type))),
            WeaponPropertyCategory.SoundsAndBounce => SemanticRows<MaterialSurfaceType>(WeaponIndexedRowKind.BounceSound, "Bounce sound", definition.BounceSounds.Count, (int)MaterialSurfaceType.Count, Present(definition.BounceSoundPointer.Type)),
            WeaponPropertyCategory.TurretAndMissile => ExactSemanticRows<WeaponTurretBarrelSpinSoundSlot>(WeaponIndexedRowKind.TurretSpinUpSound, "Turret spin-up sound", definition.Turret.BarrelSpinUpSounds.Count, (int)WeaponTurretBarrelSpinSoundSlot.Count).Concat(ExactSemanticRows<WeaponTurretBarrelSpinSoundSlot>(WeaponIndexedRowKind.TurretSpinDownSound, "Turret spin-down sound", definition.Turret.BarrelSpinDownSounds.Count, (int)WeaponTurretBarrelSpinSoundSlot.Count)),
            _ => []
        };
        foreach (WeaponIndexedRowItemViewModel row in selected) yield return row;
    }
    internal static string ModelVariantTitle(string family, int index, IReadOnlyDictionary<int, string> labels) =>
        labels.TryGetValue(index, out string? label)
            ? $"{family} — {label}"
            : $"{family} [{index:00}]";
}

public sealed class WeaponModelSlotItemViewModel : ObservableObject
{
    private WeaponModelSlotState _state;
    internal WeaponModelSlotItemViewModel(WeaponIndexedRowKind kind, int index, string family, string roleLabel, XModelAsset? semanticModel, XModelAsset? resolvedModel, WeaponModelSlotState state, bool isEditableTarget, string? sourceDetail, bool isMalformedStorage = false)
    {
        Kind = kind; Index = index; Family = family; RoleLabel = roleLabel; SemanticModel = semanticModel; ResolvedModel = resolvedModel; _state = state; IsEditableTarget = isEditableTarget; SourceDetail = sourceDetail; IsMalformedStorage = isMalformedStorage;
    }
    public WeaponIndexedRowKind Kind { get; } public int Index { get; } public string Family { get; } public string RoleLabel { get; } internal XModelAsset? SemanticModel { get; } internal XModelAsset? ResolvedModel { get; } public string? SemanticName => SemanticModel?.Name; public WeaponModelSlotState State => _state; public bool IsTablePresent => State is not (WeaponModelSlotState.TableAbsent or WeaponModelSlotState.Malformed); public bool IsEditableTarget { get; } public bool IsMalformedStorage { get; } public string? SourceDetail { get; } public string StableKey => $"{Kind}:{Index}";
    public string StateText => State switch { WeaponModelSlotState.Resolved => "Resolved", WeaponModelSlotState.Empty => "Empty", WeaponModelSlotState.Unresolved => "Unresolved", WeaponModelSlotState.NonRenderable => "Non-renderable", WeaponModelSlotState.TableAbsent => "Table absent", WeaponModelSlotState.Malformed => "Malformed table", _ => State.ToString() };
    public string DisplayName => State switch { WeaponModelSlotState.Resolved or WeaponModelSlotState.NonRenderable => $"{RoleLabel} · {SemanticName ?? "Unnamed XModel"}{(IsMalformedStorage ? " · Malformed table" : string.Empty)}", _ => $"{RoleLabel} · {StateText}" };
    internal bool HasSameReference(WeaponModelSlotItemViewModel other) => ReferenceEquals(ResolvedModel, other.ResolvedModel) && ReferenceEquals(SemanticModel, other.SemanticModel) && ReferenceState(State) == ReferenceState(other.State) && IsMalformedStorage == other.IsMalformedStorage && string.Equals(SemanticName, other.SemanticName, StringComparison.Ordinal);
    internal void SetState(WeaponModelSlotState state) { if (_state == state) return; _state = state; OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(DisplayName)); }
    private static WeaponModelSlotState ReferenceState(WeaponModelSlotState state) => state == WeaponModelSlotState.NonRenderable ? WeaponModelSlotState.Resolved : state;
    public override string ToString() => DisplayName;
}
