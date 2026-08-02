using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Per-frame writable state for PS3 default_mp.elf 0x0034E240 / 0x0034E470 /
/// 0x0034E8A0. Loaded GfxPortal models remain immutable.
/// </summary>
internal sealed class MapRenderWorldDpvsPortalTraversalContext
{
    private const int MaximumCellCount = 1024;
    private const int InitialPlaneCapacity = 16;
    private const int ChildPlaneCapacity = 0x800;
    private const int MaximumQueuedPortals = 256;
    private const int MaximumHullPoints = 64;
    private const float DirectPortalDistance = -0.125f;

    private readonly MapRenderWorldDpvsWorldTopology _topology;
    private readonly GfxWorldAsset _world;
    private readonly MapRenderWorldDpvsNormalCameraFrame _camera;
    private readonly MapRenderWorldDpvsPortalTraversalSettings _settings;
    private readonly MapRenderWorldDpvsPortalTraversalWorkspace _workspace;
    private readonly List<(GfxPortal Portal, float Distance)> _queue;
    private readonly List<MapRenderWorldDpvsCellCullCommandData> _commands;

    public MapRenderWorldDpvsPortalTraversalContext(
        MapRenderWorldDpvsWorldTopology topology,
        MapRenderWorldDpvsPortalTraversalWorkspace workspace,
        MapRenderWorldDpvsNormalCameraFrame camera,
        MapRenderWorldDpvsPortalTraversalSettings settings)
    {
        _topology = topology;
        _world = topology.World;
        _workspace = workspace;
        _camera = camera;
        _settings = settings;
        _queue = workspace.Queue;
        _commands = workspace.Commands;
    }

    public MapRenderWorldDpvsCameraTraversalFailure? Failure { get; private set; }

    public bool TryValidateWorld()
    {
        if (_topology.CameraTraversalFailure is null)
            return true;
        Failure ??= _topology.CameraTraversalFailure;
        return false;
    }

    public bool TryBuildOutsideWorldCommands(
        out IReadOnlyList<MapRenderWorldDpvsCellCullCommandData> commands)
    {
        for (int cellIndex = 0;
             cellIndex < _world.DpvsPlanes.CellCount;
             cellIndex++)
        {
            _commands.Add(new MapRenderWorldDpvsCellCullCommandData(
                cellIndex,
                _camera.CommandPlaneSet,
                _camera.FrustumPlanes.Count));
        }
        commands = _commands;
        return true;
    }

    public bool TryBuildSingleCellCommand(
        int cellIndex,
        out IReadOnlyList<MapRenderWorldDpvsCellCullCommandData> commands)
    {
        _commands.Add(new(
            cellIndex,
            _camera.CommandPlaneSet,
            _camera.FrustumPlanes.Count));
        commands = _commands;
        return true;
    }

    public bool TryTraverse(
        int startCellIndex,
        out IReadOnlyList<MapRenderWorldDpvsCellCullCommandData> commands)
    {
        if (!VisitCell(
                startCellIndex,
                parentPortal: null,
                _camera.ViewPlane,
                _camera.CommandPlaneSet,
                _camera.FrustumPlanes.Count,
                _camera.FrustumPlanes.Count,
                InitialPlaneCapacity,
                recursionDepth: 0,
                clipChildren: true))
        {
            commands = [];
            return false;
        }

        int iteration = 0;
        while (_queue.Count > 0)
        {
            GfxPortal portal = DequeuePortal();
            MapRenderWorldDpvsPortalRuntimeState state = State(portal);
            int hullCount =
                MapRenderWorldDpvsConvexHullBuilder.BuildInto(
                    state.HullPoints,
                    _workspace.ConvexHull,
                    _workspace.ConvexHullScratch);
            state.HullPoints.Clear();
            if (hullCount == 0)
                continue;
            iteration++;
            if (_settings.PortalWalkLimit != 0 &&
                iteration == _settings.PortalWalkLimit)
            {
                ClearQueue();
                break;
            }

            int windingCount = ReconstructPortalWinding(
                portal,
                _workspace.ConvexHull.AsSpan(0, hullCount),
                _workspace.ReconstructedWinding);
            MapRenderWorldDpvsCommandPlaneSet childPlaneBuffer =
                _workspace.RentChildPlaneSlot();
            if (!MapRenderWorldDpvsPortalPlaneBuilder.TryBuildInto(
                    _camera,
                    _settings,
                    _workspace.ReconstructedWinding.AsSpan(
                        0,
                        windingCount),
                    childPlaneBuffer.WritableCapacitySpan,
                    out int childPlaneCount,
                    out bool clipChildren,
                    out string? planeFailure))
            {
                commands = [];
                Fail(
                    planeFailure?.Contains("sixteen-plane", StringComparison.Ordinal) == true
                        ? MapRenderWorldDpvsCameraTraversalFailureKind.PortalPlaneCapacityExceeded
                        : MapRenderWorldDpvsCameraTraversalFailureKind.InvalidPortalGeometry,
                    planeFailure ?? "Portal child-plane generation failed.");
                return false;
            }
            if (state.RecursionDepth < _settings.PortalMinRecurseDepth)
                clipChildren = true;

            childPlaneBuffer.PublishScratchCount(childPlaneCount);
            if (!VisitCell(
                    portal.CellIndex,
                    portal,
                    PortalPlane(portal),
                    childPlaneBuffer,
                    childPlaneCount,
                    frustumPlaneCount: 0,
                    ChildPlaneCapacity,
                    state.RecursionDepth + 1,
                    clipChildren))
            {
                commands = [];
                return false;
            }
        }

        commands = _commands;
        return true;
    }

    private bool VisitCell(
        int cellIndex,
        GfxPortal? parentPortal,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsCommandPlaneSet planeBuffer,
        int planeCount,
        int frustumPlaneCount,
        int planeCapacity,
        int recursionDepth,
        bool clipChildren)
    {
        if ((uint)cellIndex >= (uint)_world.Cells.Count ||
            planeCount is < 1 ||
            planeCount > planeCapacity ||
            planeCapacity is < 1 or > ChildPlaneCapacity ||
            frustumPlaneCount < 0 ||
            frustumPlaneCount > planeCount ||
            planeBuffer.Count < planeCount)
        {
            return Fail(
                MapRenderWorldDpvsCameraTraversalFailureKind.PortalPlaneCapacityExceeded,
                "Portal recursion supplied an invalid cell or command-plane range.",
                cellIndex);
        }

        planeBuffer = ExactPlaneSet(planeBuffer, planeCount);
        _commands.Add(new(
            cellIndex,
            planeBuffer,
            frustumPlaneCount));
        if (!TrySetAncestorListStatus(parentPortal, true))
            return false;

        bool succeeded;
        if (!clipChildren)
        {
            succeeded = VisitAllFurtherCells(
                cellIndex,
                parentPlane,
                planeBuffer,
                planeCount,
                frustumPlaneCount);
        }
        else
        {
            succeeded = VisitClippedChildren(
                cellIndex,
                parentPortal,
                parentPlane,
                planeBuffer,
                planeCount,
                frustumPlaneCount,
                planeCapacity,
                recursionDepth);
        }

        if (!TrySetAncestorListStatus(parentPortal, false))
            return false;
        return succeeded;
    }

    private bool VisitClippedChildren(
        int cellIndex,
        GfxPortal? parentPortal,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsCommandPlaneSet planeBuffer,
        int planeCount,
        int frustumPlaneCount,
        int planeCapacity,
        int recursionDepth)
    {
        GfxCell cell = _world.Cells[cellIndex];
        for (int portalIndex = 0; portalIndex < cell.Portals.Count; portalIndex++)
        {
            GfxPortal portal = cell.Portals[portalIndex];
            if (ShouldSkipPortal(portal, planeBuffer.Planes, planeCount))
                continue;

            float eyeDistance = SignedDistance(PortalPlane(portal), _camera.Origin);
            if (eyeDistance > DirectPortalDistance)
            {
                if (!ProjectedWindingContainsPoint(portal, _camera.Origin))
                    continue;

                MapRenderWorldDpvsPortalRuntimeState state = State(portal);
                state.QueuedParent = null;
                // IW3 R_VisitPortalsForCell changes the parent
                // portal plane used to clip descendants, but passes the
                // existing cull-plane buffer and count through unchanged.
                // Appending the old parent plane over-clips this cell and
                // also leaks that extra plane into later sibling portals.
                if (!VisitCell(
                        portal.CellIndex,
                        portal,
                        PortalPlane(portal),
                        planeBuffer,
                        planeCount,
                        frustumPlaneCount,
                        planeCapacity,
                        state.RecursionDepth + 1,
                        clipChildren: true))
                {
                    return false;
                }
                continue;
            }

            if (!TryClipPortal(
                    portal,
                    parentPlane,
                    planeBuffer.Planes,
                    planeCount,
                    out int windingCount,
                    cellIndex,
                    portalIndex))
            {
                return false;
            }
            if (windingCount == 0)
                continue;
            foreach (Vector3 vertex in
                     _workspace.ClippedWinding.AsSpan(
                         0,
                         windingCount))
            {
                if (!TryAddHullPoint(portal, vertex, cellIndex, portalIndex))
                    return false;
            }

            MapRenderWorldDpvsPortalRuntimeState portalState = State(portal);
            if (!portalState.IsQueued)
            {
                portalState.RecursionDepth = unchecked((byte)recursionDepth);
                portalState.QueuedParent = parentPortal;
                if (!TryEnqueuePortal(portal, cellIndex, portalIndex))
                    return false;
            }
            else
            {
                portalState.RecursionDepth = (byte)Math.Min(
                    portalState.RecursionDepth,
                    recursionDepth);
                if (!ReferenceEquals(portalState.QueuedParent, parentPortal))
                    portalState.QueuedParent = null;
            }
        }
        return true;
    }

    private bool VisitAllFurtherCells(
        int cellIndex,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsCommandPlaneSet planes,
        int planeCount,
        int frustumPlaneCount)
    {
        bool[] visited = _workspace.FurtherCellVisits;
        Array.Clear(visited);
        if (!CollectFurtherCells(
                cellIndex,
                parentPlane,
                planes.Planes,
                planeCount,
                visited))
        {
            return false;
        }
        for (int furtherCell = 0; furtherCell < MaximumCellCount; furtherCell++)
        {
            if (visited[furtherCell])
            {
                _commands.Add(new(
                    furtherCell,
                    planes,
                    frustumPlaneCount));
            }
        }
        return true;
    }

    private bool CollectFurtherCells(
        int cellIndex,
        MapRenderWorldDpvsClipPlane parentPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount,
        bool[] visited)
    {
        GfxCell cell = _world.Cells[cellIndex];
        for (int portalIndex = 0; portalIndex < cell.Portals.Count; portalIndex++)
        {
            GfxPortal portal = cell.Portals[portalIndex];
            int targetCell = portal.CellIndex;
            if (visited[targetCell] ||
                ShouldSkipPortal(portal, planes, planeCount))
            {
                continue;
            }
            if (!TryClipPortal(
                    portal,
                    parentPlane,
                    planes,
                    planeCount,
                    out int clippedCount,
                    cellIndex,
                    portalIndex))
            {
                return false;
            }
            if (clippedCount == 0)
                continue;

            visited[targetCell] = true;
            // IW3 R_GetFurtherCellList_r deliberately carries
            // the original aperture through this conservative walk. Adding
            // each intermediate portal's planes here over-prunes valid cells
            // that remain visible through the original parent aperture.
            if (!CollectFurtherCells(
                    targetCell,
                    parentPlane,
                    planes,
                    planeCount,
                    visited))
            {
                return false;
            }
        }
        return true;
    }

    private bool TryClipPortal(
        GfxPortal portal,
        MapRenderWorldDpvsClipPlane parentPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planeBuffer,
        int planeCount,
        out int windingCount,
        int cellIndex,
        int portalIndex)
    {
        Vector3[] source = _workspace.PortalSourceVertices;
        int sourceCount = portal.Vertices.Count;
        if (sourceCount > source.Length)
        {
            windingCount = 0;
            return Fail(
                MapRenderWorldDpvsCameraTraversalFailureKind
                    .PortalClipCapacityExceeded,
                $"Portal winding has {sourceCount} vertices; the PS3 clip scratch supports at most {source.Length}.",
                cellIndex,
                portalIndex);
        }
        for (int vertexIndex = 0;
             vertexIndex < sourceCount;
             vertexIndex++)
        {
            source[vertexIndex] = Vertex(portal.Vertices[vertexIndex]);
        }
        if (MapRenderWorldDpvsPortalWindingClipper.TryClipInto(
                source,
                sourceCount,
                parentPlane,
                _camera.FarPlane,
                planeBuffer,
                planeCount,
                _workspace.ClippedWinding,
                out windingCount,
                out string? clipFailure))
        {
            return true;
        }

        return Fail(
            MapRenderWorldDpvsCameraTraversalFailureKind.PortalClipCapacityExceeded,
            clipFailure ?? "Portal winding clipping failed.",
            cellIndex,
            portalIndex);
    }

    private bool TryAddHullPoint(
        GfxPortal portal,
        Vector3 vertex,
        int cellIndex,
        int portalIndex)
    {
        MapRenderWorldDpvsPortalRuntimeState state = State(portal);
        if (state.HullPoints.Count == MaximumHullPoints)
        {
            int reducedCount =
                MapRenderWorldDpvsConvexHullBuilder.BuildInto(
                    state.HullPoints,
                    _workspace.ConvexHull,
                    _workspace.ConvexHullScratch);
            if (reducedCount == MaximumHullPoints)
            {
                return Fail(
                    MapRenderWorldDpvsCameraTraversalFailureKind.PortalHullCapacityExceeded,
                    "A queued portal retained sixty-four convex hull points.",
                    cellIndex,
                    portalIndex);
            }
            state.HullPoints.Clear();
            for (int index = 0; index < reducedCount; index++)
                state.HullPoints.Add(_workspace.ConvexHull[index]);
        }

        Vector3 axis0 = new(portal.HullAxis[0], portal.HullAxis[1], portal.HullAxis[2]);
        Vector3 axis1 = new(portal.HullAxis[3], portal.HullAxis[4], portal.HullAxis[5]);
        state.HullPoints.Add(new(
            Vector3.Dot(vertex, axis0),
            Vector3.Dot(vertex, axis1)));
        return true;
    }

    private bool TryEnqueuePortal(
        GfxPortal portal,
        int cellIndex,
        int portalIndex)
    {
        if (_queue.Count >= MaximumQueuedPortals)
        {
            return Fail(
                MapRenderWorldDpvsCameraTraversalFailureKind.PortalQueueCapacityExceeded,
                "More than 256 portals were queued by the camera walk.",
                cellIndex,
                portalIndex);
        }

        float distance = portal.Vertices.Max(vertex =>
            SignedDistance(
                _camera.ViewPlane,
                new(vertex.X, vertex.Y, vertex.Z)));
        int heapIndex = _queue.Count;
        _queue.Add(default);
        while (heapIndex > 0)
        {
            int parentIndex = (heapIndex - 1) >> 1;
            if (distance >= _queue[parentIndex].Distance)
                break;
            _queue[heapIndex] = _queue[parentIndex];
            heapIndex = parentIndex;
        }
        _queue[heapIndex] = (portal, distance);
        State(portal).IsQueued = true;
        return true;
    }

    private GfxPortal DequeuePortal()
    {
        GfxPortal portal = _queue[0].Portal;
        State(portal).IsQueued = false;
        int lastIndex = _queue.Count - 1;
        (GfxPortal Portal, float Distance) replacement = _queue[lastIndex];
        _queue.RemoveAt(lastIndex);
        if (_queue.Count == 0)
            return portal;

        int heapIndex = 0;
        while (true)
        {
            int childIndex = heapIndex * 2 + 1;
            if (childIndex >= _queue.Count)
                break;
            if (childIndex + 1 < _queue.Count &&
                _queue[childIndex].Distance > _queue[childIndex + 1].Distance)
            {
                childIndex++;
            }
            if (_queue[childIndex].Distance >= replacement.Distance)
                break;
            _queue[heapIndex] = _queue[childIndex];
            heapIndex = childIndex;
        }
        _queue[heapIndex] = replacement;
        return portal;
    }

    private void ClearQueue()
    {
        foreach ((GfxPortal portal, _) in _queue)
        {
            MapRenderWorldDpvsPortalRuntimeState state = State(portal);
            state.IsQueued = false;
            state.HullPoints.Clear();
        }
        _queue.Clear();
    }

    private bool TrySetAncestorListStatus(GfxPortal? portal, bool isAncestor)
    {
        int remainingPortalCount = _topology.Portals.Count;
        while (portal is not null)
        {
            if (remainingPortalCount-- == 0)
            {
                return Fail(
                    MapRenderWorldDpvsCameraTraversalFailureKind.PortalTraversalCycle,
                    "Queued-parent portal ancestry contains a cycle.");
            }
            MapRenderWorldDpvsPortalRuntimeState state = State(portal);
            if (state.IsAncestor == isAncestor)
            {
                return Fail(
                    MapRenderWorldDpvsCameraTraversalFailureKind.PortalTraversalCycle,
                    "Portal ancestor state was entered or left twice.");
            }
            state.IsAncestor = isAncestor;
            portal = state.QueuedParent;
        }
        return true;
    }

    /// <summary>
    /// Creates the storage for a queued portal's already-materialized child
    /// planes. The native 0x800 value is the logical maximum accepted by
    /// descendant commands; it is not the number of active planes that must
    /// be reserved for every queued portal.
    /// </summary>
    internal static List<MapRenderWorldDpvsClipPlane>
        CreateChildPlaneBuffer(
            IReadOnlyList<MapRenderWorldDpvsClipPlane> childPlanes)
    {
        ArgumentNullException.ThrowIfNull(childPlanes);
        if (childPlanes.Count > ChildPlaneCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childPlanes),
                $"A child plane buffer cannot exceed {ChildPlaneCapacity} planes.");
        }

        var buffer = new List<MapRenderWorldDpvsClipPlane>(
            childPlanes.Count);
        buffer.AddRange(childPlanes);
        return buffer;
    }

    private static MapRenderWorldDpvsCommandPlaneSet ExactPlaneSet(
        MapRenderWorldDpvsCommandPlaneSet planes,
        int planeCount) =>
        planes.Count == planeCount
            ? planes
            : planes.CopyPrefix(planeCount);

    private bool ShouldSkipPortal(
        GfxPortal portal,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount)
    {
        if (State(portal).IsAncestor)
            return true;
        float eyeDistance = SignedDistance(PortalPlane(portal), _camera.Origin);
        return eyeDistance > 0f || PortalBehindAnyPlane(portal, planes, planeCount);
    }

    private static bool PortalBehindAnyPlane(
        GfxPortal portal,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount)
    {
        for (int planeIndex = 0; planeIndex < planeCount; planeIndex++)
        {
            MapRenderWorldDpvsClipPlane plane = planes[planeIndex];
            bool behind = true;
            foreach (GfxPortalVertex vertex in portal.Vertices)
            {
                if (SignedDistance(
                        plane,
                        new(vertex.X, vertex.Y, vertex.Z)) > 0f)
                {
                    behind = false;
                    break;
                }
            }
            if (behind)
                return true;
        }
        return false;
    }

    private static bool ProjectedWindingContainsPoint(
        GfxPortal portal,
        Vector3 point)
    {
        Vector3 normal = new(
            portal.Plane.NormalX,
            portal.Plane.NormalY,
            portal.Plane.NormalZ);
        GetProjectionCoordinates(normal, out int xCoordinate, out int yCoordinate);
        int previousIndex = portal.Vertices.Count - 1;
        for (int index = 0; index < portal.Vertices.Count; index++)
        {
            Vector3 current = Vertex(portal.Vertices[index]);
            Vector3 previous = Vertex(portal.Vertices[previousIndex]);
            float edgeNormalX = Component(current, yCoordinate) -
                Component(previous, yCoordinate);
            float edgeNormalY = Component(previous, xCoordinate) -
                Component(current, xCoordinate);
            float deltaX = Component(point, xCoordinate) -
                Component(previous, xCoordinate);
            float deltaY = Component(point, yCoordinate) -
                Component(previous, yCoordinate);
            if (edgeNormalY * deltaY + edgeNormalX * deltaX < 0f)
                return false;
            previousIndex = index;
        }
        return true;
    }

    private static void GetProjectionCoordinates(
        Vector3 direction,
        out int xCoordinate,
        out int yCoordinate)
    {
        Vector3 squared = direction * direction;
        if (squared.X > squared.Z || squared.Y > squared.Z)
        {
            if (squared.X > squared.Y || squared.Z > squared.Y)
            {
                (xCoordinate, yCoordinate) = direction.X <= 0f
                    ? (2, 1)
                    : (1, 2);
            }
            else
            {
                (xCoordinate, yCoordinate) = direction.Y <= 0f
                    ? (0, 2)
                    : (2, 0);
            }
        }
        else
        {
            (xCoordinate, yCoordinate) = direction.Z <= 0f
                ? (1, 0)
                : (0, 1);
        }
    }

    private static int ReconstructPortalWinding(
        GfxPortal portal,
        ReadOnlySpan<Vector2> hull,
        Span<Vector3> destination)
    {
        if (destination.Length < hull.Length)
        {
            throw new ArgumentException(
                "The reconstructed portal-winding destination is too small.",
                nameof(destination));
        }
        Vector3 normal = new(
            portal.Plane.NormalX,
            portal.Plane.NormalY,
            portal.Plane.NormalZ);
        Vector3 origin = -portal.Plane.Distance * normal;
        Vector3 axis0 = new(portal.HullAxis[0], portal.HullAxis[1], portal.HullAxis[2]);
        Vector3 axis1 = new(portal.HullAxis[3], portal.HullAxis[4], portal.HullAxis[5]);
        for (int index = 0; index < hull.Length; index++)
        {
            Vector2 point = hull[index];
            destination[index] =
                origin +
                point.X * axis0 +
                point.Y * axis1;
        }
        return hull.Length;
    }

    private static MapRenderWorldDpvsClipPlane PortalPlane(GfxPortal portal) =>
        new(
            portal.Plane.NormalX,
            portal.Plane.NormalY,
            portal.Plane.NormalZ,
            portal.Plane.Distance);

    private static Vector3 Vertex(GfxPortalVertex vertex) =>
        new(vertex.X, vertex.Y, vertex.Z);

    private static float Component(Vector3 value, int index) => index switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static float SignedDistance(
        MapRenderWorldDpvsClipPlane plane,
        Vector3 point) =>
        point.X * plane.NormalX +
        point.Y * plane.NormalY +
        point.Z * plane.NormalZ +
        plane.CoefficientW;

    private MapRenderWorldDpvsPortalRuntimeState State(GfxPortal portal) =>
        _workspace.States[_topology.PortalIndex(portal)];

    private bool Fail(
        MapRenderWorldDpvsCameraTraversalFailureKind kind,
        string detail,
        int? cellIndex = null,
        int? portalIndex = null)
    {
        Failure ??= new(kind, detail, cellIndex, portalIndex);
        return false;
    }
}
