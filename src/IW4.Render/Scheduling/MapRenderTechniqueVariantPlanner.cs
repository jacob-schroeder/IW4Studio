namespace IW4.Render.Scheduling;

/// <summary>
/// Enumerates the exact draw-method rows needed to retain runtime-selectable
/// page and scene-light allocation variants. It does not assert that a shadow
/// resource is ready; that authority belongs to the frame publication.
/// </summary>
public static class MapRenderTechniqueVariantPlanner
{
    private static readonly MapRenderSurfaceType[] WorldSurfaceTypes =
    [
        MapRenderSurfaceType.Triangles,
        MapRenderSurfaceType.TrianglesNoSunShadow
    ];

    private static readonly MapRenderSurfaceType[] StaticModelSurfaceTypes =
    [
        MapRenderSurfaceType.StaticModelRigid,
        MapRenderSurfaceType.StaticModelRigidNoSunShadow
    ];

    public static MapRenderTechniqueVariantSet Plan(
        MapRenderDrawMethod drawMethod,
        MapRenderSceneLightSelectorAssetState sceneLights,
        int primaryLightIndex)
    {
        ArgumentNullException.ThrowIfNull(drawMethod);
        ArgumentNullException.ThrowIfNull(sceneLights);
        if ((uint)primaryLightIndex >= (uint)sceneLights.SceneLightCount)
            throw new ArgumentOutOfRangeException(nameof(primaryLightIndex));

        int baseVariant = sceneLights.BaseColumnByLight[primaryLightIndex];
        if ((uint)baseVariant >= MapRenderDrawMethodPageProducer.VariantCount)
        {
            throw new InvalidDataException(
                $"Scene light {primaryLightIndex} has invalid base draw-method variant {baseVariant}.");
        }

        bool rawCanUseShadowMap =
            sceneLights.CanUseShadowMapByLight[primaryLightIndex] != 0;
        bool canPrepareShadowAllocatedVariant =
            sceneLights.CanPrepareShadowAllocatedVariant(primaryLightIndex);
        int allocatedVariant = checked(
            baseVariant + MapRenderDrawMethodPageProducer.AlternateVariantDelta);
        if (canPrepareShadowAllocatedVariant &&
            (uint)allocatedVariant >=
            MapRenderDrawMethodPageProducer.VariantCount)
        {
            throw new InvalidDataException(
                $"Scene light {primaryLightIndex} with a prepared shadow allocation base variant {baseVariant} cannot apply the PS3 +{MapRenderDrawMethodPageProducer.AlternateVariantDelta} transition.");
        }

        return new(
            primaryLightIndex,
            baseVariant,
            rawCanUseShadowMap,
            canPrepareShadowAllocatedVariant,
            CreateVariants(
                drawMethod,
                WorldSurfaceTypes,
                baseVariant,
                canPrepareShadowAllocatedVariant,
                allocatedVariant),
            CreateVariants(
                drawMethod,
                StaticModelSurfaceTypes,
                baseVariant,
                canPrepareShadowAllocatedVariant,
                allocatedVariant));
    }

    private static MapRenderTechniqueVariant[] CreateVariants(
        MapRenderDrawMethod drawMethod,
        IReadOnlyList<MapRenderSurfaceType> surfaceTypes,
        int baseVariant,
        bool canPrepareShadowAllocatedVariant,
        int allocatedVariant)
    {
        var result = new List<MapRenderTechniqueVariant>(
            canPrepareShadowAllocatedVariant
                ? surfaceTypes.Count * 2
                : surfaceTypes.Count);
        foreach (MapRenderSurfaceType surfaceType in surfaceTypes)
        {
            result.Add(Create(
                drawMethod,
                surfaceType,
                MapRenderTechniqueVariantAllocation.Unshadowed,
                baseVariant));
            if (canPrepareShadowAllocatedVariant)
            {
                result.Add(Create(
                    drawMethod,
                    surfaceType,
                    MapRenderTechniqueVariantAllocation.ShadowMapAllocated,
                    allocatedVariant));
            }
        }
        return result.ToArray();
    }

    private static MapRenderTechniqueVariant Create(
        MapRenderDrawMethod drawMethod,
        MapRenderSurfaceType surfaceType,
        MapRenderTechniqueVariantAllocation allocation,
        int variant)
    {
        ReadOnlySpan<byte> row = drawMethod.GetTechniqueRow(surfaceType);
        return new(
            surfaceType,
            allocation,
            variant,
            row[variant]);
    }
}
