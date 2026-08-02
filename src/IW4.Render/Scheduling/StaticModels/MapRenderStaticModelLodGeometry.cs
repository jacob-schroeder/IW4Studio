using IW4.Assets.Assets.XModel;

namespace IW4.Render.Scheduling.StaticModels;

/// <summary>
/// One canonical, loaded XModel LOD. XModelSurfs owns a zero-based geometry
/// array while MaterialSurfaceStart addresses the cumulative parent-XModel
/// material array.
/// </summary>
public sealed record MapRenderStaticModelLodGeometry(
    int LodIndex,
    XModelLodInfo Lod,
    XModelSurfsAsset ModelSurfs,
    int MaterialSurfaceStart,
    int SurfaceCount);
