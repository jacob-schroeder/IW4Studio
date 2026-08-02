namespace IW4.Render.Scheduling.Fog;

/// <summary>
/// Canonical active, interpolated PS3 <c>GfxFog</c> state. Normal-camera
/// clearing and frame code-constant production consume this same object.
/// The independent <c>r_fog</c> enable dvar is intentionally not embedded.
/// </summary>
public sealed class MapRenderActiveFogState
{
    public MapRenderActiveFogState(
        int startTime,
        int finishTime,
        MapRenderBgra8Color color,
        float fogStart,
        float density,
        float fogMaxOpacity,
        MapRenderActiveSunFogState sunFog)
    {
        if (!float.IsFinite(fogStart))
            throw new ArgumentOutOfRangeException(nameof(fogStart));
        if (!float.IsFinite(density))
            throw new ArgumentOutOfRangeException(nameof(density));
        if (!float.IsFinite(fogMaxOpacity))
            throw new ArgumentOutOfRangeException(nameof(fogMaxOpacity));
        ArgumentNullException.ThrowIfNull(sunFog);

        StartTime = startTime;
        FinishTime = finishTime;
        Color = color;
        FogStart = fogStart;
        Density = density;
        FogMaxOpacity = fogMaxOpacity;
        SunFog = sunFog;
    }

    public int StartTime { get; }

    public int FinishTime { get; }

    public MapRenderBgra8Color Color { get; }

    public float FogStart { get; }

    public float Density { get; }

    public float FogMaxOpacity { get; }

    public MapRenderActiveSunFogState SunFog { get; }
}
