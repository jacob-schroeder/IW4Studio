using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.LightDef;
using IW4.Render.Scheduling.Lighting;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Adapts ComPrimaryLight values to Event20 GfxLight rows against one active
/// asset-pool revision.
/// </summary>
internal static class MapRenderWorldEvent20SceneLightFrameInputProducer
{
    internal static MapRenderWorldEvent20SceneLightFrameInputBuildResult Build(
        MapRenderWorldSceneSource source,
        MapRenderNormalCameraSceneLightDynamicInput dynamicInput,
        Vector3 eyeOffset)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dynamicInput);
        if (!IsFinite(eyeOffset))
        {
            return Failed(
                MapRenderWorldEvent20SceneLightFrameInputFailureKind
                    .EyeOffsetInvalid,
                "Event20 scene-light EyeOffset values must be finite.");
        }

        MapRenderWorldSceneLightSource sceneSource =
            source.SceneLights.Source ?? throw new InvalidOperationException(
                "The world source has no canonical ComWorld scene-light projection.");
        ComWorldAsset comWorld = sceneSource.ComWorld;
        int expectedCount = sceneSource.SelectorState.SceneLightCount;
        if (comWorld.PrimaryLightCount != expectedCount ||
            comWorld.PrimaryLights.Count != expectedCount)
        {
            return Failed(
                MapRenderWorldEvent20SceneLightFrameInputFailureKind
                    .SceneLightCountMismatch,
                $"selector={expectedCount};declared={comWorld.PrimaryLightCount};materialized={comWorld.PrimaryLights.Count}");
        }
        if (dynamicInput.ShadowAllocation.SceneLightCount != expectedCount)
        {
            return Failed(
                MapRenderWorldEvent20SceneLightFrameInputFailureKind
                    .ShadowAllocationSceneLightCountMismatch,
                $"lights={expectedCount};shadowAllocation={dynamicInput.ShadowAllocation.SceneLightCount}");
        }

        long poolRevision = source.AssetPoolRevisionAtConstruction;
        if (!source.AssetLookup.HasCanonicalAssetPoolRevision(poolRevision))
        {
            return Failed(
                MapRenderWorldEvent20SceneLightFrameInputFailureKind
                    .AssetPoolRevisionMismatch,
                $"The canonical asset pool is no longer at scene revision {poolRevision}.");
        }

        var adapted = new MapRenderWorldEvent20SceneLight[expectedCount];
        for (int index = 0; index < adapted.Length; index++)
        {
            ComPrimaryLight? sourceLight = comWorld.PrimaryLights[index];
            if (sourceLight is null)
            {
                return Failed(
                    MapRenderWorldEvent20SceneLightFrameInputFailureKind
                        .PrimaryLightUnavailable,
                    $"ComWorld primary light {index} is null.",
                    index);
            }
            if (!IsFinite(sourceLight))
            {
                return Failed(
                    MapRenderWorldEvent20SceneLightFrameInputFailureKind
                        .PrimaryLightValueInvalid,
                    $"ComWorld primary light {index} contains a non-finite Event20 property.",
                    index);
            }

            string? defName = sourceLight.DefName;
            LightDefAsset? definition = null;
            if (defName is not null)
            {
                if (string.IsNullOrWhiteSpace(defName))
                {
                    return Failed(
                        MapRenderWorldEvent20SceneLightFrameInputFailureKind
                            .LightDefNameInvalid,
                        $"ComWorld primary light {index} carries an empty LightDef name.",
                        index,
                        defName);
                }
                if (!source.AssetLookup.TryResolveCanonicalLightDef(
                        defName,
                        poolRevision,
                        out definition))
                {
                    return Failed(
                        MapRenderWorldEvent20SceneLightFrameInputFailureKind
                            .CanonicalLightDefUnavailable,
                        $"Canonical LightDef '{defName}' is unavailable at asset-pool revision {poolRevision}.",
                        index,
                        defName);
                }
            }

            adapted[index] = new MapRenderWorldEvent20SceneLight(
                sourceLight.Type,
                sourceLight.CanUseShadowMap,
                sourceLight.Exponent,
                ToVector(sourceLight.Color),
                ToVector(sourceLight.Dir),
                ToVector(sourceLight.Origin),
                sourceLight.Radius,
                sourceLight.CosHalfFovOuter,
                sourceLight.CosHalfFovInner,
                defName,
                definition);
        }

        if (!source.AssetLookup.HasCanonicalAssetPoolRevision(poolRevision))
        {
            return Failed(
                MapRenderWorldEvent20SceneLightFrameInputFailureKind
                    .AssetPoolRevisionMismatch,
                $"The canonical asset pool changed while adapting scene lights from revision {poolRevision}.");
        }

        return new(
            new MapRenderWorldEvent20SceneLightFrameInput(
                adapted,
                eyeOffset,
                dynamicInput,
                poolRevision),
            null);
    }

    private static bool IsFinite(ComPrimaryLight light) =>
        IsFinite(light.Color) &&
        IsFinite(light.Dir) &&
        IsFinite(light.Origin) &&
        float.IsFinite(light.Radius) &&
        float.IsFinite(light.CosHalfFovOuter) &&
        float.IsFinite(light.CosHalfFovInner);

    private static bool IsFinite(IW4.Assets.Math.Vec3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static Vector3 ToVector(IW4.Assets.Math.Vec3 value) =>
        new(value.X, value.Y, value.Z);

    private static MapRenderWorldEvent20SceneLightFrameInputBuildResult Failed(
        MapRenderWorldEvent20SceneLightFrameInputFailureKind kind,
        string detail,
        int? sceneLightIndex = null,
        string? lightDefName = null) => new(
            null,
            new MapRenderWorldEvent20SceneLightFrameInputFailure(
                kind,
                detail,
                sceneLightIndex,
                lightDefName));
}
