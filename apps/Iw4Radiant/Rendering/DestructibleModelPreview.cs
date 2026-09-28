using System.Numerics;
using IW4.Formats.XModel;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

internal static class DestructibleModelPreview
{
    internal static XModelSource Create(XModelSource intact, XModelSource wreck,
        DestructiblePreviewSettings settings)
    {
        if (settings.FrontLeftTireFlat)
            throw new InvalidDataException("The front-left flat tire animation cannot be shown in the map model preview.");

        XModelExportDocument body = intact.Document;
        _ = wreck.Document;
        foreach (string tag in new[] { "tag_glass_front", "tag_glass_front_d", "tag_hood_fx", "tag_death_fx" })
            if (!body.Bones.Any(bone => bone.Name == tag))
                throw new InvalidDataException($"The police car model is missing '{tag}'.");

        if (settings.Appearance == DestructibleAppearance.Wreck)
            return wreck;

        bool[] hidden = new bool[body.Bones.Count];
        for (int index = 0; index < hidden.Length; index++)
        {
            XModelExportBone bone = body.Bones[index];
            bool windshield = bone.Name is "tag_glass_front" or "tag_glass_front_d";
            bool hide = windshield
                ? settings.Windshield switch
                {
                    DestructibleWindowState.Intact => bone.Name == "tag_glass_front_d",
                    DestructibleWindowState.Damaged => bone.Name == "tag_glass_front",
                    DestructibleWindowState.Broken => true,
                    _ => throw new InvalidDataException("Unknown windshield preview state.")
                }
                : bone.Name.EndsWith("_d", StringComparison.Ordinal);
            hidden[index] = hide || bone.ParentIndex >= 0 && hidden[bone.ParentIndex];
        }

        XModelExportTriangle[] visible = body.Triangles.Where(triangle =>
            !(Hidden(triangle.First) && Hidden(triangle.Second) && Hidden(triangle.Third))).ToArray();
        if (visible.Length == 0)
            throw new InvalidDataException("The police car model has no visible preview geometry.");
        return new XModelSource(intact.Name, body with { Triangles = visible });

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

    internal static bool TryGetTagTransform(XModelSource intact, string name, Matrix4x4 entityTransform,
        out Matrix4x4 transform)
    {
        foreach (XModelExportBone bone in intact.Document.Bones)
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
