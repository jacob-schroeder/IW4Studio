using System.ComponentModel;
using System.Globalization;
using System.Text;
using IW4.AssetExchange.SourceFormat.XAnim;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.XAnim;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.EditorPreview;
using IW4.Render.OpenGl.XModel;
using IW4.Render.SceneBuilding;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.Editors.Weapon;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;
using Material.Icons;

namespace IW4.Studio.Desktop.ViewModels;

public sealed partial class WeaponEditorViewModel : ObservableObject,
    IAssetEditorProperties, IAssetEditorInspectorSource,
    IAssetEditorPropertiesRevealSource, IAssetEditorDiagnostics,
    IAssetEditorStagingState, IDisposable
{
    private const string CamoTableAssetName = "mp/camotable.csv";
    private readonly AssetEditorSession _session;
    private readonly AssetReferencePickerService? _assetReferencePicker;
    private readonly XModelSceneBuilder _sceneBuilder = new();
    private readonly XAnimExchange _xanimExchange = new();
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
    private IReadOnlyList<AssetValidationIssue> _animationDiagnostics = [];
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
    private string _propertySearchText = string.Empty;
    private IReadOnlyList<WeaponSemanticTabItemViewModel> _semanticTabs = [];
    private WeaponSemanticTabItemViewModel? _selectedSemanticTab;
    private IReadOnlyList<WeaponIndexedRowItemViewModel> _visibleIndexedRows = [];
    private IReadOnlyList<InspectorSectionViewModel> _visibleInspectorSections = [];
    private IReadOnlyList<InspectorSectionViewModel> _visibleSidebarInspectorSections = [];
    private IReadOnlyList<WeaponAccuracyGraphItemViewModel> _accuracyGraphs = [];
    private int _selectedAccuracyGraphIndex;
    private IReadOnlyList<WeaponLocationDamageItemViewModel> _locationDamageItems = [];
    private IReadOnlyList<float> _locationDamageMultipliers = [];
    private IReadOnlyList<InspectorPropertyRowViewModel> _visibleLocationDamageInspectorRows = [];
    private int _selectedLocationDamageIndex;
    private IReadOnlyList<WeaponBounceSurfaceItemViewModel> _bounceSurfaces = [];
    private int _selectedBounceSurfaceIndex;
    private bool _isParallelBounceSelected = true;
    private IReadOnlyList<string> _detectedModelTags = [];
    private readonly IReadOnlyList<WeaponPreviewModelFamilyItemViewModel> _previewModelFamilies;
    private readonly IReadOnlyList<WeaponCamoItemViewModel> _camoOptions;
    private WeaponPreviewModelFamilyItemViewModel? _selectedPreviewModelFamily;
    private WeaponCamoItemViewModel? _selectedCamo;
    private bool _isReplacingProjection;
    private bool _isCommittingRows;
    private XAnimPreviewViewModel? _animationPreview;
    private bool _disposed;

    public WeaponEditorViewModel(AssetEditorSession session, AssetReferencePickerService? assetReferencePicker = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != XAssetType.Weapon)
            throw new InvalidDataException("The Weapon view model can host only Weapon editor sessions.");
        _assetReferencePicker = assetReferencePicker;
        _imagePayloads = new WorkspaceGfxImagePayloadResolver(session.Workspace);
        _modelVariantLabels = CaptureModelVariantLabels(session);
        _previewModelFamilies = Array.AsReadOnly(WeaponPreviewModelFamilyItemViewModel.CreateAll());
        _camoOptions = Array.AsReadOnly(Enumerable.Range(0, WeaponDef.GunModelCount)
            .Select(index => new WeaponCamoItemViewModel(
                index,
                _modelVariantLabels.TryGetValue(index, out string? label)
                    ? WeaponSemanticLabels.HumanizeIdentifier(label)
                    : index == 0 ? "None" : $"Camo {index:00}"))
            .ToArray());
        _baseline = session.OpenDraft<WeaponDraft>();
        _workingDraft = _baseline.Copy();
        Categories = Array.AsReadOnly(WeaponCategoryItemViewModel.CreateAll());
        _selectedCategory = Categories[0];
        InitializeCamoAppearance();
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
    public XAnimPreviewViewModel? AnimationPreview => _animationPreview;
    public bool HasAnimationPreview => AnimationPreview is not null;
    public string PreviewStatus { get => _previewStatus; private set => SetProperty(ref _previewStatus, value); }
    public string PreviewMessage { get => _previewMessage; private set => SetProperty(ref _previewMessage, value); }
    public WeaponPreviewState PreviewState { get => _previewState; private set => SetProperty(ref _previewState, value); }
    public bool HasPreviewMessage => !string.IsNullOrEmpty(PreviewMessage);
    public bool HasPreviewDiagnostics =>
        _previewDiagnostics.Count > 0 ||
        _animationDiagnostics.Count > 0 ||
        _rendererDiagnostics.Count > 0;
    public IReadOnlyList<XModelLodItemViewModel> Lods { get => _lods; private set => SetProperty(ref _lods, value); }
    public int SelectedLodIndex => SelectedLod?.LodIndex ?? -1;
    public XModelLodItemViewModel? SelectedLod
    {
        get => _selectedLod;
        set
        {
            if (value is not null && !Lods.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (ReferenceEquals(value, _selectedLod)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            if (AnimationPreview is not null || _animationDiagnostics.Count > 0)
                ClearAnimationPreview(restoreStaticStatus: false);
            _selectedLod = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedLodIndex)); ResetRendererStatus();
            if (Scene is not null && SelectedModelSlot?.ResolvedModel is { } model)
            {
                PreviewMessage = string.Empty;
                PreviewStatus = $"{model.Name} · LOD {SelectedLodIndex} · " +
                    $"{(IsCamoAnimationPreviewEnabled ? "animated camo" : "static selected-model")} preview";
            }
            RefreshCollisionCapability(); RebuildDiagnostics(); NotifyState();
        }
    }
    public bool IsStudioEnvironmentEnabled { get => _isStudioEnvironmentEnabled; set => SetProperty(ref _isStudioEnvironmentEnabled, value); }
    public bool IsWireframeEnabled { get => _isWireframeEnabled; set => SetProperty(ref _isWireframeEnabled, value); }
    public bool IsCollisionEnabled { get => _isCollisionEnabled; set => SetProperty(ref _isCollisionEnabled, value); }
    public bool ShowBoneTags
    {
        get => _showBoneTags;
        set
        {
            if (!SetProperty(ref _showBoneTags, value) || !value) return;
            RefreshDetectedModelTags();
        }
    }
    public bool CanShowCollision => SelectedLod?.Lod.CollisionTriangleCount > 0;
    public IReadOnlyList<WeaponCategoryItemViewModel> Categories { get; }
    public IReadOnlyList<WeaponPreviewModelFamilyItemViewModel> PreviewModelFamilies =>
        _previewModelFamilies;
    public IReadOnlyList<WeaponCamoItemViewModel> CamoOptions => _camoOptions;

    public WeaponPreviewModelFamilyItemViewModel? SelectedPreviewModelFamily
    {
        get => _selectedPreviewModelFamily;
        set
        {
            if (value is not null && !PreviewModelFamilies.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (ReferenceEquals(value, _selectedPreviewModelFamily)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            _selectedPreviewModelFamily = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCamoSelectionEnabled));
            SelectPreviewModelSlot();
        }
    }

    public WeaponCamoItemViewModel? SelectedCamo
    {
        get => _selectedCamo;
        set
        {
            if (value is not null && !CamoOptions.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (ReferenceEquals(value, _selectedCamo)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            _selectedCamo = value;
            OnPropertyChanged();
            if (IsCamoSelectionEnabled) SelectPreviewModelSlot();
        }
    }

    public bool IsCamoSelectionEnabled =>
        SelectedPreviewModelFamily?.SupportsCamo == true;

    public string PropertySearchText
    {
        get => _propertySearchText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _propertySearchText, value)) return;
            RefreshSearchProjection();
        }
    }

    public IReadOnlyList<WeaponIndexedRowItemViewModel> VisibleIndexedRows
    {
        get => _visibleIndexedRows;
        private set => SetProperty(ref _visibleIndexedRows, value);
    }

    public IReadOnlyList<WeaponSemanticTabItemViewModel> SemanticTabs
    {
        get => _semanticTabs;
        private set => SetProperty(ref _semanticTabs, value);
    }

    public WeaponSemanticTabItemViewModel? SelectedSemanticTab
    {
        get => _selectedSemanticTab;
        set
        {
            if (_isReplacingProjection) return;
            if (value is null && SemanticTabs.Count > 0)
            {
                OnPropertyChanged();
                return;
            }
            if ((value is not null && !SemanticTabs.Contains(value)) ||
                ReferenceEquals(value, _selectedSemanticTab)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            _selectedSemanticTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHideTagTabSelected));
            OnPropertyChanged(nameof(SemanticBrowserSubtitle));
            SelectSemanticTabRow();
            RefreshSearchProjection();
        }
    }

    public IReadOnlyList<InspectorSectionViewModel> VisibleInspectorSections
    {
        get => _visibleInspectorSections;
        private set => SetProperty(ref _visibleInspectorSections, value);
    }

    public IReadOnlyList<InspectorSectionViewModel> VisibleSidebarInspectorSections
    {
        get => _visibleSidebarInspectorSections;
        private set => SetProperty(ref _visibleSidebarInspectorSections, value);
    }

    public bool HasVisibleIndexedRows => VisibleIndexedRows.Count > 0;
    public bool ShowsSemanticTabs =>
        SemanticTabs.Count > 1 && string.IsNullOrWhiteSpace(PropertySearchText);
    public string SemanticBrowserSubtitle =>
        string.IsNullOrWhiteSpace(PropertySearchText)
            ? SelectedSemanticTab?.Title ?? string.Empty
            : "All matching slots";
    public bool ShowsSemanticBrowser => HasIndexedRows && !IsLocationDamageCategory;
    public bool HasVisibleInspectorRows =>
        VisibleInspectorSections.Count > 0 ||
        VisibleSidebarInspectorSections.Count > 0 ||
        VisibleLocationDamageInspectorRows.Count > 0;
    public bool HasVisibleSidebarInspectorRows =>
        VisibleSidebarInspectorSections.Count > 0;
    public bool HasPropertySidebarContent =>
        HasIndexedRows ||
        HasVisibleSidebarInspectorRows ||
        HasVisibleLocationDamageInspectorRows;
    public bool IsAccuracyCategory =>
        SelectedCategory.Id == WeaponPropertyCategory.KickRecoilAndAccuracy;
    public bool IsLocationDamageCategory =>
        SelectedCategory.Id == WeaponPropertyCategory.DamageRangeAndAiTuning;
    public bool IsBounceCategory =>
        SelectedCategory.Id == WeaponPropertyCategory.PhysicsAndProjectile;
    public bool IsHideTagsCategory =>
        SelectedCategory.Id == WeaponPropertyCategory.HideTagsAndNoteTracks;
    public bool IsHideTagTabSelected =>
        SelectedSemanticTab?.Rows.Any(row =>
            row.Kind == WeaponIndexedRowKind.HideTag) == true;

    public IReadOnlyList<WeaponAccuracyGraphItemViewModel> AccuracyGraphs
    {
        get => _accuracyGraphs;
        private set => SetProperty(ref _accuracyGraphs, value);
    }

    public int SelectedAccuracyGraphIndex
    {
        get => _selectedAccuracyGraphIndex;
        set
        {
            if (value < 0 || value >= AccuracyGraphs.Count ||
                value == _selectedAccuracyGraphIndex) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            _selectedAccuracyGraphIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAccuracyGraph));
            SelectAccuracyGraph(AccuracyGraphs[value].RowKind);
        }
    }

    public WeaponAccuracyGraphItemViewModel? SelectedAccuracyGraph =>
        SelectedAccuracyGraphIndex >= 0 &&
        SelectedAccuracyGraphIndex < AccuracyGraphs.Count
            ? AccuracyGraphs[SelectedAccuracyGraphIndex]
            : null;

    public IReadOnlyList<WeaponLocationDamageItemViewModel> LocationDamageItems
    {
        get => _locationDamageItems;
        private set => SetProperty(ref _locationDamageItems, value);
    }

    public IReadOnlyList<float> LocationDamageMultipliers
    {
        get => _locationDamageMultipliers;
        private set => SetProperty(ref _locationDamageMultipliers, value);
    }

    public IReadOnlyList<InspectorPropertyRowViewModel> VisibleLocationDamageInspectorRows
    {
        get => _visibleLocationDamageInspectorRows;
        private set => SetProperty(ref _visibleLocationDamageInspectorRows, value);
    }

    public bool HasVisibleLocationDamageInspectorRows =>
        VisibleLocationDamageInspectorRows.Count > 0;

    public int SelectedLocationDamageIndex
    {
        get => _selectedLocationDamageIndex;
        set
        {
            if (value < 0 || value >= LocationDamageItems.Count ||
                value == _selectedLocationDamageIndex) return;
            _selectedLocationDamageIndex = value;
            OnPropertyChanged();
            SelectIndexedRow(WeaponIndexedRowKind.LocationDamage, value);
        }
    }

    public IReadOnlyList<WeaponBounceSurfaceItemViewModel> BounceSurfaces
    {
        get => _bounceSurfaces;
        private set => SetProperty(ref _bounceSurfaces, value);
    }

    public int SelectedBounceSurfaceIndex
    {
        get => _selectedBounceSurfaceIndex;
        set
        {
            if (value < 0 || value >= BounceSurfaces.Count ||
                value == _selectedBounceSurfaceIndex) return;
            _selectedBounceSurfaceIndex = value;
            OnPropertyChanged();
            SelectBounceIndexedRow();
        }
    }

    public bool IsParallelBounceSelected
    {
        get => _isParallelBounceSelected;
        set
        {
            if (!SetProperty(ref _isParallelBounceSelected, value)) return;
            OnPropertyChanged(nameof(IsPerpendicularBounceSelected));
            SelectBounceIndexedRow();
        }
    }

    public bool IsPerpendicularBounceSelected
    {
        get => !IsParallelBounceSelected;
        set
        {
            if (value) IsParallelBounceSelected = false;
        }
    }

    public IReadOnlyList<string> DetectedModelTags
    {
        get => _detectedModelTags;
        private set => SetProperty(ref _detectedModelTags, value);
    }

    public bool HasDetectedModelTags => DetectedModelTags.Count > 0;
    public bool CanFindModelTags => Scene is not null && HasDetectedModelTags;
    public string DetectedModelTagCountText =>
        DetectedModelTags.Count == 1
            ? "1 model tag"
            : $"{DetectedModelTags.Count} model tags";

    public WeaponCategoryItemViewModel SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_isReplacingProjection || !Categories.Contains(value) || ReferenceEquals(value, _selectedCategory)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            if (AnimationPreview is not null || _animationDiagnostics.Count > 0)
                ClearAnimationPreview(restoreStaticStatus: true);
            _selectedCategory = value; OnPropertyChanged(); NotifyCategoryPresentation();
            RebuildIndexedRows(null); RefreshInspector();
        }
    }
    public IReadOnlyList<WeaponIndexedRowItemViewModel> IndexedRows { get => _indexedRows; private set => SetProperty(ref _indexedRows, value); }
    public bool HasIndexedRows => IndexedRows.Count > 0;
    public bool HasSelectedIndexedRow => SelectedIndexedRow is not null;
    public WeaponIndexedRowItemViewModel? SelectedIndexedRow
    {
        get => _selectedIndexedRow;
        set
        {
            if (_isReplacingProjection) return;
            if (value is null && IndexedRows.Count > 0)
            {
                OnPropertyChanged();
                return;
            }
            if ((value is not null && !IndexedRows.Contains(value)) ||
                ReferenceEquals(value, _selectedIndexedRow)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            if (AnimationPreview is not null || _animationDiagnostics.Count > 0)
                ClearAnimationPreview(restoreStaticStatus: true);
            _selectedIndexedRow = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedIndexedRow)); SyncSelectedSemanticTab(); SyncSelectedModelSlot(); RefreshInspector();
        }
    }
    public IReadOnlyList<WeaponModelSlotItemViewModel> ModelSlots { get => _modelSlots; private set => SetProperty(ref _modelSlots, value); }
    public WeaponModelSlotItemViewModel? SelectedModelSlot
    {
        get => _selectedModelSlot;
        set
        {
            if (_isReplacingProjection || (value is not null && !ModelSlots.Contains(value)) || ReferenceEquals(value, _selectedModelSlot)) return;
            if (!TryCommitInspectorRows()) { OnPropertyChanged(); return; }
            _selectedModelSlot = value; OnPropertyChanged();
            SyncPreviewSelectors();
            if (value is not null)
            {
                WeaponCategoryItemViewModel modelsCategory = Categories.Single(category =>
                    category.Id == WeaponPropertyCategory.Models);
                if (!ReferenceEquals(_selectedCategory, modelsCategory))
                {
                    _selectedCategory = modelsCategory;
                    OnPropertyChanged(nameof(SelectedCategory));
                    NotifyCategoryPresentation();
                    RebuildIndexedRows(value.StableKey);
                }
                _selectedIndexedRow = IndexedRows.FirstOrDefault(row => row.Kind == value.Kind && row.Index == value.Index);
                OnPropertyChanged(nameof(SelectedIndexedRow)); OnPropertyChanged(nameof(HasSelectedIndexedRow)); SyncSelectedSemanticTab(); RefreshInspector();
            }
            RebuildSelectedModelPreview();
            RefreshCamoAppearanceProjection();
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
    public string CandidateState => !HasDefinition ? "Invalid" : !IsEditable ? "Read-only" : HasCamoErrors || _candidateDiagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error) ? "Invalid" : HasUnappliedChanges ? "Modified" : "Unchanged";
    public bool HasErrors => Diagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error);
    public bool HasUnappliedChanges => StagedRowsHaveInput || !_session.CandidateMatchesCurrent(_workingDraft);
    public bool CanApply => IsEditable && HasUnappliedChanges && !HasStagedErrors && !HasCamoErrors && !_candidateDiagnostics.Any(issue => issue.Severity == AssetValidationSeverity.Error);
    public bool CanRevert => IsEditable && HasUnappliedChanges;

    public void ApplyDraft()
    {
        if (!IsEditable || !TryCommitInspectorRows()) return;
        RefreshValidation();
        if (HasCamoErrors || _candidateDiagnostics.Any(issue =>
                issue.Severity == AssetValidationSeverity.Error))
        {
            RevealProperties();
            return;
        }
        try
        {
            if (_pendingCamoCompile is { } compiled)
            {
                if (!_session.ApplyCompiledWeapon(
                        _workingDraft,
                        compiled.Providers,
                        out IReadOnlyList<AssetValidationIssue> issues))
                {
                    SetCamoDiagnostics(issues);
                    if (issues.Any(issue =>
                            issue.Severity == AssetValidationSeverity.Error))
                    {
                        RevealProperties();
                        return;
                    }
                }
            }
            else
            {
                _ = _session.Apply<WeaponDraft>(draft =>
                    draft.ReplaceWith(_workingDraft));
            }
            ClearCamoAppearanceDraft();
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
        ClearCamoAppearanceDraft();
        _workingDraft = _baseline.Copy(); RefreshAll();
    }

    public void AssignDetectedModelTag(string tagName)
    {
        if (!IsEditable || string.IsNullOrWhiteSpace(tagName) ||
            SelectedIndexedRow is not
            {
                Kind: WeaponIndexedRowKind.HideTag,
                CanEditValue: true
            } row)
        {
            return;
        }

        string value = tagName.Trim();
        Mutate(() => _workingDraft.SetVariantHideTags(
            row.Index,
            new ScriptStringReference(
                0,
                value,
                ScriptStringHandle.Null,
                default)));
    }

    internal void PreviewAnimation(
        InspectorAssetReferencePropertyRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.AssetType != XAssetType.XAnim ||
            string.IsNullOrWhiteSpace(row.AssetName))
        {
            return;
        }

        string animationName = row.AssetName!;
        if (SelectedModelSlot?.ResolvedModel is not { } model ||
            Scene is null ||
            SelectedLod is null)
        {
            CompleteAnimationPreviewFailure(
                "Select a resolved XModel in the preview before starting this XAnim.");
            return;
        }

        if (!SelectedLod.Lod.HasCompleteSkinning)
        {
            CompleteAnimationPreviewFailure(
                $"LOD {SelectedLod.LodIndex} of '{model.Name}' does not contain a complete native skinning payload.");
            return;
        }

        if (!TryResolveAnimation(animationName, out XAnimPartsAsset? animation) ||
            animation is null)
        {
            CompleteAnimationPreviewFailure(
                $"XAnim '{animationName}' is unresolved in the current workspace.");
            return;
        }

        try
        {
            XAnimPlaybackClip clip = _xanimExchange.Decode(animation);
            if (!XAnimPreviewScene.TryCreate(
                    clip,
                    model,
                    out XAnimPreviewScene? previewScene,
                    out string reason) ||
                previewScene is null)
            {
                CompleteAnimationPreviewFailure(
                    $"XAnim '{animationName}' cannot animate '{model.Name}': {reason}");
                return;
            }

            var preview = new XAnimPreviewViewModel(
                animation,
                clip,
                [previewScene]);
            ReplaceAnimationPreview(preview);
            _animationDiagnostics = [];
            PreviewMessage = string.Empty;
            PreviewStatus = CreateAnimationPreviewStatus(model.Name, preview);
            RebuildDiagnostics();
            NotifyState();
            preview.TogglePlayback();
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   InvalidDataException or
                   ArgumentException or
                   OverflowException)
        {
            CompleteAnimationPreviewFailure(
                $"XAnim '{animationName}' preview failed: {exception.Message}");
        }
    }

    internal void ToggleAnimationPreview() =>
        AnimationPreview?.TogglePlayback();

    internal void RestartAnimationPreview() =>
        AnimationPreview?.RestartPlayback();

    internal void PauseAnimationPreview() =>
        AnimationPreview?.PausePlayback();

    internal void StopAnimationPreview() =>
        ClearAnimationPreview(restoreStaticStatus: true);

    internal void Mutate(Action mutation, bool rebuildInspector = true)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (SelectedCategory.Id == WeaponPropertyCategory.AnimationNames)
            ClearAnimationPreview(restoreStaticStatus: true);
        mutation(); RefreshValidation(); RebuildModelSlots();
        if (!_isCommittingRows)
        {
            if (rebuildInspector) RefreshInspector();
            else RefreshSemanticVisualizations();
        }
        NotifyState();
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
            CompleteCamoModelMutation(stableKey);
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
            if (AnimationPreview is { } animationPreview)
            {
                PreviewStatus = CreateAnimationPreviewStatus(
                    model.Name,
                    animationPreview);
            }
            else
            {
                int totalGroups =
                    (uploadResult?.ExecutableGroupCount ?? 0) +
                    (uploadResult?.BlockedGroupCount ?? 0);
                PreviewStatus = uploadResult is null
                    ? $"{model.Name} · LOD {SelectedLodIndex} · " +
                        $"{(IsCamoAnimationPreviewEnabled ? "animated camo" : "static selected-model")} preview"
                    : $"{model.Name} · LOD {SelectedLodIndex} · {uploadResult.ExecutableGroupCount}/{totalGroups} render groups executable";
            }
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
        SyncPreviewSelectors();
        RefreshCamoAppearanceProjection();
    }

    private void SyncPreviewSelectors()
    {
        WeaponPreviewModelFamilyItemViewModel? family = _selectedModelSlot is null
            ? PreviewModelFamilies.FirstOrDefault()
            : PreviewModelFamilies.FirstOrDefault(candidate =>
                candidate.RowKind == _selectedModelSlot.Kind);
        WeaponCamoItemViewModel? camo = CamoOptions.FirstOrDefault(candidate =>
            candidate.Index == (_selectedModelSlot is
            {
                Kind: WeaponIndexedRowKind.GunModel or WeaponIndexedRowKind.WorldGunModel
            } ? _selectedModelSlot.Index : 0));
        if (!ReferenceEquals(_selectedPreviewModelFamily, family))
        {
            _selectedPreviewModelFamily = family;
            OnPropertyChanged(nameof(SelectedPreviewModelFamily));
            OnPropertyChanged(nameof(IsCamoSelectionEnabled));
        }
        if (!ReferenceEquals(_selectedCamo, camo))
        {
            _selectedCamo = camo;
            OnPropertyChanged(nameof(SelectedCamo));
        }
    }

    private void SelectPreviewModelSlot()
    {
        if (SelectedPreviewModelFamily is not { } family) return;
        int index = family.SupportsCamo ? SelectedCamo?.Index ?? 0 : 0;
        WeaponModelSlotItemViewModel? slot = ModelSlots.FirstOrDefault(candidate =>
            candidate.Kind == family.RowKind && candidate.Index == index);
        if (ReferenceEquals(_selectedModelSlot, slot)) return;
        _selectedModelSlot = slot;
        OnPropertyChanged(nameof(SelectedModelSlot));
        RebuildSelectedModelPreview();
        RefreshCamoAppearanceProjection();
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
        if (IsAuthoredCamoModel(semanticModel))
        {
            return new(
                kind,
                index,
                family,
                roleLabel,
                semanticModel,
                semanticModel,
                WeaponModelSlotState.Resolved,
                IsEditable && !malformedStorage,
                "Authored in this Weapon draft",
                malformedStorage);
        }
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

    private bool TryResolveAnimation(
        string name,
        out XAnimPartsAsset? resolved)
    {
        var pool = _session.Workspace.LoadedZone.Context.AssetPool;
        resolved = null;
        if (!pool.TryResolve(
                XAssetType.XAnim,
                name,
                out XAnimPartsAsset? current) ||
            current is null ||
            current.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !pool.TryGetSlot(address, out var slot) ||
            slot is null ||
            slot.ActiveProvider.IsReferencePlaceholder)
        {
            return false;
        }

        resolved = current;
        return true;
    }

    private void ReplaceAnimationPreview(
        XAnimPreviewViewModel? preview)
    {
        if (ReferenceEquals(_animationPreview, preview))
            return;

        XAnimPreviewViewModel? previous = _animationPreview;
        _animationPreview = preview;
        previous?.Dispose();
        OnPropertyChanged(nameof(AnimationPreview));
        OnPropertyChanged(nameof(HasAnimationPreview));
    }

    private static string CreateAnimationPreviewStatus(
        string? modelName,
        XAnimPreviewViewModel preview)
    {
        string displayModelName = string.IsNullOrWhiteSpace(modelName)
            ? "<unnamed XModel>"
            : modelName;
        XAnimPreviewScene? scene = preview.SelectedScene;
        return scene is null
            ? Bounded($"{displayModelName} · {preview.Name}")
            : Bounded(
                $"{displayModelName} · {preview.Name} · " +
                $"{scene.MatchedTrackCount}/{preview.BoneCount} bone tracks matched");
    }

    private void CompleteAnimationPreviewFailure(string message)
    {
        ReplaceAnimationPreview(null);
        _animationDiagnostics =
        [
            new AssetValidationIssue(
                "weapon.preview.animation",
                message,
                AssetValidationSeverity.Warning)
        ];
        if (Scene is not null)
        {
            PreviewMessage = "Animation preview unavailable";
            PreviewStatus = Bounded(message);
        }
        RebuildDiagnostics();
        NotifyState();
    }

    private void ClearAnimationPreview(bool restoreStaticStatus)
    {
        ReplaceAnimationPreview(null);
        _animationDiagnostics = [];
        if (restoreStaticStatus &&
            SelectedModelSlot?.ResolvedModel is { } model &&
            Scene is not null)
        {
            PreviewMessage = string.Empty;
            PreviewStatus =
                $"{model.Name} · LOD {SelectedLodIndex} · " +
                $"{(IsCamoAnimationPreviewEnabled ? "animated camo" : "static selected-model")} preview";
        }
        RebuildDiagnostics();
        NotifyState();
    }

    private void RebuildIndexedRows(string? preferredKey)
    {
        _isReplacingProjection = true;
        try
        {
            IndexedRows = Array.AsReadOnly(WeaponIndexedRowItemViewModel.Create(_workingDraft, SelectedCategory.Id, _modelVariantLabels).ToArray());
            _selectedIndexedRow = IndexedRows.FirstOrDefault(row => row.StableKey == preferredKey) ?? IndexedRows.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedIndexedRow));
            OnPropertyChanged(nameof(HasSelectedIndexedRow));
            OnPropertyChanged(nameof(HasIndexedRows));
            OnPropertyChanged(nameof(HasPropertySidebarContent));
            RebuildSemanticTabs();
            SyncSelectedModelSlot();
        }
        finally { _isReplacingProjection = false; }
    }
    private void RebuildSemanticTabs()
    {
        WeaponSemanticTabItemViewModel Tab(
            string key,
            string title,
            Func<WeaponIndexedRowItemViewModel, bool> belongsToTab,
            WeaponIndexedRowKind? selectionKind = null) =>
            new(key, title, IndexedRows.Where(belongsToTab), selectionKind);

        IEnumerable<WeaponSemanticTabItemViewModel> projected = SelectedCategory.Id switch
        {
            WeaponPropertyCategory.Models =>
            [
                Tab("models.view", "View models", row =>
                    row.Kind == WeaponIndexedRowKind.GunModel),
                Tab("models.world", "World models", row =>
                    row.Kind == WeaponIndexedRowKind.WorldGunModel),
                Tab("models.single", "Single models", row =>
                    row.Kind.IsModel() && row.Kind is not
                        (WeaponIndexedRowKind.GunModel or
                            WeaponIndexedRowKind.WorldGunModel))
            ],
            WeaponPropertyCategory.AnimationNames =>
            [
                Tab("animations.weapon", "Weapon", row =>
                    row.Kind == WeaponIndexedRowKind.VariantAnimation),
                Tab("animations.right-hand", "Right hand", row =>
                    row.Kind == WeaponIndexedRowKind.RightAnimation),
                Tab("animations.left-hand", "Left hand", row =>
                    row.Kind == WeaponIndexedRowKind.LeftAnimation)
            ],
            WeaponPropertyCategory.HideTagsAndNoteTracks =>
            [
                Tab("note-tracks.hide-tags", "Hide tags", row =>
                    row.Kind == WeaponIndexedRowKind.HideTag),
                Tab("note-tracks.sound", "Sound notetracks", row =>
                    row.Kind == WeaponIndexedRowKind.SoundNoteMapping),
                Tab("note-tracks.rumble", "Rumble notetracks", row =>
                    row.Kind == WeaponIndexedRowKind.RumbleNoteMapping)
            ],
            WeaponPropertyCategory.KickRecoilAndAccuracy =>
            [
                Tab("accuracy.ai-vs-ai-current", "AI vs. AI · Current", row =>
                    row.Kind == WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph,
                    WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph),
                Tab("accuracy.ai-vs-player-current", "AI vs. player · Current", row =>
                    row.Kind == WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph,
                    WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph),
                Tab("accuracy.ai-vs-ai-original", "AI vs. AI · Original", row =>
                    row.Kind == WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph,
                    WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph),
                Tab("accuracy.ai-vs-player-original", "AI vs. player · Original", row =>
                    row.Kind == WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph,
                    WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph)
            ],
            WeaponPropertyCategory.PhysicsAndProjectile =>
            [
                Tab("bounce.parallel", "Parallel", row =>
                    row.Kind == WeaponIndexedRowKind.ProjectileParallelBounce),
                Tab("bounce.perpendicular", "Perpendicular", row =>
                    row.Kind == WeaponIndexedRowKind.ProjectilePerpendicularBounce)
            ],
            WeaponPropertyCategory.SoundsAndBounce =>
            [
                Tab("sounds.surface-bounce", "Surface bounce", row =>
                    row.Kind == WeaponIndexedRowKind.BounceSound)
            ],
            WeaponPropertyCategory.TurretAndMissile =>
            [
                Tab("turret.spin-up", "Spin up", row =>
                    row.Kind == WeaponIndexedRowKind.TurretSpinUpSound),
                Tab("turret.spin-down", "Spin down", row =>
                    row.Kind == WeaponIndexedRowKind.TurretSpinDownSound)
            ],
            WeaponPropertyCategory.DamageRangeAndAiTuning =>
            [
                Tab("damage.locations", "Locations", row =>
                    row.Kind == WeaponIndexedRowKind.LocationDamage)
            ],
            _ => []
        };

        string? preferredKey = _selectedSemanticTab?.Key;
        SemanticTabs = Array.AsReadOnly(projected
            .Where(tab => tab.Rows.Count > 0 || tab.SelectionKind is not null)
            .ToArray());
        _selectedSemanticTab = _selectedIndexedRow is null
            ? SemanticTabs.FirstOrDefault(tab => tab.Key == preferredKey) ??
                SemanticTabs.FirstOrDefault()
            : SemanticTabs.FirstOrDefault(tab =>
                tab.Rows.Contains(_selectedIndexedRow)) ??
                SemanticTabs.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedSemanticTab));
        OnPropertyChanged(nameof(ShowsSemanticTabs));
        OnPropertyChanged(nameof(SemanticBrowserSubtitle));
        OnPropertyChanged(nameof(ShowsSemanticBrowser));
        OnPropertyChanged(nameof(IsHideTagTabSelected));
    }
    private void SyncSelectedSemanticTab()
    {
        WeaponSemanticTabItemViewModel? selected = _selectedIndexedRow is null
            ? SemanticTabs.FirstOrDefault(tab =>
                ReferenceEquals(tab, _selectedSemanticTab)) ??
                SemanticTabs.FirstOrDefault()
            : SemanticTabs.FirstOrDefault(tab =>
                tab.Rows.Contains(_selectedIndexedRow));
        if (ReferenceEquals(selected, _selectedSemanticTab)) return;
        _selectedSemanticTab = selected;
        OnPropertyChanged(nameof(SelectedSemanticTab));
        OnPropertyChanged(nameof(IsHideTagTabSelected));
        OnPropertyChanged(nameof(SemanticBrowserSubtitle));
    }
    private void SelectSemanticTabRow()
    {
        if (_selectedSemanticTab is not { } tab) return;
        if (tab.Rows.Count == 0)
        {
            if (tab.SelectionKind is { } selectionKind)
                SelectEmptyAccuracyGraph(selectionKind);
            return;
        }
        int preferredIndex = SelectedIndexedRow?.Index ?? tab.Rows[0].Index;
        if (SelectedIndexedRow?.Kind is not
                (WeaponIndexedRowKind.ProjectileParallelBounce or
                    WeaponIndexedRowKind.ProjectilePerpendicularBounce or
                    WeaponIndexedRowKind.BounceSound) &&
            tab.Rows[0].Kind is
                WeaponIndexedRowKind.ProjectileParallelBounce or
                WeaponIndexedRowKind.ProjectilePerpendicularBounce or
                WeaponIndexedRowKind.BounceSound)
            preferredIndex = _selectedBounceSurfaceIndex;
        WeaponIndexedRowItemViewModel row = tab.Rows.FirstOrDefault(candidate =>
            candidate.Index == preferredIndex) ?? tab.Rows[0];
        if (!ReferenceEquals(row, SelectedIndexedRow)) SelectedIndexedRow = row;
    }

    private void SelectAccuracyGraph(WeaponIndexedRowKind kind)
    {
        WeaponIndexedRowItemViewModel? row = IndexedRows.FirstOrDefault(candidate =>
            candidate.Kind == kind && candidate.Index == 0);
        if (row is not null)
        {
            if (!ReferenceEquals(row, SelectedIndexedRow)) SelectedIndexedRow = row;
            return;
        }

        WeaponSemanticTabItemViewModel? tab = SemanticTabs.FirstOrDefault(candidate =>
            candidate.SelectionKind == kind);
        if (!ReferenceEquals(tab, _selectedSemanticTab))
        {
            _selectedSemanticTab = tab;
            OnPropertyChanged(nameof(SelectedSemanticTab));
            OnPropertyChanged(nameof(IsHideTagTabSelected));
            OnPropertyChanged(nameof(SemanticBrowserSubtitle));
        }
        if (_selectedIndexedRow is not null)
        {
            _selectedIndexedRow = null;
            OnPropertyChanged(nameof(SelectedIndexedRow));
            OnPropertyChanged(nameof(HasSelectedIndexedRow));
        }
        RefreshInspector();
    }

    private void SelectEmptyAccuracyGraph(WeaponIndexedRowKind kind)
    {
        int graphIndex = AccuracyGraphs.ToList().FindIndex(graph =>
            graph.RowKind == kind);
        if (graphIndex >= 0 && graphIndex != _selectedAccuracyGraphIndex)
        {
            _selectedAccuracyGraphIndex = graphIndex;
            OnPropertyChanged(nameof(SelectedAccuracyGraphIndex));
            OnPropertyChanged(nameof(SelectedAccuracyGraph));
        }
        if (_selectedIndexedRow is null) return;
        _selectedIndexedRow = null;
        OnPropertyChanged(nameof(SelectedIndexedRow));
        OnPropertyChanged(nameof(HasSelectedIndexedRow));
        RefreshInspector();
    }
    private void SyncSelectedModelSlot()
    {
        if (SelectedIndexedRow is not { } row || !row.Kind.IsModel()) return;
        _selectedModelSlot = ModelSlots.FirstOrDefault(slot => slot.Kind == row.Kind && slot.Index == row.Index); OnPropertyChanged(nameof(SelectedModelSlot));
        SyncPreviewSelectors();
        if (!_isReplacingProjection) RebuildSelectedModelPreview();
        RefreshCamoAppearanceProjection();
    }
    private void RebuildSelectedModelPreview()
    {
        if (AnimationPreview is not null || _animationDiagnostics.Count > 0)
            ClearAnimationPreview(restoreStaticStatus: false);
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
                    _imagePayloads,
                    CaptureCamoPreviewProviders());
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
            _previewStatus = $"{model.Name} · LOD {_selectedLod?.LodIndex ?? -1} · {(IsCamoAnimationPreviewEnabled ? "animated camo" : "static selected-model")} preview";
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
        RefreshDetectedModelTags();
        RefreshCollisionCapability(); RebuildDiagnostics(); NotifyState();
    }
    private void RefreshInspector()
    {
        foreach (INotifyPropertyChanged row in _stagedRows) row.PropertyChanged -= StagedRow_PropertyChanged; _stagedRows.Clear(); InspectorSelection = WeaponInspectorProjection.Create(this);
        if (InspectorSelection is not null) foreach (INotifyPropertyChanged row in InspectorSelection.Sections.SelectMany(section => section.Rows).OfType<INotifyPropertyChanged>()) if (row is IInspectorStagedPropertyRow) { _stagedRows.Add(row); row.PropertyChanged += StagedRow_PropertyChanged; }
        RefreshSemanticVisualizations();
        RefreshSearchProjection();
        NotifyState();
    }

    private void RefreshSearchProjection()
    {
        string query = PropertySearchText.Trim();
        IReadOnlyList<WeaponIndexedRowItemViewModel> indexedSource =
            string.IsNullOrEmpty(query)
                ? SelectedSemanticTab?.Rows ?? IndexedRows
                : IndexedRows;
        bool Matches(string? value) =>
            string.IsNullOrEmpty(query) ||
            value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

        VisibleIndexedRows = Array.AsReadOnly(indexedSource
            .Where(row => Matches(row.Title) || Matches(row.Detail))
            .ToArray());
        VisibleLocationDamageInspectorRows = InspectorSelection is null
            ? []
            : Array.AsReadOnly(InspectorSelection.Sections
                .SelectMany(section => section.Rows)
                .Where(IsLocationDamageInspectorRow)
                .Where(row =>
                    Matches(row.Label) ||
                    Matches(row.FieldPath) ||
                    Matches(row.Description))
                .ToArray());
        IReadOnlyList<InspectorSectionViewModel> cards = InspectorSelection is null
            ? []
            : Array.AsReadOnly(InspectorSelection.Sections
                .SelectMany(section => section.Rows
                    .Where(row => !IsLocationDamageInspectorRow(row))
                    .Select(row => new
                    {
                        Title = InspectorCardTitle(row, section.Title),
                        Row = row,
                        section.IsExpanded
                    }))
                .GroupBy(item => item.Title, StringComparer.Ordinal)
                .Select(group => new InspectorSectionViewModel(
                    group.Key,
                    group.Select(item => item.Row),
                    group.First().IsExpanded))
                .ToArray());
        InspectorSectionViewModel[] visibleCards = cards
            .Select(section => new InspectorSectionViewModel(
                section.Title,
                section.Rows.Where(row =>
                    Matches(row.Label) ||
                    Matches(row.FieldPath) ||
                    Matches(row.Description)),
                section.IsExpanded))
            .Where(section => section.Rows.Count > 0)
            .ToArray();
        VisibleInspectorSections = Array.AsReadOnly(visibleCards
            .Where(section => !UsesInspectorSidebar(section.Title, cards.Count))
            .ToArray());
        VisibleSidebarInspectorSections = Array.AsReadOnly(visibleCards
            .Where(section => UsesInspectorSidebar(section.Title, cards.Count))
            .ToArray());
        OnPropertyChanged(nameof(HasVisibleIndexedRows));
        OnPropertyChanged(nameof(HasVisibleInspectorRows));
        OnPropertyChanged(nameof(HasVisibleSidebarInspectorRows));
        OnPropertyChanged(nameof(HasVisibleLocationDamageInspectorRows));
        OnPropertyChanged(nameof(HasPropertySidebarContent));
        OnPropertyChanged(nameof(ShowsSemanticTabs));
        OnPropertyChanged(nameof(SemanticBrowserSubtitle));
    }

    private bool IsLocationDamageInspectorRow(
        InspectorPropertyRowViewModel row) =>
        IsLocationDamageCategory &&
        SelectedIndexedRow?.Kind == WeaponIndexedRowKind.LocationDamage &&
        (row.FieldPath.StartsWith(
             "weapon.definition.locationDamageMultipliers[",
             StringComparison.Ordinal) ||
         row.FieldPath == "weapon.selection.storage");

    private bool UsesInspectorSidebar(string title, int cardCount)
    {
        if (HasIndexedRows)
            return cardCount > 1 &&
                title.StartsWith("Selected ", StringComparison.Ordinal);

        return SelectedCategory.Id switch
        {
            WeaponPropertyCategory.Overview => title is
                "Variant identity" or "Definition identity" or "Native identity",
            WeaponPropertyCategory.ClassificationAndReticle => title == "Reticle",
            WeaponPropertyCategory.ViewAndPositionalMovement => title == "Positional movement",
            WeaponPropertyCategory.HudIconsAndAmmo => title is "HUD icons" or "Variant HUD",
            WeaponPropertyCategory.Timing => title is "Night-vision timing" or "Fuse timing",
            WeaponPropertyCategory.AimAndMovementTuning => title == "Movement and zoom",
            WeaponPropertyCategory.OverlayAdsAndSpread => title is
                "Overlay and reticle" or "ADS sway and error",
            WeaponPropertyCategory.EffectsAndMaterials => title == "Shell-eject effects",
            WeaponPropertyCategory.HintsAndRumble => title == "Rumble",
            WeaponPropertyCategory.TailAndPreservedStorage => title is
                "Variant flags" or "Native storage",
            _ => false
        };
    }

    private string InspectorCardTitle(
        InspectorPropertyRowViewModel row,
        string fallbackTitle)
    {
        string path = row.FieldPath;
        if (!HasDefinition) return fallbackTitle;
        if (IsSelectedIndexedRowProperty(path)) return SelectedSlotCardTitle();

        return SelectedCategory.Id switch
        {
            WeaponPropertyCategory.Overview => OverviewCardTitle(path),
            WeaponPropertyCategory.ClassificationAndReticle =>
                path.StartsWith("weapon.definition.reticle.", StringComparison.Ordinal)
                    ? "Reticle"
                    : "Weapon classification",
            WeaponPropertyCategory.ViewAndPositionalMovement =>
                path.StartsWith("weapon.definition.positionalMovement.", StringComparison.Ordinal)
                    ? "Positional movement"
                    : "View movement",
            WeaponPropertyCategory.HudIconsAndAmmo => HudAndAmmoCardTitle(path),
            WeaponPropertyCategory.Timing => TimingCardTitle(path),
            WeaponPropertyCategory.AimAndMovementTuning => AimMovementCardTitle(path),
            WeaponPropertyCategory.OverlayAdsAndSpread => OverlayAdsCardTitle(path),
            WeaponPropertyCategory.PhysicsAndProjectile => PhysicsCardTitle(path),
            WeaponPropertyCategory.KickRecoilAndAccuracy => RecoilCardTitle(path),
            WeaponPropertyCategory.DamageRangeAndAiTuning => DamageCardTitle(path),
            WeaponPropertyCategory.EffectsAndMaterials =>
                path.StartsWith("weapon.definition.shellEjectEffects.", StringComparison.Ordinal)
                    ? "Shell-eject effects"
                    : "Flash effects",
            WeaponPropertyCategory.SoundsAndBounce => SoundCardTitle(path),
            WeaponPropertyCategory.HintsAndRumble =>
                path.StartsWith("weapon.definition.rumble.", StringComparison.Ordinal)
                    ? "Rumble"
                    : "Hints and feedback",
            WeaponPropertyCategory.TurretAndMissile => TurretCardTitle(path),
            WeaponPropertyCategory.TailAndPreservedStorage => StorageCardTitle(path),
            _ => fallbackTitle
        };
    }

    private bool IsSelectedIndexedRowProperty(string path)
    {
        if (path == "weapon.selection" ||
            path.StartsWith("weapon.selection.", StringComparison.Ordinal))
            return true;
        if (SelectedIndexedRow is not { } selected) return false;
        if (SelectedCategory.Id is WeaponPropertyCategory.Models or
            WeaponPropertyCategory.AnimationNames or
            WeaponPropertyCategory.HideTagsAndNoteTracks)
            return true;

        return selected.Kind switch
        {
            WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph or
            WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph or
            WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph or
            WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph =>
                path.Contains("GraphKnots[", StringComparison.Ordinal),
            WeaponIndexedRowKind.ProjectileParallelBounce or
            WeaponIndexedRowKind.ProjectilePerpendicularBounce =>
                path.StartsWith("weapon.definition.projectile.parallelBounce[", StringComparison.Ordinal) ||
                path.StartsWith("weapon.definition.projectile.perpendicularBounce[", StringComparison.Ordinal),
            WeaponIndexedRowKind.BounceSound =>
                path.StartsWith("weapon.definition.bounceSounds[", StringComparison.Ordinal),
            WeaponIndexedRowKind.TurretSpinUpSound =>
                path.StartsWith("weapon.definition.turret.barrelSpinUpSounds[", StringComparison.Ordinal),
            WeaponIndexedRowKind.TurretSpinDownSound =>
                path.StartsWith("weapon.definition.turret.barrelSpinDownSounds[", StringComparison.Ordinal),
            _ => false
        };
    }

    private string SelectedSlotCardTitle() => SelectedIndexedRow?.Kind switch
    {
        WeaponIndexedRowKind.GunModel or
        WeaponIndexedRowKind.HandModel or
        WeaponIndexedRowKind.WorldGunModel or
        WeaponIndexedRowKind.WorldClipModel or
        WeaponIndexedRowKind.RocketModel or
        WeaponIndexedRowKind.KnifeModel or
        WeaponIndexedRowKind.WorldKnifeModel or
        WeaponIndexedRowKind.ProjectileModel => "Selected model",
        WeaponIndexedRowKind.VariantAnimation or
        WeaponIndexedRowKind.RightAnimation or
        WeaponIndexedRowKind.LeftAnimation => "Selected animation",
        WeaponIndexedRowKind.HideTag => "Selected hide tag",
        WeaponIndexedRowKind.SoundNoteMapping => "Selected sound notetrack mapping",
        WeaponIndexedRowKind.RumbleNoteMapping => "Selected rumble notetrack mapping",
        WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph or
        WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph or
        WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph or
        WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph => "Selected accuracy knot",
        WeaponIndexedRowKind.ProjectileParallelBounce or
        WeaponIndexedRowKind.ProjectilePerpendicularBounce => "Selected bounce surface",
        WeaponIndexedRowKind.BounceSound => "Selected surface sound",
        WeaponIndexedRowKind.TurretSpinUpSound or
        WeaponIndexedRowKind.TurretSpinDownSound => "Selected barrel-spin sound",
        _ => "Selected semantic slot"
    };

    private static string OverviewCardTitle(string path)
    {
        if (path.EndsWith(".internalName", StringComparison.Ordinal)) return "Native identity";
        if (path.StartsWith("weapon.definition.", StringComparison.Ordinal)) return "Definition identity";
        string member = LastPathMember(path);
        if (member is "displayName" or "alternateWeaponName" or "alternateWeaponIndex")
            return "Variant identity";
        if (member.StartsWith("ads", StringComparison.Ordinal)) return "ADS presentation";
        if (member.EndsWith("Time", StringComparison.Ordinal) ||
            member.EndsWith("Length", StringComparison.Ordinal))
            return "Variant timing";
        return "Variant combat values";
    }

    private static string HudAndAmmoCardTitle(string path)
    {
        if (path.StartsWith("weapon.definition.ammo.", StringComparison.Ordinal))
            return "Ammo and damage";
        if (path.StartsWith("weapon.definition.icons.", StringComparison.Ordinal))
            return "HUD icons";
        return "Variant HUD";
    }

    private static string TimingCardTitle(string path)
    {
        string member = LastPathMember(path);
        if (member.StartsWith("reload", StringComparison.Ordinal)) return "Reload timing";
        if (member.StartsWith("melee", StringComparison.Ordinal) ||
            member.StartsWith("rechamber", StringComparison.Ordinal))
            return "Melee and rechamber timing";
        if (member.StartsWith("nightVision", StringComparison.Ordinal)) return "Night-vision timing";
        if (member is "fuseTime" or "aiFuseTime") return "Fuse timing";
        if (member.StartsWith("raise", StringComparison.Ordinal) ||
            member.StartsWith("drop", StringComparison.Ordinal) ||
            member.StartsWith("altDrop", StringComparison.Ordinal) ||
            member.StartsWith("quick", StringComparison.Ordinal) ||
            member.StartsWith("breach", StringComparison.Ordinal) ||
            member.StartsWith("empty", StringComparison.Ordinal) ||
            member.StartsWith("sprint", StringComparison.Ordinal) ||
            member.StartsWith("stunned", StringComparison.Ordinal))
            return "Handling and movement timing";
        return "Fire and detonation timing";
    }

    private static string AimMovementCardTitle(string path)
    {
        string member = LastPathMember(path);
        return member.StartsWith("aim", StringComparison.Ordinal) ||
            member.StartsWith("autoAim", StringComparison.Ordinal) ||
            member.StartsWith("enemyCrosshair", StringComparison.Ordinal)
                ? "Aim assistance"
                : "Movement and zoom";
    }

    private static string OverlayAdsCardTitle(string path)
    {
        if (path.StartsWith("weapon.definition.overlay.", StringComparison.Ordinal))
            return "Overlay and reticle";
        string member = LastPathMember(path);
        if (member.StartsWith("hipSpread", StringComparison.Ordinal)) return "Hip-fire spread";
        if (member.Contains("Idle", StringComparison.Ordinal) ||
            member.Contains("Bob", StringComparison.Ordinal) ||
            member == "hipReticleSidePosition")
            return "View bob and idle";
        if (member.StartsWith("adsSway", StringComparison.Ordinal) ||
            member.StartsWith("adsViewError", StringComparison.Ordinal))
            return "ADS sway and error";
        return "Weapon sway";
    }

    private static string PhysicsCardTitle(string path)
    {
        if (path.StartsWith("weapon.definition.physics.", StringComparison.Ordinal))
            return "Physics and ballistics";
        if (path.StartsWith("weapon.definition.projectile.", StringComparison.Ordinal))
            return "Projectile behavior";
        return "Physics assets";
    }

    private static string RecoilCardTitle(string path)
    {
        if (path.Contains("AccuracyGraphKnotCount", StringComparison.Ordinal) ||
            path.Contains("accuracyGraphKnotCount", StringComparison.Ordinal))
            return "Accuracy graph storage";
        if (path.StartsWith("weapon.definition.accuracy.", StringComparison.Ordinal))
            return "AI accuracy";
        if (path.StartsWith("weapon.definition.projectile.gunKickAndDistance.", StringComparison.Ordinal))
        {
            string member = LastPathMember(path);
            if (member.StartsWith("ads", StringComparison.Ordinal)) return "ADS recoil";
            if (member.StartsWith("hip", StringComparison.Ordinal)) return "Hip-fire recoil";
            return "Engagement distance";
        }
        return "Accuracy and recoil";
    }

    private static string DamageCardTitle(string path)
    {
        if (path.StartsWith("weapon.definition.turnSpeedAndRange.", StringComparison.Ordinal))
            return "AI turn speed and range";
        string member = LastPathMember(path);
        if (member.StartsWith("destabil", StringComparison.Ordinal)) return "AI destabilization";
        if (member.StartsWith("adsTransition", StringComparison.Ordinal)) return "ADS transition rates";
        return "Damage and range";
    }

    private static string SoundCardTitle(string path)
    {
        const string prefix = "weapon.definition.primarySounds.";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return "Weapon sounds";
        string member = path[prefix.Length..];
        if (member.StartsWith("fire", StringComparison.Ordinal) ||
            member.StartsWith("emptyFire", StringComparison.Ordinal))
            return "Fire sounds";
        if (member.StartsWith("melee", StringComparison.Ordinal) ||
            member.StartsWith("rechamber", StringComparison.Ordinal))
            return "Melee and rechamber sounds";
        if (member.StartsWith("reload", StringComparison.Ordinal)) return "Reload sounds";
        if (member.StartsWith("pickup", StringComparison.Ordinal) ||
            member.StartsWith("ammoPickup", StringComparison.Ordinal) ||
            member.StartsWith("projectile", StringComparison.Ordinal) ||
            member.StartsWith("pullback", StringComparison.Ordinal))
            return "Pickup and projectile sounds";
        return "Handling and mode sounds";
    }

    private static string TurretCardTitle(string path)
    {
        if (path.StartsWith("weapon.definition.missileConeSound.", StringComparison.Ordinal))
            return "Missile-cone sound";
        if (path.StartsWith("weapon.definition.turret.", StringComparison.Ordinal))
            return "Turret barrel";
        return "Turret scope and heat";
    }

    private static string StorageCardTitle(string path)
    {
        if (path.StartsWith("weapon.preserved.", StringComparison.Ordinal) ||
            path.EndsWith(".offset", StringComparison.Ordinal))
            return "Native storage";
        if (path.StartsWith("weapon.variant.", StringComparison.Ordinal)) return "Variant flags";

        string member = LastPathMember(path);
        if (member.Contains("ammo", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("clip", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("reload", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("rechamber", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("empty", StringComparison.OrdinalIgnoreCase))
            return "Ammo and reload flags";
        if (member.Contains("projectile", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("explosion", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("bullet", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("deton", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("grenade", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("throw", StringComparison.OrdinalIgnoreCase))
            return "Projectile and explosive flags";
        if (member.Contains("ads", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("aim", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("crosshair", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("laser", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("thermal", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("killIcon", StringComparison.OrdinalIgnoreCase))
            return "Aiming and display flags";
        if (member.Contains("lockon", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("missile", StringComparison.OrdinalIgnoreCase) ||
            member.Contains("turret", StringComparison.OrdinalIgnoreCase))
            return "Turret and missile flags";
        return "Weapon behavior flags";
    }

    private static string LastPathMember(string path)
    {
        int separator = path.LastIndexOf('.');
        string member = separator < 0 ? path : path[(separator + 1)..];
        int index = member.IndexOf('[');
        return index < 0 ? member : member[..index];
    }

    private void RefreshSemanticVisualizations()
    {
        RefreshAccuracyGraphs();
        RefreshLocationDamage();
        RefreshBounceSurfaces();
        RefreshDetectedModelTags();
    }

    private void RefreshAccuracyGraphs()
    {
        WeaponDef? definition = _workingDraft.Definition;
        WeaponIndexedRowKind? selectedKind = SelectedIndexedRow?.Kind;
        WeaponIndexedRowKind preferredKind = selectedKind is
            WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph or
            WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph or
            WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph or
            WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph
                ? selectedKind.Value
                : SelectedAccuracyGraph?.RowKind ??
                    WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph;
        AccuracyGraphs = definition is null
            ? []
            : Array.AsReadOnly(new[]
            {
                new WeaponAccuracyGraphItemViewModel(
                    WeaponIndexedRowKind.AiVsAiCurrentAccuracyGraph,
                    "AI vs. AI",
                    "Current",
                    _workingDraft.Variant.AiVsAiAccuracyGraphKnots),
                new WeaponAccuracyGraphItemViewModel(
                    WeaponIndexedRowKind.AiVsPlayerCurrentAccuracyGraph,
                    "AI vs. player",
                    "Current",
                    _workingDraft.Variant.AiVsPlayerAccuracyGraphKnots),
                new WeaponAccuracyGraphItemViewModel(
                    WeaponIndexedRowKind.AiVsAiOriginalAccuracyGraph,
                    "AI vs. AI",
                    "Original",
                    definition.Accuracy.OriginalAiVsAiGraphKnots),
                new WeaponAccuracyGraphItemViewModel(
                    WeaponIndexedRowKind.AiVsPlayerOriginalAccuracyGraph,
                    "AI vs. player",
                    "Original",
                    definition.Accuracy.OriginalAiVsPlayerGraphKnots)
            });
        _selectedAccuracyGraphIndex = Math.Max(0,
            AccuracyGraphs.ToList().FindIndex(graph => graph.RowKind == preferredKind));
        OnPropertyChanged(nameof(SelectedAccuracyGraphIndex));
        OnPropertyChanged(nameof(SelectedAccuracyGraph));
    }

    private void RefreshLocationDamage()
    {
        IReadOnlyList<float> values = _workingDraft.Definition?.LocationDamageMultipliers ?? [];
        LocationDamageMultipliers = Array.AsReadOnly(values.ToArray());
        LocationDamageItems = Array.AsReadOnly(Enumerable.Range(0, (int)HitLocation.Count)
            .Select(index => new WeaponLocationDamageItemViewModel(
                WeaponSemanticLabels.HumanizeIdentifier(((HitLocation)index).ToString()),
                index < values.Count ? values[index] : null))
            .ToArray());
        int selectedIndex = SelectedIndexedRow is
            {
                Kind: WeaponIndexedRowKind.LocationDamage
            } row ? row.Index : _selectedLocationDamageIndex;
        _selectedLocationDamageIndex = Math.Clamp(
            selectedIndex,
            0,
            Math.Max(0, LocationDamageItems.Count - 1));
        OnPropertyChanged(nameof(SelectedLocationDamageIndex));
    }

    private void RefreshBounceSurfaces()
    {
        WeaponDef? definition = _workingDraft.Definition;
        BounceSurfaces = definition is null
            ? []
            : Array.AsReadOnly(Enumerable.Range(0, (int)MaterialSurfaceType.Count)
                .Select(index => new WeaponBounceSurfaceItemViewModel(
                    WeaponSemanticLabels.HumanizeIdentifier(
                        ((MaterialSurfaceType)index).ToString()),
                    index < definition.Projectile.ParallelBounce.Count
                        ? definition.Projectile.ParallelBounce[index]
                        : null,
                    index < definition.Projectile.PerpendicularBounce.Count
                        ? definition.Projectile.PerpendicularBounce[index]
                        : null,
                    index < definition.BounceSounds.Count
                        ? definition.BounceSounds[index].Name
                        : null))
                .ToArray());
        if (SelectedIndexedRow is
            {
                Kind: WeaponIndexedRowKind.ProjectileParallelBounce or
                    WeaponIndexedRowKind.ProjectilePerpendicularBounce
            } row)
        {
            _selectedBounceSurfaceIndex = row.Index;
            _isParallelBounceSelected =
                row.Kind == WeaponIndexedRowKind.ProjectileParallelBounce;
        }
        _selectedBounceSurfaceIndex = Math.Clamp(
            _selectedBounceSurfaceIndex,
            0,
            Math.Max(0, BounceSurfaces.Count - 1));
        OnPropertyChanged(nameof(SelectedBounceSurfaceIndex));
        OnPropertyChanged(nameof(IsParallelBounceSelected));
        OnPropertyChanged(nameof(IsPerpendicularBounceSelected));
    }

    private void RefreshDetectedModelTags()
    {
        DetectedModelTags = Scene is null
            ? []
            : Array.AsReadOnly(Scene.Bones
                .Select(bone => bone.Name)
                .Where(name => name.StartsWith("tag_", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        OnPropertyChanged(nameof(HasDetectedModelTags));
        OnPropertyChanged(nameof(CanFindModelTags));
        OnPropertyChanged(nameof(DetectedModelTagCountText));
    }

    private void SelectBounceIndexedRow() => SelectIndexedRow(
        IsParallelBounceSelected
            ? WeaponIndexedRowKind.ProjectileParallelBounce
            : WeaponIndexedRowKind.ProjectilePerpendicularBounce,
        SelectedBounceSurfaceIndex);

    private void SelectIndexedRow(WeaponIndexedRowKind kind, int index)
    {
        if (_isReplacingProjection) return;
        WeaponIndexedRowItemViewModel? row = IndexedRows.FirstOrDefault(candidate =>
            candidate.Kind == kind && candidate.Index == index);
        if (row is not null && !ReferenceEquals(row, SelectedIndexedRow))
            SelectedIndexedRow = row;
    }

    private void NotifyCategoryPresentation()
    {
        OnPropertyChanged(nameof(IsAccuracyCategory));
        OnPropertyChanged(nameof(IsLocationDamageCategory));
        OnPropertyChanged(nameof(IsBounceCategory));
        OnPropertyChanged(nameof(IsHideTagsCategory));
        OnPropertyChanged(nameof(ShowsSemanticBrowser));
        OnPropertyChanged(nameof(IsHideTagTabSelected));
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
        Diagnostics = Array.AsReadOnly(_candidateDiagnostics.Concat(_camoDiagnostics).Concat(_previewDiagnostics).Concat(_animationDiagnostics).Concat(_rendererDiagnostics).GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity)).Select(group => group.First()).ToArray());
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ReplaceAnimationPreview(null); DisposeCamoAppearance(); foreach (INotifyPropertyChanged row in _stagedRows) row.PropertyChanged -= StagedRow_PropertyChanged; _stagedRows.Clear(); _sceneCache.Clear(); _scene = null; _lods = []; _selectedLod = null; AssetReferenceSelectionRequested = null; PropertiesRevealRequested = null;
    }
}

public enum WeaponPropertyCategory { Overview, Models, AnimationNames, HideTagsAndNoteTracks, ClassificationAndReticle, ViewAndPositionalMovement, HudIconsAndAmmo, Timing, AimAndMovementTuning, OverlayAdsAndSpread, PhysicsAndProjectile, KickRecoilAndAccuracy, DamageRangeAndAiTuning, EffectsAndMaterials, SoundsAndBounce, HintsAndRumble, TurretAndMissile, TailAndPreservedStorage }
public enum WeaponPreviewState { NoSelection, Empty, TableAbsent, Malformed, Unresolved, NonRenderable, Ready, Failed }
public enum WeaponModelSlotState { Resolved, Empty, Unresolved, NonRenderable, TableAbsent, Malformed }
public sealed class WeaponCategoryItemViewModel
{
    private WeaponCategoryItemViewModel(
        WeaponPropertyCategory id,
        string title,
        string subtitle,
        MaterialIconKind icon)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
    }

    internal WeaponPropertyCategory Id { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public MaterialIconKind Icon { get; }

    internal static WeaponCategoryItemViewModel[] CreateAll() =>
    [
        new(WeaponPropertyCategory.Overview, "Overview and variant", "Summary and identity", MaterialIconKind.InformationOutline),
        new(WeaponPropertyCategory.Models, "Models", "XModels and camos", MaterialIconKind.CubeOutline),
        new(WeaponPropertyCategory.AnimationNames, "Animation names", "View and hand animation slots", MaterialIconKind.AnimationPlayOutline),
        new(WeaponPropertyCategory.HideTagsAndNoteTracks, "Hide tags and note tracks", "Tags and notetrack mapping", MaterialIconKind.TagMultipleOutline),
        new(WeaponPropertyCategory.ClassificationAndReticle, "Classification and reticle", "Display and targeting", MaterialIconKind.Crosshairs),
        new(WeaponPropertyCategory.ViewAndPositionalMovement, "View and positional movement", "Viewmodel placement", MaterialIconKind.AxisArrow),
        new(WeaponPropertyCategory.HudIconsAndAmmo, "HUD icons and ammo", "HUD and ammunition", MaterialIconKind.Ammunition),
        new(WeaponPropertyCategory.Timing, "Timing", "Fire and reload timing", MaterialIconKind.TimerOutline),
        new(WeaponPropertyCategory.AimAndMovementTuning, "Aim and movement tuning", "Accuracy and movement", MaterialIconKind.TuneVariant),
        new(WeaponPropertyCategory.OverlayAdsAndSpread, "Overlay, ADS, and spread", "Overlay and sight behavior", MaterialIconKind.ImageFilterCenterFocus),
        new(WeaponPropertyCategory.PhysicsAndProjectile, "Physics and projectile", "Projectile and bounce", MaterialIconKind.RocketLaunchOutline),
        new(WeaponPropertyCategory.KickRecoilAndAccuracy, "Kick, recoil, and accuracy", "Recoil and accuracy graphs", MaterialIconKind.ChartBellCurve),
        new(WeaponPropertyCategory.DamageRangeAndAiTuning, "Damage, range, and AI tuning", "Damage and hit locations", MaterialIconKind.Target),
        new(WeaponPropertyCategory.EffectsAndMaterials, "Flash and shell effects", "Muzzle and shell effects", MaterialIconKind.Flare),
        new(WeaponPropertyCategory.SoundsAndBounce, "Sounds and bounce response", "Audio and surface response", MaterialIconKind.Waveform),
        new(WeaponPropertyCategory.HintsAndRumble, "Hints and rumble", "Prompts and feedback", MaterialIconKind.Vibration),
        new(WeaponPropertyCategory.TurretAndMissile, "Turret and missile-cone sound", "Turret and missile tuning", MaterialIconKind.Radar),
        new(WeaponPropertyCategory.TailAndPreservedStorage, "Tail bytes and preserved storage", "Native preserved values", MaterialIconKind.DatabaseLockOutline)
    ];
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

public sealed class WeaponSemanticTabItemViewModel
{
    internal WeaponSemanticTabItemViewModel(
        string key,
        string title,
        IEnumerable<WeaponIndexedRowItemViewModel> rows,
        WeaponIndexedRowKind? selectionKind)
    {
        Key = key;
        Title = title;
        Rows = Array.AsReadOnly(rows.ToArray());
        SelectionKind = selectionKind;
    }

    internal string Key { get; }
    public string Title { get; }
    internal IReadOnlyList<WeaponIndexedRowItemViewModel> Rows { get; }
    internal WeaponIndexedRowKind? SelectionKind { get; }
    public string CountText => Rows.Count == 1 ? "1 item" : $"{Rows.Count} items";
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

public sealed class WeaponPreviewModelFamilyItemViewModel
{
    private WeaponPreviewModelFamilyItemViewModel(
        WeaponIndexedRowKind rowKind,
        string title,
        bool supportsCamo)
    {
        RowKind = rowKind;
        Title = title;
        SupportsCamo = supportsCamo;
    }

    internal WeaponIndexedRowKind RowKind { get; }
    private string Title { get; }
    internal bool SupportsCamo { get; }

    internal static WeaponPreviewModelFamilyItemViewModel[] CreateAll() =>
    [
        new(WeaponIndexedRowKind.GunModel, "View model", supportsCamo: true),
        new(WeaponIndexedRowKind.WorldGunModel, "World model", supportsCamo: true),
        new(WeaponIndexedRowKind.HandModel, "Hand model", supportsCamo: false),
        new(WeaponIndexedRowKind.WorldClipModel, "World clip model", supportsCamo: false),
        new(WeaponIndexedRowKind.RocketModel, "Rocket model", supportsCamo: false),
        new(WeaponIndexedRowKind.KnifeModel, "Knife model", supportsCamo: false),
        new(WeaponIndexedRowKind.WorldKnifeModel, "World knife model", supportsCamo: false),
        new(WeaponIndexedRowKind.ProjectileModel, "Projectile model", supportsCamo: false)
    ];

    public override string ToString() => Title;
}

public sealed class WeaponCamoItemViewModel(int index, string title)
{
    internal int Index { get; } = index;
    private string Title { get; } = title;
    public override string ToString() => Title;
}

public sealed class WeaponAccuracyGraphItemViewModel
{
    internal WeaponAccuracyGraphItemViewModel(
        WeaponIndexedRowKind rowKind,
        string title,
        string variant,
        IReadOnlyList<Vec2> points)
    {
        RowKind = rowKind;
        Title = title;
        Variant = variant;
        Points = Array.AsReadOnly(points.ToArray());
    }

    internal WeaponIndexedRowKind RowKind { get; }
    private string Title { get; }
    private string Variant { get; }
    public IReadOnlyList<Vec2> Points { get; }
    private string DisplayName => $"{Title} · {Variant}";
    public override string ToString() => DisplayName;
}

public sealed class WeaponLocationDamageItemViewModel(
    string title,
    float? multiplier)
{
    public string Title { get; } = title;
    public string MultiplierText => multiplier is { } value && float.IsFinite(value)
        ? $"{value:0.###}×"
        : "—";
}

public sealed class WeaponBounceSurfaceItemViewModel(
    string title,
    float? parallel,
    float? perpendicular,
    string? soundName)
{
    public string Title { get; } = title;
    public string ParallelText => Format(parallel);
    public string PerpendicularText => Format(perpendicular);
    public string SoundText => string.IsNullOrWhiteSpace(soundName)
        ? "No bounce sound"
        : soundName;

    private static string Format(float? value) =>
        value is { } number && float.IsFinite(number)
            ? number.ToString("0.###", CultureInfo.InvariantCulture)
            : "—";
}
