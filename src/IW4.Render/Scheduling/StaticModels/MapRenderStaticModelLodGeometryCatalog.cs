using IW4.Assets.Assets.XModel;

namespace IW4.Render.Scheduling.StaticModels;

/// <summary>
/// Validates and exposes every canonical LOD that the native selector may
/// return, beginning at XModel.MaxLoadedLod. No vertex or index payload is
/// copied here; all rows reference loader-materialized XModelSurfs objects.
/// </summary>
public static class MapRenderStaticModelLodGeometryCatalog
{
    public static bool TryCreate(
        XModelAsset? model,
        out IReadOnlyList<MapRenderStaticModelLodGeometry> geometries)
    {
        geometries = [];
        if (model is null)
            return false;

        int lodCount = model.NumLods == 0
            ? model.Lods.Count
            : model.NumLods;
        int firstLoadedLod = model.MaxLoadedLod;
        if (lodCount <= 0 ||
            lodCount > 4 ||
            lodCount > model.Lods.Count ||
            firstLoadedLod < 0 ||
            firstLoadedLod >= lodCount)
        {
            return false;
        }

        var result = new MapRenderStaticModelLodGeometry[
            lodCount - firstLoadedLod];
        for (int lodIndex = firstLoadedLod;
             lodIndex < lodCount;
             lodIndex++)
        {
            XModelLodInfo lod = model.Lods[lodIndex];
            XModelSurfsAsset? modelSurfs = lod.ModelSurfs;
            int surfaceCount = lod.NumSurfs;
            if (modelSurfs is null ||
                surfaceCount <= 0 ||
                surfaceCount > modelSurfs.Surfaces.Count)
            {
                geometries = [];
                return false;
            }

            result[lodIndex - firstLoadedLod] = new(
                lodIndex,
                lod,
                modelSurfs,
                lod.SurfIndex,
                surfaceCount);
        }

        geometries = Array.AsReadOnly(result);
        return true;
    }
}
