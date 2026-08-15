using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;

namespace IW4.Render.Scheduling.Shadows;

public enum MapRenderWorldCameraColorPhaseDisposition : byte
{
    RetainCameraColorOrPreview = 0,
    NativeSelectedNoCameraColor = 1,
    ShadowOnlyMaterialContract = 2
}

/// <summary>
/// Decides whether a completed generic material preview may stand in for the
/// native world-material selection. Camera region None belongs to an auxiliary
/// target rather than normal-camera color. A successful shadow-caster plan is
/// otherwise not by itself sufficient: ordinary world materials also own slot
/// 2, so the selected native pass remains authoritative.
/// </summary>
public sealed record MapRenderWorldCameraColorPhasePlan(
    MapRenderWorldCameraColorPhaseDisposition Disposition,
    int? SelectedTechniqueSlot)
{
    public bool SuppressGenericCameraColorFallback =>
        Disposition is not
            MapRenderWorldCameraColorPhaseDisposition
                .RetainCameraColorOrPreview;
}

public static class MapRenderWorldCameraColorPhasePlanner
{
    public const string ShadowOnlyMaterialName = "w/shadowcaster";
    public const string ShadowOnlyTechniqueSetName = "w_shadowcaster";
    public const string FullbrightTechniqueName = "vertcol_simple_fog_nc";
    public const string WireframeTechniqueName = "wireframe_solid_nc";
    public const MaterialGameFlags ShadowOnlyGameFlags =
        MaterialGameFlags.MaterialSpecificShadowCaster |
        MaterialGameFlags.NoMarks;
    public const MaterialSortKey ShadowOnlySortKey = (MaterialSortKey)34;
    public const MaterialStateFlags ShadowOnlyStateFlags =
        MaterialStateFlags.WritesDepth | MaterialStateFlags.UsesDepthBuffer;
    public const GfxCameraRegionType ShadowOnlyCameraRegion =
        GfxCameraRegionType.None;

    public static MapRenderWorldCameraColorPhasePlan Plan(
        MaterialAsset material,
        MaterialTechniqueSetAsset? techniqueSet,
        RenderAssetLookup lookup,
        int? selectedTechniqueSlot,
        bool selectedCameraColorPassAvailable)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(lookup);

        if (material.CameraRegion != GfxCameraRegionType.None &&
            selectedCameraColorPassAvailable)
            return Retain(selectedTechniqueSlot);

        techniqueSet ??= material.TechniqueSet ??
            lookup.ResolveTechniqueSet(material.TechniqueSetPointer);
        if (techniqueSet is null)
        {
            return material.CameraRegion == GfxCameraRegionType.None
                ? NoCameraColor(selectedTechniqueSlot)
                : Retain(selectedTechniqueSlot);
        }

        MapRenderSunShadowCasterMaterialPlanResult casterResult =
            MapRenderSunShadowCasterMaterialPlanner.Plan(
                material,
                techniqueSet,
                lookup);
        IReadOnlyList<MaterialTechniqueSlot> resolvedSlots =
            lookup.ResolveTechniqueSlots(techniqueSet);
        return PlanResolved(
            material,
            techniqueSet,
            resolvedSlots,
            selectedTechniqueSlot,
            selectedCameraColorPassAvailable,
            casterResult.Plan);
    }

    internal static MapRenderWorldCameraColorPhasePlan PlanResolved(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        IReadOnlyList<MaterialTechniqueSlot> resolvedSlots,
        int? selectedTechniqueSlot,
        bool selectedCameraColorPassAvailable,
        MapRenderSunShadowCasterMaterialPlan? casterPlan)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(techniqueSet);
        ArgumentNullException.ThrowIfNull(resolvedSlots);

        if (material.CameraRegion == GfxCameraRegionType.None)
        {
            return casterPlan is not null &&
                MatchesShadowOnlyUtilityContract(
                    material,
                    techniqueSet,
                    resolvedSlots,
                    casterPlan)
                ? new MapRenderWorldCameraColorPhasePlan(
                    MapRenderWorldCameraColorPhaseDisposition
                        .ShadowOnlyMaterialContract,
                    selectedTechniqueSlot)
                : NoCameraColor(selectedTechniqueSlot);
        }

        if (selectedCameraColorPassAvailable || casterPlan is null)
            return Retain(selectedTechniqueSlot);

        if (selectedTechniqueSlot ==
            MapRenderSunShadowCasterMaterialPlanner
                .SunShadowTechniqueSlot)
        {
            return NoCameraColor(selectedTechniqueSlot);
        }

        return Retain(selectedTechniqueSlot);
    }

    private static bool MatchesShadowOnlyUtilityContract(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        IReadOnlyList<MaterialTechniqueSlot> resolvedSlots,
        MapRenderSunShadowCasterMaterialPlan casterPlan)
    {
        if (!string.Equals(
                NormalizeName(material.Info.Name),
                ShadowOnlyMaterialName,
                StringComparison.Ordinal) ||
            !string.Equals(
                NormalizeName(techniqueSet.Name),
                ShadowOnlyTechniqueSetName,
                StringComparison.Ordinal) ||
            material.Info.GameFlags != ShadowOnlyGameFlags ||
            material.Info.SortKey != ShadowOnlySortKey ||
            material.StateFlags != ShadowOnlyStateFlags ||
            material.CameraRegion != ShadowOnlyCameraRegion ||
            material.TextureCount != 1 ||
            material.Textures.Count != 1 ||
            casterPlan.Kind !=
                MapRenderSunShadowCasterMaterialKind.Opaque ||
            !string.Equals(
                NormalizeName(casterPlan.Technique.Name),
                MapRenderSunShadowCasterMaterialPlanner
                    .WorldOpaqueNoColorTechniqueName,
                StringComparison.Ordinal))
        {
            return false;
        }

        MaterialTechniqueSlot[] materializedSlots = resolvedSlots
            .Where(slot => slot.Technique is not null)
            .OrderBy(slot => slot.Index)
            .ToArray();
        if (materializedSlots.Length != 3 ||
            materializedSlots[0].Index !=
                MapRenderSunShadowCasterMaterialPlanner
                    .SunShadowTechniqueSlot ||
            !ReferenceEquals(
                materializedSlots[0].Technique,
                casterPlan.Technique) ||
            materializedSlots[1].Index !=
                (int)MaterialTechniqueType.Unlit ||
            materializedSlots[1].Technique is not { } fullbright ||
            fullbright.PassCount != 1 ||
            fullbright.Passes.Count != 1 ||
            !string.Equals(
                NormalizeName(fullbright.Name),
                FullbrightTechniqueName,
                StringComparison.Ordinal) ||
            materializedSlots[2].Index !=
                (int)MaterialTechniqueType.WireframeSolid ||
            materializedSlots[2].Technique is not { } wireframe ||
            wireframe.PassCount != 1 ||
            wireframe.Passes.Count != 1 ||
            !string.Equals(
                NormalizeName(wireframe.Name),
                WireframeTechniqueName,
                StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static MapRenderWorldCameraColorPhasePlan Retain(
        int? selectedTechniqueSlot) => new(
        MapRenderWorldCameraColorPhaseDisposition
            .RetainCameraColorOrPreview,
        selectedTechniqueSlot);

    private static MapRenderWorldCameraColorPhasePlan NoCameraColor(
        int? selectedTechniqueSlot) => new(
        MapRenderWorldCameraColorPhaseDisposition
            .NativeSelectedNoCameraColor,
        selectedTechniqueSlot);

    private static string NormalizeName(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
