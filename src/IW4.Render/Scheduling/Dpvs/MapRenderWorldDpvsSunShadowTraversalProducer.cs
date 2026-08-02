using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Scalar-equivalent producer for native view indices 1 then 2. Every retained
/// GfxCell emits the Event 0x0D command shape used by the PS3 renderer.
/// </summary>
public static class MapRenderWorldDpvsSunShadowTraversalProducer
{
    public static MapRenderWorldDpvsSunShadowTraversalBuildResult Build(
        GfxWorldAsset world,
        MapRenderWorldDpvsSunShadowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(world);
        return Build(
            world,
            frame,
            new MapRenderWorldDpvsWorkingSet(world));
    }

    internal static MapRenderWorldDpvsSunShadowTraversalBuildResult Build(
        GfxWorldAsset world,
        MapRenderWorldDpvsSunShadowFrame frame,
        MapRenderWorldDpvsWorkingSet workingSet)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(workingSet);
        workingSet.ValidateWorld(world);

        MapRenderWorldDpvsWorldTopology topology =
            workingSet.Topology;
        if (topology.SunShadowCellStorageFailure is { } storageFailure)
            return Failed(storageFailure);

        if (!TryBuildPartition(
                world,
                topology,
                frame.Partition0,
                workingSet.SunShadowPartition0Traversal,
                out MapRenderWorldDpvsViewCommandSet? partition0,
                out MapRenderWorldDpvsSunShadowTraversalFailure? failure))
        {
            return Failed(failure!);
        }
        if (!TryBuildPartition(
                world,
                topology,
                frame.Partition1,
                workingSet.SunShadowPartition1Traversal,
                out MapRenderWorldDpvsViewCommandSet? partition1,
                out failure))
        {
            return Failed(failure!);
        }

        return MapRenderWorldDpvsSunShadowTraversalBuildResult.Succeeded(
            new(
                frame.ProducerIdentity,
                frame.SourceRevision,
                partition0!,
                partition1!));
    }

    private static bool TryBuildPartition(
        GfxWorldAsset world,
        MapRenderWorldDpvsWorldTopology topology,
        MapRenderWorldDpvsSunShadowPartitionClipSet clipSet,
        MapRenderWorldDpvsSunShadowTraversalWorkspace workspace,
        out MapRenderWorldDpvsViewCommandSet? commandSet,
        out MapRenderWorldDpvsSunShadowTraversalFailure? failure)
    {
        commandSet = null;
        failure = null;
        workspace.Begin();
        try
        {
            ReadOnlySpan<MapRenderWorldDpvsClipPlane> frustumPlanes =
                clipSet.FrustumPlaneSpan;
            for (int planeIndex = 0;
                 planeIndex < frustumPlanes.Length;
                 planeIndex++)
            {
                if (!IsFinite(frustumPlanes[planeIndex]))
                {
                    failure = new(
                        MapRenderWorldDpvsSunShadowTraversalFailureKind
                            .InvalidClipPlane,
                        $"{clipSet.ViewIndex} frustum plane {planeIndex} contains a non-finite coefficient.",
                        clipSet.ViewIndex,
                        PlaneIndex: planeIndex);
                    return false;
                }
            }

            MapRenderWorldDpvsCommandPlaneSet commandPlanes =
                clipSet.FrustumCommandPlaneSet;
            List<MapRenderWorldDpvsCellCullCommandData> commands =
                workspace.Commands;
            for (int cellIndex = 0;
                 cellIndex < world.DpvsPlanes.CellCount;
                 cellIndex++)
            {
                if (!topology.TryGetCellBounds(
                        cellIndex,
                        out MapRenderWorldDpvsBounds bounds))
                {
                    failure = new(
                        MapRenderWorldDpvsSunShadowTraversalFailureKind
                            .InvalidCellBounds,
                        $"GfxCell {cellIndex} has non-finite bounds or a negative half-size.",
                        clipSet.ViewIndex,
                        cellIndex);
                    return false;
                }
                if (MapRenderWorldDpvsAabbPlaneTester.IsOutside(
                        bounds,
                        frustumPlanes))
                {
                    continue;
                }

                commands.Add(new(
                    cellIndex,
                    commandPlanes,
                    clipSet.FrustumPlaneCount));
            }

            commandSet = new(
                clipSet.ViewIndex,
                MapRenderWorldDpvsCommandOrigin.SunShadowFrustumTraversal,
                commands);
            return true;
        }
        finally
        {
            workspace.Exit();
        }
    }

    private static bool IsFinite(MapRenderWorldDpvsClipPlane plane) =>
        float.IsFinite(plane.NormalX) &&
        float.IsFinite(plane.NormalY) &&
        float.IsFinite(plane.NormalZ) &&
        float.IsFinite(plane.CoefficientW);

    private static MapRenderWorldDpvsSunShadowTraversalBuildResult Failed(
        MapRenderWorldDpvsSunShadowTraversalFailure failure) =>
        MapRenderWorldDpvsSunShadowTraversalBuildResult.Failed(failure);
}
