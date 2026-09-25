using System.Numerics;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.XModel;
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

    public IReadOnlyList<Matrix4x4> SkinningPalette { get; }
}

/// <summary>
/// Backend-neutral animation pose projected onto one compatible XModel or an
/// ordered composition of attached XModels. XAnim root-motion delta remains
/// unapplied for an in-place editor preview.
/// </summary>
public sealed class XAnimPreviewScene
{
    private readonly XAnimPlaybackClip _clip;
    private readonly IReadOnlyList<XAnimPreviewCompositionBone> _bones;
    private readonly int[] _trackIndexByBone;

    private XAnimPreviewScene(
        XAnimPlaybackClip clip,
        string modelName,
        IReadOnlyList<XAnimPreviewCompositionBone> bones,
        int[] trackIndexByBone,
        int matchedTrackCount)
    {
        _clip = clip;
        _bones = bones;
        _trackIndexByBone = trackIndexByBone;
        ModelName = modelName;
        MatchedTrackCount = matchedTrackCount;
    }

    public string ModelName { get; }

    public int BoneCount => _bones.Count;

    public int MatchedTrackCount { get; }

    public int UnmatchedTrackCount => _clip.BoneCount - MatchedTrackCount;

    public int TrackCount => _clip.BoneCount;

    public static bool TryCreate(
        XAnimPlaybackClip clip,
        XModelAsset model,
        out XAnimPreviewScene? scene,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(model);
        return TryCreate(
            clip,
            [new XAnimPreviewModelComponent(model)],
            out scene,
            out reason);
    }

    public static bool TryCreate(
        XAnimPlaybackClip clip,
        IReadOnlyList<XAnimPreviewModelComponent> components,
        out XAnimPreviewScene? scene,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(components);

        if (!XAnimPreviewComposition.TryProject(
                components,
                out XAnimPreviewComposition? composition,
                out reason) ||
            composition is null)
        {
            scene = null;
            return false;
        }

        var modelBoneByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < composition.Bones.Count; index++)
        {
            modelBoneByName.TryAdd(composition.Bones[index].Name, index);
        }

        var trackIndexByBone = Enumerable.Repeat(
            -1,
            composition.Bones.Count).ToArray();
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

        scene = new XAnimPreviewScene(
            clip,
            CreateModelName(components),
            composition.Bones,
            trackIndexByBone,
            matchedTrackCount);
        reason = string.Empty;
        return true;
    }

    public XAnimPreviewPose Sample(float frame)
    {
        var sampledTracks = new XAnimLocalBoneTransform[_clip.BoneCount];
        var globalTransforms = new Matrix4x4[_bones.Count];
        var poseBones = new XAnimPreviewBone[_bones.Count];
        var skinningPalette = new Matrix4x4[_bones.Count];
        SamplePalette(frame, sampledTracks, globalTransforms, skinningPalette);
        for (int boneIndex = 0; boneIndex < _bones.Count; boneIndex++)
        {
            Vector3 renderPosition =
                RenderCoordinateConverter.GameToRenderPosition(globalTransforms[boneIndex].Translation);
            poseBones[boneIndex] = new XAnimPreviewBone(
                _bones[boneIndex].ParentIndex,
                renderPosition,
                _trackIndexByBone[boneIndex] >= 0);
        }

        return new XAnimPreviewPose(
            Array.AsReadOnly(poseBones),
            Array.AsReadOnly(skinningPalette));
    }

    /// <summary>Samples skinning transforms into caller-owned buffers for animation playback.</summary>
    public void SamplePalette(float frame, Span<XAnimLocalBoneTransform> sampledTracks,
        Span<Matrix4x4> globalTransforms, Span<Matrix4x4> skinningPalette)
    {
        if (sampledTracks.Length < _clip.BoneCount ||
            globalTransforms.Length < _bones.Count || skinningPalette.Length < _bones.Count)
            throw new ArgumentException("XAnim palette sampling buffers are too small.");
        _clip.Sample(frame, sampledTracks);
        for (int boneIndex = 0; boneIndex < _bones.Count; boneIndex++)
        {
            int trackIndex = _trackIndexByBone[boneIndex];
            XAnimPreviewCompositionBone bone = _bones[boneIndex];
            Vector3 localPosition = bone.BindLocalPosition;
            Quaternion localRotation = bone.BindLocalRotation;
            if (trackIndex >= 0)
            {
                XAnimLocalBoneTransform sampled = sampledTracks[trackIndex];
                localPosition = bone.ModelLocalTranslation + sampled.Translation;
                localRotation = sampled.Rotation;
            }

            Matrix4x4 local = Matrix4x4.CreateFromQuaternion(localRotation);
            local.Translation = localPosition;
            int parentIndex = bone.ParentIndex;
            Matrix4x4 global = parentIndex < 0
                ? local
                : local * globalTransforms[parentIndex];
            globalTransforms[boneIndex] = global;
            skinningPalette[boneIndex] = bone.InverseModelBindGlobalTransform * global;
        }
    }

    private static string CreateModelName(
        IReadOnlyList<XAnimPreviewModelComponent> components) =>
        string.Join(
            " + ",
            components.Select(component =>
                string.IsNullOrWhiteSpace(component.Model.Name)
                    ? "<unnamed XModel>"
                    : component.Model.Name));
}
