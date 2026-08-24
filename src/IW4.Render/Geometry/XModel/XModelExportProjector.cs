using System.Numerics;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Export.XModel;

namespace IW4.Render.Geometry.XModel;

/// <summary>
/// Projects one loader-materialized XModel LOD to the complete XMODEL_EXPORT
/// v6 handoff. This intentionally reads packed XSurface streams directly:
/// XModelRenderScene has already compacted and converted render-only data.
/// </summary>
public static class XModelExportProjector
{
    private const TextureSemantic ColorTextureSemantic =
        TextureSemantic.ColorMap;
    private const int DObjSkelMatSize = 0x40;

    public static bool TryProjectLoadedLod(
        XModelAsset model,
        int loadedLodIndex,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(model);
        document = null;
        var failures = new List<string>();
        if (!XModelLodGeometryCatalog.TryCreate(model, out IReadOnlyList<XModelLodGeometry> geometries))
        {
            blockers = ["The XModel has no valid canonical loaded LOD geometry."];
            return false;
        }

        XModelLodGeometry? geometry = geometries.FirstOrDefault(value =>
            value.LodIndex == loadedLodIndex);
        if (geometry is null)
        {
            blockers = [$"LOD {loadedLodIndex} is not a loaded canonical XModel LOD."];
            return false;
        }
        if (!XSurfaceVertexDecoder.TryCreate(
                XSurfaceVertexDecoder.DefaultTexCoordSource,
                out XSurfaceVertexDecoder? decoder) ||
            decoder is null)
        {
            blockers = ["The recovered XSurface UV0 decoder is unavailable."];
            return false;
        }

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
            var objects = new List<XModelExportObject>(geometry.SurfaceCount);
            var materials = new List<XModelExportMaterial>();
            var materialRows = new Dictionary<MaterialAsset, int>(ReferenceEqualityComparer.Instance);

            for (int surfaceOffset = 0;
                 surfaceOffset < geometry.SurfaceCount;
                 surfaceOffset++)
            {
                XSurface? surface = geometry.ModelSurfs.Surfaces[surfaceOffset];
                string prefix = $"LOD {loadedLodIndex} surface {surfaceOffset}";
                if (surface is null)
                {
                    failures.Add($"{prefix}: XSurface is unresolved.");
                    continue;
                }

                int materialIndex = ResolveMaterial(
                    model,
                    geometry.MaterialSurfaceStart,
                    surfaceOffset,
                    materialRows,
                    materials,
                    prefix,
                    failures);
                if (materialIndex < 0)
                    continue;

                List<IReadOnlyList<XModelExportBoneWeight>>? surfaceWeights =
                    TryProjectSurfaceWeights(surface, bones.Count, prefix, failures);
                if (surfaceWeights is null)
                    continue;

                int vertexBase = vertices.Count;
                XModelExportCorner[] corners = new XModelExportCorner[surface.VertCount];
                bool vertexFailure = false;
                for (int vertexIndex = 0;
                     vertexIndex < surface.VertCount;
                     vertexIndex++)
                {
                    if (!XSurfaceVertexDecoder.TryReadPosition(surface, vertexIndex, out Vector3 position) ||
                        !decoder.TryReadNormal(surface, vertexIndex, out Vector3 normal) ||
                        !decoder.TryReadColor(surface, vertexIndex, out Vector4 color) ||
                        !decoder.TryReadTexCoord(surface, vertexIndex, out Vector2 uv0) ||
                        !IsFinite(position) || !IsFinite(normal) ||
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
                    if (IsDegenerate(
                            vertices[vertexBase + first].Position,
                            vertices[vertexBase + second].Position,
                            vertices[vertexBase + third].Position))
                    {
                        failures.Add($"{prefix} triangle {triangleIndex}: degenerate topology cannot be exported without dropping geometry.");
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

    private static List<IReadOnlyList<XModelExportBoneWeight>>? TryProjectSurfaceWeights(
        XSurface surface,
        int boneCount,
        string prefix,
        List<string> failures)
    {
        int blendedVertexCount = checked(
            surface.VertexInfo.Blend0 + surface.VertexInfo.Blend1 +
            surface.VertexInfo.Blend2 + surface.VertexInfo.Blend3);
        int blendValueCount = checked(
            surface.VertexInfo.Blend0 + surface.VertexInfo.Blend1 * 3 +
            surface.VertexInfo.Blend2 * 5 + surface.VertexInfo.Blend3 * 7);
        bool hasRigid = surface.VertListCount != 0 || surface.VertList.Count != 0;
        bool hasBlend = blendedVertexCount != 0 || surface.VertexInfo.VertsBlend.Count != 0;
        if (hasRigid && hasBlend)
        {
            failures.Add($"{prefix}: mixed rigid and blended skinning has no proven checkpoint-1 ordering.");
            return null;
        }
        if (hasRigid)
            return TryProjectRigidWeights(surface, boneCount, prefix, failures);
        if (hasBlend)
            return TryProjectBlendedWeights(surface, boneCount, blendedVertexCount, blendValueCount, prefix, failures);

        failures.Add($"{prefix}: no rigid or blended skinning rows cover its vertices.");
        return null;
    }

    private static List<IReadOnlyList<XModelExportBoneWeight>>? TryProjectRigidWeights(
        XSurface surface,
        int boneCount,
        string prefix,
        List<string> failures)
    {
        if (surface.VertListCount != surface.VertList.Count)
        {
            failures.Add($"{prefix}: VertListCount does not match materialized rigid rows.");
            return null;
        }
        var rows = new List<IReadOnlyList<XModelExportBoneWeight>>(surface.VertCount);
        foreach (XRigidVertList? row in surface.VertList)
        {
            if (row is null || row.VertCount == 0 ||
                (row.BoneOffset & (DObjSkelMatSize - 1)) != 0 ||
                row.BoneOffset / DObjSkelMatSize >= boneCount ||
                row.TriOffset + row.TriCount > surface.TriCount)
            {
                failures.Add($"{prefix}: a rigid VertList row has invalid coverage, triangle range, or DObjSkelMat bone offset.");
                return null;
            }
            int boneIndex = row.BoneOffset / DObjSkelMatSize;
            for (int index = 0; index < row.VertCount; index++)
                rows.Add([new XModelExportBoneWeight(boneIndex, 1f)]);
        }
        if (rows.Count != surface.VertCount)
        {
            failures.Add($"{prefix}: rigid VertList rows cover {rows.Count} of {surface.VertCount} vertices.");
            return null;
        }
        return rows;
    }

    private static List<IReadOnlyList<XModelExportBoneWeight>>? TryProjectBlendedWeights(
        XSurface surface,
        int boneCount,
        int blendedVertexCount,
        int blendValueCount,
        string prefix,
        List<string> failures)
    {
        if (blendedVertexCount != surface.VertCount ||
            surface.VertexInfo.VertsBlend.Count != blendValueCount)
        {
            failures.Add($"{prefix}: Blend0..Blend3 and VertsBlend do not provide exact full vertex coverage.");
            return null;
        }

        var rows = new List<IReadOnlyList<XModelExportBoneWeight>>(surface.VertCount);
        int offset = 0;
        for (int influenceCount = 1; influenceCount <= 4; influenceCount++)
        {
            int rowCount = influenceCount switch
            {
                1 => surface.VertexInfo.Blend0,
                2 => surface.VertexInfo.Blend1,
                3 => surface.VertexInfo.Blend2,
                _ => surface.VertexInfo.Blend3
            };
            for (int row = 0; row < rowCount; row++)
            {
                var weights = new List<XModelExportBoneWeight>(influenceCount);
                int boneOffset = surface.VertexInfo.VertsBlend[offset++];
                if (!TryGetBoneIndex(boneOffset, boneCount, out int primaryBone))
                {
                    failures.Add($"{prefix}: blended vertex {rows.Count} has an invalid primary DObjSkelMat bone offset.");
                    return null;
                }
                var secondary = new List<(int BoneIndex, float Weight)>();
                for (int influence = 1; influence < influenceCount; influence++)
                {
                    int encodedBone = surface.VertexInfo.VertsBlend[offset++];
                    float weight = surface.VertexInfo.VertsBlend[offset++] / 65535f;
                    if (!TryGetBoneIndex(encodedBone, boneCount, out int boneIndex) ||
                        !float.IsFinite(weight) || weight < 0f)
                    {
                        failures.Add($"{prefix}: blended vertex {rows.Count} has an invalid secondary bone offset or weight.");
                        return null;
                    }
                    secondary.Add((boneIndex, weight));
                }
                float primaryWeight = 1f - secondary.Sum(value => value.Weight);
                if (!float.IsFinite(primaryWeight) || primaryWeight < 0f)
                {
                    failures.Add($"{prefix}: blended vertex {rows.Count} has a negative or non-finite primary weight.");
                    return null;
                }
                weights.Add(new XModelExportBoneWeight(primaryBone, primaryWeight));
                weights.AddRange(secondary.Select(value => new XModelExportBoneWeight(value.BoneIndex, value.Weight)));
                float total = weights.Sum(value => value.Weight);
                if (!float.IsFinite(total) || MathF.Abs(total - 1f) > 0.00001f)
                {
                    failures.Add($"{prefix}: blended vertex {rows.Count} weights are not normalized.");
                    return null;
                }
                rows.Add(weights);
            }
        }
        if (offset == blendValueCount && rows.Count == surface.VertCount)
            return rows;

        failures.Add($"{prefix}: blended VertsBlend traversal did not cover its declared payload exactly.");
        return null;
    }

    private static bool TryGetBoneIndex(int encodedOffset, int boneCount, out int boneIndex)
    {
        boneIndex = -1;
        if ((encodedOffset & (DObjSkelMatSize - 1)) != 0)
            return false;
        boneIndex = encodedOffset / DObjSkelMatSize;
        return boneIndex >= 0 && boneIndex < boneCount;
    }

    private static bool IsDegenerate(Vector3 first, Vector3 second, Vector3 third) =>
        Vector3.Cross(second - first, third - first) == Vector3.Zero;

    private static bool IsExportString(string? value) =>
        !string.IsNullOrEmpty(value) && !value.Any(char.IsControl);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

}
