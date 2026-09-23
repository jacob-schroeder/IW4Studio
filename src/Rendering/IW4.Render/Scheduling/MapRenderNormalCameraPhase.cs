namespace IW4.Render.Scheduling;

/// <summary>
/// PS3 normal-camera draw-list phase ids used by the Event1F phase dispatcher.
/// They are not the differently numbered Xbox output ids.
/// </summary>
public enum MapRenderNormalCameraPhase : byte
{
    LitOpaque = 0,
    LightMapOpaque = 1,
    LitTrans = 2,
    DepthHack = 3,
    Emissive = 6
}
