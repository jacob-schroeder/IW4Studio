namespace IW4.Render.Scheduling.Clear;

/// <summary>
/// Inputs to PS3 <c>R_GetFarPlaneDist</c>. A nonzero <c>r_zfar</c> value wins;
/// zero selects the renderer fallback.
/// </summary>
public sealed class MapRenderNormalCameraFarPlaneState
{
    public MapRenderNormalCameraFarPlaneState(
        float rZFar,
        float rendererFallback)
    {
        if (!float.IsFinite(rZFar))
            throw new ArgumentOutOfRangeException(nameof(rZFar));
        if (!float.IsFinite(rendererFallback))
            throw new ArgumentOutOfRangeException(nameof(rendererFallback));

        RZFar = rZFar;
        RendererFallback = rendererFallback;
    }

    public float RZFar { get; }

    public float RendererFallback { get; }

    public float EffectiveDistance => RZFar != 0.0f
        ? RZFar
        : RendererFallback;
}
