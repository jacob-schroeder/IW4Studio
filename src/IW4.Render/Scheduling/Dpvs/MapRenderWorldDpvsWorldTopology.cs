using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Immutable, world-identity-scoped indexes and cardinality checks shared by
/// moving-camera DPVS frames. Conditional AABB-tree validation remains in the
/// culler so malformed rows are still reported only when a command reaches
/// them.
/// </summary>
internal sealed class MapRenderWorldDpvsWorldTopology
{
    private const int MaximumCameraCellCount = 1024;

    private readonly Dictionary<GfxPortal, int> _portalIndexByReference =
        new(ReferenceEqualityComparer.Instance);
    private readonly GfxPortal[] _portals;
    private readonly MapRenderWorldDpvsBounds[] _cellBounds;
    private readonly bool[] _cellBoundsValid;

    public MapRenderWorldDpvsWorldTopology(GfxWorldAsset world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));

        CameraTraversalFailure = ValidateCameraPortals(
            world,
            _portalIndexByReference,
            out _portals);
        StaticCullFailure = ValidateStaticCullStorage(
            world,
            out int surfaceCount,
            out int staticModelCount);
        SurfaceCount = surfaceCount;
        StaticModelCount = staticModelCount;
        SurfaceWordCount = StaticCullFailure is null
            ? WordCount(surfaceCount)
            : 0;
        StaticModelWordCount = StaticCullFailure is null
            ? WordCount(staticModelCount)
            : 0;

        int cellCount = world.DpvsPlanes.CellCount;
        if (cellCount < 0 || world.Cells.Count != cellCount)
        {
            SunShadowCellStorageFailure = new(
                MapRenderWorldDpvsSunShadowTraversalFailureKind
                    .InvalidWorldCellStorage,
                $"GfxWorld materializes {world.Cells.Count} cells but dpvsPlanes declares {cellCount}.");
            _cellBounds = [];
            _cellBoundsValid = [];
        }
        else
        {
            _cellBounds = new MapRenderWorldDpvsBounds[cellCount];
            _cellBoundsValid = new bool[cellCount];
            for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                _cellBoundsValid[cellIndex] =
                    MapRenderWorldDpvsAabbPlaneTester.TryGetBounds(
                        world.Cells[cellIndex].Bounds,
                        out _cellBounds[cellIndex]);
            }
        }

        CameraCellNodeVisitCapacity =
            world.DpvsPlanes.Nodes.Count;
        SkySurfaceWordCount = world.SurfaceCount < 0
            ? 0
            : WordCount(world.SurfaceCount);
    }

    public GfxWorldAsset World { get; }

    public int SurfaceCount { get; }

    public int StaticModelCount { get; }

    public int SurfaceWordCount { get; }

    public int StaticModelWordCount { get; }

    public int SkySurfaceWordCount { get; }

    public int CameraCellNodeVisitCapacity { get; }

    public IReadOnlyList<GfxPortal> Portals => _portals;

    public MapRenderWorldDpvsCameraTraversalFailure?
        CameraTraversalFailure { get; }

    public MapRenderWorldDpvsStaticCullFailure? StaticCullFailure { get; }

    public MapRenderWorldDpvsSunShadowTraversalFailure?
        SunShadowCellStorageFailure { get; }

    public int PortalIndex(GfxPortal portal) =>
        _portalIndexByReference[portal];

    public bool TryGetCellBounds(
        int cellIndex,
        out MapRenderWorldDpvsBounds bounds)
    {
        if ((uint)cellIndex >= (uint)_cellBounds.Length ||
            !_cellBoundsValid[cellIndex])
        {
            bounds = default;
            return false;
        }

        bounds = _cellBounds[cellIndex];
        return true;
    }

    private static MapRenderWorldDpvsCameraTraversalFailure?
        ValidateCameraPortals(
            GfxWorldAsset world,
            Dictionary<GfxPortal, int> portalIndexByReference,
            out GfxPortal[] portals)
    {
        var materializedPortals = new List<GfxPortal>();
        int cellCount = world.DpvsPlanes.CellCount;
        if (cellCount is <= 0 or > MaximumCameraCellCount ||
            world.Cells.Count != cellCount)
        {
            portals = [];
            return new(
                MapRenderWorldDpvsCameraTraversalFailureKind
                    .InvalidWorldCellStorage,
                $"PS3 camera DPVS requires one through {MaximumCameraCellCount} materialized cells; count={cellCount}, rows={world.Cells.Count}.");
        }

        for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
        {
            GfxCell cell = world.Cells[cellIndex];
            if (cell.PortalCount < 0 ||
                cell.Portals.Count != cell.PortalCount)
            {
                portals = [];
                return new(
                    MapRenderWorldDpvsCameraTraversalFailureKind
                        .InvalidPortalStorage,
                    $"Cell {cellIndex} declares {cell.PortalCount} portals but materializes {cell.Portals.Count}.",
                    cellIndex);
            }

            for (int portalIndex = 0;
                 portalIndex < cell.Portals.Count;
                 portalIndex++)
            {
                GfxPortal portal = cell.Portals[portalIndex];
                if (portalIndexByReference.ContainsKey(portal))
                {
                    portals = [];
                    return new(
                        MapRenderWorldDpvsCameraTraversalFailureKind
                            .InvalidPortalStorage,
                        "A materialized GfxPortal row is shared by multiple owning-cell slots.",
                        cellIndex,
                        portalIndex);
                }
                if (portal.VertexCount is < 3 or > 128 ||
                    portal.Vertices.Count != portal.VertexCount ||
                    portal.HullAxis.Count != 6)
                {
                    portals = [];
                    return new(
                        MapRenderWorldDpvsCameraTraversalFailureKind
                            .InvalidPortalGeometry,
                        $"Cell {cellIndex} portal {portalIndex} has invalid vertex or hull-axis cardinality.",
                        cellIndex,
                        portalIndex);
                }
                if (portal.CellIndex >= cellCount)
                {
                    portals = [];
                    return new(
                        MapRenderWorldDpvsCameraTraversalFailureKind
                            .InvalidPortalTargetCell,
                        $"Cell {cellIndex} portal {portalIndex} targets cell {portal.CellIndex} outside {cellCount} cells.",
                        cellIndex,
                        portalIndex);
                }
                if (!IsFinite(portal.Plane) ||
                    portal.Vertices.Any(static vertex => !IsFinite(vertex)) ||
                    portal.HullAxis.Any(static value =>
                        !float.IsFinite(value)))
                {
                    portals = [];
                    return new(
                        MapRenderWorldDpvsCameraTraversalFailureKind
                            .InvalidPortalGeometry,
                        $"Cell {cellIndex} portal {portalIndex} contains non-finite geometry.",
                        cellIndex,
                        portalIndex);
                }

                portalIndexByReference.Add(
                    portal,
                    materializedPortals.Count);
                materializedPortals.Add(portal);
            }
        }

        portals = materializedPortals.ToArray();
        return null;
    }

    private static MapRenderWorldDpvsStaticCullFailure?
        ValidateStaticCullStorage(
            GfxWorldAsset world,
            out int surfaceCount,
            out int staticModelCount)
    {
        surfaceCount = world.SurfaceCount;
        staticModelCount = 0;
        if (surfaceCount < 0 ||
            world.Dpvs.SModelCount > int.MaxValue ||
            world.Dpvs.StaticSurfaceCount > int.MaxValue)
        {
            return new(
                MapRenderWorldDpvsStaticCullFailureKind
                    .InvalidWorldCardinality,
                "GfxWorld contains a negative or host-unrepresentable DPVS count.");
        }

        staticModelCount = (int)world.Dpvs.SModelCount;
        int staticSurfaceCount = (int)world.Dpvs.StaticSurfaceCount;
        if (world.Dpvs.Surfaces.Count != surfaceCount ||
            world.Dpvs.SurfaceBounds.Count != surfaceCount ||
            world.Dpvs.SModelInsts.Count != staticModelCount ||
            world.Dpvs.SortedSurfIndex.Count < staticSurfaceCount)
        {
            return new(
                MapRenderWorldDpvsStaticCullFailureKind
                    .InvalidWorldCardinality,
                "Materialized DPVS surface, cull-bound, static-model, or sorted-index storage disagrees with its native count.");
        }

        int cellCount = world.DpvsPlanes.CellCount;
        if (cellCount < 0 ||
            world.CellTrees.Count != cellCount ||
            world.CellTreeCounts.Count != cellCount)
        {
            return new(
                MapRenderWorldDpvsStaticCullFailureKind
                    .CellTreeCardinalityMismatch,
                "Materialized cell-tree storage does not match GfxWorld.dpvsPlanes.cellCount.");
        }

        return null;
    }

    private static int WordCount(int count) =>
        checked((int)(((long)count + 31) / 32));

    private static bool IsFinite(GfxPortalPlane plane) =>
        float.IsFinite(plane.NormalX) &&
        float.IsFinite(plane.NormalY) &&
        float.IsFinite(plane.NormalZ) &&
        float.IsFinite(plane.Distance);

    private static bool IsFinite(GfxPortalVertex vertex) =>
        float.IsFinite(vertex.X) &&
        float.IsFinite(vertex.Y) &&
        float.IsFinite(vertex.Z);
}
