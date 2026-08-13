using System.Numerics;
using IW4.Assets.Assets.XModel;

namespace IW4.Assets.XModel.Export;

/// <summary>
/// Projects the canonical XModel BaseMat skeleton to the global transforms
/// carried by XMODEL_EXPORT. ParentList entries are native backward deltas.
/// </summary>
public static class XModelExportSkeletonProjector
{
    public static bool TryProject(
        XModelAsset model,
        out IReadOnlyList<XModelExportBone> bones,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(model);
        int boneCount = model.NumBones;
        int rootCount = model.NumRootBones;
        var failures = new List<string>();
        if (rootCount > boneCount ||
            model.BoneNames.Count != boneCount ||
            model.BaseMat.Count != boneCount ||
            model.ParentList.Count != boneCount - rootCount)
        {
            bones = [];
            blockers =
            [
                "BoneNames, BaseMat, NumBones, NumRootBones, and ParentList cardinalities do not agree."
            ];
            return false;
        }

        var result = new List<XModelExportBone>(boneCount);
        for (int index = 0; index < boneCount; index++)
        {
            string? name = model.BoneNames[index]?.Text;
            DObjAnimMat? baseMat = model.BaseMat[index];
            if (!IsExportString(name) ||
                baseMat is null ||
                !IsFinite(baseMat.Quat) ||
                !IsFinite(baseMat.Trans) ||
                !float.IsFinite(baseMat.TransWeight))
            {
                failures.Add(
                    $"Bone {index}: name or BaseMat transform is unresolved, invalid, or non-finite.");
                continue;
            }

            int parentIndex = -1;
            if (index >= rootCount)
            {
                int encodedDelta = model.ParentList[index - rootCount];
                parentIndex = index - encodedDelta;
                if (encodedDelta <= 0 || parentIndex < 0 || parentIndex >= index)
                {
                    failures.Add(
                        $"Bone {index}: ParentList delta {encodedDelta} does not resolve to an earlier bone.");
                    continue;
                }
            }

            result.Add(new XModelExportBone(
                name!,
                parentIndex,
                new Vector3(baseMat.Trans.X, baseMat.Trans.Y, baseMat.Trans.Z),
                new Quaternion(
                    baseMat.Quat.X,
                    baseMat.Quat.Y,
                    baseMat.Quat.Z,
                    baseMat.Quat.W)));
        }

        bones = Array.AsReadOnly(result.ToArray());
        blockers = Array.AsReadOnly(failures.ToArray());
        return failures.Count == 0;
    }

    private static bool IsExportString(string? value) =>
        !string.IsNullOrEmpty(value) && !value.Any(char.IsControl);

    private static bool IsFinite(DObjQuat value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static bool IsFinite(IW4.Assets.Math.Vec3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
