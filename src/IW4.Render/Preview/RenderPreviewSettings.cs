namespace IW4.Render.Preview;

/// <summary>
/// Backend-neutral, per-frame preview policy. Resource creation and graphics
/// API capability choices are deliberately absent; those remain backend work.
/// </summary>
public readonly record struct RenderPreviewSettings
{
    public RenderPreviewSettings(
        bool showSky,
        bool showDiagnosticGeometry,
        bool showTexturedGeometry,
        bool showWireframe,
        int? isolatedWorldSurfaceIndex,
        bool useRsxVertexPlacementDiagnostic,
        int? rsxFragmentOutputDiagnostic,
        float animationTimeSeconds = 0f)
    {
        if (isolatedWorldSurfaceIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(isolatedWorldSurfaceIndex));
        }
        if (rsxFragmentOutputDiagnostic is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rsxFragmentOutputDiagnostic));
        }
        if (!float.IsFinite(animationTimeSeconds) ||
            animationTimeSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(animationTimeSeconds),
                "Preview animation time must be finite and nonnegative.");
        }

        ShowSky = showSky;
        ShowDiagnosticGeometry = showDiagnosticGeometry;
        ShowTexturedGeometry = showTexturedGeometry;
        ShowWireframe = showWireframe;
        IsolatedWorldSurfaceIndex = isolatedWorldSurfaceIndex;
        UseRsxVertexPlacementDiagnostic =
            useRsxVertexPlacementDiagnostic;
        RsxFragmentOutputDiagnostic = rsxFragmentOutputDiagnostic;
        AnimationTimeSeconds = animationTimeSeconds == 0f
            ? 0f
            : animationTimeSeconds;
    }

    public bool ShowSky { get; }

    public bool ShowDiagnosticGeometry { get; }

    public bool ShowTexturedGeometry { get; }

    public bool ShowWireframe { get; }

    public int? IsolatedWorldSurfaceIndex { get; }

    public bool UseRsxVertexPlacementDiagnostic { get; }

    public int? RsxFragmentOutputDiagnostic { get; }

    /// <summary>
    /// Effective animation time for this exact frame. Callers that need
    /// deterministic captures provide a fixed value; interactive hosts may
    /// derive it from a monotonic clock before invoking the pure planner.
    /// </summary>
    public float AnimationTimeSeconds { get; }

    public static RenderPreviewSettings Default { get; } = new(
        showSky: true,
        showDiagnosticGeometry: false,
        showTexturedGeometry: true,
        showWireframe: false,
        isolatedWorldSurfaceIndex: null,
        useRsxVertexPlacementDiagnostic: false,
        rsxFragmentOutputDiagnostic: null,
        animationTimeSeconds: 0f);
}
