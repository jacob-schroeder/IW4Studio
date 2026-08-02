using IW4.Render.Geometry;

namespace IW4.Render.Scheduling.StaticModels;

/// <summary>
/// Carries the PS3 static-model camera-region gate into the normal-camera queue
/// without discarding prepared geometry. Normal-camera static rows are emitted
/// only when the material camera region is less than five.
/// </summary>
internal static class MapRenderOpenGlStaticCameraRegionPolicy
{
    internal const byte EmissiveCameraRegion = 2;
    internal const byte AuxiliaryTargetCameraRegion = 5;

    /// <summary>
    /// Resolves the technique selected by the native normal-camera phase.
    /// Static material surfaces in camera region two are submitted through the
    /// draw method's emissive phase; every other normal-camera region retains
    /// the page/light-selector technique.
    /// </summary>
    internal static int? ResolveNormalCameraTechniqueSlot(
        byte cameraRegion,
        int? pageTechniqueSlot,
        MapRenderDrawMethod? drawMethod)
    {
        if (cameraRegion != EmissiveCameraRegion)
            return pageTechniqueSlot;
        if (drawMethod is null ||
            drawMethod.EmissiveTechnique ==
                MapRenderDrawMethodInitializer.NoneTechnique)
        {
            return null;
        }

        return drawMethod.EmissiveTechnique;
    }

    internal static bool OwnsNormalCameraColor(byte cameraRegion) =>
        cameraRegion < AuxiliaryTargetCameraRegion;

    /// <summary>
    /// A generic material preview may stand in for ordinary camera regions,
    /// but never for the emissive phase: its blend, z-feather, falloff, and
    /// view-dependent program semantics require the exact authored group.
    /// </summary>
    internal static bool AllowsGenericNormalCameraFallback(
        byte cameraRegion) =>
        OwnsNormalCameraColor(cameraRegion) &&
        cameraRegion != EmissiveCameraRegion;

    internal static byte? ResolveUniformRegion(
        IReadOnlyList<MapRenderStaticModelInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        if (instances.Count == 0)
            return null;

        byte region = instances[0].CameraRegion;
        for (int index = 1; index < instances.Count; index++)
        {
            if (instances[index].CameraRegion != region)
                return null;
        }

        return region;
    }

    /// <summary>
    /// Suppresses only a positively resolved all-region-five authored pass
    /// group. Empty, mixed, or unresolved metadata retains the legacy path so
    /// incomplete metadata cannot silently remove visible geometry.
    /// </summary>
    internal static bool SuppressNormalCameraGroup(
        IReadOnlyList<byte?> passCameraRegions)
    {
        ArgumentNullException.ThrowIfNull(passCameraRegions);
        return passCameraRegions.Count > 0 &&
            passCameraRegions.All(region =>
                region == AuxiliaryTargetCameraRegion);
    }
}
