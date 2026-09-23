namespace IW4.Render.Scheduling.Clear;

/// <summary>
/// PS3 <c>r_clear</c> enum values consumed by <c>R_GetClearColor</c>.
/// </summary>
public enum MapRenderNormalCameraClearMode
{
    Never = 0,
    DeveloperOnlyBlink = 1,
    Blink = 2,
    Steady = 3,
    FogColor = 4
}
