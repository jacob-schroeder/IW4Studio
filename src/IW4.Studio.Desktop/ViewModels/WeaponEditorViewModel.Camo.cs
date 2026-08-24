using Avalonia.Media.Imaging;
using IW4.AssetExchange.XModel;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.XModel;
using IW4.Studio.Desktop.Editors.Weapon;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed partial class WeaponEditorViewModel
{
    private IReadOnlyList<WeaponCamoStyleItemViewModel> _camoStyles = [];
    private IReadOnlyList<WeaponCamoMaterialItemViewModel> _camoMaterials = [];
    private WeaponCamoStyleItemViewModel _selectedCamoStyle = null!;
    private WeaponCamoMaterialItemViewModel? _selectedCamoMaterial;
    private XModelAsset? _camoSourceModel;
    private string? _camoSourceSlotKey;
    private XModelImportImage? _camoImage;
    private Bitmap? _camoImagePreview;
    private string? _camoImageName;
    private double _camoLoopSeconds = 10d;
    private double _compiledCamoLoopSeconds = 10d;
    private bool _isCamoEditorOpen;
    private bool _isCamoAnimationPaused;
    private WeaponCamoCompileResult? _pendingCamoCompile;
    private string? _pendingCamoSlotKey;
    private bool _isApplyingCamoModelMutation;
    private IReadOnlyList<AssetValidationIssue> _camoDiagnostics = [];

    public IReadOnlyList<WeaponCamoStyleItemViewModel> CamoStyles =>
        _camoStyles;

    public IReadOnlyList<WeaponCamoMaterialItemViewModel> CamoMaterials =>
        _camoMaterials;

    public WeaponCamoStyleItemViewModel SelectedCamoStyle
    {
        get => _selectedCamoStyle;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!CamoStyles.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetProperty(ref _selectedCamoStyle, value))
                return;

            NotifyCamoAppearanceState();
            CompileCamoAppearance();
        }
    }

    public WeaponCamoMaterialItemViewModel? SelectedCamoMaterial
    {
        get => _selectedCamoMaterial;
        set
        {
            if (value is not null && !CamoMaterials.Contains(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (!SetProperty(ref _selectedCamoMaterial, value))
                return;

            NotifyCamoAppearanceState();
            CompileCamoAppearance();
        }
    }

    public double CamoLoopSeconds
    {
        get => _camoLoopSeconds;
        set
        {
            if (!SetProperty(ref _camoLoopSeconds, value))
                return;

            OnPropertyChanged(nameof(CamoLoopSecondsText));
        }
    }

    public string CamoLoopSecondsText => $"{CamoLoopSeconds:0} sec";

    internal void CommitCamoLoopSeconds()
    {
        if (_compiledCamoLoopSeconds.Equals(_camoLoopSeconds))
            return;

        CompileCamoAppearance();
    }

    public bool IsCamoEditorOpen
    {
        get => _isCamoEditorOpen;
        private set => SetProperty(ref _isCamoEditorOpen, value);
    }

    public bool IsCamoAnimationPaused
    {
        get => _isCamoAnimationPaused;
        set
        {
            if (!SetProperty(ref _isCamoAnimationPaused, value))
                return;
            OnPropertyChanged(nameof(CamoAnimationPauseText));
        }
    }

    public bool CanOpenCamoEditor =>
        !_disposed &&
        IsEditable &&
        SelectedModelSlot is
        {
            Kind: WeaponIndexedRowKind.GunModel or
                WeaponIndexedRowKind.WorldGunModel,
            State: WeaponModelSlotState.Resolved,
            ResolvedModel: not null
        } slot &&
        (_pendingCamoSlotKey is null || string.Equals(
            _pendingCamoSlotKey,
            slot.StableKey,
            StringComparison.Ordinal));

    public bool CanChooseCamoImage =>
        CanOpenCamoEditor && SelectedCamoMaterial is not null;

    public bool HasCamoImage => _camoImage is not null;

    public Bitmap? CamoImagePreview => _camoImagePreview;

    public string CamoImageName => _camoImageName ?? "No image selected";

    public string CamoImageDetails => _camoImage is { } image
        ? $"{image.Width:N0} × {image.Height:N0} · {image.RgbaBytes.Count:N0} RGBA bytes"
        : "PNG or JPEG";

    public string CamoTargetText => SelectedModelSlot is { } slot
        ? $"{slot.RoleLabel} · {slot.SemanticName ?? "Unnamed XModel"}"
        : "No weapon model selected";

    public string CamoEditorAvailabilityText => CanOpenCamoEditor
        ? "Customize the selected weapon model"
        : _pendingCamoSlotKey is not null
            ? "Apply or Revert the staged camo before editing another model"
            : "Select a resolved view or world weapon model";

    public bool IsAnimatedCamoSelected =>
        SelectedCamoStyle.Style == WeaponCamoStyle.Animated;

    public bool IsCamoAnimationPreviewEnabled =>
        SelectedModelSlot?.ResolvedModel is { } model &&
        model.Materials.OfType<MaterialAsset>().Any(IsAnimatedCamoMaterial);

    public string CamoAnimationPauseText => IsCamoAnimationPaused
        ? "Resume"
        : "Pause";

    public bool HasCamoErrors => _camoDiagnostics.Any(issue =>
        issue.Severity == AssetValidationSeverity.Error);

    public bool HasCamoStatus => !string.IsNullOrWhiteSpace(CamoStatusText);

    public bool CamoStatusIsError => HasCamoErrors;

    public string CamoStatusText
    {
        get
        {
            AssetValidationIssue? error = _camoDiagnostics.FirstOrDefault(issue =>
                issue.Severity == AssetValidationSeverity.Error);
            if (error is not null)
                return error.Message;
            if (_pendingCamoCompile is not null)
            {
                return $"Previewing {SelectedCamoStyle.Title.ToLowerInvariant()} camo. " +
                    "Use the editor Apply button to keep it.";
            }
            if (_camoImage is null)
                return "Choose a PNG or JPEG to preview it on this model.";
            return "Choose a compatible material to stage the camo.";
        }
    }

    internal void ToggleCamoEditor()
    {
        if (IsCamoEditorOpen)
        {
            IsCamoEditorOpen = false;
            return;
        }
        if (!CanOpenCamoEditor)
            return;

        RefreshCamoAppearanceProjection();
        IsCamoEditorOpen = true;
    }

    internal void CloseCamoEditor() => IsCamoEditorOpen = false;

    internal string? CaptureCamoTargetIdentity() => _camoSourceSlotKey;

    internal bool IsCurrentCamoTarget(string? targetIdentity) =>
        targetIdentity is not null &&
        CanChooseCamoImage &&
        string.Equals(
            targetIdentity,
            _camoSourceSlotKey,
            StringComparison.Ordinal);

    internal void SetCamoImage(
        string fileName,
        XModelImportImage image,
        Bitmap preview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(preview);
        if (!CanChooseCamoImage)
            throw new InvalidOperationException(
                "The selected weapon model has no compatible material target.");

        Bitmap? previous = _camoImagePreview;
        _camoImage = image;
        _camoImagePreview = preview;
        _camoImageName = fileName;
        previous?.Dispose();
        SetCamoDiagnostics([]);
        NotifyCamoAppearanceState();
        CompileCamoAppearance();
    }

    internal void ReportCamoImageFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        SetCamoDiagnostics([
            new AssetValidationIssue(
                "weapon.camo.image",
                message,
                AssetValidationSeverity.Error)
        ]);
    }

    private void InitializeCamoAppearance()
    {
        _camoStyles = Array.AsReadOnly([
            new WeaponCamoStyleItemViewModel(
                WeaponCamoStyle.Static,
                "Static",
                "Uses the material's existing shading without movement."),
            new WeaponCamoStyleItemViewModel(
                WeaponCamoStyle.Animated,
                "Animated",
                $"Bootstraps the proven flow shader as {WeaponCamoCompiler.AnimatedTechniqueSetName}.")
        ]);
        _selectedCamoStyle = _camoStyles[0];
    }

    private void RefreshCamoAppearanceProjection()
    {
        WeaponModelSlotItemViewModel? slot = SelectedModelSlot;
        bool supported = slot is
        {
            Kind: WeaponIndexedRowKind.GunModel or
                WeaponIndexedRowKind.WorldGunModel,
            State: WeaponModelSlotState.Resolved,
            ResolvedModel: not null
        } &&
        (_pendingCamoSlotKey is null || string.Equals(
            _pendingCamoSlotKey,
            slot.StableKey,
            StringComparison.Ordinal));
        if (!supported)
        {
            IsCamoEditorOpen = false;
            if (_pendingCamoSlotKey is null)
                ResetCamoSourceProjection();
            NotifyCamoAppearanceState();
            return;
        }

        XModelAsset model = slot!.ResolvedModel!;
        bool keepPendingSource = _pendingCamoCompile is not null &&
            string.Equals(_pendingCamoSlotKey, slot.StableKey, StringComparison.Ordinal);
        bool sourceChanged = !string.Equals(
                _camoSourceSlotKey,
                slot.StableKey,
                StringComparison.Ordinal) ||
            (!keepPendingSource && !ReferenceEquals(_camoSourceModel, model));
        if (sourceChanged)
        {
            ResetCamoInput();
            _camoSourceSlotKey = slot.StableKey;
            _camoSourceModel = model;
            BuildCamoMaterialProjection(model);
        }

        NotifyCamoAppearanceState();
    }

    private void BuildCamoMaterialProjection(XModelAsset model)
    {
        WeaponCamoMaterialItemViewModel[] materials = model.Materials
            .OfType<MaterialAsset>()
            .Select((material, index) => (material, index))
            .GroupBy(row => CanonicalAssetName(row.material.Info.Name),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group =>
            {
                MaterialAsset material = ResolveCamoMaterial(group.First().material);
                int[] surfaces = group.Select(row => row.index).ToArray();
                return new WeaponCamoMaterialItemViewModel(
                    material,
                    group.Key,
                    surfaces);
            })
            .OrderBy(item => item.FirstSurfaceIndex)
            .ToArray();

        _camoMaterials = Array.AsReadOnly(materials);
        _selectedCamoMaterial = materials.FirstOrDefault();
        OnPropertyChanged(nameof(CamoMaterials));
        OnPropertyChanged(nameof(SelectedCamoMaterial));
    }

    private MaterialAsset ResolveCamoMaterial(MaterialAsset candidate)
    {
        string name = CanonicalAssetName(candidate.Info.Name);
        MaterialAsset? staged = CaptureCamoPreviewProviders()
            .OfType<MaterialAsset>()
            .LastOrDefault(material =>
                !IsReferenceAsset(material) &&
                string.Equals(
                    CanonicalAssetName(material.Info.Name),
                    name,
                    StringComparison.OrdinalIgnoreCase));
        if (staged is not null)
            return staged;
        if (!IsReferenceAsset(candidate) && candidate.TechniqueSet is not null)
            return candidate;
        return _session.TryResolveWorkspaceMaterial(name, out MaterialAsset? resolved) &&
            resolved is not null
                ? resolved
                : candidate;
    }

    private void CompileCamoAppearance()
    {
        _compiledCamoLoopSeconds = _camoLoopSeconds;
        if (_camoImage is null ||
            _camoSourceModel is null ||
            _camoSourceSlotKey is null ||
            SelectedCamoMaterial is null)
        {
            NotifyCamoAppearanceState();
            return;
        }
        WeaponModelSlotItemViewModel? slot = SelectedModelSlot;
        if (slot is null || !string.Equals(
                slot.StableKey,
                _camoSourceSlotKey,
                StringComparison.Ordinal))
        {
            SetCamoDiagnostics([
                new AssetValidationIssue(
                    "weapon.camo.target",
                    "The selected weapon model changed before the camo could be compiled.",
                    AssetValidationSeverity.Error)
            ]);
            return;
        }

        MaterialTechniqueSetAsset? animatedTechniqueSet = null;
        if (SelectedCamoStyle.Style == WeaponCamoStyle.Animated)
        {
            MaterialTechniqueSetAsset? sourceTechniqueSet =
                SelectedCamoMaterial.Material.TechniqueSet;
            string sourceTechniqueSetName = CanonicalAssetName(
                sourceTechniqueSet?.Name);
            if (sourceTechniqueSet is not null &&
                (string.Equals(
                    sourceTechniqueSetName,
                    WeaponCamoCompiler.AnimatedTechniqueSetName,
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                    sourceTechniqueSetName,
                    WeaponCamoCompiler.AnimatedTechniqueSetTemplateName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                animatedTechniqueSet = sourceTechniqueSet;
            }
            else
            {
                if (!_session.TryResolveWorkspaceTechniqueSet(
                        WeaponCamoCompiler.AnimatedTechniqueSetName,
                        out animatedTechniqueSet))
                {
                    _ = _session.TryResolveWorkspaceTechniqueSet(
                        WeaponCamoCompiler.AnimatedTechniqueSetTemplateName,
                        out animatedTechniqueSet);
                }
            }
        }

        var request = new WeaponCamoCompileRequest(
            _camoSourceModel,
            SelectedCamoMaterial.Material,
            _camoImage,
            SelectedCamoStyle.Style,
            (float)CamoLoopSeconds,
            animatedTechniqueSet,
            CreateCamoScopeIdentity(slot));
        if (!WeaponCamoCompiler.TryCompile(
                request,
                out WeaponCamoCompileResult? compiled,
                out string? blocker) ||
            compiled is null)
        {
            SetCamoDiagnostics([
                new AssetValidationIssue(
                    "weapon.camo.compile",
                    blocker ?? "The camo could not be compiled.",
                    AssetValidationSeverity.Error)
            ]);
            return;
        }

        _pendingCamoCompile = compiled;
        _pendingCamoSlotKey = slot.StableKey;
        SetCamoDiagnostics([]);
        NotifyCamoAppearanceState();
        _isApplyingCamoModelMutation = true;
        try
        {
            switch (slot.Kind)
            {
                case WeaponIndexedRowKind.GunModel:
                    MutateModel(
                        slot.Kind,
                        slot.Index,
                        () => _workingDraft.SetDefinitionGunModels(
                            slot.Index,
                            compiled.Model));
                    break;
                case WeaponIndexedRowKind.WorldGunModel:
                    MutateModel(
                        slot.Kind,
                        slot.Index,
                        () => _workingDraft.SetDefinitionWorldGunModels(
                            slot.Index,
                            compiled.Model));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Only view and world weapon models support custom camo.");
            }
        }
        finally
        {
            _isApplyingCamoModelMutation = false;
        }

        NotifyCamoAppearanceState();
    }

    private void SetCamoDiagnostics(
        IEnumerable<AssetValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _camoDiagnostics = Array.AsReadOnly(issues
            .GroupBy(issue => (issue.FieldPath, issue.Message, issue.Severity))
            .Select(group => group.First())
            .ToArray());
        RebuildDiagnostics();
        NotifyCamoAppearanceState();
        NotifyState();
    }

    private IReadOnlyList<BaseAsset> CaptureCamoPreviewProviders()
    {
        IEnumerable<BaseAsset> providers =
            _session.CaptureAppliedWeaponProviders();
        if (_pendingCamoCompile is not null)
            providers = providers.Concat(_pendingCamoCompile.Providers);

        return Array.AsReadOnly(providers
            .GroupBy(provider =>
                $"{(int)provider.SerializedAssetType}:" +
                CanonicalAssetName(provider.SerializedAssetName)
                    .ToLowerInvariant(),
                StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray());
    }

    private bool IsAuthoredCamoModel(XModelAsset model)
    {
        if (_pendingCamoCompile is { } pending &&
            ReferenceEquals(model, pending.Model))
        {
            return true;
        }

        string name = CanonicalAssetName(model.Name);
        return _session.CaptureAppliedWeaponProviders()
            .OfType<XModelAsset>()
            .Any(provider =>
                !IsReferenceAsset(provider) &&
                string.Equals(
                    CanonicalAssetName(provider.Name),
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAnimatedCamoMaterial(MaterialAsset candidate)
    {
        MaterialAsset material = ResolveCamoMaterial(candidate);
        string techniqueSetName = CanonicalAssetName(
            material.TechniqueSet?.Name);
        return (string.Equals(
                    techniqueSetName,
                    WeaponCamoCompiler.AnimatedTechniqueSetName,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    techniqueSetName,
                    WeaponCamoCompiler.AnimatedTechniqueSetTemplateName,
                    StringComparison.OrdinalIgnoreCase)) &&
            material.Constants.Any(constant =>
                constant.NameHash == WeaponCamoCompiler.UvAnimParmsHash &&
                constant.Literal.Y != 0f);
    }

    private void ClearCamoAppearanceDraft()
    {
        IsCamoEditorOpen = false;
        _pendingCamoCompile = null;
        _pendingCamoSlotKey = null;
        _camoDiagnostics = [];
        ResetCamoSourceProjection();
        NotifyCamoAppearanceState();
        RebuildDiagnostics();
    }

    private void CompleteCamoModelMutation(string stableKey)
    {
        if (_isApplyingCamoModelMutation ||
            !string.Equals(
                _pendingCamoSlotKey,
                stableKey,
                StringComparison.Ordinal))
        {
            return;
        }

        IsCamoEditorOpen = false;
        _pendingCamoCompile = null;
        _pendingCamoSlotKey = null;
        _camoDiagnostics = [];
        ResetCamoSourceProjection();
        NotifyCamoAppearanceState();
        RebuildDiagnostics();
    }

    private void ResetCamoSourceProjection()
    {
        ResetCamoInput();
        _camoSourceModel = null;
        _camoSourceSlotKey = null;
        _camoMaterials = [];
        _selectedCamoMaterial = null;
        OnPropertyChanged(nameof(CamoMaterials));
        OnPropertyChanged(nameof(SelectedCamoMaterial));
    }

    private void ResetCamoInput()
    {
        Bitmap? preview = _camoImagePreview;
        _camoImagePreview = null;
        _camoImage = null;
        _camoImageName = null;
        preview?.Dispose();
        SetCamoDiagnostics([]);
        OnPropertyChanged(nameof(CamoImagePreview));
        OnPropertyChanged(nameof(HasCamoImage));
        OnPropertyChanged(nameof(CamoImageName));
        OnPropertyChanged(nameof(CamoImageDetails));
    }

    private void DisposeCamoAppearance()
    {
        Bitmap? preview = _camoImagePreview;
        _camoImagePreview = null;
        preview?.Dispose();
    }

    private void NotifyCamoAppearanceState()
    {
        OnPropertyChanged(nameof(CanOpenCamoEditor));
        OnPropertyChanged(nameof(CanChooseCamoImage));
        OnPropertyChanged(nameof(CamoTargetText));
        OnPropertyChanged(nameof(CamoEditorAvailabilityText));
        OnPropertyChanged(nameof(IsAnimatedCamoSelected));
        OnPropertyChanged(nameof(IsCamoAnimationPreviewEnabled));
        OnPropertyChanged(nameof(CamoAnimationPauseText));
        OnPropertyChanged(nameof(HasCamoErrors));
        OnPropertyChanged(nameof(HasCamoStatus));
        OnPropertyChanged(nameof(CamoStatusIsError));
        OnPropertyChanged(nameof(CamoStatusText));
        OnPropertyChanged(nameof(HasCamoImage));
        OnPropertyChanged(nameof(CamoImagePreview));
        OnPropertyChanged(nameof(CamoImageName));
        OnPropertyChanged(nameof(CamoImageDetails));
    }

    private static bool IsReferenceAsset(BaseAsset asset) =>
        asset.SerializedAssetName?.StartsWith(',') == true;

    private string CreateCamoScopeIdentity(WeaponModelSlotItemViewModel slot)
    {
        string weaponName = CanonicalAssetName(
            _session.Entry.NormalizedName ??
            _session.Entry.OriginalName ??
            Name);
        string family = slot.Kind == WeaponIndexedRowKind.GunModel
            ? "view"
            : "world";
        return $"{weaponName}:{family}:{slot.Index}";
    }

    private static string CanonicalAssetName(string? name) =>
        string.IsNullOrEmpty(name)
            ? string.Empty
            : name.StartsWith(',') ? name[1..] : name;
}

public sealed class WeaponCamoMaterialItemViewModel
{
    internal WeaponCamoMaterialItemViewModel(
        MaterialAsset material,
        string name,
        IReadOnlyList<int> surfaceIndices)
    {
        Material = material ?? throw new ArgumentNullException(nameof(material));
        Name = name;
        SurfaceIndices = surfaceIndices;
    }

    internal MaterialAsset Material { get; }
    internal int FirstSurfaceIndex => SurfaceIndices[0];
    public string Name { get; }
    public IReadOnlyList<int> SurfaceIndices { get; }
    public string UsageText => SurfaceIndices.Count == 1
        ? $"surface {SurfaceIndices[0]}"
        : $"{SurfaceIndices.Count} surfaces";
    public string DisplayName => $"{Name} · {UsageText}";
    public override string ToString() => DisplayName;
}

public sealed class WeaponCamoStyleItemViewModel
{
    internal WeaponCamoStyleItemViewModel(
        WeaponCamoStyle style,
        string title,
        string description)
    {
        Style = style;
        Title = title;
        Description = description;
    }

    internal WeaponCamoStyle Style { get; }
    public string Title { get; }
    public string Description { get; }
    public override string ToString() => Title;
}
