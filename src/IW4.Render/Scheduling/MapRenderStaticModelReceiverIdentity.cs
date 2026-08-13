using IW4.Assets.Assets.Material;
using IW4.Render.Geometry;

namespace IW4.Render.Scheduling;

/// <summary>
/// Complete ownership of one selected static-model material surface. Page
/// membership is not a model-wide property: it belongs to this exact object,
/// selected LOD, and material-surface tuple. Primary-light ownership remains
/// per draw instance for the independent selector-column decision.
/// </summary>
public readonly record struct MapRenderStaticModelReceiverIdentity
{
    public MapRenderStaticModelReceiverIdentity(
        MapRenderStaticModelInstance instance,
        int lodIndex)
        : this(
            instance.ObjectIndex,
            lodIndex,
            instance.SurfaceIndex,
            instance.CameraRegion,
            instance.PrimaryLightIndex)
    {
    }

    public MapRenderStaticModelReceiverIdentity(
        int objectIndex,
        int lodIndex,
        int materialSurfaceIndex,
        GfxCameraRegionType cameraRegion,
        int primaryLightIndex)
    {
        if (objectIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(objectIndex));
        if (lodIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(lodIndex));
        if (materialSurfaceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialSurfaceIndex));
        }
        if ((uint)primaryLightIndex > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(primaryLightIndex));
        }

        ObjectIndex = objectIndex;
        LodIndex = lodIndex;
        MaterialSurfaceIndex = materialSurfaceIndex;
        CameraRegion = cameraRegion;
        PrimaryLightIndex = primaryLightIndex;
    }

    public int ObjectIndex { get; }

    public int LodIndex { get; }

    public int MaterialSurfaceIndex { get; }

    public GfxCameraRegionType CameraRegion { get; }

    public int PrimaryLightIndex { get; }
}
