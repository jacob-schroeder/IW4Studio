using System.Runtime.Versioning;

using IW4.Render.Diagnostics;
using IW4.Render.EditorPreview;
using IW4.Render.Metal.Resources;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Metal;

[SupportedOSPlatform("macos")]
public sealed partial class MetalMapRenderer
{
    private const long DefaultTextureResidencyBudgetBytes =
        384L * 1024L * 1024L;
    private const long DefaultTextureUploadBudgetBytesPerFrame =
        24L * 1024L * 1024L;
    private const int DefaultTextureEvictionGraceFrames = 8;

    private readonly List<RenderSemanticIdentity>
        _normalCameraVisibleTextures = [];
    private readonly HashSet<RenderSemanticIdentity>
        _normalCameraVisibleTextureSet = [];
    private MetalTextureResidencyFrameResult
        _normalCameraTextureResidencyFrame;
    private long _normalCameraTextureResidencyFrameIndex = -1;
    private RenderCamera? _normalCameraTextureResidencyCamera;
    private bool _normalCameraVisibilityDrivenTextureResidency;

    /// <summary>
    /// Maximum full-resolution normal-camera texture storage retained by a
    /// camera-initialized progressive load. No-view loads remain completely
    /// eager. Permanent one-pixel fallback views are not charged.
    /// </summary>
    public long TextureResidencyBudgetBytes { get; set; } =
        DefaultTextureResidencyBudgetBytes;

    /// <summary>
    /// Maximum newly resident texture payload scheduled in one frame. One
    /// individually larger visible texture may cross this limit so it cannot
    /// remain deferred forever.
    /// </summary>
    public long TextureUploadBudgetBytesPerFrame { get; set; } =
        DefaultTextureUploadBudgetBytesPerFrame;

    public int TextureEvictionGraceFrames { get; set; } =
        DefaultTextureEvictionGraceFrames;

    public long TextureGpuResidentBytes =>
        _resources.ResidentTextureByteCount;

    private void PrepareNormalCameraTextureResidency(RenderCamera camera)
    {
        if (_normalCameraTextureResidencyFrameIndex == _frameIndex)
            return;

        using MapRenderCpuPhaseScope cpuPhase = _telemetry.BeginCpuPhase(
            MapRenderCpuPhase.StaticResourceAdmission);
        if (!_normalCameraVisibilityDrivenTextureResidency)
        {
            int residentCount = _resources.ResidentTextureCount;
            if (residentCount != _resources.TextureCount)
            {
                throw new InvalidOperationException(
                    "An eager Metal scene lost complete texture residency.");
            }
            _normalCameraTextureResidencyFrame = new(
                residentCount,
                _resources.ResidentTextureByteCount,
                UploadCount: 0,
                UploadBytes: 0,
                AuthoredUploadBytes: 0,
                EvictionCount: 0,
                EvictionBytes: 0,
                DeferredCount: 0);
            _normalCameraTextureResidencyFrameIndex = _frameIndex;
            _normalCameraTextureResidencyCamera = camera;
            PublishNormalCameraTextureResidencyTelemetry();
            return;
        }

        _normalCameraVisibleTextures.Clear();
        _normalCameraVisibleTextureSet.Clear();
        if (ShowTexturedGeometry && _sceneSnapshot is { } snapshot)
        {
            IReadOnlyList<MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot>> groups =
                    _drawOrder?.Order(
                        camera.Position,
                        camera.Forward) ??
                    snapshot.NormalCameraDraws.DrawGroups;
            // Translated RSX rows have no semantically valid fallback.
            // Request those bindings first so the bounded upload lane cannot
            // let generic preview textures starve exact authored execution.
            CollectVisibleNormalCameraTextures(
                groups,
                translatedOnly: true);
            CollectVisibleNormalCameraTextures(
                groups,
                translatedOnly: false);
        }

        _normalCameraTextureResidencyFrame =
            _resources.PrepareTextureResidency(
                _normalCameraVisibleTextures,
                _frameIndex,
                TextureResidencyBudgetBytes,
                TextureUploadBudgetBytesPerFrame,
                Math.Max(0, TextureEvictionGraceFrames));
        _normalCameraTextureResidencyFrameIndex = _frameIndex;
        _normalCameraTextureResidencyCamera = camera;
        PublishNormalCameraTextureResidencyTelemetry();
    }

    private void CollectVisibleNormalCameraTextures(
        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> groups,
        bool translatedOnly)
    {
        for (int groupIndex = 0;
             groupIndex < groups.Count;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    groups[groupIndex];
            if (!_normalCameraAuthorizedGroups.Contains(group) ||
                !IsNormalCameraGroupSelected(group))
            {
                continue;
            }
            int visibleRunCount = PrepareNormalCameraVisibleRuns(
                group,
                out _,
                out _);
            if (visibleRunCount == 0)
                continue;

            CollectNormalCameraGroupTextures(
                group,
                translatedOnly);
        }
    }

    private void PrefetchInitialNormalCameraTextureResidency(
        HashSet<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> initialGroups,
        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> neighborhoodGroups)
    {
        ArgumentNullException.ThrowIfNull(initialGroups);
        ArgumentNullException.ThrowIfNull(neighborhoodGroups);
        _normalCameraVisibleTextures.Clear();
        _normalCameraVisibleTextureSet.Clear();
        CollectPrefetchNormalCameraGroupTextures(
            initialGroups,
            neighborhoodGroups,
            translatedOnly: true,
            initialSelection: true);
        CollectPrefetchNormalCameraGroupTextures(
            initialGroups,
            neighborhoodGroups,
            translatedOnly: true,
            initialSelection: false);
        CollectPrefetchNormalCameraGroupTextures(
            initialGroups,
            neighborhoodGroups,
            translatedOnly: false,
            initialSelection: true);
        CollectPrefetchNormalCameraGroupTextures(
            initialGroups,
            neighborhoodGroups,
            translatedOnly: false,
            initialSelection: false);

        _resources.PrepareTextureResidency(
            _normalCameraVisibleTextures,
            frameIndex: 0,
            TextureResidencyBudgetBytes,
            uploadBudgetBytes: long.MaxValue,
            Math.Max(0, TextureEvictionGraceFrames));
    }

    private void CollectPrefetchNormalCameraGroupTextures(
        IReadOnlySet<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> initialGroups,
        IReadOnlyList<MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot>> neighborhoodGroups,
        bool translatedOnly,
        bool initialSelection)
    {
        for (int groupIndex = 0;
             groupIndex < neighborhoodGroups.Count;
             groupIndex++)
        {
            MapRenderEditorDrawGroup<
                RenderNormalCameraDrawSubmissionSnapshot> group =
                    neighborhoodGroups[groupIndex];
            if (initialGroups.Contains(group) != initialSelection)
                continue;

            CollectNormalCameraGroupTextures(
                group,
                translatedOnly);
        }
    }

    private void CollectNormalCameraGroupTextures(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group,
        bool translatedOnly)
    {
        ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot> draws =
            group.AuthoredPassSpan;
        for (int passIndex = 0; passIndex < draws.Length; passIndex++)
        {
            MetalPreparedNormalCameraPass pass =
                _normalCameraPasses[draws[passIndex].PreparedPass];
            bool translated = pass.GenericMaterial is null;
            if (translated != translatedOnly)
                continue;

            if (translated)
            {
                for (int bindingIndex = 0;
                     bindingIndex < pass.TextureBindings.Length;
                     bindingIndex++)
                {
                    AddVisibleTexture(
                        pass.TextureBindings[bindingIndex].Resource);
                }
                continue;
            }

            if (pass.GenericMaterial is not { } generic)
                continue;
            for (int destination = 0;
                 destination < generic.Textures.Length;
                 destination++)
            {
                if (IsGenericMaterialTextureActive(
                        generic,
                        destination))
                {
                    AddVisibleTexture(
                        generic.Textures[destination].Resource);
                }
            }
        }
    }

    private void AddVisibleTexture(MetalTextureResource texture)
    {
        RenderSemanticIdentity identity = texture.Descriptor.Identity;
        if (_normalCameraVisibleTextureSet.Add(identity))
            _normalCameraVisibleTextures.Add(identity);
    }

    private bool IsNormalCameraGroupTextureReady(
        MapRenderEditorDrawGroup<
            RenderNormalCameraDrawSubmissionSnapshot> group)
    {
        ReadOnlySpan<RenderNormalCameraDrawSubmissionSnapshot> draws =
            group.AuthoredPassSpan;
        for (int passIndex = 0; passIndex < draws.Length; passIndex++)
        {
            MetalPreparedNormalCameraPass pass =
                _normalCameraPasses[draws[passIndex].PreparedPass];
            if (pass.GenericMaterial is not null)
                continue;
            for (int bindingIndex = 0;
                 bindingIndex < pass.TextureBindings.Length;
                 bindingIndex++)
            {
                if (!pass.TextureBindings[bindingIndex].Resource.IsResident)
                    return false;
            }
        }
        return true;
    }

    private void PublishNormalCameraTextureResidencyTelemetry()
    {
        MetalTextureResidencyFrameResult frame =
            _normalCameraTextureResidencyFrame;
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidentCount,
            frame.ResidentCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidentBytes,
            frame.ResidentBytes);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidencyUploadCount,
            frame.UploadCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidencyUploadBytes,
            frame.UploadBytes);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidencyEvictionCount,
            frame.EvictionCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidencyEvictionBytes,
            frame.EvictionBytes);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureResidencyDeferredCount,
            frame.DeferredCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureAuthoredBcUploadBytes,
            frame.AuthoredUploadBytes);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureDecodedFallbackRetainedBytes,
            _resources.TextureDecodedFallbackRetainedByteCount);
        _telemetry.SetCounter(
            MapRenderFrameCounter.TextureAuthoredBcSourceBytes,
            _resources.TextureAuthoredSourceByteCount);
    }

    private bool IsNormalCameraTextureWorkingSetSettled(
        RenderCamera requestedCamera) =>
        _normalCameraTextureResidencyFrameIndex >= 0 &&
        _normalCameraTextureResidencyFrame.DeferredCount == 0 &&
        _normalCameraTextureResidencyCamera == requestedCamera;

    private void ResetNormalCameraTextureResidency()
    {
        _normalCameraVisibleTextures.Clear();
        _normalCameraVisibleTextureSet.Clear();
        _normalCameraTextureResidencyFrame = default;
        _normalCameraTextureResidencyFrameIndex = -1;
        _normalCameraTextureResidencyCamera = null;
    }

    private void ConfigureNormalCameraTextureResidency(
        bool visibilityDriven) =>
        _normalCameraVisibilityDrivenTextureResidency = visibilityDriven;

    private void ResetNormalCameraTextureResidencyFrameState()
    {
        _normalCameraTextureResidencyFrame = default;
        _normalCameraTextureResidencyFrameIndex = -1;
        _normalCameraTextureResidencyCamera = null;
    }
}
