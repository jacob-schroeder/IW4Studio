using IW4.Assets.Assets.Material;

namespace IW4.Render.Scheduling;

/// <summary>
/// All page/allocation technique results for one authored primary-light
/// index. Separate world and static-model collections preserve native row
/// ownership even when two conditions happen to select the same slot.
/// </summary>
public sealed class MapRenderTechniqueVariantSet
{
    private readonly MapRenderTechniqueVariant[] _worldVariants;
    private readonly MapRenderTechniqueVariant[] _staticModelVariants;

    internal MapRenderTechniqueVariantSet(
        int primaryLightIndex,
        int baseSceneLightVariant,
        bool rawCanUseShadowMap,
        bool canPrepareShadowAllocatedVariant,
        IReadOnlyList<MapRenderTechniqueVariant> worldVariants,
        IReadOnlyList<MapRenderTechniqueVariant> staticModelVariants)
    {
        if (primaryLightIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(primaryLightIndex));
        ArgumentNullException.ThrowIfNull(worldVariants);
        ArgumentNullException.ThrowIfNull(staticModelVariants);

        PrimaryLightIndex = primaryLightIndex;
        BaseSceneLightVariant = baseSceneLightVariant;
        RawCanUseShadowMap = rawCanUseShadowMap;
        CanPrepareShadowAllocatedVariant =
            canPrepareShadowAllocatedVariant;
        _worldVariants = worldVariants.ToArray();
        _staticModelVariants = staticModelVariants.ToArray();
        WorldVariants = Array.AsReadOnly(_worldVariants);
        StaticModelVariants = Array.AsReadOnly(_staticModelVariants);
    }

    public int PrimaryLightIndex { get; }

    public int BaseSceneLightVariant { get; }

    /// <summary>
    /// Exact loaded ComPrimaryLight.CanUseShadowMap byte projected to bool.
    /// The dedicated directional-sun producer does not derive its ownership
    /// from this byte.
    /// </summary>
    public bool RawCanUseShadowMap { get; }

    /// <summary>
    /// Whether an allocated (+3) sidecar is retained for a supported runtime
    /// producer. This does not indicate current-frame allocation/readiness.
    /// </summary>
    public bool CanPrepareShadowAllocatedVariant { get; }

    public IReadOnlyList<MapRenderTechniqueVariant> WorldVariants { get; }

    public IReadOnlyList<MapRenderTechniqueVariant> StaticModelVariants
        { get; }

    public MapRenderTechniqueVariant GetWorldVariant(
        MapRenderWorldSurfacePageMembership page,
        MapRenderTechniqueVariantAllocation allocation)
    {
        GfxDrawSurfSurfaceType surfaceType = page switch
        {
            MapRenderWorldSurfacePageMembership.PageZero =>
                GfxDrawSurfSurfaceType.Triangles,
            MapRenderWorldSurfacePageMembership.PageOne =>
                GfxDrawSurfSurfaceType.TrianglesNoSunShadow,
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        return GetVariant(_worldVariants, surfaceType, allocation);
    }

    public MapRenderTechniqueVariant GetStaticModelVariant(
        bool noSunShadowPage,
        MapRenderTechniqueVariantAllocation allocation) =>
        GetVariant(
            _staticModelVariants,
            noSunShadowPage
                ? GfxDrawSurfSurfaceType.StaticModelRigidNoSunShadow
                : GfxDrawSurfSurfaceType.StaticModelRigid,
            allocation);

    private static MapRenderTechniqueVariant GetVariant(
        IReadOnlyList<MapRenderTechniqueVariant> variants,
        GfxDrawSurfSurfaceType surfaceType,
        MapRenderTechniqueVariantAllocation allocation)
    {
        MapRenderTechniqueVariant? result = variants.SingleOrDefault(
            variant => variant.SurfaceType == surfaceType &&
                       variant.Allocation == allocation);
        return result ?? throw new InvalidOperationException(
            $"No {surfaceType}/{allocation} technique variant was retained.");
    }
}
