using System.Numerics;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Formats.XModel;
using IW4.Render.EditorPreview;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

/// <summary>Prepared, in-place Rangers hands and Beretta idle viewmodel for Walk.</summary>
internal sealed class WalkPlayerPreview
{
    private const string HandModelName = "viewhands_us_army";
    private const string WeaponName = "beretta_mp";
    private readonly XAnimPlaybackClip _clip;
    private readonly XAnimPreviewScene _scene;
    private readonly int _viewBoneIndex;
    private readonly Vector3 _viewBindPosition;
    private readonly WalkVertex[] _vertices;
    private readonly SceneVertex[] _sampledVertices;
    private readonly IReadOnlyList<(string Material, int Start, int Count)> _batches;
    private readonly IReadOnlyDictionary<string, (int Width, int Height, byte[] Pixels,
        IW4.Formats.SourceFormat.Material.MaterialSurfaceState Surface)> _textures;

    private WalkPlayerPreview(XAnimPlaybackClip clip, XAnimPreviewScene scene, int viewBoneIndex, Vector3 viewBindPosition,
        WalkVertex[] vertices, IReadOnlyList<(string Material, int Start, int Count)> batches,
        IReadOnlyDictionary<string, (int Width, int Height, byte[] Pixels,
            IW4.Formats.SourceFormat.Material.MaterialSurfaceState Surface)> textures)
    {
        _clip = clip;
        _scene = scene;
        _viewBoneIndex = viewBoneIndex;
        _viewBindPosition = viewBindPosition;
        _vertices = vertices;
        _sampledVertices = new SceneVertex[vertices.Length];
        _batches = batches;
        _textures = textures;
    }

    internal int VertexCount => _vertices.Length;
    internal IReadOnlyList<(string Material, int Start, int Count)> Batches => _batches;
    internal (int Width, int Height, byte[] Pixels,
        IW4.Formats.SourceFormat.Material.MaterialSurfaceState Surface) Texture(string material) => _textures[material];

    internal static WalkPlayerPreview Load(string bootstrapRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapRoot);
        string root = Path.GetFullPath(bootstrapRoot);
        var (gunName, idleName, hiddenTags) = ReadWeapon(Path.Combine(root, "weapons", WeaponName));
        var assets = new NativeModelPreviewAssets(root);
        XModelSource hands = assets.LoadSource(HandModelName);
        XModelSource gun = assets.LoadSource(gunName);
        var components = new XAnimPreviewModelComponent[]
        {
            new(assets.LoadModel(HandModelName).Model),
            new(assets.LoadModel(gunName).Model, "tag_weapon")
        };
        XAnimPlaybackClip clip = new XAnimExchange().Read(root, idleName);
        if (!XAnimPreviewScene.TryCreate(clip, components, out XAnimPreviewScene? scene, out string reason) || scene is null)
            throw new InvalidDataException($"Beretta idle viewmodel cannot be composed: {reason}");
        int expectedBoneCount = hands.Document.Bones.Count + gun.Document.Bones.Count;
        if (scene.BoneCount != expectedBoneCount)
            throw new InvalidDataException("Beretta idle viewmodel skeleton does not match projected geometry.");
        int viewBoneIndex = -1;
        for (int index = 0; index < hands.Document.Bones.Count; index++)
            if (hands.Document.Bones[index].Name == "tag_view") viewBoneIndex = index;
        if (viewBoneIndex < 0)
            throw new InvalidDataException("The player hands model has no tag_view camera anchor.");

        var byMaterial = new Dictionary<string, List<WalkVertex>>(StringComparer.Ordinal);
        AddModel(hands.Document, 0, new HashSet<string>(StringComparer.Ordinal), byMaterial);
        AddModel(gun.Document, hands.Document.Bones.Count, hiddenTags, byMaterial);
        var vertices = new List<WalkVertex>();
        var batches = new List<(string Material, int Start, int Count)>();
        var textures = new Dictionary<string, (int Width, int Height, byte[] Pixels,
            IW4.Formats.SourceFormat.Material.MaterialSurfaceState Surface)>(StringComparer.Ordinal);
        foreach (var (material, materialVertices) in byMaterial)
        {
            textures.Add(material, assets.ResolveTexture(material));
            batches.Add((material, vertices.Count, materialVertices.Count));
            vertices.AddRange(materialVertices);
        }
        if (vertices.Count == 0)
            throw new InvalidDataException("Beretta idle viewmodel has no visible triangles.");
        return new WalkPlayerPreview(clip, scene, viewBoneIndex, hands.Document.Bones[viewBoneIndex].GlobalOffset,
            vertices.ToArray(), batches, textures);
    }

    internal SceneVertex[] Sample(double seconds, out Vector3 viewOrigin)
    {
        if (!double.IsFinite(seconds)) seconds = 0;
        float frame = _clip.NumFrames > 0
            ? (float)((Math.Max(0, seconds) * _clip.Framerate) % _clip.NumFrames)
            : 0;
        IReadOnlyList<Matrix4x4> palette = _scene.Sample(frame).SkinningPalette;
        // The rig's camera is above its model origin. Use the same sampled pose
        // as the geometry so first-person framing stays relative to tag_view.
        viewOrigin = Vector3.Transform(_viewBindPosition, palette[_viewBoneIndex]);
        for (int index = 0; index < _vertices.Length; index++)
        {
            WalkVertex vertex = _vertices[index];
            Vector3 position = Vector3.Zero, normal = Vector3.Zero;
            foreach (XModelExportBoneWeight weight in vertex.Weights)
            {
                int bone = checked(vertex.BoneOffset + weight.BoneIndex);
                Matrix4x4 transform = palette[bone];
                position += Vector3.Transform(vertex.BindPosition, transform) * weight.Weight;
                normal += Vector3.TransformNormal(vertex.BindNormal, transform) * weight.Weight;
            }
            if (normal.LengthSquared() > 0.000001f) normal = Vector3.Normalize(normal);
            _sampledVertices[index] = new SceneVertex(position, normal, vertex.Uv, vertex.Color);
        }
        return _sampledVertices;
    }

    private static void AddModel(XModelExportDocument model, int boneOffset,
        IReadOnlySet<string> hiddenTags, Dictionary<string, List<WalkVertex>> byMaterial)
    {
        bool[] hiddenBones = new bool[model.Bones.Count];
        for (int index = 0; index < model.Bones.Count; index++)
        {
            int parent = model.Bones[index].ParentIndex;
            hiddenBones[index] = hiddenTags.Contains(model.Bones[index].Name) ||
                parent >= 0 && hiddenBones[parent];
        }
        foreach (XModelExportTriangle triangle in model.Triangles)
        {
            if (hiddenTags.Count != 0 &&
                IsHidden(triangle.First) && IsHidden(triangle.Second) && IsHidden(triangle.Third))
                continue;
            string material = model.Materials[triangle.MaterialIndex].Name;
            if (!byMaterial.TryGetValue(material, out List<WalkVertex>? vertices))
                byMaterial.Add(material, vertices = []);
            // XMODEL_EXPORT triangles are clockwise-front in the Radiant coordinate frame.
            Add(triangle.First);
            Add(triangle.Third);
            Add(triangle.Second);

            bool IsHidden(XModelExportCorner corner)
            {
                XModelExportVertex vertex = model.Vertices[corner.VertexIndex];
                if (vertex.Weights.Count == 0) return false;
                XModelExportBoneWeight dominant = vertex.Weights[0];
                foreach (XModelExportBoneWeight weight in vertex.Weights)
                    if (weight.Weight > dominant.Weight) dominant = weight;
                return (uint)dominant.BoneIndex < (uint)hiddenBones.Length && hiddenBones[dominant.BoneIndex];
            }

            void Add(XModelExportCorner corner)
            {
                XModelExportVertex vertex = model.Vertices[corner.VertexIndex];
                if (vertex.Weights.Count == 0)
                    throw new InvalidDataException("Beretta idle viewmodel has a vertex without skinning weights.");
                foreach (XModelExportBoneWeight weight in vertex.Weights)
                    if ((uint)weight.BoneIndex >= (uint)model.Bones.Count ||
                        !float.IsFinite(weight.Weight) || weight.Weight < 0)
                        throw new InvalidDataException("Beretta idle viewmodel has an invalid skinning weight.");
                vertices.Add(new WalkVertex(vertex.Position, corner.Normal, corner.Uv0,
                    corner.Color, vertex.Weights, boneOffset));
            }
        }
    }

    private static (string Gun, string Idle, IReadOnlySet<string> HiddenTags) ReadWeapon(string path)
    {
        string[] parts = File.ReadAllText(path).Split('\\');
        if (parts.Length < 3 || parts[0] != "WEAPONFILE" || parts.Length % 2 != 1)
            throw new InvalidDataException($"Weapon source '{path}' is not a complete WEAPONFILE info string.");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < parts.Length; index += 2)
            if (!fields.TryAdd(parts[index], parts[index + 1]))
                throw new InvalidDataException($"Weapon source '{path}' repeats field '{parts[index]}'.");
        string Required(string key)
        {
            if (!fields.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value) ||
                value.Contains('/') || value.Contains('\\') || value is "." or "..")
                throw new InvalidDataException($"Weapon source '{path}' has no valid '{key}' asset name.");
            return value;
        }
        string[] hidden = fields.GetValueOrDefault("hideTags", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (Required("gunModel"), Required("idleAnim"),
            new HashSet<string>(hidden, StringComparer.Ordinal));
    }

    private readonly record struct WalkVertex(Vector3 BindPosition, Vector3 BindNormal, Vector2 Uv,
        Vector4 Color, IReadOnlyList<XModelExportBoneWeight> Weights, int BoneOffset);
}
