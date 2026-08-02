using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Immutable, world-identity-scoped fast-worker caster inputs. Native
/// LSB-first world-caster bits and static draw-inst flag eligibility are
/// converted once into the MSB-first layout used by operational DPVS views.
/// </summary>
internal sealed class MapRenderSunShadowCasterTopology
{
    private readonly uint[] _surfaceCasterMaskMsb;
    private readonly uint[] _staticCasterEligibilityMsb;

    public MapRenderSunShadowCasterTopology(GfxWorldAsset world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Failure = Validate(
            world,
            out int surfaceCount,
            out int worldCasterSurfaceCount,
            out int staticModelCount,
            out int surfaceWordCount,
            out int staticModelWordCount);
        SurfaceCount = surfaceCount;
        WorldCasterSurfaceCount = worldCasterSurfaceCount;
        StaticModelCount = staticModelCount;
        SurfaceWordCount = surfaceWordCount;
        StaticModelWordCount = staticModelWordCount;
        if (Failure is not null)
        {
            _surfaceCasterMaskMsb = [];
            _staticCasterEligibilityMsb = [];
            return;
        }

        _surfaceCasterMaskMsb = new uint[surfaceWordCount];
        int worldCasterCapacity = 0;
        for (int wordIndex = 0;
             wordIndex < surfaceWordCount;
             wordIndex++)
        {
            uint mask = ReverseBits(
                world.Dpvs.SurfaceCastsSunShadow[wordIndex]);
            if (wordIndex == surfaceWordCount - 1)
                mask &= ValidMsbTailMask(worldCasterSurfaceCount);
            _surfaceCasterMaskMsb[wordIndex] = mask;
            worldCasterCapacity = checked(
                worldCasterCapacity + BitOperations.PopCount(mask));
        }
        WorldCasterCapacity = worldCasterCapacity;

        _staticCasterEligibilityMsb = new uint[staticModelWordCount];
        int staticCasterCapacity = 0;
        for (int staticModelIndex = 0;
             staticModelIndex < staticModelCount;
             staticModelIndex++)
        {
            // Fast-worker static caster admission rejects flags+0x26 bit zero.
            if ((world.Dpvs.SModelDrawInsts[staticModelIndex].Flags & 0x01) !=
                0)
            {
                continue;
            }

            _staticCasterEligibilityMsb[staticModelIndex >> 5] |=
                0x8000_0000u >> (staticModelIndex & 31);
            staticCasterCapacity++;
        }
        StaticCasterCapacity = staticCasterCapacity;
    }

    public GfxWorldAsset World { get; }

    public int SurfaceCount { get; }

    public int WorldCasterSurfaceCount { get; }

    public int StaticModelCount { get; }

    public int SurfaceWordCount { get; }

    public int StaticModelWordCount { get; }

    public int WorldCasterCapacity { get; }

    public int StaticCasterCapacity { get; }

    public MapRenderSunShadowCasterCatalogFailure? Failure { get; }

    public ReadOnlySpan<uint> SurfaceCasterMaskMsb =>
        _surfaceCasterMaskMsb;

    public ReadOnlySpan<uint> StaticCasterEligibilityMsb =>
        _staticCasterEligibilityMsb;

    private static MapRenderSunShadowCasterCatalogFailure? Validate(
        GfxWorldAsset world,
        out int surfaceCount,
        out int worldCasterSurfaceCount,
        out int staticModelCount,
        out int surfaceWordCount,
        out int staticModelWordCount)
    {
        surfaceCount = world.SurfaceCount;
        worldCasterSurfaceCount = 0;
        staticModelCount = 0;
        surfaceWordCount = 0;
        staticModelWordCount = 0;

        if (surfaceCount < 0 ||
            world.Dpvs.Surfaces.Count != surfaceCount)
        {
            return new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .InvalidWorldSurfaceCardinality,
                $"GfxWorld declares {surfaceCount} surfaces but retains {world.Dpvs.Surfaces.Count} DPVS surface rows.");
        }

        if (surfaceCount != 0)
        {
            if (world.Models.Count == 0)
            {
                return new(
                    MapRenderSunShadowCasterCatalogFailureKind
                        .InvalidWorldSurfaceCardinality,
                    "A non-empty GfxWorld has no brush model zero to define the native BSP sun-shadow caster prefix.");
            }

            worldCasterSurfaceCount = world.Models[0].SurfaceCount;
            if (worldCasterSurfaceCount > surfaceCount)
            {
                return new(
                    MapRenderSunShadowCasterCatalogFailureKind
                        .InvalidWorldSurfaceCardinality,
                    $"GfxWorld brush model zero declares {worldCasterSurfaceCount} BSP caster surfaces for only {surfaceCount} total surfaces.");
            }
        }

        if (world.Dpvs.StaticSurfaceCount !=
            (uint)worldCasterSurfaceCount)
        {
            return new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .InvalidWorldSurfaceCardinality,
                $"GfxWorld brush model zero declares {worldCasterSurfaceCount} BSP caster surfaces but DPVS declares {world.Dpvs.StaticSurfaceCount} static surfaces.");
        }

        if (world.Dpvs.SModelCount > int.MaxValue)
        {
            return new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .InvalidWorldStaticModelCardinality,
                "GfxWorld.dpvs.smodelCount is not host-representable.");
        }

        staticModelCount = (int)world.Dpvs.SModelCount;
        if (world.Dpvs.SModelInsts.Count != staticModelCount ||
            world.Dpvs.SModelDrawInsts.Count != staticModelCount)
        {
            return new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .InvalidWorldStaticModelCardinality,
                $"GfxWorld declares {staticModelCount} static models but retains {world.Dpvs.SModelInsts.Count} cull rows and {world.Dpvs.SModelDrawInsts.Count} draw-instance rows.");
        }

        // Native surfaceCastsSunShadow is indexed only by brush model zero's
        // static BSP surface prefix. Later world-surface rows belong to brush
        // submodels and are intentionally outside this fast-worker bitset.
        surfaceWordCount = WordCount(worldCasterSurfaceCount);
        staticModelWordCount = WordCount(staticModelCount);
        if (world.Dpvs.SurfaceCastsSunShadow.Count < surfaceWordCount)
        {
            return new(
                MapRenderSunShadowCasterCatalogFailureKind
                    .SurfaceCasterMaskUnavailable,
                $"GfxWorld.dpvs.surfaceCastsSunShadow retains {world.Dpvs.SurfaceCastsSunShadow.Count} words but {surfaceWordCount} are required for the {worldCasterSurfaceCount}-surface brush-model-zero caster prefix.");
        }

        return null;
    }

    private static uint ReverseBits(uint value)
    {
        value = ((value & 0x5555_5555u) << 1) |
                ((value >> 1) & 0x5555_5555u);
        value = ((value & 0x3333_3333u) << 2) |
                ((value >> 2) & 0x3333_3333u);
        value = ((value & 0x0f0f_0f0fu) << 4) |
                ((value >> 4) & 0x0f0f_0f0fu);
        value = ((value & 0x00ff_00ffu) << 8) |
                ((value >> 8) & 0x00ff_00ffu);
        return (value << 16) | (value >> 16);
    }

    private static uint ValidMsbTailMask(int count)
    {
        int remainder = count & 31;
        return remainder == 0
            ? uint.MaxValue
            : uint.MaxValue << (32 - remainder);
    }

    private static int WordCount(int count) =>
        checked((int)(((long)count + 31) / 32));
}
