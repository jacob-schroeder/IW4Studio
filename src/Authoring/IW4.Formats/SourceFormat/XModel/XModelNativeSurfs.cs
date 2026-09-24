using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Game.Assets.XModel;
using IW4.Game.Math;

namespace IW4.Formats.SourceFormat.XModel;

/// <summary>Lossless semantic surface streams for the PS3 XModelSurfs linker.</summary>
internal static class XModelNativeSurfs
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = true
    };

    internal static string PathFor(string name) => $"xmodelsurfs_native/{name}.json";

    internal static string Serialize(XModelSurfsAsset asset)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, "XModelSurfs");
        if (asset.PartBits.Count != 6 || asset.NumSurfs != asset.Surfaces.Count)
            throw new InvalidDataException($"XModelSurfs '{name}' has incomplete native surface rows.");
        NativeSurfs document = new(
            1, name, asset.NumSurfs, asset.Pad0A, asset.PartBits.ToArray(),
            asset.Surfaces.Select(ToSource).ToArray());
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    internal static XModelSurfsAsset Read(string sourceDirectory, string name)
    {
        name = SourceOutput.NormalizeOwnedAssetName(name, "XModelSurfs");
        string path = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(sourceDirectory), PathFor(name));
        NativeSurfs source = JsonSerializer.Deserialize<NativeSurfs>(
            File.ReadAllText(path), JsonOptions) ??
            throw new InvalidDataException($"XModelSurfs '{name}' has no native source body.");
        if (source.Version != 1 || source.Name != name ||
            source.PartBits?.Length != 6 || source.Surfaces is null ||
            source.NumSurfs != source.Surfaces.Length)
            throw new InvalidDataException($"XModelSurfs '{name}' has an invalid native source header.");
        return new XModelSurfsAsset
        {
            Name = name,
            NumSurfs = source.NumSurfs,
            Pad0A = source.Pad0A,
            PartBits = source.PartBits,
            Surfaces = source.Surfaces.Select(FromSource).ToArray()
        };
    }

    private static NativeSurface ToSource(XSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.VertListCount != surface.VertList.Count ||
            surface.TriIndices.Count != surface.TriCount * 3 ||
            surface.Verts0.Count != surface.VertCount * 16 ||
            surface.Verts1.Count != surface.VertCount * 16 ||
            surface.PartBits.Count != 6 ||
            surface.VertexInfo.VertsBlend.Count !=
                surface.VertexInfo.Blend0 + surface.VertexInfo.Blend1 * 3 +
                surface.VertexInfo.Blend2 * 5 + surface.VertexInfo.Blend3 * 7)
            throw new InvalidDataException("XSurface has incomplete native geometry streams.");
        foreach (XRigidVertList rigid in surface.VertList)
        {
            if (rigid.CollisionTree is null && rigid.CollisionTreePointer.Value != 0)
                throw new InvalidDataException("XSurface has an unresolved collision-tree pointer.");
            if (rigid.CollisionTree is { } tree &&
                (tree.Nodes.Count != tree.NodeCount || tree.Leafs.Count != tree.LeafCount))
                throw new InvalidDataException("XSurface has incomplete collision-tree rows.");
        }
        return new NativeSurface(
            surface.TileMode, surface.DeformedRaw, surface.StreamFlags,
            surface.Pad03, surface.VertCount, surface.TriCount,
            surface.TriIndices.ToArray(),
            new NativeVertexInfo(
                surface.VertexInfo.Blend0, surface.VertexInfo.Blend1,
                surface.VertexInfo.Blend2, surface.VertexInfo.Blend3,
                surface.VertexInfo.VertsBlend.ToArray()),
            surface.Verts0.ToArray(), surface.Vb0.StreamSource, surface.Vb0.DataOffset,
            surface.Verts1.ToArray(), surface.Vb1.StreamSource, surface.Vb1.DataOffset,
            surface.VertList.Select(rigid => new NativeRigid(
                rigid.BoneOffset, rigid.VertCount, rigid.TriOffset, rigid.TriCount,
                rigid.CollisionTree is null ? null : new NativeTree(
                    rigid.CollisionTree.Trans, rigid.CollisionTree.Scale,
                    rigid.CollisionTree.Nodes.ToArray(), rigid.CollisionTree.Leafs.ToArray()))).ToArray(),
            surface.IndexBuffer.DataOffset, surface.PartBits.ToArray());
    }

    private static XSurface FromSource(NativeSurface source)
    {
        if (source.TriIndices is null || source.VertexInfo is null ||
            source.VertexInfo.VertsBlend is null || source.Verts0 is null ||
            source.Verts1 is null || source.VertList is null ||
            source.PartBits?.Length != 6 ||
            source.TriIndices.Length != source.TriCount * 3 ||
            source.Verts0.Length != source.VertCount * 16 ||
            source.Verts1.Length != source.VertCount * 16 ||
            source.VertexInfo.VertsBlend.Length !=
                source.VertexInfo.Blend0 + source.VertexInfo.Blend1 * 3 +
                source.VertexInfo.Blend2 * 5 + source.VertexInfo.Blend3 * 7)
            throw new InvalidDataException("XSurface has incomplete native geometry streams.");
        return new XSurface
        {
            TileMode = source.TileMode,
            DeformedRaw = source.DeformedRaw,
            StreamFlags = source.StreamFlags,
            Pad03 = source.Pad03,
            VertCount = source.VertCount,
            TriCount = source.TriCount,
            TriIndices = source.TriIndices,
            VertexInfo = new XSurfaceVertexInfo
            {
                Blend0 = source.VertexInfo.Blend0,
                Blend1 = source.VertexInfo.Blend1,
                Blend2 = source.VertexInfo.Blend2,
                Blend3 = source.VertexInfo.Blend3,
                VertsBlend = source.VertexInfo.VertsBlend
            },
            Verts0 = source.Verts0,
            Vb0 = new GfxVertexBuffer { StreamSource = source.Vb0StreamSource, DataOffset = source.Vb0DataOffset },
            Verts1 = source.Verts1,
            Vb1 = new GfxVertexBuffer { StreamSource = source.Vb1StreamSource, DataOffset = source.Vb1DataOffset },
            VertListCount = source.VertList.Length,
            VertList = source.VertList.Select(FromSource).ToArray(),
            IndexBuffer = new GfxIndexBuffer { DataOffset = source.IndexDataOffset },
            PartBits = source.PartBits
        };
    }

    private static XRigidVertList FromSource(NativeRigid source)
    {
        XSurfaceCollisionTree? tree = source.CollisionTree is null ? null : new XSurfaceCollisionTree
        {
            Trans = source.CollisionTree.Trans,
            Scale = source.CollisionTree.Scale,
            NodeCount = source.CollisionTree.Nodes?.Length ?? throw new InvalidDataException("Collision tree nodes are missing."),
            Nodes = source.CollisionTree.Nodes,
            LeafCount = source.CollisionTree.Leafs?.Length ?? throw new InvalidDataException("Collision tree leaves are missing."),
            Leafs = source.CollisionTree.Leafs
        };
        return new XRigidVertList
        {
            BoneOffset = source.BoneOffset,
            VertCount = source.VertCount,
            TriOffset = source.TriOffset,
            TriCount = source.TriCount,
            CollisionTree = tree
        };
    }

    private sealed record NativeSurfs(
        int Version, string Name, ushort NumSurfs, ushort Pad0A,
        uint[] PartBits, NativeSurface[] Surfaces);

    private sealed record NativeSurface(
        XSurfaceTileMode TileMode, byte DeformedRaw, XSurfaceStreamFlags StreamFlags,
        byte Pad03, ushort VertCount, ushort TriCount, ushort[] TriIndices,
        NativeVertexInfo VertexInfo, byte[] Verts0, int Vb0StreamSource,
        int Vb0DataOffset, byte[] Verts1, int Vb1StreamSource, int Vb1DataOffset,
        NativeRigid[] VertList, int IndexDataOffset, uint[] PartBits);

    private sealed record NativeVertexInfo(
        ushort Blend0, ushort Blend1, ushort Blend2, ushort Blend3,
        ushort[] VertsBlend);

    private sealed record NativeRigid(
        ushort BoneOffset, ushort VertCount, ushort TriOffset, ushort TriCount,
        NativeTree? CollisionTree);

    private sealed record NativeTree(
        Vec3 Trans, Vec3 Scale, XSurfaceCollisionNode[] Nodes,
        XSurfaceCollisionLeaf[] Leafs);
}
