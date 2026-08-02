using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Scalar-equivalent PS3 camera sky-surface contribution at 0x00350640.
/// </summary>
public static class MapRenderWorldDpvsCameraSkyCuller
{
    public static MapRenderWorldDpvsCameraSkyCullResult Cull(
        GfxWorldAsset world,
        MapRenderWorldDpvsCameraSkyCullInput input)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(input);
        uint[] surfaceBits = world.SurfaceCount < 0
            ? []
            : new uint[WordCount(world.SurfaceCount)];
        return CullCore(world, input, surfaceBits);
    }

    internal static MapRenderWorldDpvsCameraSkyCullResult Cull(
        GfxWorldAsset world,
        MapRenderWorldDpvsCameraSkyCullInput input,
        MapRenderWorldDpvsWorkingSet workingSet)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(workingSet);
        workingSet.ValidateWorld(world);

        MapRenderWorldDpvsCameraSkyCullWorkspace workspace =
            workingSet.CameraSky;
        workspace.Begin();
        try
        {
            return CullCore(world, input, workspace.SurfaceBits);
        }
        finally
        {
            workspace.Exit();
        }
    }

    private static MapRenderWorldDpvsCameraSkyCullResult CullCore(
        GfxWorldAsset world,
        MapRenderWorldDpvsCameraSkyCullInput input,
        uint[] surfaceBits)
    {
        if (world.SurfaceCount < 0)
        {
            return Failed(
                MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidWorldCardinality,
                "GfxWorld contains a negative surface count.");
        }

        if (!input.IsEnabled)
        {
            return MapRenderWorldDpvsCameraSkyCullResult.Succeeded(
                new(surfaceBits, world.SurfaceCount));
        }

        if (world.Dpvs.StaticSurfaceCount > int.MaxValue ||
            world.Dpvs.SurfaceBounds.Count != world.SurfaceCount ||
            world.Dpvs.SortedSurfIndex.Count < (int)world.Dpvs.StaticSurfaceCount ||
            world.SkyCount > int.MaxValue ||
            world.Skies.Count != (int)world.SkyCount)
        {
            return Failed(
                MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidWorldCardinality,
                "GfxWorld sky, surface-bound, or sorted-index storage disagrees with its native count.");
        }

        for (int planeIndex = 0; planeIndex < input.Planes.Count; planeIndex++)
        {
            if (!IsFinite(input.Planes[planeIndex]))
            {
                return Failed(
                    MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidClipPlane,
                    $"Camera sky plane {planeIndex} contains a non-finite coefficient.",
                    elementIndex: planeIndex);
            }
        }

        for (int skyIndex = 0; skyIndex < world.Skies.Count; skyIndex++)
        {
            GfxSky sky = world.Skies[skyIndex];
            if (sky.SkySurfCount < 0 || sky.SkyStartSurfs.Count != sky.SkySurfCount)
            {
                return Failed(
                    MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidSkyCardinality,
                    $"Sky {skyIndex} declares {sky.SkySurfCount} surface positions but materializes {sky.SkyStartSurfs.Count}.",
                    skyIndex);
            }

            for (int ordinal = 0; ordinal < sky.SkyStartSurfs.Count; ordinal++)
            {
                int sortedPosition = sky.SkyStartSurfs[ordinal];
                if ((uint)sortedPosition >= (uint)world.Dpvs.SortedSurfIndex.Count)
                {
                    return Failed(
                        MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidSortedSurfacePosition,
                        $"Sky {skyIndex} entry {ordinal} references sorted position {sortedPosition} outside {world.Dpvs.SortedSurfIndex.Count} rows.",
                        skyIndex,
                        ordinal);
                }

                int surfaceIndex = world.Dpvs.SortedSurfIndex[sortedPosition];
                if ((uint)surfaceIndex >= (uint)world.SurfaceCount)
                {
                    return Failed(
                        MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidSurfaceIndex,
                        $"Sky {skyIndex} entry {ordinal} resolves to surface {surfaceIndex} outside {world.SurfaceCount} rows.",
                        skyIndex,
                        ordinal);
                }
                if (TestBit(surfaceBits, surfaceIndex))
                    continue;

                GfxSurfaceBounds surfaceBounds = world.Dpvs.SurfaceBounds[surfaceIndex];
                if (!MapRenderWorldDpvsAabbPlaneTester.TryGetBounds(
                        surfaceBounds.Bounds,
                        out MapRenderWorldDpvsBounds bounds))
                {
                    return Failed(
                        MapRenderWorldDpvsCameraSkyCullFailureKind.InvalidSurfaceBounds,
                        $"Sky surface {surfaceIndex} has malformed DPVS midpoint/half-size cull bounds.",
                        skyIndex,
                        ordinal);
                }
                if (!MapRenderWorldDpvsAabbPlaneTester.IsOutside(
                        bounds,
                        input.PlaneSpan))
                {
                    SetBit(surfaceBits, surfaceIndex);
                }
            }
        }

        return MapRenderWorldDpvsCameraSkyCullResult.Succeeded(
            new(surfaceBits, world.SurfaceCount));
    }

    private static bool IsFinite(MapRenderWorldDpvsClipPlane plane) =>
        float.IsFinite(plane.NormalX) &&
        float.IsFinite(plane.NormalY) &&
        float.IsFinite(plane.NormalZ) &&
        float.IsFinite(plane.CoefficientW);

    private static int WordCount(int count) =>
        checked((int)(((long)count + 31) / 32));

    private static bool TestBit(uint[] words, int index) =>
        (words[index >> 5] & (0x8000_0000u >> (index & 31))) != 0;

    private static void SetBit(uint[] words, int index) =>
        words[index >> 5] |= 0x8000_0000u >> (index & 31);

    private static MapRenderWorldDpvsCameraSkyCullResult Failed(
        MapRenderWorldDpvsCameraSkyCullFailureKind kind,
        string detail,
        int? skyIndex = null,
        int? elementIndex = null) =>
        MapRenderWorldDpvsCameraSkyCullResult.Failed(
            new(kind, detail, skyIndex, elementIndex));
}
