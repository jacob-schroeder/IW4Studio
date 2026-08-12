using System.Numerics;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.Render.Geometry;
using IW4.Render.Scheduling.StaticModels;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    public XModelRenderScene BuildXModel(XModelAsset model)
    {
        ArgumentNullException.ThrowIfNull(model);
        string? loadedName = model.Name;
        if (string.IsNullOrWhiteSpace(loadedName))
        {
            throw new InvalidOperationException(
                "The XModel projection requires a loaded model name.");
        }
        string modelName = loadedName;
        if (model.NumSurfs == 0 && model.Lods.All(lod => lod.NumSurfs == 0))
        {
            return new XModelRenderScene(
                modelName,
                [],
                defaultLodIndex: -1,
                MapRenderBounds.Empty,
                [$"XModel '{modelName}' contains no renderable surfaces."]);
        }
        if (!MapRenderStaticModelLodGeometryCatalog.TryCreate(
                model,
                out IReadOnlyList<MapRenderStaticModelLodGeometry> lodGeometries))
        {
            throw new InvalidOperationException(
                $"XModel '{modelName}' does not contain a complete " +
                "loaded LOD geometry catalog.");
        }

        StaticVertexDecoder decoder =
            SelectStaticVertexDecoder(GenericFallbackTexCoordSource) ??
            throw new InvalidOperationException(
                "The PS3 static-XSurface vertex route is unavailable.");
        var diagnostics = new List<string>();
        var lods = new List<XModelRenderLod>(lodGeometries.Count);
        MapRenderBounds aggregateBounds = MapRenderBounds.Empty;

        foreach (MapRenderStaticModelLodGeometry lodGeometry in lodGeometries)
        {
            if (!float.IsFinite(lodGeometry.Lod.Dist))
            {
                throw IncompleteLod(
                    modelName,
                    lodGeometry.LodIndex,
                    "distance is not finite");
            }

            var surfaces = new List<XModelRenderSurface>(
                lodGeometry.SurfaceCount);
            MapRenderBounds lodBounds = MapRenderBounds.Empty;
            for (int surfaceOffset = 0;
                 surfaceOffset < lodGeometry.SurfaceCount;
                 surfaceOffset++)
            {
                int parentMaterialIndex = checked(
                    lodGeometry.MaterialSurfaceStart + surfaceOffset);
                MaterialAsset? material =
                    (uint)parentMaterialIndex < (uint)model.Materials.Count
                        ? model.Materials[parentMaterialIndex]
                        : null;
                string? materialName = material?.Info.Name;
                if (string.IsNullOrWhiteSpace(materialName))
                {
                    throw IncompleteLod(
                        modelName,
                        lodGeometry.LodIndex,
                        $"surface {surfaceOffset} has no parent material at index {parentMaterialIndex}");
                }

                XSurface surface =
                    lodGeometry.ModelSurfs.Surfaces[surfaceOffset];
                XModelRenderSurface projection = ProjectXModelSurface(
                    modelName,
                    lodGeometry.LodIndex,
                    surfaceOffset,
                    parentMaterialIndex,
                    materialName,
                    surface,
                    decoder,
                    diagnostics);
                surfaces.Add(projection);
                lodBounds = IncludeBounds(lodBounds, projection.Bounds);
            }

            if (!lodBounds.IsValid)
            {
                throw IncompleteLod(
                    modelName,
                    lodGeometry.LodIndex,
                    "no renderable surface geometry was projected");
            }

            var lod = new XModelRenderLod(
                lodGeometry.LodIndex,
                lodGeometry.Lod.Dist,
                lodBounds,
                surfaces);
            lods.Add(lod);
            aggregateBounds = IncludeBounds(aggregateBounds, lodBounds);
        }

        return new XModelRenderScene(
            modelName,
            lods,
            lodGeometries[0].LodIndex,
            aggregateBounds,
            diagnostics);
    }

    private static XModelRenderSurface ProjectXModelSurface(
        string modelName,
        int lodIndex,
        int geometrySurfaceIndex,
        int parentMaterialIndex,
        string materialName,
        XSurface surface,
        StaticVertexDecoder decoder,
        List<string> diagnostics)
    {
        if (surface.VertCount == 0 || surface.TriCount == 0)
        {
            throw IncompleteLod(
                modelName,
                lodIndex,
                $"surface {geometrySurfaceIndex} declares no geometry");
        }

        var decodedPositions = new Vector3[surface.VertCount];
        var decodedNormals = new Vector3[surface.VertCount];
        var decodedUvs = new Vector2[surface.VertCount];
        var decodedVertices = new bool[surface.VertCount];
        var decodedNormalAvailable = new bool[surface.VertCount];
        var decodedUvAvailable = new bool[surface.VertCount];
        for (int vertexIndex = 0;
             vertexIndex < surface.VertCount;
             vertexIndex++)
        {
            if (!TryReadXSurfaceLocalPosition(
                    surface.Verts0,
                    vertexIndex,
                    out Vector3 gamePosition))
            {
                continue;
            }

            decodedPositions[vertexIndex] =
                ToRenderCoordinates(gamePosition);
            decodedVertices[vertexIndex] = true;

            if (decoder.TryReadNormal(
                    surface,
                    vertexIndex,
                    out Vector3 gameNormal) &&
                IsReasonable(gameNormal))
            {
                decodedNormals[vertexIndex] =
                    ToRenderCoordinates(gameNormal);
                decodedNormalAvailable[vertexIndex] = true;
            }

            if (decoder.TryReadTexCoord(
                    surface,
                    vertexIndex,
                    out Vector2 rawUv) &&
                TryPrepareTexCoord(
                    rawUv,
                    allowSanitization: false,
                    out Vector2 uv,
                    out _))
            {
                decodedUvs[vertexIndex] = uv;
                decodedUvAvailable[vertexIndex] = true;
            }
        }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<uint>(surface.TriCount * 3);
        var projectedIndexBySource = new int[surface.VertCount];
        Array.Fill(projectedIndexBySource, -1);
        MapRenderBounds bounds = MapRenderBounds.Empty;
        int skippedTriangles = 0;
        int fallbackNormalVertices = 0;
        int fallbackUvVertices = 0;

        for (int triangleIndex = 0;
             triangleIndex < surface.TriCount;
             triangleIndex++)
        {
            int indexOffset = triangleIndex * 3;
            if (indexOffset + 2 >= surface.TriIndices.Count)
            {
                skippedTriangles++;
                continue;
            }

            int i0 = surface.TriIndices[indexOffset];
            int i1 = surface.TriIndices[indexOffset + 1];
            int i2 = surface.TriIndices[indexOffset + 2];
            if ((uint)i0 >= surface.VertCount ||
                (uint)i1 >= surface.VertCount ||
                (uint)i2 >= surface.VertCount ||
                !decodedVertices[i0] ||
                !decodedVertices[i1] ||
                !decodedVertices[i2])
            {
                skippedTriangles++;
                continue;
            }
            if (i0 == i1 || i1 == i2 || i2 == i0)
                continue;

            AddProjectedIndex(i0);
            AddProjectedIndex(i1);
            AddProjectedIndex(i2);
        }

        if (indices.Count == 0 || !bounds.IsValid)
        {
            throw IncompleteLod(
                modelName,
                lodIndex,
                $"surface {geometrySurfaceIndex} has no valid triangles");
        }
        if (skippedTriangles > 0)
        {
            diagnostics.Add(
                $"LOD {lodIndex} surface {geometrySurfaceIndex}: skipped {skippedTriangles} of {surface.TriCount} invalid triangles.");
        }
        if (fallbackNormalVertices > 0 || fallbackUvVertices > 0)
        {
            diagnostics.Add(
                $"LOD {lodIndex} surface {geometrySurfaceIndex}: defaulted projected vertex channels (normal={fallbackNormalVertices}, uv={fallbackUvVertices}).");
        }

        return new XModelRenderSurface(
            geometrySurfaceIndex,
            parentMaterialIndex,
            materialName,
            positions,
            normals,
            uvs,
            indices,
            bounds);

        void AddProjectedIndex(int sourceIndex)
        {
            int projectedIndex = projectedIndexBySource[sourceIndex];
            if (projectedIndex < 0)
            {
                projectedIndex = positions.Count;
                projectedIndexBySource[sourceIndex] = projectedIndex;
                Vector3 position = decodedPositions[sourceIndex];
                positions.Add(position);
                normals.Add(decodedNormals[sourceIndex]);
                uvs.Add(decodedUvs[sourceIndex]);
                if (!decodedNormalAvailable[sourceIndex])
                    fallbackNormalVertices++;
                if (!decodedUvAvailable[sourceIndex])
                    fallbackUvVertices++;
                bounds = bounds.Include(position);
            }

            indices.Add(checked((uint)projectedIndex));
        }
    }

    private static InvalidOperationException IncompleteLod(
        string modelName,
        int lodIndex,
        string detail) =>
        new(
            $"XModel '{modelName}' loaded LOD {lodIndex} is incomplete: {detail}.");
}
