using System.Numerics;
using System.Collections.Immutable;
using IW4.Formats.XModel;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

internal static class DestructibleModelPreview
{
    internal static XModelSource Create(XModelSource model, IReadOnlyList<DestructiblePreviewPart> parts,
        ImmutableDictionary<int, DestructiblePartState>? states)
    {
        XModelExportDocument body = model.Document;
        foreach (DestructiblePreviewPart part in parts)
            foreach (string tag in new[] { part.IntactTag, part.DamagedTag })
                if (!body.Bones.Any(bone => bone.Name == tag))
                    throw new InvalidDataException($"The {model.Name} model is missing '{tag}'.");

        bool[] hidden = new bool[body.Bones.Count];
        for (int index = 0; index < hidden.Length; index++)
        {
            XModelExportBone bone = body.Bones[index];
            bool hide = bone.Name.EndsWith("_d", StringComparison.Ordinal);
            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                DestructiblePreviewPart part = parts[partIndex];
                if (bone.Name != part.IntactTag && bone.Name != part.DamagedTag) continue;
                DestructiblePartState state = states?.GetValueOrDefault(partIndex) ?? DestructiblePartState.Intact;
                hide = state switch
                {
                    DestructiblePartState.Intact => bone.Name == part.DamagedTag,
                    DestructiblePartState.Damaged => bone.Name == part.IntactTag,
                    DestructiblePartState.Broken => true,
                    _ => throw new InvalidDataException("Unknown part preview state.")
                };
                break;
            }
            hidden[index] = hide || bone.ParentIndex >= 0 && hidden[bone.ParentIndex];
        }

        XModelExportTriangle[] visible = body.Triangles.Where(triangle =>
            !(Hidden(triangle.First) && Hidden(triangle.Second) && Hidden(triangle.Third))).ToArray();
        if (visible.Length == 0)
            throw new InvalidDataException($"The {model.Name} model has no visible preview geometry.");
        return new XModelSource(model.Name, body with { Triangles = visible });

        bool Hidden(XModelExportCorner corner)
        {
            XModelExportVertex vertex = body.Vertices[corner.VertexIndex];
            if (vertex.Weights.Count == 0) return false;
            XModelExportBoneWeight dominant = vertex.Weights[0];
            foreach (XModelExportBoneWeight weight in vertex.Weights)
                if (weight.Weight > dominant.Weight) dominant = weight;
            return hidden[dominant.BoneIndex];
        }
    }

    internal static bool TryGetTagTransform(XModelSource model, string name, Matrix4x4 entityTransform,
        out Matrix4x4 transform)
    {
        foreach (XModelExportBone bone in model.Document.Bones)
            if (bone.Name == name)
            {
                transform = Matrix4x4.CreateFromQuaternion(bone.GlobalRotation) *
                    Matrix4x4.CreateTranslation(bone.GlobalOffset) * entityTransform;
                return true;
            }
        transform = default;
        return false;
    }
}
