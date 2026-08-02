using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Scalar-equivalent implementation of the PS3 Event 0x0D static AABB-tree
/// culler at 0x00350EF8 / 0x00350928. Output words use the PS3 MSB-first bit
/// convention.
/// </summary>
public static class MapRenderWorldDpvsStaticCellCuller
{
    public static MapRenderWorldDpvsStaticCullResult Cull(
        GfxWorldAsset world,
        MapRenderWorldDpvsViewCommandSet commandSet)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(commandSet);

        if (!TryValidateWorld(
                world,
                out int surfaceCount,
                out int staticModelCount,
                out MapRenderWorldDpvsStaticCullFailure? failure))
        {
            return MapRenderWorldDpvsStaticCullResult.Failed(failure!);
        }

        var context = new MapRenderWorldDpvsStaticCullContext(
            world,
            new uint[WordCount(surfaceCount)],
            new uint[WordCount(staticModelCount)]);
        context.BeginFrame();
        return CullValidated(
            world,
            commandSet,
            context,
            surfaceCount,
            staticModelCount);
    }

    internal static MapRenderWorldDpvsStaticCullResult Cull(
        GfxWorldAsset world,
        MapRenderWorldDpvsViewCommandSet commandSet,
        MapRenderWorldDpvsWorkingSet workingSet)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(commandSet);
        ArgumentNullException.ThrowIfNull(workingSet);
        workingSet.ValidateWorld(world);

        MapRenderWorldDpvsWorldTopology topology = workingSet.Topology;
        if (topology.StaticCullFailure is { } failure)
            return MapRenderWorldDpvsStaticCullResult.Failed(failure);

        MapRenderWorldDpvsStaticCullWorkspace workspace =
            workingSet.StaticCull(commandSet.ViewIndex);
        workspace.Begin();
        try
        {
            return CullValidated(
                world,
                commandSet,
                workspace.Context,
                topology.SurfaceCount,
                topology.StaticModelCount);
        }
        finally
        {
            workspace.Exit();
        }
    }

    private static MapRenderWorldDpvsStaticCullResult CullValidated(
        GfxWorldAsset world,
        MapRenderWorldDpvsViewCommandSet commandSet,
        MapRenderWorldDpvsStaticCullContext context,
        int surfaceCount,
        int staticModelCount)
    {
        ReadOnlySpan<MapRenderWorldDpvsCellCullCommandData> commands =
            commandSet.CommandSpan;
        for (int commandIndex = 0;
             commandIndex < commands.Length;
             commandIndex++)
        {
            MapRenderWorldDpvsCellCullCommandData command =
                commands[commandIndex];
            if ((uint)command.CellIndex >= (uint)world.DpvsPlanes.CellCount)
            {
                return Failed(
                    MapRenderWorldDpvsStaticCullFailureKind.InvalidCellIndex,
                    $"Cell command {command.CellIndex} escapes the {world.DpvsPlanes.CellCount} world cells.",
                    command.CellIndex);
            }

            for (int planeIndex = 0;
                 planeIndex < command.Event0DPlaneCount;
                 planeIndex++)
            {
                if (!IsFinite(command.Event0DPlaneSpan[planeIndex]))
                {
                    return Failed(
                        MapRenderWorldDpvsStaticCullFailureKind.InvalidClipPlane,
                        $"Cell {command.CellIndex} plane {planeIndex} contains a non-finite coefficient.",
                        command.CellIndex,
                        elementIndex: planeIndex);
                }
            }

            GfxCellTree cellTree = world.CellTrees[command.CellIndex];
            uint declaredTreeCount = world.CellTreeCounts[command.CellIndex].AabbTreeCount;
            if (declaredTreeCount > int.MaxValue ||
                cellTree.AabbTrees.Count != (int)declaredTreeCount)
            {
                return Failed(
                    MapRenderWorldDpvsStaticCullFailureKind.AabbTreeCardinalityMismatch,
                    $"Cell {command.CellIndex} declares {declaredTreeCount} AABB rows but materializes {cellTree.AabbTrees.Count}.",
                    command.CellIndex);
            }

            // The native handler treats a null cell-tree pointer as a no-op.
            if (cellTree.AabbTrees.Count == 0)
                continue;

            context.BeginCommand(command.CellIndex, cellTree.AabbTrees);
            if (!context.CullTree(0, command.Event0DPlaneSpan))
                return MapRenderWorldDpvsStaticCullResult.Failed(context.Failure!);
        }

        return MapRenderWorldDpvsStaticCullResult.Succeeded(
            new MapRenderWorldDpvsViewVisibility(
                commandSet.ViewIndex,
                context.SurfaceBits,
                context.StaticModelBits,
                surfaceCount,
                staticModelCount));
    }

    private static bool TryValidateWorld(
        GfxWorldAsset world,
        out int surfaceCount,
        out int staticModelCount,
        out MapRenderWorldDpvsStaticCullFailure? failure)
    {
        surfaceCount = world.SurfaceCount;
        staticModelCount = 0;
        failure = null;
        if (surfaceCount < 0 ||
            world.Dpvs.SModelCount > int.MaxValue ||
            world.Dpvs.StaticSurfaceCount > int.MaxValue)
        {
            failure = new(
                MapRenderWorldDpvsStaticCullFailureKind.InvalidWorldCardinality,
                "GfxWorld contains a negative or host-unrepresentable DPVS count.");
            return false;
        }

        staticModelCount = (int)world.Dpvs.SModelCount;
        int staticSurfaceCount = (int)world.Dpvs.StaticSurfaceCount;
        if (world.Dpvs.Surfaces.Count != surfaceCount ||
            world.Dpvs.SurfaceBounds.Count != surfaceCount ||
            world.Dpvs.SModelInsts.Count != staticModelCount ||
            world.Dpvs.SortedSurfIndex.Count < staticSurfaceCount)
        {
            failure = new(
                MapRenderWorldDpvsStaticCullFailureKind.InvalidWorldCardinality,
                "Materialized DPVS surface, cull-bound, static-model, or sorted-index storage disagrees with its native count.");
            return false;
        }

        int cellCount = world.DpvsPlanes.CellCount;
        if (cellCount < 0 ||
            world.CellTrees.Count != cellCount ||
            world.CellTreeCounts.Count != cellCount)
        {
            failure = new(
                MapRenderWorldDpvsStaticCullFailureKind.CellTreeCardinalityMismatch,
                "Materialized cell-tree storage does not match GfxWorld.dpvsPlanes.cellCount.");
            return false;
        }

        return true;
    }

    private static int WordCount(int count) =>
        checked((int)(((long)count + 31) / 32));

    private static bool IsFinite(MapRenderWorldDpvsClipPlane plane) =>
        float.IsFinite(plane.NormalX) &&
        float.IsFinite(plane.NormalY) &&
        float.IsFinite(plane.NormalZ) &&
        float.IsFinite(plane.CoefficientW);

    private static MapRenderWorldDpvsStaticCullResult Failed(
        MapRenderWorldDpvsStaticCullFailureKind kind,
        string detail,
        int? cellIndex = null,
        int? treeIndex = null,
        int? elementIndex = null) =>
        MapRenderWorldDpvsStaticCullResult.Failed(
            new(kind, detail, cellIndex, treeIndex, elementIndex));

}
