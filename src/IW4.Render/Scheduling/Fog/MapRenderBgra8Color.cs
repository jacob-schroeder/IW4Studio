namespace IW4.Render.Scheduling.Fog;

/// <summary>
/// Four-byte BGRA value in the order stored by the active PS3
/// <c>GfxFog</c> state.
/// </summary>
public readonly record struct MapRenderBgra8Color(
    byte Blue,
    byte Green,
    byte Red,
    byte Alpha);
