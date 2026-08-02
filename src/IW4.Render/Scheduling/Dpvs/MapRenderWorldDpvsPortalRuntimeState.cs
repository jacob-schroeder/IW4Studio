using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>Per-walk writable bytes/pointers from the head of GfxPortal.</summary>
internal sealed class MapRenderWorldDpvsPortalRuntimeState
{
    public MapRenderWorldDpvsPortalRuntimeState()
    {
        HullPoints = [];
    }

    public bool IsQueued { get; set; }

    public bool IsAncestor { get; set; }

    public byte RecursionDepth { get; set; }

    public List<Vector2> HullPoints { get; }

    public GfxPortal? QueuedParent { get; set; }

    public void Reset()
    {
        IsQueued = false;
        IsAncestor = false;
        RecursionDepth = 0;
        HullPoints.Clear();
        QueuedParent = null;
    }
}
