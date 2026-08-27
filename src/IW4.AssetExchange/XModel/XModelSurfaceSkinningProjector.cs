using IW4.Assets.Assets.XModel;

namespace IW4.AssetExchange.XModel;

/// <summary>
/// Projects the native rigid or blended XSurface skinning rows without
/// changing their source-vertex ordering.
/// </summary>
public static class XModelSurfaceSkinningProjector
{
    private const int DObjSkelMatSize = 0x40;

    public static bool TryProject(
        XSurface surface,
        int boneCount,
        out IReadOnlyList<IReadOnlyList<XModelExportBoneWeight>> weights,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(surface);
        weights = [];
        blocker = string.Empty;
        int blendedVertexCount = checked(
            surface.VertexInfo.Blend0 + surface.VertexInfo.Blend1 +
            surface.VertexInfo.Blend2 + surface.VertexInfo.Blend3);
        int blendValueCount = checked(
            surface.VertexInfo.Blend0 + surface.VertexInfo.Blend1 * 3 +
            surface.VertexInfo.Blend2 * 5 + surface.VertexInfo.Blend3 * 7);
        bool hasRigid =
            surface.VertListCount != 0 || surface.VertList.Count != 0;
        bool hasBlend =
            blendedVertexCount != 0 ||
            surface.VertexInfo.VertsBlend.Count != 0;
        if (hasRigid && hasBlend)
        {
            blocker = "mixed rigid and blended skinning has no proven checkpoint-1 ordering.";
            return false;
        }

        List<IReadOnlyList<XModelExportBoneWeight>>? projected;
        if (hasRigid)
        {
            projected = TryProjectRigid(
                surface,
                boneCount,
                out blocker);
        }
        else if (hasBlend)
        {
            projected = TryProjectBlended(
                surface,
                boneCount,
                blendedVertexCount,
                blendValueCount,
                out blocker);
        }
        else
        {
            blocker = "no rigid or blended skinning rows cover its vertices.";
            return false;
        }

        if (projected is null)
            return false;

        weights = Array.AsReadOnly(projected.ToArray());
        return true;
    }

    private static List<IReadOnlyList<XModelExportBoneWeight>>?
        TryProjectRigid(
            XSurface surface,
            int boneCount,
            out string blocker)
    {
        blocker = string.Empty;
        if (surface.VertListCount != surface.VertList.Count)
        {
            blocker = "VertListCount does not match materialized rigid rows.";
            return null;
        }

        var rows = new List<IReadOnlyList<XModelExportBoneWeight>>(
            surface.VertCount);
        foreach (XRigidVertList? row in surface.VertList)
        {
            if (row is null || row.VertCount == 0 ||
                (row.BoneOffset & (DObjSkelMatSize - 1)) != 0 ||
                row.BoneOffset / DObjSkelMatSize >= boneCount ||
                row.TriOffset + row.TriCount > surface.TriCount)
            {
                blocker = "a rigid VertList row has invalid coverage, triangle range, or DObjSkelMat bone offset.";
                return null;
            }

            int boneIndex = row.BoneOffset / DObjSkelMatSize;
            for (int index = 0; index < row.VertCount; index++)
            {
                rows.Add(
                [
                    new XModelExportBoneWeight(boneIndex, 1f)
                ]);
            }
        }

        if (rows.Count != surface.VertCount)
        {
            blocker = $"rigid VertList rows cover {rows.Count} of {surface.VertCount} vertices.";
            return null;
        }

        return rows;
    }

    private static List<IReadOnlyList<XModelExportBoneWeight>>?
        TryProjectBlended(
            XSurface surface,
            int boneCount,
            int blendedVertexCount,
            int blendValueCount,
            out string blocker)
    {
        blocker = string.Empty;
        if (blendedVertexCount != surface.VertCount ||
            surface.VertexInfo.VertsBlend.Count != blendValueCount)
        {
            blocker = "Blend0..Blend3 and VertsBlend do not provide exact full vertex coverage.";
            return null;
        }

        var rows = new List<IReadOnlyList<XModelExportBoneWeight>>(
            surface.VertCount);
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
                var rowWeights = new List<XModelExportBoneWeight>(
                    influenceCount);
                int boneOffset = surface.VertexInfo.VertsBlend[offset++];
                if (!TryGetBoneIndex(
                        boneOffset,
                        boneCount,
                        out int primaryBone))
                {
                    blocker = $"blended vertex {rows.Count} has an invalid primary DObjSkelMat bone offset.";
                    return null;
                }

                var secondary = new List<(int BoneIndex, float Weight)>(
                    influenceCount - 1);
                for (int influence = 1;
                     influence < influenceCount;
                     influence++)
                {
                    int encodedBone =
                        surface.VertexInfo.VertsBlend[offset++];
                    float weight =
                        surface.VertexInfo.VertsBlend[offset++] / 65535f;
                    if (!TryGetBoneIndex(
                            encodedBone,
                            boneCount,
                            out int boneIndex) ||
                        !float.IsFinite(weight) ||
                        weight < 0f)
                    {
                        blocker = $"blended vertex {rows.Count} has an invalid secondary bone offset or weight.";
                        return null;
                    }

                    secondary.Add((boneIndex, weight));
                }

                float primaryWeight =
                    1f - secondary.Sum(value => value.Weight);
                if (!float.IsFinite(primaryWeight) || primaryWeight < 0f)
                {
                    blocker = $"blended vertex {rows.Count} has a negative or non-finite primary weight.";
                    return null;
                }

                rowWeights.Add(new XModelExportBoneWeight(
                    primaryBone,
                    primaryWeight));
                rowWeights.AddRange(secondary.Select(value =>
                    new XModelExportBoneWeight(
                        value.BoneIndex,
                        value.Weight)));
                float total = rowWeights.Sum(value => value.Weight);
                if (!float.IsFinite(total) ||
                    MathF.Abs(total - 1f) > 0.00001f)
                {
                    blocker = $"blended vertex {rows.Count} weights are not normalized.";
                    return null;
                }

                rows.Add(rowWeights);
            }
        }

        if (offset == blendValueCount && rows.Count == surface.VertCount)
            return rows;

        blocker = "blended VertsBlend traversal did not cover its declared payload exactly.";
        return null;
    }

    private static bool TryGetBoneIndex(
        int encodedOffset,
        int boneCount,
        out int boneIndex)
    {
        boneIndex = -1;
        if ((encodedOffset & (DObjSkelMatSize - 1)) != 0)
            return false;

        boneIndex = encodedOffset / DObjSkelMatSize;
        return boneIndex >= 0 && boneIndex < boneCount;
    }
}
