using System.Numerics;
using Avalonia.Media.Imaging;
using IW4.Formats.XModel;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

/// <summary>Static bind-pose previews from the native PS3 bootstrap source bundle.</summary>
internal sealed class FactionModelPreview
{
    private readonly NativeModelPreviewAssets _assets;
    private readonly XModelPreviewRenderer _renderer;
    private readonly object _renderGate = new();
    private (string Body, string Head)? _appearanceNames;
    private XModelSource? _appearance;

    internal FactionModelPreview(string bootstrapRoot)
    {
        _assets = new NativeModelPreviewAssets(bootstrapRoot);
        _renderer = new XModelPreviewRenderer(_assets.ResolveTexture);
    }

    internal Bitmap Render(string bodyName, string? headName, int size,
        float yaw = -45, float pitch = 25, float zoom = 1, Vector2 pan = default)
    {
        lock (_renderGate)
        {
            XModelSource body = _assets.LoadSource(bodyName);
            XModelSource source = body;
            if (!string.IsNullOrWhiteSpace(headName))
            {
                if (_appearanceNames != (bodyName, headName) || _appearance is null)
                {
                    _appearance = Combine(body, _assets.LoadSource(headName));
                    _appearanceNames = (bodyName, headName);
                }
                source = _appearance;
            }
            return _renderer.Render(source, size, yaw, pitch, zoom, pan);
        }
    }

    internal Bitmap RenderHands(string handsName, int size,
        float yaw = -45, float pitch = 25, float zoom = 1, Vector2 pan = default)
    {
        lock (_renderGate)
            return _renderer.Render(_assets.LoadSource(handsName), size, yaw, pitch, zoom, pan);
    }

    private static XModelSource Combine(XModelSource body, XModelSource head)
    {
        XModelExportDocument baseDoc = body.Document, headDoc = head.Document;
        XModelExportBone[] headRoots = headDoc.Bones.Where(bone => bone.ParentIndex < 0).ToArray();
        if (headRoots.Length != 1)
            throw new InvalidDataException($"Head XModel '{head.Name}' needs one root bone for bind-pose attachment; found {headRoots.Length}.");
        XModelExportBone headRoot = headRoots[0];
        XModelExportBone[] bodyBones = baseDoc.Bones.Where(bone => bone.Name == headRoot.Name).ToArray();
        if (bodyBones.Length != 1)
            throw new InvalidDataException($"Body XModel '{body.Name}' needs one '{headRoot.Name}' attachment bone for head '{head.Name}'; found {bodyBones.Length}.");
        XModelExportBone bodyBone = bodyBones[0];
        Matrix4x4 headBind = Matrix4x4.CreateFromQuaternion(headRoot.GlobalRotation) *
            Matrix4x4.CreateTranslation(headRoot.GlobalOffset);
        Matrix4x4 bodyBind = Matrix4x4.CreateFromQuaternion(bodyBone.GlobalRotation) *
            Matrix4x4.CreateTranslation(bodyBone.GlobalOffset);
        if (!Matrix4x4.Invert(headBind, out Matrix4x4 inverseHead))
            throw new InvalidDataException($"Head XModel '{head.Name}' has a non-invertible root bind pose.");
        Matrix4x4 placement = inverseHead * bodyBind;
        int vertexOffset = baseDoc.Vertices.Count, materialOffset = baseDoc.Materials.Count,
            objectOffset = baseDoc.Objects.Count;
        XModelExportCorner Place(XModelExportCorner corner)
        {
            Vector3 normal = Vector3.TransformNormal(corner.Normal, placement);
            return corner with
            {
                VertexIndex = checked(corner.VertexIndex + vertexOffset),
                Normal = normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.Zero
            };
        }
        XModelExportTriangle[] triangles = headDoc.Triangles.Select(triangle => triangle with
        {
            ObjectIndex = checked(triangle.ObjectIndex + objectOffset),
            MaterialIndex = checked(triangle.MaterialIndex + materialOffset),
            First = Place(triangle.First), Second = Place(triangle.Second), Third = Place(triangle.Third)
        }).ToArray();
        var combined = new XModelExportDocument(baseDoc.Bones,
            baseDoc.Vertices.Concat(headDoc.Vertices.Select(vertex => vertex with
                { Position = Vector3.Transform(vertex.Position, placement) })).ToArray(),
            baseDoc.Triangles.Concat(triangles).ToArray(),
            baseDoc.Objects.Concat(headDoc.Objects).ToArray(),
            baseDoc.Materials.Concat(headDoc.Materials).ToArray());
        return new XModelSource($"{body.Name} + {head.Name}", combined);
    }
}
