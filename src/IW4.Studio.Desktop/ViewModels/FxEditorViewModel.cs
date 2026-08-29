using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.EditorPreview;
using IW4.Render.SceneBuilding;
using IW4.Runtime.Assets.Sound;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class FxEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IDisposable
{
    private static readonly TimeSpan PlaybackTickInterval =
        TimeSpan.FromMilliseconds(16);

    private readonly FxEffectDefAsset _effect;
    private readonly FastFileWorkspace? _workspace;
    private readonly WorkspaceGfxImagePayloadResolver? _imagePayloads;
    private readonly XModelSceneBuilder _visualSceneBuilder = new();
    private readonly FxPreviewScene? _previewScene;
    private readonly DispatcherTimer _playbackTimer;
    private readonly string _previewUnavailableReason;
    private IReadOnlyList<FxVisualDependencyViewModel>
        _selectedVisualDependencies = [];
    private FxElementViewModel? _selectedElement;
    private FxVisualDependencyViewModel? _selectedVisualDependency;
    private XModelAsset? _cachedModel;
    private XModelRenderScene? _cachedModelScene;
    private MaterialAsset? _cachedMaterial;
    private XModelRenderScene? _cachedMaterialScene;
    private XModelRenderScene? _selectedVisualScene;
    private SoundPreviewViewModel? _selectedSoundPreview;
    private FxPreviewFrame? _previewFrame;
    private string _selectedVisualBuildStatus = string.Empty;
    private string? _selectedVisualRendererStatus;
    private long _visualSceneCacheRevision = -1;
    private double _currentMilliseconds;
    private double _playbackStartMilliseconds;
    private long _playbackStartTimestamp;
    private bool _showSelectedOnly;
    private bool _isPlaying;
    private bool _disposed;

    public FxEditorViewModel(
        FxEffectDefAsset effect,
        FastFileWorkspace? workspace = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effect = effect;
        _workspace = workspace;
        _imagePayloads = workspace is null
            ? null
            : new WorkspaceGfxImagePayloadResolver(workspace);

        Name = string.IsNullOrWhiteSpace(effect.Name)
            ? "<unnamed FX>"
            : effect.Name;
        string rootFlagsText = $"0x{unchecked((uint)effect.Flags):X8}";
        string totalSizeText = $"{effect.TotalSize:N0} bytes";
        LoopingLifeText = effect.MsecLoopingLife == int.MaxValue
            ? "Infinite"
            : FormatMilliseconds(effect.MsecLoopingLife);
        LoopingCount = effect.ElemDefCountLooping;
        OneShotCount = effect.ElemDefCountOneShot;
        EmissionCount = effect.ElemDefCountEmission;

        var elements = new FxElementViewModel[effect.ElemDefs.Count];
        for (int index = 0; index < elements.Length; index++)
        {
            FxElementGroup group;
            int groupIndex;
            if (index < LoopingCount)
            {
                group = FxElementGroup.Looping;
                groupIndex = index;
            }
            else if (index < LoopingCount + OneShotCount)
            {
                group = FxElementGroup.OneShot;
                groupIndex = index - LoopingCount;
            }
            else
            {
                group = FxElementGroup.Emission;
                groupIndex = index - LoopingCount - OneShotCount;
            }

            elements[index] = new FxElementViewModel(
                effect.ElemDefs[index],
                index,
                group,
                groupIndex);
        }

        Elements = Array.AsReadOnly(elements);
        _selectedElement = Elements.FirstOrDefault();
        RootProperties =
        [
            new("Flags", rootFlagsText),
            new("Compiled allocation", totalSizeText),
            new("Looping life", LoopingLifeText),
            new("Looping rows", LoopingCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("One-shot rows", OneShotCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Emission rows", EmissionCount.ToString("N0", CultureInfo.CurrentCulture))
        ];

        if (FxPreviewScene.TryCreate(
                effect,
                out FxPreviewScene? previewScene,
                out string reason) &&
            previewScene is not null)
        {
            _previewScene = previewScene;
            _previewUnavailableReason = string.Empty;
            _previewFrame = previewScene.Sample(0f);
        }
        else
        {
            _previewUnavailableReason = reason;
        }

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = PlaybackTickInterval
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        RefreshSelectedVisualDependencies();
    }

    public string Name { get; }

    public string LoopingLifeText { get; }

    public int LoopingCount { get; }

    public int OneShotCount { get; }

    public int EmissionCount { get; }

    public int ElementCount => Elements.Count;

    public string ElementCountText =>
        $"{ElementCount:N0} {(ElementCount == 1 ? "element" : "elements")}";

    public string GroupCountText =>
        $"{LoopingCount:N0} looping · {OneShotCount:N0} one-shot · " +
        $"{EmissionCount:N0} emission";

    public IReadOnlyList<FxElementViewModel> Elements { get; }

    public IReadOnlyList<AssetEditorProperty> RootProperties { get; }

    public FxElementViewModel? SelectedElement
    {
        get => _selectedElement;
        set
        {
            if (value is not null && !Elements.Contains(value))
                return;
            if (!SetProperty(ref _selectedElement, value))
                return;

            OnPropertyChanged(nameof(SelectedElementIndex));
            OnPropertyChanged(nameof(HasSelectedElement));
            OnPropertyChanged(nameof(SelectedElementTitle));
            OnPropertyChanged(nameof(SelectedElementSummary));
            OnPropertyChanged(nameof(SelectedProperties));
            OnPropertyChanged(nameof(PropertySectionName));
            OnPropertyChanged(nameof(EditorProperties));
            OnPropertyChanged(nameof(PreviewRepresentationText));
            RefreshSelectedVisualDependencies();
            if (ShowSelectedOnly)
                RefreshFrame();
        }
    }

    public int SelectedElementIndex => SelectedElement?.GlobalIndex ?? -1;

    public bool HasSelectedElement => SelectedElement is not null;

    public string SelectedElementTitle => SelectedElement?.DisplayName ??
        "No element selected";

    public string SelectedElementSummary => SelectedElement?.DetailSummary ??
        "Select an FX element to inspect its compiled data.";

    public IReadOnlyList<AssetEditorProperty> SelectedProperties =>
        SelectedElement?.Properties ?? RootProperties;

    public string PropertySectionName => SelectedElement is null
        ? "FX DATA"
        : $"FX ELEMENT {SelectedElement.GlobalIndex:N0}";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
        SelectedProperties;

    public FxPreviewFrame? PreviewFrame
    {
        get => _previewFrame;
        private set
        {
            if (!SetProperty(ref _previewFrame, value))
                return;
            OnPropertyChanged(nameof(VisibleInstanceCountText));
        }
    }

    public bool HasPreview => _previewScene is not null;

    public int PreviewDurationMilliseconds =>
        _previewScene?.DurationMilliseconds ?? 0;

    public string PreviewDurationText => HasPreview
        ? FormatMilliseconds(PreviewDurationMilliseconds)
        : "Unavailable";

    public string VisibleInstanceCountText =>
        $"{PreviewFrame?.Instances.Count ?? 0:N0} live / " +
        $"{_previewScene?.ScheduledInstanceCount ?? 0:N0} scheduled";

    public string PreviewStatus
    {
        get
        {
            if (!HasPreview)
                return _previewUnavailableReason;

            var parts = new List<string>
            {
                "fixed editor seed 173",
                "editor random approximation",
                "root looping + one-shot rows",
                "compiled visual and velocity curves"
            };
            if (EmissionCount > 0)
                parts.Add("emission rows shown structurally only");
            if (_previewScene!.DurationWasCapped)
                parts.Add("timeline capped at 8 seconds");
            if (_previewScene.InstanceLimitWasApplied)
                parts.Add("instance count bounded for editor playback");
            return string.Join(" · ", parts);
        }
    }

    public string PreviewRepresentationText => SelectedElement is null
        ? "Select an element to see its renderer fidelity."
        : SelectedElement.PreviewRepresentation;

    public string RendererScopeText =>
        "The sandbox uses a fixed editor seed and deterministic hash rather " +
        "than the engine random table. It evaluates root birth timing, " +
        "delay/lifetime ranges, " +
        "spawn offsets, velocity/gravity motion, and color/alpha/size/scale/" +
        "rotation samples. It renders billboard color cards and explicit " +
        "debug glyphs for specialized engine systems. The Visual / Audio " +
        "tab separately executes the selected Material's editor camera-color " +
        "shader/texture graph, renders selected XModels through their authored " +
        "camera-color material passes, and offers manual Sound alias playback; " +
        "those dependencies are inspected at the origin rather than emitted " +
        "inside the sandbox.";

    public IReadOnlyList<FxVisualDependencyViewModel>
        SelectedVisualDependencies => _selectedVisualDependencies;

    public bool HasMultipleVisualDependencies =>
        SelectedVisualDependencies.Count > 1;

    public FxVisualDependencyViewModel? SelectedVisualDependency
    {
        get => _selectedVisualDependency;
        set
        {
            if (value is null &&
                _selectedVisualDependency is not null &&
                SelectedVisualDependencies.Contains(
                    _selectedVisualDependency))
            {
                return;
            }
            if (value is not null &&
                !SelectedVisualDependencies.Contains(value))
            {
                return;
            }
            if (!SetProperty(ref _selectedVisualDependency, value))
                return;

            RefreshSelectedVisualProjection();
            OnPropertyChanged(nameof(SelectedVisualTitle));
            OnPropertyChanged(nameof(SelectedVisualKindText));
        }
    }

    public string SelectedVisualTitle =>
        SelectedVisualDependency?.AssetName ?? "No previewable dependency";

    public string SelectedVisualKindText =>
        SelectedVisualDependency?.KindText ?? "NO VISUAL";

    public XModelRenderScene? SelectedVisualScene => _selectedVisualScene;

    public int SelectedVisualLodIndex =>
        SelectedVisualScene?.DefaultLodIndex ?? -1;

    public bool HasSelectedVisualScene => SelectedVisualScene is not null;

    public SoundPreviewViewModel? SelectedSoundPreview =>
        _selectedSoundPreview;

    public bool HasSelectedSoundPreview => SelectedSoundPreview is not null;

    public bool ShowsSelectedVisualEmptyState =>
        !HasSelectedVisualScene && !HasSelectedSoundPreview;

    public string SelectedVisualStatus =>
        !string.IsNullOrWhiteSpace(_selectedVisualRendererStatus)
            ? _selectedVisualRendererStatus
            : _selectedVisualBuildStatus;

    public bool HasSelectedVisualRendererFailure =>
        !string.IsNullOrWhiteSpace(_selectedVisualRendererStatus);

    public void ReportSelectedVisualRendererStatus(string? message)
    {
        string? normalized = !HasSelectedVisualScene ||
            string.IsNullOrWhiteSpace(message)
            ? null
            : message;
        if (string.Equals(
                _selectedVisualRendererStatus,
                normalized,
                StringComparison.Ordinal))
        {
            return;
        }

        _selectedVisualRendererStatus = normalized;
        OnPropertyChanged(nameof(SelectedVisualStatus));
        OnPropertyChanged(nameof(HasSelectedVisualRendererFailure));
    }

    public double CurrentMilliseconds
    {
        get => _currentMilliseconds;
        set
        {
            if (_disposed || !HasPreview)
                return;
            PausePlayback();
            SetCurrentMilliseconds(value);
        }
    }

    public string CurrentTimeText =>
        $"{FormatMilliseconds((int)Math.Round(CurrentMilliseconds))} / " +
        PreviewDurationText;

    public bool ShowSelectedOnly
    {
        get => _showSelectedOnly;
        set
        {
            if (!SetProperty(ref _showSelectedOnly, value))
                return;
            RefreshFrame();
        }
    }

    public bool CanPlay =>
        !_disposed &&
        _previewScene is { ScheduledInstanceCount: > 0 } &&
        PreviewDurationMilliseconds > 0;

    public bool CanRestart => !_disposed && HasPreview;

    public bool IsPlaying => _isPlaying;

    public bool ShowPlayIcon => !IsPlaying;

    public bool ShowPauseIcon => IsPlaying;

    public string PlayPauseToolTip => IsPlaying
        ? "Pause FX preview"
        : "Play FX preview";

    public void TogglePlayback()
    {
        if (IsPlaying)
        {
            PausePlayback();
            return;
        }
        if (!CanPlay)
            return;

        if (CurrentMilliseconds >= PreviewDurationMilliseconds)
            SetCurrentMilliseconds(0d);
        _playbackStartMilliseconds = CurrentMilliseconds;
        _playbackStartTimestamp = Stopwatch.GetTimestamp();
        _isPlaying = true;
        _playbackTimer.Start();
        NotifyPlaybackStateChanged();
    }

    public void PausePlayback()
    {
        if (!IsPlaying)
            return;

        UpdatePlaybackPosition();
        _playbackTimer.Stop();
        _isPlaying = false;
        NotifyPlaybackStateChanged();
    }

    public void RestartPlayback()
    {
        if (!CanRestart)
            return;

        SetCurrentMilliseconds(0d);
        if (IsPlaying)
        {
            _playbackStartMilliseconds = 0d;
            _playbackStartTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playbackTimer.Stop();
        _playbackTimer.Tick -= PlaybackTimer_Tick;
        ReplaceSelectedSoundPreview(null);
        ClearVisualSceneCache();
        if (_isPlaying)
        {
            _isPlaying = false;
            NotifyPlaybackStateChanged();
        }
    }

    private void RefreshSelectedVisualDependencies()
    {
        FxElemDef? element = SelectedElementIndex >= 0 &&
            SelectedElementIndex < _effect.ElemDefs.Count
                ? _effect.ElemDefs[SelectedElementIndex]
                : null;
        _selectedVisualDependencies = element is null
            ? []
            : BuildVisualDependencies(element);
        _selectedVisualDependency =
            _selectedVisualDependencies.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedVisualDependencies));
        OnPropertyChanged(nameof(HasMultipleVisualDependencies));
        OnPropertyChanged(nameof(SelectedVisualDependency));
        OnPropertyChanged(nameof(SelectedVisualTitle));
        OnPropertyChanged(nameof(SelectedVisualKindText));
        RefreshSelectedVisualProjection();
    }

    private void RefreshSelectedVisualProjection()
    {
        _selectedVisualScene = null;
        _selectedVisualRendererStatus = null;
        ReplaceSelectedSoundPreview(null);

        FxVisualDependencyViewModel? dependency =
            SelectedVisualDependency;
        if (dependency is null)
        {
            _selectedVisualBuildStatus =
                SelectedElement is null
                    ? "Select an FX element to inspect its referenced visual or audio asset."
                    : SelectedElement.GlobalIndex < _effect.ElemDefs.Count
                        ? DescribeMissingDependency(
                            _effect.ElemDefs[SelectedElement.GlobalIndex])
                        : "The selected FX element is unavailable.";
            NotifySelectedVisualProjectionChanged();
            return;
        }

        if (_workspace is null || _imagePayloads is null)
        {
            _selectedVisualBuildStatus =
                $"{dependency.KindText} '{dependency.AssetName}' cannot be " +
                "previewed because no loaded fastfile workspace is available.";
            NotifySelectedVisualProjectionChanged();
            return;
        }

        try
        {
            EnsureVisualSceneCacheRevision();
            switch (dependency.Kind)
            {
                case FxVisualDependencyKind.Material:
                    BuildSelectedMaterialPreview(dependency);
                    break;
                case FxVisualDependencyKind.XModel:
                    BuildSelectedModelPreview(dependency);
                    break;
                case FxVisualDependencyKind.Sound:
                    BuildSelectedSoundPreview(dependency);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _selectedVisualBuildStatus =
                $"{dependency.KindText} '{dependency.AssetName}' preview " +
                $"failed: {exception.Message}";
        }

        NotifySelectedVisualProjectionChanged();
    }

    private void BuildSelectedMaterialPreview(
        FxVisualDependencyViewModel dependency)
    {
        if (dependency.Material is not { } sourceMaterial)
        {
            _selectedVisualBuildStatus =
                $"Material reference '{dependency.AssetName}' is unresolved.";
            return;
        }

        MaterialAsset material = ResolveCurrentMaterial(sourceMaterial);
        XModelRenderScene scene;
        if (ReferenceEquals(_cachedMaterial, material) &&
            _cachedMaterialScene is { } cachedScene)
        {
            scene = cachedScene;
        }
        else
        {
            long poolRevision = _workspace!.LoadedZone.Context.AssetPool.Revision;
            scene = _visualSceneBuilder.BuildMaterialPreview(
                material,
                WorkspaceRenderAssetSource.Create(
                    _workspace,
                    "FX Material preview assets"),
                _imagePayloads!,
                CaptureInlineMaterialAssets(material));
            EnsurePoolRevision(poolRevision, "Material");
            _cachedMaterial = material;
            _cachedMaterialScene = scene;
        }

        _selectedVisualScene = scene;
        _selectedVisualBuildStatus = scene.Diagnostics.Count == 0
            ? "Prepared authored Material on the preview sphere · the renderer " +
              "will execute its translated camera-color shader, samplers, " +
              "textures, constants, and render state."
            : "Authored Material on the preview sphere · " +
              $"{scene.Diagnostics.Count:N0} scene diagnostic(s): " +
              scene.Diagnostics[0];
    }

    private void BuildSelectedModelPreview(
        FxVisualDependencyViewModel dependency)
    {
        if (dependency.Model is not { } sourceModel)
        {
            _selectedVisualBuildStatus =
                $"XModel reference '{dependency.AssetName}' is unresolved.";
            return;
        }

        XModelAsset model = ResolveCurrentModel(sourceModel);
        XModelRenderScene scene;
        if (ReferenceEquals(_cachedModel, model) &&
            _cachedModelScene is { } cachedScene)
        {
            scene = cachedScene;
        }
        else
        {
            long poolRevision = _workspace!.LoadedZone.Context.AssetPool.Revision;
            scene = _visualSceneBuilder.Build(
                model,
                WorkspaceRenderAssetSource.Create(
                    _workspace,
                    "FX XModel material assets"),
                _imagePayloads!);
            EnsurePoolRevision(poolRevision, "XModel");
            if (scene.Lods.Count > 0)
            {
                _cachedModel = model;
                _cachedModelScene = scene;
            }
        }

        if (scene.Lods.Count == 0)
        {
            _selectedVisualBuildStatus =
                $"XModel '{dependency.AssetName}' has no complete loaded LOD geometry.";
            return;
        }

        _selectedVisualScene = scene;
        string lodText = $"{scene.Lods.Count:N0} LOD" +
            (scene.Lods.Count == 1 ? string.Empty : "s");
        _selectedVisualBuildStatus = scene.Diagnostics.Count == 0
            ? $"Prepared XModel · {lodText} · the renderer will execute its " +
              "available authored camera-color material shaders and textures."
            : $"Prepared XModel · {lodText} · " +
              $"{scene.Diagnostics.Count:N0} scene diagnostic(s); the renderer " +
              $"will execute only available material passes. {scene.Diagnostics[0]}";
    }

    private void BuildSelectedSoundPreview(
        FxVisualDependencyViewModel dependency)
    {
        string? soundName = dependency.SoundName;
        if (string.IsNullOrWhiteSpace(soundName) ||
            !_workspace!.LoadedZone.Context.AssetPool.TryResolve(
                XAssetType.Sound,
                soundName,
                out SoundAliasListAsset? sound) ||
            sound is null ||
            !_workspace.LoadedZone.Context.AssetPool.TryGetEntry(
                sound,
                out var entry) ||
            entry.IsReferencePlaceholder)
        {
            _selectedVisualBuildStatus =
                $"Sound alias reference '{dependency.AssetName}' is unresolved.";
            return;
        }

        _workspace.TryGetSoundPayloadResolver(
            sound,
            out ISoundPayloadResolver resolver,
            out string unavailableReason);
        ReplaceSelectedSoundPreview(new SoundPreviewViewModel(
            sound,
            resolver,
            unavailableReason));
        _selectedVisualBuildStatus =
            "Manual Sound alias preview · audio is never started by FX " +
            "timeline playback or scrubbing.";
    }

    private MaterialAsset ResolveCurrentMaterial(MaterialAsset material)
    {
        string? name = material.Info.Name;
        return !string.IsNullOrWhiteSpace(name) &&
            _workspace!.LoadedZone.Context.AssetPool.TryResolve(
                XAssetType.Material,
                name,
                out MaterialAsset? current) &&
            current is not null
                ? current
                : material;
    }

    private XModelAsset ResolveCurrentModel(XModelAsset model)
    {
        return !string.IsNullOrWhiteSpace(model.Name) &&
            _workspace!.LoadedZone.Context.AssetPool.TryResolve(
                XAssetType.XModel,
                model.Name,
                out XModelAsset? current) &&
            current is not null
                ? current
                : model;
    }

    private void EnsureVisualSceneCacheRevision()
    {
        long revision = _workspace!.LoadedZone.Context.AssetPool.Revision;
        if (_visualSceneCacheRevision == revision)
            return;

        _visualSceneCacheRevision = revision;
        ClearVisualSceneCache();
    }

    private void ClearVisualSceneCache()
    {
        _cachedModel = null;
        _cachedModelScene = null;
        _cachedMaterial = null;
        _cachedMaterialScene = null;
    }

    private void EnsurePoolRevision(long expectedRevision, string assetKind)
    {
        long currentRevision =
            _workspace!.LoadedZone.Context.AssetPool.Revision;
        if (currentRevision != expectedRevision)
        {
            throw new InvalidOperationException(
                $"The asset pool changed while the selected FX {assetKind} " +
                $"preview was being built: start={expectedRevision};end={currentRevision}.");
        }
    }

    private void ReplaceSelectedSoundPreview(
        SoundPreviewViewModel? preview)
    {
        if (ReferenceEquals(_selectedSoundPreview, preview))
            return;

        _selectedSoundPreview?.Dispose();
        _selectedSoundPreview = preview;
        OnPropertyChanged(nameof(SelectedSoundPreview));
        OnPropertyChanged(nameof(HasSelectedSoundPreview));
        OnPropertyChanged(nameof(ShowsSelectedVisualEmptyState));
    }

    private void NotifySelectedVisualProjectionChanged()
    {
        OnPropertyChanged(nameof(SelectedVisualScene));
        OnPropertyChanged(nameof(SelectedVisualLodIndex));
        OnPropertyChanged(nameof(HasSelectedVisualScene));
        OnPropertyChanged(nameof(ShowsSelectedVisualEmptyState));
        OnPropertyChanged(nameof(SelectedVisualStatus));
        OnPropertyChanged(nameof(HasSelectedVisualRendererFailure));
    }

    private static IReadOnlyList<BaseAsset> CaptureInlineMaterialAssets(
        MaterialAsset material)
    {
        var staged = new List<BaseAsset>();
        foreach (MaterialTextureDef texture in material.Textures)
        {
            GfxImageAsset? image = texture.Water?.Image ?? texture.Image;
            if (image is not null &&
                image.RuntimeAddress?.AssetPoolAddress is null)
            {
                staged.Add(image);
            }
        }
        return Array.AsReadOnly(staged.ToArray());
    }

    private static IReadOnlyList<FxVisualDependencyViewModel>
        BuildVisualDependencies(FxElemDef element)
    {
        var dependencies = new List<FxVisualDependencyViewModel>();
        if (element.ElemType == FxElemType.Decal)
        {
            foreach ((FxElemMarkVisuals mark, int index) in
                     element.MarkVisualArray
                         .Take(element.VisualCount)
                         .Select((mark, index) => (mark, index)))
            {
                dependencies.Add(FxVisualDependencyViewModel.ForMaterial(
                    $"Decal pair {index:N0} · material 0",
                    mark.Material0));
                dependencies.Add(FxVisualDependencyViewModel.ForMaterial(
                    $"Decal pair {index:N0} · material 1",
                    mark.Material1));
            }
            return Array.AsReadOnly(dependencies.ToArray());
        }

        IEnumerable<FxElemDefVisuals> visuals = element.VisualCount switch
        {
            0 => [],
            1 => [element.Visuals],
            _ => element.VisualArray.Take(element.VisualCount)
        };
        int visualIndex = 0;
        foreach (FxElemDefVisuals visual in visuals)
        {
            string slot = $"Visual {visualIndex:N0}";
            switch (visual.Visual)
            {
                case FxMaterialVisual material:
                    dependencies.Add(
                        FxVisualDependencyViewModel.ForMaterial(
                            slot,
                            material.Material));
                    break;
                case FxModelVisual model:
                    dependencies.Add(FxVisualDependencyViewModel.ForModel(
                        slot,
                        model.Model));
                    break;
                case FxSoundVisual sound:
                    dependencies.Add(FxVisualDependencyViewModel.ForSound(
                        slot,
                        sound.SoundName));
                    break;
            }
            visualIndex++;
        }
        return Array.AsReadOnly(dependencies.ToArray());
    }

    private static string DescribeMissingDependency(FxElemDef element) =>
        element.ElemType switch
        {
            FxElemType.OmniLight or FxElemType.SpotLight =>
                "Engine light elements have no child Material, XModel, or Sound asset.",
            FxElemType.Runner =>
                "Runner elements reference nested FX. Recursive runner execution is not enabled.",
            FxElemType.Trail =>
                "This Trail has no loaded Material visual dependency to inspect.",
            _ => "This element has no loaded Material, XModel, or Sound dependency to inspect."
        };

    private void PlaybackTimer_Tick(object? sender, EventArgs e) =>
        UpdatePlaybackPosition();

    private void UpdatePlaybackPosition()
    {
        if (!IsPlaying || !CanPlay)
            return;

        double elapsedMilliseconds = Stopwatch.GetElapsedTime(
            _playbackStartTimestamp).TotalMilliseconds;
        double time = _playbackStartMilliseconds + elapsedMilliseconds;
        if (time >= PreviewDurationMilliseconds)
        {
            time %= PreviewDurationMilliseconds;
            _playbackStartMilliseconds = time;
            _playbackStartTimestamp = Stopwatch.GetTimestamp();
        }
        SetCurrentMilliseconds(time);
    }

    private void SetCurrentMilliseconds(double value)
    {
        double bounded = Math.Clamp(
            double.IsFinite(value) ? value : 0d,
            0d,
            PreviewDurationMilliseconds);
        if (!SetProperty(ref _currentMilliseconds, bounded))
            return;

        OnPropertyChanged(nameof(CurrentTimeText));
        RefreshFrame();
    }

    private void RefreshFrame()
    {
        if (_previewScene is null)
        {
            PreviewFrame = null;
            return;
        }

        int? isolated = ShowSelectedOnly && SelectedElement is not null
            ? SelectedElement.GlobalIndex
            : null;
        PreviewFrame = _previewScene.Sample(
            (float)CurrentMilliseconds,
            isolated);
    }

    private void NotifyPlaybackStateChanged()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(ShowPlayIcon));
        OnPropertyChanged(nameof(ShowPauseIcon));
        OnPropertyChanged(nameof(PlayPauseToolTip));
    }

    internal static string FormatMilliseconds(int milliseconds)
    {
        if (milliseconds == int.MaxValue)
            return "Infinite";
        if (milliseconds < 0)
            return $"{milliseconds:N0} ms";
        TimeSpan duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalSeconds >= 1d
            ? $"{duration.TotalSeconds:0.###} s"
            : $"{milliseconds:N0} ms";
    }
}

public sealed class FxElementViewModel
{
    internal FxElementViewModel(
        FxElemDef element,
        int globalIndex,
        FxElementGroup group,
        int groupIndex)
    {
        ArgumentNullException.ThrowIfNull(element);
        GlobalIndex = globalIndex;
        GroupLabel = group switch
        {
            FxElementGroup.Looping => "LOOPING",
            FxElementGroup.OneShot => "ONE-SHOT",
            _ => "EMISSION"
        };
        TypeText = FormatIdentifier(element.ElemType.ToString());
        DisplayName = $"{TypeText} · {GroupLabel} {groupIndex:N0}";
        TimingSummary = FormatTiming(element, group);
        VisualSummary = FormatVisualSummary(element);
        string sampleSummary =
            $"{element.VelSamples.Count:N0} velocity · " +
            $"{element.VisSamples.Count:N0} visual samples";
        DetailSummary =
            $"{TimingSummary} · {VisualSummary} · {sampleSummary}";
        PreviewRepresentation = FormatPreviewRepresentation(
            element.ElemType,
            group);
        Properties = BuildProperties(element, group, globalIndex, groupIndex);
    }

    public int GlobalIndex { get; }

    public string GroupLabel { get; }

    public string TypeText { get; }

    public string DisplayName { get; }

    public string TimingSummary { get; }

    public string VisualSummary { get; }

    public string DetailSummary { get; }

    public string PreviewRepresentation { get; }

    public IReadOnlyList<AssetEditorProperty> Properties { get; }

    private static IReadOnlyList<AssetEditorProperty> BuildProperties(
        FxElemDef element,
        FxElementGroup group,
        int globalIndex,
        int groupIndex)
    {
        var properties = new List<AssetEditorProperty>
        {
            new("Table index", globalIndex.ToString("N0", CultureInfo.CurrentCulture)),
            new("Element band", $"{GroupName(group)} row {groupIndex:N0}"),
            new("Type", $"{FormatIdentifier(element.ElemType.ToString())} ({(byte)element.ElemType:N0})"),
            new("Flags", $"0x{unchecked((uint)element.Flags):X8}"),
            new(SpawnPropertyName(group), FormatSpawn(element, group)),
            new("Spawn delay", FormatRange(element.SpawnDelayMsec, "ms")),
            new("Life span", FormatRange(element.LifeSpanMsec, "ms")),
            new("Spawn range", FormatRange(element.SpawnRange)),
            new("Fade-in range", FormatRange(element.FadeInRange)),
            new("Fade-out range", FormatRange(element.FadeOutRange)),
            new("Frustum cull radius", FormatFloat(element.SpawnFrustumCullRadius)),
            new("Spawn origin", FormatRanges(element.SpawnOrigin)),
            new("Radial offset", FormatRange(element.SpawnOffsetRadius)),
            new("Height offset", FormatRange(element.SpawnOffsetHeight)),
            new("Spawn angles", FormatRanges(element.SpawnAngles, " rad")),
            new("Angular velocity", FormatRanges(element.AngularVelocity, " rad/ms")),
            new("Initial rotation", FormatRange(element.InitialRotation, " rad")),
            new("Gravity", FormatRange(element.Gravity)),
            new("Reflection", FormatRange(element.ReflectionFactor)),
            new("Velocity samples", FormatSamples(element.VelIntervalCount, element.VelSamples.Count)),
            new("Visual samples", FormatSamples(element.VisStateIntervalCount, element.VisSamples.Count)),
            new("Visual alternatives", element.VisualCount.ToString("N0", CultureInfo.CurrentCulture)),
            new("Visuals", FormatVisualSummary(element)),
            new("Atlas", FormatAtlas(element.Atlas)),
            new("Collision midpoint", FormatVector(element.CollBounds.MidPoint)),
            new("Collision half-size", FormatVector(element.CollBounds.HalfSize)),
            new("FX on impact", FormatReference(element.EffectOnImpact.Name)),
            new("FX on death", FormatReference(element.EffectOnDeath.Name)),
            new("Emitted FX", FormatReference(element.EffectEmitted.Name)),
            new("Emit distance", FormatRange(element.EmitDist)),
            new("Emit variance", FormatRange(element.EmitDistVariance)),
            new("Extended payload", FormatExtended(element)),
            new("Sort order", element.SortOrder.ToString("N0", CultureInfo.CurrentCulture)),
            new("Lighting fraction", $"{element.LightingFrac:N0} / 255"),
            new("Use item clip", element.UseItemClip == 0 ? "No" : $"Yes ({element.UseItemClip:N0})"),
            new("Fade info", $"0x{element.FadeInfo:X2}")
        };
        return Array.AsReadOnly(properties.ToArray());
    }

    private static string FormatTiming(
        FxElemDef element,
        FxElementGroup group) => group switch
        {
            FxElementGroup.Looping =>
                $"every {FxEditorViewModel.FormatMilliseconds(element.Spawn.LoopingIntervalMsec)}, " +
                $"{FormatCount(element.Spawn.Count)}",
            FxElementGroup.OneShot =>
                $"{FormatRange(element.Spawn.LoopingIntervalMsec, element.Spawn.Count)} births",
            _ => "child-emission template"
        };

    private static string FormatSpawn(
        FxElemDef element,
        FxElementGroup group) => group switch
        {
            FxElementGroup.Looping =>
                $"interval {FxEditorViewModel.FormatMilliseconds(element.Spawn.LoopingIntervalMsec)} · " +
                $"count {FormatCount(element.Spawn.Count)}",
            FxElementGroup.OneShot =>
                $"count {FormatRange(element.Spawn.LoopingIntervalMsec, element.Spawn.Count)}",
            _ =>
                $"union words {element.Spawn.LoopingIntervalMsec:N0}, {element.Spawn.Count:N0} · driven by parent emission"
        };

    private static string SpawnPropertyName(FxElementGroup group) =>
        group switch
        {
            FxElementGroup.Looping => "Loop spawn",
            FxElementGroup.OneShot => "One-shot count",
            _ => "Emission spawn union"
        };

    private static string GroupName(FxElementGroup group) => group switch
    {
        FxElementGroup.Looping => "Looping",
        FxElementGroup.OneShot => "One-shot",
        _ => "Emission"
    };

    private static string FormatVisualSummary(FxElemDef element)
    {
        if (element.VisualCount == 0)
            return "No visual";
        if (element.ElemType == FxElemType.Decal)
        {
            string[] pairs = element.MarkVisualArray
                .Take(element.VisualCount)
                .Select((mark, index) =>
                    $"{index}: {MaterialName(mark.Material0)} + " +
                    MaterialName(mark.Material1))
                .ToArray();
            return pairs.Length == 0
                ? $"{element.VisualCount:N0} unresolved decal pair(s)"
                : string.Join("; ", pairs);
        }

        IEnumerable<FxElemDefVisuals> visuals = element.VisualCount > 1
            ? element.VisualArray.Take(element.VisualCount)
            : [element.Visuals];
        string[] names = visuals
            .Select(VisualName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        return names.Length == 0
            ? element.ElemType is FxElemType.OmniLight or FxElemType.SpotLight
                ? "No child visual (engine light element)"
                : $"{element.VisualCount:N0} unresolved visual(s)"
            : string.Join(", ", names);
    }

    private static string VisualName(FxElemDefVisuals visual) =>
        visual.Visual switch
        {
            FxMaterialVisual material => MaterialName(material.Material),
            FxModelVisual model => model.Model?.Name ?? "<unresolved XModel>",
            FxEffectVisual effect =>
                FormatReference(effect.EffectDef.Name),
            FxSoundVisual sound =>
                string.IsNullOrWhiteSpace(sound.SoundName)
                    ? "<unnamed sound alias>"
                    : sound.SoundName,
            FxNoChildVisual => "<no child>",
            _ => "<unresolved>"
        };

    private static string MaterialName(MaterialAsset? material) =>
        material?.Info.Name ?? "<unresolved material>";

    private static string FormatExtended(FxElemDef element) =>
        element.Extended switch
        {
            { TrailDef: { } trail } =>
                $"Trail · {trail.Verts.Count:N0} vertices · " +
                $"{trail.Inds.Count:N0} indices · repeat {trail.RepeatDist:N0}",
            { SparkFountainDef: { } spark } =>
                $"Spark fountain · {spark.SparkCount:N0} sparks · " +
                $"velocity {FormatFloat(spark.VelMin)}…{FormatFloat(spark.VelMax)}",
            { DefaultBytePayload: { } value } => $"Raw byte 0x{value:X2}",
            _ => "None"
        };

    private static string FormatAtlas(FxElemAtlas atlas)
    {
        int columns = 1 << Math.Min(atlas.ColIndexBits, (byte)15);
        int rows = 1 << Math.Min(atlas.RowIndexBits, (byte)15);
        string start = (atlas.Behavior & 0x03) switch
        {
            0 => "fixed start",
            1 => "random start",
            2 => "indexed start",
            _ => "fixed-range start"
        };
        string advance = (atlas.Behavior & 0x04) != 0
            ? "over life"
            : "by FPS";
        string loop = (atlas.Behavior & 0x08) != 0
            ? $"loop {atlas.LoopCount:N0}×"
            : "continuous";
        return $"behavior 0x{atlas.Behavior:X2} ({start}, {advance}, {loop}) · " +
            $"index {atlas.Index:N0} · " +
            $"{atlas.EntryCount:N0} entries · {columns:N0}×{rows:N0} · " +
            $"{atlas.Fps:N0} fps";
    }

    private static string FormatSamples(byte intervalCount, int sampleCount) =>
        sampleCount == 0
            ? $"Unavailable ({intervalCount:N0} intervals)"
            : $"{sampleCount:N0} samples across {intervalCount:N0} intervals";

    private static string FormatRange(FxIntRange range, string unit) =>
        $"base {range.Base:N0} · amplitude {range.Amplitude:N0} · " +
        $"range {FormatRange(range.Base, range.Amplitude)} {unit}";

    private static string FormatRange(int @base, int amplitude)
    {
        long end = (long)@base + amplitude;
        return $"{@base:N0}…{end:N0}";
    }

    private static string FormatRange(FxFloatRange range, string unit = "") =>
        $"base {FormatFloat(range.Base)} · amplitude " +
        $"{FormatFloat(range.Amplitude)} · range " +
        $"{FormatFloat(range.Base)}…{FormatFloat(range.Base + range.Amplitude)}{unit}";

    private static string FormatRanges(
        IReadOnlyList<FxFloatRange> ranges,
        string unit = "")
    {
        if (ranges.Count == 0)
            return "Unavailable";
        string[] axes = ["X", "Y", "Z"];
        return string.Join(
            " · ",
            ranges.Take(3).Select((range, index) =>
                $"{axes[index]} {FormatFloat(range.Base)}…" +
                $"{FormatFloat(range.Base + range.Amplitude)}{unit}"));
    }

    private static string FormatVector(Vec3 value) =>
        $"({FormatFloat(value.X)}, {FormatFloat(value.Y)}, {FormatFloat(value.Z)})";

    private static string FormatReference(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "None" : name;

    private static string FormatCount(int count) => count == int.MaxValue
        ? "infinite"
        : $"{count:N0} spawns";

    private static string FormatFloat(float value) =>
        float.IsFinite(value)
            ? value.ToString("0.######", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    private static string FormatIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        var result = new StringBuilder(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (index > 0 &&
                char.IsUpper(current) &&
                (char.IsLower(value[index - 1]) ||
                 index + 1 < value.Length && char.IsLower(value[index + 1])))
            {
                result.Append(' ');
            }
            result.Append(current);
        }
        return result.ToString();
    }

    private static string FormatPreviewRepresentation(
        FxElemType type,
        FxElementGroup group)
    {
        string prefix = group == FxElementGroup.Emission
            ? "Not root-scheduled. "
            : string.Empty;
        return prefix + (type switch
        {
            FxElemType.SpriteBillboard =>
                "Simulated birth, motion, and visual curves; drawn as a camera-facing color card. The selected Material can be rendered separately with its authored shader and textures; FX atlas-frame UVs are not applied to the sandbox card.",
            FxElemType.SpriteOriented =>
                "Simulated birth, motion, and visual curves; drawn as an oriented-card proxy. The selected Material can be inspected separately; the complete engine orientation and atlas path is not active.",
            FxElemType.Tail =>
                "Simulated birth, motion, and visual curves; drawn as a velocity-aligned tail proxy.",
            FxElemType.Trail =>
                "Timeline marker plus stored trail-profile glyph. Runtime trail-point spawning, sweep topology, and scrolling are not simulated.",
            FxElemType.Cloud or FxElemType.SparkCloud =>
                "Type-colored cloud proxy. The specialized particle-cloud pool and material pipeline are not executed.",
            FxElemType.SparkFountain =>
                "Spark-fountain glyph using the compiled element timing; collision clusters and the extended trajectory simulation are not executed.",
            FxElemType.Model =>
                "Model-box proxy in the sandbox. The selected XModel can be rendered separately with its authored materials and textures; model physics and per-particle placement are not active.",
            FxElemType.OmniLight =>
                "Wire-sphere light proxy colored from the visual curve; it does not inject illumination into a scene.",
            FxElemType.SpotLight =>
                "Wire-cone light proxy colored from the visual curve; it does not inject illumination into a scene.",
            FxElemType.Sound =>
                "Speaker marker in the sandbox. The selected Sound alias can be played manually and is never auto-played by the FX timeline.",
            FxElemType.Decal =>
                "Planar decal marker in the sandbox. Each selected decal Material can be inspected on the preview sphere; projection still requires a receiver surface and collision context.",
            FxElemType.Runner =>
                "Nested-FX marker only. Recursive runner execution requires asset resolution plus cycle and depth guards.",
            _ => "Unknown element type rendered as a diagnostic marker."
        });
    }
}

public sealed class FxVisualDependencyViewModel
{
    private FxVisualDependencyViewModel(
        FxVisualDependencyKind kind,
        string slotLabel,
        string assetName,
        MaterialAsset? material = null,
        XModelAsset? model = null,
        string? soundName = null)
    {
        Kind = kind;
        SlotLabel = slotLabel;
        AssetName = assetName;
        Material = material;
        Model = model;
        SoundName = soundName;
    }

    internal FxVisualDependencyKind Kind { get; }

    internal MaterialAsset? Material { get; }

    internal XModelAsset? Model { get; }

    internal string? SoundName { get; }

    private string SlotLabel { get; }

    internal string AssetName { get; }

    internal string KindText => Kind switch
    {
        FxVisualDependencyKind.Material => "MATERIAL",
        FxVisualDependencyKind.XModel => "XMODEL",
        _ => "SOUND"
    };

    public string DisplayName =>
        $"{SlotLabel} · {KindText} · {AssetName}";

    internal static FxVisualDependencyViewModel ForMaterial(
        string slotLabel,
        MaterialAsset? material) => new(
            FxVisualDependencyKind.Material,
            slotLabel,
            material?.Info.Name ?? "<unresolved material>",
            material: material);

    internal static FxVisualDependencyViewModel ForModel(
        string slotLabel,
        XModelAsset? model) => new(
            FxVisualDependencyKind.XModel,
            slotLabel,
            model?.Name ?? "<unresolved XModel>",
            model: model);

    internal static FxVisualDependencyViewModel ForSound(
        string slotLabel,
        string? soundName) => new(
            FxVisualDependencyKind.Sound,
            slotLabel,
            string.IsNullOrWhiteSpace(soundName)
                ? "<unnamed sound alias>"
                : soundName,
            soundName: soundName);
}

internal enum FxVisualDependencyKind
{
    Material,
    XModel,
    Sound
}

internal enum FxElementGroup
{
    Looping,
    OneShot,
    Emission
}
