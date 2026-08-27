using System.Numerics;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;

namespace IW4.AssetExchange.XModel;

/// <summary>
/// Projects one loader-materialized XModel LOD to the complete XMODEL_EXPORT
/// v6 handoff by reading the native packed XSurface streams directly.
/// </summary>
public static class XModelExportProjector
{
    private const TextureSemantic ColorTextureSemantic =
        TextureSemantic.ColorMap;

    public static bool TryProjectLoadedLod(
        XModelAsset model,
        int loadedLodIndex,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(model);
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
            document = null;
            blockers = ["The XModel has no valid canonical loaded LOD geometry."];
            return false;
        }

        for (int lodIndex = firstLoadedLod; lodIndex < lodCount; lodIndex++)
        {
            XModelLodInfo loadedLod = model.Lods[lodIndex];
            if (loadedLod.ModelSurfs is null ||
                loadedLod.NumSurfs <= 0 ||
                loadedLod.NumSurfs > loadedLod.ModelSurfs.Surfaces.Count)
            {
                document = null;
                blockers = ["The XModel has no valid canonical loaded LOD geometry."];
                return false;
            }
        }

        if (loadedLodIndex < firstLoadedLod || loadedLodIndex >= lodCount)
        {
            document = null;
            blockers = [$"LOD {loadedLodIndex} is not a loaded canonical XModel LOD."];
            return false;
        }

        XModelLodInfo lod = model.Lods[loadedLodIndex];
        return TryProjectLod(
            model,
            lod.ModelSurfs!,
            lod.SurfIndex,
            lod.NumSurfs,
            loadedLodIndex,
            out document,
            out blockers);
    }

    /// <summary>
    /// Projects any active LOD whose native XModelSurfs payload was
    /// materialized by the loader. Unlike renderer selection, source export
    /// must not discard rows below MaxLoadedLod.
    /// </summary>
    public static bool TryProjectMaterializedLod(
        XModelAsset model,
        int lodIndex,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(model);
        document = null;
        int lodCount = model.NumLods == 0
            ? model.Lods.Count
            : model.NumLods;
        if (lodCount is < 1 or > 4 ||
            lodCount > model.Lods.Count ||
            lodIndex < 0 ||
            lodIndex >= lodCount)
        {
            blockers = [$"LOD {lodIndex} is not an active XModel LOD."];
            return false;
        }

        XModelLodInfo lod = model.Lods[lodIndex];
        XModelSurfsAsset? modelSurfs = lod.ModelSurfs;
        int surfaceCount = lod.NumSurfs;
        if (modelSurfs is null ||
            surfaceCount <= 0 ||
            surfaceCount > modelSurfs.Surfaces.Count)
        {
            blockers =
            [
                $"LOD {lodIndex} has no complete materialized XModelSurfs geometry."
            ];
            return false;
        }

        return TryProjectLod(
            model,
            modelSurfs,
            lod.SurfIndex,
            surfaceCount,
            lodIndex,
            out document,
            out blockers);
    }

    private static bool TryProjectLod(
        XModelAsset model,
        XModelSurfsAsset modelSurfs,
        int materialSurfaceStart,
        int surfaceCount,
        int loadedLodIndex,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers)
    {
        document = null;
        var failures = new List<string>();
        if (!XModelExportSkeletonProjector.TryProject(
                model,
                out IReadOnlyList<XModelExportBone> bones,
                out IReadOnlyList<string> skeletonBlockers))
        {
            blockers = skeletonBlockers;
            return false;
        }

        try
        {
            var vertices = new List<XModelExportVertex>();
            var triangles = new List<XModelExportTriangle>();
            var objects = new List<XModelExportObject>(surfaceCount);
            var materials = new List<XModelExportMaterial>();
            var materialRows = new Dictionary<MaterialAsset, int>(ReferenceEqualityComparer.Instance);

            for (int surfaceOffset = 0;
                 surfaceOffset < surfaceCount;
                 surfaceOffset++)
            {
                XSurface? surface = modelSurfs.Surfaces[surfaceOffset];
                string prefix = $"LOD {loadedLodIndex} surface {surfaceOffset}";
                if (surface is null)
                {
                    failures.Add($"{prefix}: XSurface is unresolved.");
                    continue;
                }

                int materialIndex = ResolveMaterial(
                    model,
                    materialSurfaceStart,
                    surfaceOffset,
                    materialRows,
                    materials,
                    prefix,
                    failures);
                if (materialIndex < 0)
                    continue;

                if (!XModelSurfaceSkinningProjector.TryProject(
                        surface,
                        bones.Count,
                        out IReadOnlyList<IReadOnlyList<XModelExportBoneWeight>>
                            surfaceWeights,
                        out string skinningBlocker))
                {
                    failures.Add($"{prefix}: {skinningBlocker}");
                    continue;
                }

                int vertexBase = vertices.Count;
                XModelExportCorner[] corners = new XModelExportCorner[surface.VertCount];
                bool vertexFailure = false;
                for (int vertexIndex = 0;
                     vertexIndex < surface.VertCount;
                     vertexIndex++)
                {
                    if (!XSurfaceVertexCodec.TryReadReasonablePosition(surface.Verts0, vertexIndex, out Vector3 position) ||
                        !XSurfaceVertexCodec.TryReadNormal(surface.Verts1, vertexIndex, out Vector3 normal) ||
                        !XSurfaceVertexCodec.TryReadColor(surface.Verts1, vertexIndex, out Vector4 color) ||
                        !XSurfaceVertexCodec.TryReadUv0(surface.Verts1, vertexIndex, out Vector2 uv0) ||
                        !IsFinite(normal) ||
                        !IsFinite(color) || !IsFinite(uv0))
                    {
                        failures.Add($"{prefix} vertex {vertexIndex}: position, normal, color, or UV0 could not be decoded as finite data.");
                        vertexFailure = true;
                        break;
                    }

                    vertices.Add(new XModelExportVertex(
                        position,
                        surfaceWeights[vertexIndex]));
                    corners[vertexIndex] = new XModelExportCorner(
                        vertexBase + vertexIndex,
                        normal,
                        color,
                        uv0);
                }
                if (vertexFailure)
                    continue;

                int expectedIndices = checked(surface.TriCount * 3);
                if (surface.TriIndices.Count != expectedIndices)
                {
                    failures.Add($"{prefix}: expected {expectedIndices} triangle indices but found {surface.TriIndices.Count}.");
                    continue;
                }

                int objectIndex = objects.Count;
                bool triangleFailure = false;
                for (int triangleIndex = 0;
                     triangleIndex < surface.TriCount;
                     triangleIndex++)
                {
                    int indexOffset = triangleIndex * 3;
                    int first = surface.TriIndices[indexOffset];
                    int second = surface.TriIndices[indexOffset + 1];
                    int third = surface.TriIndices[indexOffset + 2];
                    if (first >= surface.VertCount || second >= surface.VertCount || third >= surface.VertCount)
                    {
                        failures.Add($"{prefix} triangle {triangleIndex}: an index is outside VertCount {surface.VertCount}.");
                        triangleFailure = true;
                        break;
                    }
                    triangles.Add(new XModelExportTriangle(
                        objectIndex,
                        materialIndex,
                        corners[first],
                        corners[second],
                        corners[third]));
                }
                if (triangleFailure)
                    continue;

                objects.Add(new XModelExportObject($"surf{surfaceOffset}"));
            }

            if (failures.Count != 0)
            {
                blockers = failures.AsReadOnly();
                return false;
            }

            document = new XModelExportDocument(
                bones,
                vertices,
                triangles,
                objects,
                materials);
            blockers = [];
            return true;
        }
        catch (OverflowException exception)
        {
            blockers = [$"LOD {loadedLodIndex} has an overflowing XSurface count or offset: {exception.Message}"];
            return false;
        }
    }

    private static int ResolveMaterial(
        XModelAsset model,
        int materialSurfaceStart,
        int surfaceOffset,
        Dictionary<MaterialAsset, int> materialRows,
        List<XModelExportMaterial> materials,
        string prefix,
        List<string> failures)
    {
        int parentMaterialIndex;
        try
        {
            parentMaterialIndex = checked(materialSurfaceStart + surfaceOffset);
        }
        catch (OverflowException)
        {
            failures.Add($"{prefix}: MaterialSurfaceStart overflowed.");
            return -1;
        }
        if (parentMaterialIndex < 0 || parentMaterialIndex >= model.Materials.Count ||
            model.Materials[parentMaterialIndex] is not { } material ||
            !IsExportString(material.Info.Name))
        {
            failures.Add($"{prefix}: parent material slot {parentMaterialIndex} is unresolved or has no valid IW4 material name.");
            return -1;
        }
        if (materialRows.TryGetValue(material, out int existing))
            return existing;

        string colorMapPath = TryGetColorMapPath(material);
        int index = materials.Count;
        materials.Add(new XModelExportMaterial(material.Info.Name!, colorMapPath));
        materialRows.Add(material, index);
        return index;
    }

    private static string TryGetColorMapPath(MaterialAsset material)
    {
        MaterialTextureDef[] candidates = material.Textures
            .Where(texture => texture is not null && texture.Semantic == ColorTextureSemantic)
            .ToArray();
        if (candidates.Length != 1 || candidates[0].Image?.Name is not { } imageName ||
            !IsExportString(imageName))
        {
            return string.Empty;
        }

        string assetName = imageName.StartsWith(",", StringComparison.Ordinal)
            ? imageName[1..]
            : imageName;
        return IsExportString(assetName)
            ? $"../images/{assetName}.dds"
            : string.Empty;
    }

    private static bool IsExportString(string? value) =>
        !string.IsNullOrEmpty(value) && !value.Any(char.IsControl);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

}
