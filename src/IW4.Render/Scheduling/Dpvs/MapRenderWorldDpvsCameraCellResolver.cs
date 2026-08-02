using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Exact scalar reconstruction of the PS3 camera-cell BSP walk at
/// default_mp.elf 0x003489D8.
/// </summary>
public static class MapRenderWorldDpvsCameraCellResolver
{
    public static MapRenderWorldDpvsCameraCellResolutionResult Resolve(
        GfxWorldAsset world,
        Vector3 cameraOrigin) =>
        ResolveCore(world, cameraOrigin, workspace: null);

    internal static MapRenderWorldDpvsCameraCellResolutionResult Resolve(
        GfxWorldAsset world,
        Vector3 cameraOrigin,
        MapRenderWorldDpvsCameraCellResolverWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return ResolveCore(world, cameraOrigin, workspace);
    }

    private static MapRenderWorldDpvsCameraCellResolutionResult ResolveCore(
        GfxWorldAsset world,
        Vector3 cameraOrigin,
        MapRenderWorldDpvsCameraCellResolverWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!IsFinite(cameraOrigin))
        {
            return Failed(
                MapRenderWorldDpvsCameraCellFailureKind.InvalidCameraOrigin,
                "Camera origin contains a non-finite component.");
        }

        int cellCount = world.DpvsPlanes.CellCount;
        if (cellCount <= 0 || cellCount >= ushort.MaxValue)
        {
            return Failed(
                MapRenderWorldDpvsCameraCellFailureKind.InvalidCellCount,
                $"PS3 packed cell leaves cannot represent cell count {cellCount}.");
        }
        if (world.PlaneCount < 0 ||
            world.DpvsPlanes.Planes.Count != world.PlaneCount)
        {
            return Failed(
                MapRenderWorldDpvsCameraCellFailureKind.PlaneCardinalityMismatch,
                "Materialized DPVS planes do not match GfxWorld.planeCount.");
        }
        if (world.NodeCount < 0 ||
            world.DpvsPlanes.Nodes.Count != world.NodeCount)
        {
            return Failed(
                MapRenderWorldDpvsCameraCellFailureKind.NodeCardinalityMismatch,
                "Materialized packed DPVS nodes do not match GfxWorld.nodeCount.");
        }
        if (world.DpvsPlanes.Nodes.Count == 0)
        {
            return Failed(
                MapRenderWorldDpvsCameraCellFailureKind.MissingRootNode,
                "Packed DPVS node storage has no root entry.");
        }

        IReadOnlyList<ushort> nodes = world.DpvsPlanes.Nodes;
        HashSet<int>? visitedOffsets = workspace is null
            ? []
            : null;
        workspace?.Begin();
        int nodeOffset = 0;
        int internalBase = cellCount + 1;
        while (true)
        {
            if ((uint)nodeOffset >= (uint)nodes.Count)
            {
                return Failed(
                    MapRenderWorldDpvsCameraCellFailureKind.InvalidChildOffset,
                    $"Packed DPVS traversal escapes the {nodes.Count}-ushort node table.",
                    nodeOffset);
            }
            bool firstVisit = workspace?.TryVisit(nodeOffset) ??
                visitedOffsets!.Add(nodeOffset);
            if (!firstVisit)
            {
                return Failed(
                    MapRenderWorldDpvsCameraCellFailureKind.TraversalCycle,
                    $"Packed DPVS traversal revisits ushort offset {nodeOffset}.",
                    nodeOffset);
            }

            int nodeValue = nodes[nodeOffset];
            if (nodeValue < internalBase)
            {
                int cellIndex = nodeValue - 1;
                if (cellIndex >= cellCount)
                {
                    return Failed(
                        MapRenderWorldDpvsCameraCellFailureKind.InvalidLeafCell,
                        $"Packed DPVS leaf {nodeValue} escapes {cellCount} cells.",
                        nodeOffset);
                }
                return MapRenderWorldDpvsCameraCellResolutionResult.Succeeded(cellIndex);
            }

            int planeIndex = nodeValue - internalBase;
            if ((uint)planeIndex >= (uint)world.DpvsPlanes.Planes.Count)
            {
                return Failed(
                    MapRenderWorldDpvsCameraCellFailureKind.InvalidPlaneIndex,
                    $"Packed DPVS node at ushort offset {nodeOffset} references plane {planeIndex} outside {world.DpvsPlanes.Planes.Count} rows.",
                    nodeOffset,
                    planeIndex);
            }

            DpvsPlane plane = world.DpvsPlanes.Planes[planeIndex];
            if (!IsFinite(plane))
            {
                return Failed(
                    MapRenderWorldDpvsCameraCellFailureKind.InvalidPlane,
                    $"DPVS plane {planeIndex} contains a non-finite coefficient.",
                    nodeOffset,
                    planeIndex);
            }

            float signedDistance =
                cameraOrigin.X * plane.NormalX +
                cameraOrigin.Y * plane.NormalY +
                cameraOrigin.Z * plane.NormalZ -
                plane.Distance;
            int nextOffset;
            if (signedDistance <= 0f)
            {
                if (nodeOffset + 1 >= nodes.Count)
                {
                    return Failed(
                        MapRenderWorldDpvsCameraCellFailureKind.InvalidChildOffset,
                        $"Internal DPVS node at ushort offset {nodeOffset} lacks its offset cell.",
                        nodeOffset,
                        planeIndex);
                }
                nextOffset = nodeOffset + nodes[nodeOffset + 1];
            }
            else
            {
                nextOffset = nodeOffset + 2;
            }

            if (nextOffset < 0 || nextOffset >= nodes.Count)
            {
                return Failed(
                    MapRenderWorldDpvsCameraCellFailureKind.InvalidChildOffset,
                    $"Internal DPVS node at ushort offset {nodeOffset} selects invalid child offset {nextOffset}.",
                    nodeOffset,
                    planeIndex);
            }
            nodeOffset = nextOffset;
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(DpvsPlane plane) =>
        float.IsFinite(plane.NormalX) &&
        float.IsFinite(plane.NormalY) &&
        float.IsFinite(plane.NormalZ) &&
        float.IsFinite(plane.Distance);

    private static MapRenderWorldDpvsCameraCellResolutionResult Failed(
        MapRenderWorldDpvsCameraCellFailureKind kind,
        string detail,
        int? nodeOffset = null,
        int? planeIndex = null) =>
        MapRenderWorldDpvsCameraCellResolutionResult.Failed(
            new(kind, detail, nodeOffset, planeIndex));
}
