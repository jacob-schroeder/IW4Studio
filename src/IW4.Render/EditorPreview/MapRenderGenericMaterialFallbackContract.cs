using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Lighting;
using IW4.Render.Materials;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Immutable generic-material facts recovered from the selected authored
/// contract. Both native backends consume this instead of independently
/// reclassifying color transfer or static-model lighting semantics.
/// </summary>
internal readonly record struct MapRenderGenericMaterialFallbackContract(
    int ColorInputLinearizationMask,
    MapRenderStaticInstanceLightingPayload StaticInstanceLightingPayload,
    bool UsesStaticModelLighting,
    bool StaticModelLightingAddsDirectionalDiffuse,
    bool StaticModelLightingAddsDirectionalSpecular)
{
    internal static MapRenderGenericMaterialFallbackContract Create(
        RenderNormalCameraDrawSourceKind sourceKind,
        ShaderExecutionContract execution,
        IReadOnlyList<MaterialColorLayer> colorLayers)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(colorLayers);

        int colorInputLinearizationMask =
            MapRenderGenericMaterialColorInputContract
                .ResolveLinearizationMask(
                    execution,
                    colorLayers,
                    MapRenderScene.MaxColorLayerCount);
        MapRenderStaticModelLightingContract lightingContract = default;
        bool usesStaticModelLighting =
            sourceKind == RenderNormalCameraDrawSourceKind.StaticModel &&
            MapRenderStaticModelLightingContract.TryCreate(
                execution,
                out lightingContract);
        return new MapRenderGenericMaterialFallbackContract(
            colorInputLinearizationMask,
            usesStaticModelLighting
                ? MapRenderStaticInstanceLightingPayload
                    .BaseLightingCoords
                : MapRenderStaticInstanceLightingPayload.None,
            usesStaticModelLighting,
            usesStaticModelLighting &&
                lightingContract.AddsDirectionalDiffuse,
            usesStaticModelLighting &&
                lightingContract.AddsDirectionalSpecular);
    }
}
