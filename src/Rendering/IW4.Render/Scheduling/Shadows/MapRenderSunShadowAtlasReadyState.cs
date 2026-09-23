using IW4.Render.Scheduling.Dpvs;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Renderer-agnostic authority that both sun-shadow partitions completed for
/// one three-view frame. The backend resource remains separately owned and
/// must carry this identical revision before it can be bound.
/// </summary>
public sealed class MapRenderSunShadowAtlasReadyState
{
    internal MapRenderSunShadowAtlasReadyState(
        MapRenderWorldDpvsThreeViewFrame frame)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public long Revision => Frame.Revision;

    public MapRenderWorldDpvsThreeViewFrame Frame { get; }
}
