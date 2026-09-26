using System.Numerics;
using Avalonia.Media.Imaging;
using IW4.Formats.XModel;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

/// <summary>Static bind-pose previews from the native PS3 bootstrap source bundle.</summary>
internal sealed class FactionModelPreview
{
    internal sealed record Frame(Bitmap Bitmap, IReadOnlyList<int> AvailableLods, int Lod,
        int Triangles, int Vertices);

    private readonly NativeModelPreviewAssets _assets;
    private readonly XModelPreviewRenderer _renderer;
    private readonly string _bootstrapRoot;
    private readonly Dictionary<string, (NativeModelPreviewAssets Assets, XModelPreviewRenderer Renderer)> _custom =
        new(StringComparer.Ordinal);
    private readonly object _renderGate = new();
    private (string? Root, string Body, string Head, int BodyLod, int HeadLod)? _appearanceNames;
    private XModelSource? _appearance;
    private (string? Root, string Body, string Head)? _framingNames;
    private XModelSource? _framingAppearance;

    internal FactionModelPreview(string bootstrapRoot)
    {
        _bootstrapRoot = bootstrapRoot;
        _assets = new NativeModelPreviewAssets(bootstrapRoot);
        _renderer = new XModelPreviewRenderer(_assets.ResolveTexture);
    }

    private (NativeModelPreviewAssets Assets, XModelPreviewRenderer Renderer) AssetSet(string? customRoot)
    {
        if (customRoot is null) return (_assets, _renderer);
        if (_custom.TryGetValue(customRoot, out var cached)) return cached;
        var assets = new NativeModelPreviewAssets(_bootstrapRoot, customRoot);
        cached = (assets, new XModelPreviewRenderer(assets.ResolveTexture));
        _custom.Add(customRoot, cached);
        return cached;
    }

    internal Frame Render(string bodyName, string? headName, int size,
        float yaw = -45, float pitch = 25, float zoom = 1, Vector2 pan = default,
        string? customRoot = null, int lod = 0)
    {
        lock (_renderGate)
        {
            var (assets, renderer) = AssetSet(customRoot);
            IReadOnlyList<int> available = assets.AvailableLods(bodyName);
            if (available.Count == 0)
                throw new InvalidDataException($"Native XModel '{bodyName}' has no previewable LOD geometry.");
            int framingLod = available.Contains(0) ? 0 : available[0];
            lod = available.Contains(lod) ? lod : framingLod;
            XModelSource body = assets.LoadSource(bodyName, lod);
            XModelSource source = body;
            XModelSource framingSource = lod == framingLod ? body : assets.LoadSource(bodyName, framingLod);
            if (!string.IsNullOrWhiteSpace(headName))
            {
                IReadOnlyList<int> headLods = assets.AvailableLods(headName);
                if (headLods.Count == 0)
                    throw new InvalidDataException($"Native XModel '{headName}' has no previewable LOD geometry.");
                int headLod = headLods.Where(index => index <= lod).DefaultIfEmpty(headLods[0]).Max();
                if (_appearanceNames != (customRoot, bodyName, headName, lod, headLod) || _appearance is null)
                {
                    _appearance = Combine(body, assets.LoadSource(headName, headLod));
                    _appearanceNames = (customRoot, bodyName, headName, lod, headLod);
                }
                source = _appearance;
                if (_framingNames != (customRoot, bodyName, headName) || _framingAppearance is null)
                {
                    int framingHeadLod = headLods.Where(index => index <= framingLod)
                        .DefaultIfEmpty(headLods[0]).Max();
                    _framingAppearance = lod == framingLod && headLod == framingHeadLod
                        ? source
                        : Combine(framingSource, assets.LoadSource(headName, framingHeadLod));
                    _framingNames = (customRoot, bodyName, headName);
                }
                framingSource = _framingAppearance;
            }
            return RenderFrame(renderer, source, framingSource, available, lod, size, yaw, pitch, zoom, pan);
        }
    }

    internal Frame RenderHands(string handsName, int size,
        float yaw = -45, float pitch = 25, float zoom = 1, Vector2 pan = default,
        string? customRoot = null, int lod = 0)
    {
        lock (_renderGate)
        {
            var (assets, renderer) = AssetSet(customRoot);
            IReadOnlyList<int> available = assets.AvailableLods(handsName);
            if (available.Count == 0)
                throw new InvalidDataException($"Native XModel '{handsName}' has no previewable LOD geometry.");
            int framingLod = available.Contains(0) ? 0 : available[0];
            lod = available.Contains(lod) ? lod : framingLod;
            return RenderFrame(renderer, assets.LoadSource(handsName, lod), assets.LoadSource(handsName, framingLod), available, lod,
                size, yaw, pitch, zoom, pan);
        }
    }

    private static Frame RenderFrame(XModelPreviewRenderer renderer, XModelSource source, XModelSource framingSource,
        IReadOnlyList<int> available, int lod, int size, float yaw, float pitch, float zoom, Vector2 pan)
    {
        XModelExportDocument document = source.Document;
        return new Frame(renderer.Render(source, size, yaw, pitch, zoom, pan, framingSource), available, lod,
            document.Triangles.Count, document.Vertices.Count);
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
