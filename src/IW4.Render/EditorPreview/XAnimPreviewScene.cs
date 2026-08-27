using System.Numerics;
using IW4.AssetExchange.SourceFormat.XAnim;
using IW4.AssetExchange.XModel;
using IW4.Assets.Assets.XModel;
using IW4.Render.Transforms;

namespace IW4.Render.EditorPreview;

public readonly record struct XAnimPreviewBone(
    int ParentIndex,
    Vector3 Position,
    bool IsAnimated);

public sealed class XAnimPreviewPose
{
    internal XAnimPreviewPose(
        IReadOnlyList<XAnimPreviewBone> bones,
        IReadOnlyList<Matrix4x4> skinningPalette)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(skinningPalette);
        if (bones.Count != skinningPalette.Count)
        {
            throw new ArgumentException(
                "An XAnim preview pose requires one skinning transform per bone.",
                nameof(skinningPalette));
        }

        Bones = bones;
        SkinningPalette = skinningPalette;
    }

    public IReadOnlyList<XAnimPreviewBone> Bones { get; }

    internal IReadOnlyList<Matrix4x4> SkinningPalette { get; }
}

/// <summary>
/// Backend-neutral animation pose projected onto one compatible XModel
/// skeleton. XAnim root-motion delta remains unapplied for an in-place
/// editor preview.
/// </summary>
public sealed class XAnimPreviewScene
{
    private readonly XAnimPlaybackClip _clip;
    private readonly IReadOnlyList<XModelExportBone> _bones;
    private readonly int[] _trackIndexByBone;
    private readonly Vector3[] _bindLocalPositions;
    private readonly Quaternion[] _bindLocalRotations;
    private readonly Matrix4x4[] _inverseBindGlobalTransforms;

    private XAnimPreviewScene(
        XAnimPlaybackClip clip,
        XModelAsset model,
        IReadOnlyList<XModelExportBone> bones,
        int[] trackIndexByBone,
        Vector3[] bindLocalPositions,
        Quaternion[] bindLocalRotations,
        Matrix4x4[] inverseBindGlobalTransforms,
        int matchedTrackCount)
    {
        _clip = clip;
        _bones = bones;
        _trackIndexByBone = trackIndexByBone;
        _bindLocalPositions = bindLocalPositions;
        _bindLocalRotations = bindLocalRotations;
        _inverseBindGlobalTransforms = inverseBindGlobalTransforms;
        ModelName = string.IsNullOrWhiteSpace(model.Name)
            ? "<unnamed XModel>"
            : model.Name;
        MatchedTrackCount = matchedTrackCount;
    }

    public string ModelName { get; }

    public int BoneCount => _bones.Count;

    public int MatchedTrackCount { get; }

    public int UnmatchedTrackCount => _clip.BoneCount - MatchedTrackCount;

    public static bool TryCreate(
        XAnimPlaybackClip clip,
        XModelAsset model,
        out XAnimPreviewScene? scene,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(model);

        if (!XModelExportSkeletonProjector.TryProject(
                model,
                out IReadOnlyList<XModelExportBone> bones,
                out IReadOnlyList<string> blockers))
        {
            scene = null;
            reason = blockers.FirstOrDefault() ??
                "The XModel skeleton could not be projected.";
            return false;
        }

        var modelBoneByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < bones.Count; index++)
        {
            if (!modelBoneByName.TryAdd(bones[index].Name, index))
            {
                scene = null;
                reason = $"The XModel contains more than one bone named '{bones[index].Name}'.";
                return false;
            }
        }

        var trackIndexByBone = Enumerable.Repeat(-1, bones.Count).ToArray();
        int matchedTrackCount = 0;
        for (int trackIndex = 0;
             trackIndex < clip.BoneNames.Count;
             trackIndex++)
        {
            if (!modelBoneByName.TryGetValue(
                    clip.BoneNames[trackIndex],
                    out int boneIndex) ||
                trackIndexByBone[boneIndex] >= 0)
            {
                continue;
            }

            trackIndexByBone[boneIndex] = trackIndex;
            matchedTrackCount++;
        }

        if (matchedTrackCount == 0)
        {
            scene = null;
            reason = "The XAnim and XModel do not share any bone names.";
            return false;
        }

        if (!TryCreateLocalBindPose(
                bones,
                out Vector3[] bindLocalPositions,
                out Quaternion[] bindLocalRotations,
                out Matrix4x4[] inverseBindGlobalTransforms,
                out reason))
        {
            scene = null;
            return false;
        }

        scene = new XAnimPreviewScene(
            clip,
            model,
            bones,
            trackIndexByBone,
            bindLocalPositions,
            bindLocalRotations,
            inverseBindGlobalTransforms,
            matchedTrackCount);
        reason = string.Empty;
        return true;
    }

    public XAnimPreviewPose Sample(float frame)
    {
        var sampledTracks = new XAnimLocalBoneTransform[_clip.BoneCount];
        _clip.Sample(frame, sampledTracks);

        var globalTransforms = new Matrix4x4[_bones.Count];
        var poseBones = new XAnimPreviewBone[_bones.Count];
        var skinningPalette = new Matrix4x4[_bones.Count];
        for (int boneIndex = 0; boneIndex < _bones.Count; boneIndex++)
        {
            int trackIndex = _trackIndexByBone[boneIndex];
            Vector3 localPosition = _bindLocalPositions[boneIndex];
            Quaternion localRotation = _bindLocalRotations[boneIndex];
            bool isAnimated = false;
            if (trackIndex >= 0)
            {
                XAnimLocalBoneTransform sampled = sampledTracks[trackIndex];
                localPosition += sampled.Translation;
                localRotation = sampled.Rotation;
                isAnimated = true;
            }

            Matrix4x4 local = Matrix4x4.CreateFromQuaternion(localRotation);
            local.Translation = localPosition;
            int parentIndex = _bones[boneIndex].ParentIndex;
            Matrix4x4 global = parentIndex < 0
                ? local
                : local * globalTransforms[parentIndex];
            globalTransforms[boneIndex] = global;
            skinningPalette[boneIndex] =
                _inverseBindGlobalTransforms[boneIndex] * global;

            Vector3 renderPosition =
                RenderCoordinateConverter.GameToRenderPosition(global.Translation);
            poseBones[boneIndex] = new XAnimPreviewBone(
                parentIndex,
                renderPosition,
                isAnimated);
        }

        return new XAnimPreviewPose(
            Array.AsReadOnly(poseBones),
            Array.AsReadOnly(skinningPalette));
    }

    private static bool TryCreateLocalBindPose(
        IReadOnlyList<XModelExportBone> bones,
        out Vector3[] positions,
        out Quaternion[] rotations,
        out Matrix4x4[] inverseBindGlobalTransforms,
        out string reason)
    {
        positions = new Vector3[bones.Count];
        rotations = new Quaternion[bones.Count];
        inverseBindGlobalTransforms = new Matrix4x4[bones.Count];
        var globalTransforms = new Matrix4x4[bones.Count];
        for (int index = 0; index < bones.Count; index++)
        {
            XModelExportBone bone = bones[index];
            Quaternion globalRotation = Normalize(bone.GlobalRotation);
            Matrix4x4 global = Matrix4x4.CreateFromQuaternion(globalRotation);
            global.Translation = bone.GlobalOffset;
            globalTransforms[index] = global;
            if (!Matrix4x4.Invert(
                    global,
                    out inverseBindGlobalTransforms[index]))
            {
                reason = $"Bone '{bone.Name}' has a non-invertible bind transform.";
                return false;
            }

            Matrix4x4 local = global;
            if (bone.ParentIndex >= 0)
            {
                if (bone.ParentIndex >= index ||
                    !Matrix4x4.Invert(
                        globalTransforms[bone.ParentIndex],
                        out Matrix4x4 inverseParent))
                {
                    reason = $"Bone '{bone.Name}' has an invalid parent transform.";
                    return false;
                }

                local = global * inverseParent;
            }

            positions[index] = local.Translation;
            rotations[index] = Normalize(
                Quaternion.CreateFromRotationMatrix(local));
            if (!IsFinite(positions[index]) ||
                !IsFinite(rotations[index]))
            {
                reason = $"Bone '{bone.Name}' has a non-finite bind transform.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static Quaternion Normalize(Quaternion value)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
            return Quaternion.Identity;
        float inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
        return new Quaternion(
            value.X * inverseLength,
            value.Y * inverseLength,
            value.Z * inverseLength,
            value.W * inverseLength);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
