using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// World-scoped mutable storage used by moving-camera DPVS. Each view owns a
/// distinct lane, so same-frame camera and shadow culls may execute in
/// parallel. A lane is cleared before every use and its immutable result
/// snapshots the active words before the lane is released.
/// </summary>
internal sealed class MapRenderWorldDpvsWorkingSet
{
    private readonly MapRenderWorldDpvsStaticCullWorkspace[]
        _staticCullByView;

    public MapRenderWorldDpvsWorkingSet(GfxWorldAsset world)
    {
        Topology = new(world);
        CameraCellResolver = new(Topology.CameraCellNodeVisitCapacity);
        PortalTraversal = new(Topology);
        CameraSky = new(Topology.SkySurfaceWordCount);
        SunShadowPartition0Traversal = new(
            world.DpvsPlanes.CellCount);
        SunShadowPartition1Traversal = new(
            world.DpvsPlanes.CellCount);
        _staticCullByView =
        [
            new(Topology),
            new(Topology),
            new(Topology)
        ];
    }

    public MapRenderWorldDpvsWorldTopology Topology { get; }

    public MapRenderWorldDpvsCameraCellResolverWorkspace
        CameraCellResolver { get; }

    public MapRenderWorldDpvsPortalTraversalWorkspace
        PortalTraversal { get; }

    public MapRenderWorldDpvsCameraSkyCullWorkspace CameraSky { get; }

    public MapRenderWorldDpvsSunShadowTraversalWorkspace
        SunShadowPartition0Traversal { get; }

    public MapRenderWorldDpvsSunShadowTraversalWorkspace
        SunShadowPartition1Traversal { get; }

    public void ValidateWorld(GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!ReferenceEquals(Topology.World, world))
        {
            throw new ArgumentException(
                "A DPVS working set can only be used with the exact world instance that created it.",
                nameof(world));
        }
    }

    public MapRenderWorldDpvsStaticCullWorkspace StaticCull(
        MapRenderWorldDpvsViewIndex viewIndex)
    {
        if (!Enum.IsDefined(viewIndex))
            throw new ArgumentOutOfRangeException(nameof(viewIndex));
        return _staticCullByView[(int)viewIndex];
    }
}

internal abstract class MapRenderWorldDpvsExclusiveWorkspace
{
    private int _isActive;

    protected void Enter()
    {
        if (Interlocked.CompareExchange(ref _isActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A DPVS working-set lane cannot service overlapping frames.");
        }
    }

    public void Exit()
    {
        if (Interlocked.Exchange(ref _isActive, 0) != 1)
        {
            throw new InvalidOperationException(
                "A DPVS working-set lane was released without being active.");
        }
    }
}

internal sealed class MapRenderWorldDpvsCameraCellResolverWorkspace
{
    private readonly int[] _visitGenerationByNodeOffset;
    private int _visitGeneration;

    public MapRenderWorldDpvsCameraCellResolverWorkspace(int nodeCount)
    {
        _visitGenerationByNodeOffset = new int[Math.Max(0, nodeCount)];
    }

    public void Begin()
    {
        if (_visitGeneration == int.MaxValue)
        {
            Array.Clear(_visitGenerationByNodeOffset);
            _visitGeneration = 1;
            return;
        }

        _visitGeneration++;
        if (_visitGeneration == 0)
            _visitGeneration = 1;
    }

    public bool TryVisit(int nodeOffset)
    {
        if ((uint)nodeOffset >=
            (uint)_visitGenerationByNodeOffset.Length)
        {
            return true;
        }
        if (_visitGenerationByNodeOffset[nodeOffset] ==
            _visitGeneration)
        {
            return false;
        }

        _visitGenerationByNodeOffset[nodeOffset] = _visitGeneration;
        return true;
    }
}

internal sealed class MapRenderWorldDpvsPortalTraversalWorkspace :
    MapRenderWorldDpvsExclusiveWorkspace
{
    private readonly List<MapRenderWorldDpvsCommandPlaneSet>
        _childPlaneSlots = [];
    private int _activeChildPlaneSlotCount;

    public MapRenderWorldDpvsPortalTraversalWorkspace(
        MapRenderWorldDpvsWorldTopology topology)
    {
        States = new MapRenderWorldDpvsPortalRuntimeState[
            topology.Portals.Count];
        for (int index = 0; index < States.Length; index++)
            States[index] = new();

        Queue = new(Math.Min(256, topology.Portals.Count));
        Commands = new(Math.Clamp(
            topology.World.DpvsPlanes.CellCount,
            1,
            1024));
        FurtherCellVisits = new bool[1024];
        PortalSourceVertices = new Vector3[128];
        ClippedWinding = new Vector3[128];
        ConvexHull = new Vector2[64];
        ConvexHullScratch =
            new MapRenderWorldDpvsConvexHullBuilder.Scratch();
        ReconstructedWinding = new Vector3[64];
    }

    public MapRenderWorldDpvsPortalRuntimeState[] States { get; }

    public List<(GfxPortal Portal, float Distance)> Queue { get; }

    public List<MapRenderWorldDpvsCellCullCommandData> Commands { get; }

    public bool[] FurtherCellVisits { get; }

    public Vector3[] PortalSourceVertices { get; }

    public Vector3[] ClippedWinding { get; }

    public Vector2[] ConvexHull { get; }

    public MapRenderWorldDpvsConvexHullBuilder.Scratch
        ConvexHullScratch { get; }

    public Vector3[] ReconstructedWinding { get; }

    public MapRenderWorldDpvsCommandPlaneSet RentChildPlaneSlot()
    {
        // A slot is issued once per child aperture for this traversal. It is
        // never recycled while _commands can still reference it; Begin resets
        // the cursor only after the serialized provider has synchronously
        // consumed the previous command set.
        int slotIndex = _activeChildPlaneSlotCount++;
        if (slotIndex == _childPlaneSlots.Count)
        {
            _childPlaneSlots.Add(
                MapRenderWorldDpvsCommandPlaneSet.CreateReusableScratch(
                    MapRenderWorldDpvsPortalPlaneBuilder
                        .MaximumPlaneCount));
        }
        return _childPlaneSlots[slotIndex];
    }

    public void Begin()
    {
        Enter();
        Queue.Clear();
        Commands.Clear();
        _activeChildPlaneSlotCount = 0;
        Array.Clear(FurtherCellVisits);
        foreach (MapRenderWorldDpvsPortalRuntimeState state in States)
            state.Reset();
    }
}

internal sealed class MapRenderWorldDpvsStaticCullWorkspace :
    MapRenderWorldDpvsExclusiveWorkspace
{
    public MapRenderWorldDpvsStaticCullWorkspace(
        MapRenderWorldDpvsWorldTopology topology)
    {
        Context = new(
            topology.World,
            new uint[topology.SurfaceWordCount],
            new uint[topology.StaticModelWordCount]);
    }

    public MapRenderWorldDpvsStaticCullContext Context { get; }

    public void Begin()
    {
        Enter();
        Context.BeginFrame();
    }
}

internal sealed class MapRenderWorldDpvsCameraSkyCullWorkspace :
    MapRenderWorldDpvsExclusiveWorkspace
{
    public MapRenderWorldDpvsCameraSkyCullWorkspace(int surfaceWordCount)
    {
        SurfaceBits = new uint[surfaceWordCount];
    }

    public uint[] SurfaceBits { get; }

    public void Begin()
    {
        Enter();
        Array.Clear(SurfaceBits);
    }
}

internal sealed class MapRenderWorldDpvsSunShadowTraversalWorkspace :
    MapRenderWorldDpvsExclusiveWorkspace
{
    public MapRenderWorldDpvsSunShadowTraversalWorkspace(int cellCount)
    {
        Commands = new(Math.Clamp(cellCount, 0, 1024));
    }

    public List<MapRenderWorldDpvsCellCullCommandData> Commands { get; }

    public void Begin()
    {
        Enter();
        Commands.Clear();
    }
}
