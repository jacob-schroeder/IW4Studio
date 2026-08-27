using System.Numerics;
using IW4.AssetExchange.XModel;
using IW4.Assets.Assets.XModel;

namespace IW4.Render.EditorPreview;

/// <summary>
/// One ordered XModel in an animation-preview composition. The first component
/// is the untagged root; every later component attaches its root bones to a
/// named bone in an earlier component.
/// </summary>
public sealed record XAnimPreviewModelComponent
{
    public XAnimPreviewModelComponent(
        XModelAsset model,
        string? attachmentBoneName = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        AttachmentBoneName = string.IsNullOrWhiteSpace(attachmentBoneName)
            ? null
            : attachmentBoneName;
    }

    public XModelAsset Model { get; }

    public string? AttachmentBoneName { get; }
}

internal sealed class XAnimPreviewComposition
{
    private XAnimPreviewComposition(
        IReadOnlyList<XAnimPreviewCompositionComponent> components,
        IReadOnlyList<XAnimPreviewCompositionBone> bones)
    {
        Components = components;
        Bones = bones;
    }

    internal IReadOnlyList<XAnimPreviewCompositionComponent> Components
    {
        get;
    }

    internal IReadOnlyList<XAnimPreviewCompositionBone> Bones { get; }

    internal static bool TryProject(
        IReadOnlyList<XAnimPreviewModelComponent> components,
        out XAnimPreviewComposition? composition,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(components);
        composition = null;
        if (components.Count == 0)
        {
            reason = "An XAnim preview composition requires at least one XModel.";
            return false;
        }
        if (components[0] is null)
        {
            reason = "XAnim preview component 0 is null.";
            return false;
        }
        if (components[0].AttachmentBoneName is not null)
        {
            reason = "The first XAnim preview component must be an untagged root model.";
            return false;
        }

        var projectedComponents = new List<XAnimPreviewCompositionComponent>(
            components.Count);
        var combinedBones = new List<XAnimPreviewCompositionBone>();
        for (int componentIndex = 0;
             componentIndex < components.Count;
             componentIndex++)
        {
            XAnimPreviewModelComponent? component = components[componentIndex];
            if (component is null)
            {
                reason = $"XAnim preview component {componentIndex} is null.";
                return false;
            }
            if (componentIndex > 0 &&
                component.AttachmentBoneName is null)
            {
                reason =
                    $"XAnim preview component {componentIndex} requires an attachment bone.";
                return false;
            }
            if (!XModelExportSkeletonProjector.TryProject(
                    component.Model,
                    out IReadOnlyList<XModelExportBone> modelBones,
                    out IReadOnlyList<string> blockers))
            {
                reason = blockers.FirstOrDefault() ??
                    $"Component {componentIndex} skeleton could not be projected.";
                return false;
            }
            var modelBoneNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (XModelExportBone modelBone in modelBones)
            {
                if (!modelBoneNames.Add(modelBone.Name))
                {
                    reason =
                        $"The XModel contains more than one bone named '{modelBone.Name}'.";
                    return false;
                }
            }

            int attachmentBoneIndex = -1;
            Matrix4x4 attachmentBindTransform = Matrix4x4.Identity;
            if (component.AttachmentBoneName is { } attachmentBoneName)
            {
                attachmentBoneIndex = FindAttachmentBone(
                    projectedComponents,
                    attachmentBoneName);
                if (attachmentBoneIndex < 0)
                {
                    string modelName = DisplayName(component.Model);
                    reason =
                        $"Attachment bone '{attachmentBoneName}' for '{modelName}' was not found in a preceding component.";
                    return false;
                }
                attachmentBindTransform =
                    combinedBones[attachmentBoneIndex].BindGlobalTransform;
            }

            int boneOffset = combinedBones.Count;
            var componentBones = new XAnimPreviewCompositionBone[
                modelBones.Count];
            for (int boneIndex = 0;
                 boneIndex < modelBones.Count;
                 boneIndex++)
            {
                XModelExportBone modelBone = modelBones[boneIndex];
                if (!TryProjectBone(
                        modelBone,
                        modelBones,
                        attachmentBoneIndex,
                        attachmentBindTransform,
                        boneOffset,
                        out XAnimPreviewCompositionBone projectedBone,
                        out reason))
                {
                    return false;
                }

                componentBones[boneIndex] = projectedBone;
                combinedBones.Add(projectedBone);
            }

            projectedComponents.Add(new XAnimPreviewCompositionComponent(
                component,
                boneOffset,
                Array.AsReadOnly(componentBones),
                attachmentBindTransform));
        }

        composition = new XAnimPreviewComposition(
            Array.AsReadOnly(projectedComponents.ToArray()),
            Array.AsReadOnly(combinedBones.ToArray()));
        reason = string.Empty;
        return true;
    }

    private static int FindAttachmentBone(
        IReadOnlyList<XAnimPreviewCompositionComponent> components,
        string attachmentBoneName)
    {
        for (int componentIndex = components.Count - 1;
             componentIndex >= 0;
             componentIndex--)
        {
            XAnimPreviewCompositionComponent component =
                components[componentIndex];
            for (int boneIndex = 0;
                 boneIndex < component.Bones.Count;
                 boneIndex++)
            {
                if (string.Equals(
                        component.Bones[boneIndex].Name,
                        attachmentBoneName,
                        StringComparison.Ordinal))
                {
                    return checked(component.BoneOffset + boneIndex);
                }
            }
        }

        return -1;
    }

    private static bool TryProjectBone(
        XModelExportBone modelBone,
        IReadOnlyList<XModelExportBone> modelBones,
        int attachmentBoneIndex,
        Matrix4x4 attachmentBindTransform,
        int boneOffset,
        out XAnimPreviewCompositionBone projected,
        out string reason)
    {
        Quaternion modelGlobalRotation = Normalize(modelBone.GlobalRotation);
        Matrix4x4 modelGlobal =
            Matrix4x4.CreateFromQuaternion(modelGlobalRotation);
        modelGlobal.Translation = modelBone.GlobalOffset;
        if (!Matrix4x4.Invert(
                modelGlobal,
                out Matrix4x4 inverseModelBindGlobal))
        {
            projected = default!;
            reason =
                $"Bone '{modelBone.Name}' has a non-invertible bind transform.";
            return false;
        }

        Matrix4x4 local = modelGlobal;
        int parentIndex = attachmentBoneIndex;
        if (modelBone.ParentIndex >= 0)
        {
            if ((uint)modelBone.ParentIndex >= (uint)modelBones.Count ||
                !TryCreateModelGlobal(
                    modelBones[modelBone.ParentIndex],
                    out Matrix4x4 modelParentGlobal) ||
                !Matrix4x4.Invert(
                    modelParentGlobal,
                    out Matrix4x4 inverseParent))
            {
                projected = default!;
                reason =
                    $"Bone '{modelBone.Name}' has an invalid parent transform.";
                return false;
            }

            local = modelGlobal * inverseParent;
            parentIndex = checked(boneOffset + modelBone.ParentIndex);
        }

        Vector3 localPosition = local.Translation;
        Quaternion localRotation = Normalize(
            Quaternion.CreateFromRotationMatrix(local));
        Matrix4x4 bindGlobal = modelGlobal * attachmentBindTransform;
        if (!IsFinite(localPosition) ||
            !IsFinite(localRotation) ||
            !IsFinite(bindGlobal))
        {
            projected = default!;
            reason = $"Bone '{modelBone.Name}' has a non-finite bind transform.";
            return false;
        }

        projected = new XAnimPreviewCompositionBone(
            modelBone.Name,
            parentIndex,
            localPosition,
            localRotation,
            inverseModelBindGlobal,
            bindGlobal);
        reason = string.Empty;
        return true;
    }

    private static bool TryCreateModelGlobal(
        XModelExportBone bone,
        out Matrix4x4 transform)
    {
        Quaternion rotation = Normalize(bone.GlobalRotation);
        transform = Matrix4x4.CreateFromQuaternion(rotation);
        transform.Translation = bone.GlobalOffset;
        return IsFinite(transform);
    }

    private static string DisplayName(XModelAsset model) =>
        string.IsNullOrWhiteSpace(model.Name)
            ? "<unnamed XModel>"
            : model.Name;

    private static Quaternion Normalize(Quaternion value)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) ||
            lengthSquared <= float.Epsilon)
        {
            return Quaternion.Identity;
        }

        float inverseLength = 1f / MathF.Sqrt(lengthSquared);
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

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) &&
        float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) &&
        float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) &&
        float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) &&
        float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) &&
        float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) &&
        float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) &&
        float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) &&
        float.IsFinite(value.M44);
}

internal sealed record XAnimPreviewCompositionComponent(
    XAnimPreviewModelComponent Source,
    int BoneOffset,
    IReadOnlyList<XAnimPreviewCompositionBone> Bones,
    Matrix4x4 AttachmentBindTransform);

internal sealed record XAnimPreviewCompositionBone(
    string Name,
    int ParentIndex,
    Vector3 BindLocalPosition,
    Quaternion BindLocalRotation,
    Matrix4x4 InverseModelBindGlobalTransform,
    Matrix4x4 BindGlobalTransform);
