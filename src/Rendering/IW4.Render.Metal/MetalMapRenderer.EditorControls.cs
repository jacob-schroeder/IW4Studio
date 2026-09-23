using System.Runtime.Versioning;

using IW4.Render.Preview;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.Metal;

public sealed record MapRenderMetalNormalCameraPresentationExecutionResult(
    RenderFramePlan FramePlan,
    bool UsesFilmColorManipulation,
    bool UsesGlow,
    bool ResolvedStoredSamplePair,
    bool ExecutedFilmColorManipulation,
    bool ExecutedTranslatedPostFx,
    bool ExecutedGlow,
    int GlowGaussianPassCount,
    int FullscreenDrawCount,
    bool WroteCurrentHostBackBuffer)
{
    public long FrameRevision => FramePlan.FrameRevision;

    public MapRenderSurfaceExtents SurfaceExtents =>
        FramePlan.SurfaceExtents;

    public MapRenderPixelExtent SceneTargetExtent =>
        SurfaceExtents.SceneTarget;

    public MapRenderPixelExtent HostFramebufferExtent =>
        SurfaceExtents.HostFramebuffer;

    public bool RequiresLinearHostScale => SurfaceExtents.RequiresHostScale;

    public bool UsesLinearHostSampling => true;

    public bool IsSuccess =>
        ResolvedStoredSamplePair &&
        (!UsesFilmColorManipulation || ExecutedFilmColorManipulation) &&
        ExecutedTranslatedPostFx &&
        (!UsesGlow || ExecutedGlow) &&
        WroteCurrentHostBackBuffer;
}

[SupportedOSPlatform("macos")]
public sealed partial class MetalMapRenderer
{
    private float? _previewAnimationTimeSecondsOverride;
    private int? _loadedIsolatedWorldSurfaceIndex;

    public bool ShowSky { get; set; } = true;

    public bool UseRsxVertexPlacementDiagnostic { get; set; }

    public int? IsolatedWorldSurfaceIndex { get; set; }

    public int? RsxFragmentOutputDiagnostic { get; set; }

    /// <summary>
    /// Freezes the effective per-frame preview animation time when set.
    /// Leave null for monotonic elapsed-time behavior.
    /// </summary>
    public float? PreviewAnimationTimeSecondsOverride
    {
        get => _previewAnimationTimeSecondsOverride;
        set
        {
            if (value is { } animationTimeSeconds &&
                (!float.IsFinite(animationTimeSeconds) ||
                 animationTimeSeconds < 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Preview animation time must be finite and nonnegative.");
            }

            _previewAnimationTimeSecondsOverride = value is 0f
                ? 0f
                : value;
        }
    }

    public RenderFramePlan? LastFramePlan { get; private set; }

    public MapRenderMetalNormalCameraPresentationExecutionResult?
        LastEditorPreviewPresentationResult
    { get; private set; }

    private RenderPreviewSettings CreateFramePreviewSettings(
        float animationTimeSeconds) => new(
        showSky: ShowSky,
        showDiagnosticGeometry: ShowDiagnosticGeometry,
        showTexturedGeometry: ShowTexturedGeometry,
        showWireframe: ShowWireframe,
        isolatedWorldSurfaceIndex: _loadedIsolatedWorldSurfaceIndex,
        useRsxVertexPlacementDiagnostic:
            UseRsxVertexPlacementDiagnostic,
        rsxFragmentOutputDiagnostic: RsxFragmentOutputDiagnostic,
        animationTimeSeconds: animationTimeSeconds);

    internal static float ResolvePreviewAnimationTimeSeconds(
        float? animationTimeSecondsOverride,
        double elapsedTimeSeconds)
    {
        if (animationTimeSecondsOverride is { } fixedTime)
        {
            if (!float.IsFinite(fixedTime) || fixedTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(animationTimeSecondsOverride));
            }
            return fixedTime == 0f ? 0f : fixedTime;
        }
        if (!double.IsFinite(elapsedTimeSeconds) || elapsedTimeSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(elapsedTimeSeconds));

        float effectiveTime = (float)elapsedTimeSeconds;
        if (!float.IsFinite(effectiveTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedTimeSeconds),
                "Elapsed preview animation time exceeds the frame contract range.");
        }
        return effectiveTime == 0f ? 0f : effectiveTime;
    }
}
