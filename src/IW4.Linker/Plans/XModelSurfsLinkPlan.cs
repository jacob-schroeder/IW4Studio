using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>Frozen XModelSurfs provider and its PS3 surface-stream graph.</summary>
internal sealed class XModelSurfsLinkPlan : AssetLinkPlan
{
    private XModelSurfsLinkPlan(
        AssetKey key,
        string originalSerializedName,
        XModelSurfsAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        Root = CreateOwnedRoot(definition, freeze);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        XModelSurfsAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.XModelSurfs,
                originalSerializedName,
                freeze);
        }

        return new XModelSurfsLinkPlan(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static void ValidateReferenceShape(XModelSurfsAsset definition)
    {
        if (definition.NumSurfs != 0 || definition.Pad0A != 0 ||
            definition.Surfaces.Count != 0 ||
            definition.PartBits.Any(value => value != 0) ||
            definition.PartBits.Count is not (0 or 6))
        {
            throw new InvalidDataException(
                "A comma-prefixed XModelSurfs provider must have a zeroed reference body.");
        }
    }

    private LinkStorageSymbol CreateOwnedRoot(
        XModelSurfsAsset definition,
        LinkAssetFreezeScope freeze)
    {
        if (definition.PartBits.Count != 6)
            throw new InvalidDataException("XModelSurfs.PartBits requires exactly six words.");
        if (definition.Surfaces.Count > ushort.MaxValue)
            throw new InvalidDataException("XModelSurfs surface count exceeds UInt16.");
        bool hasSurfaceTable = definition.Surfaces.Count != 0 ||
            definition.SurfsPointer.Type != PointerType.Null;
        if (hasSurfaceTable && definition.NumSurfs != definition.Surfaces.Count)
        {
            throw new NotSupportedException(
                "XModelSurfs.NumSurfs must equal its retained surface rows when a surface table is present.");
        }

        LinkStorageTarget? surfaces = ResolveSurfaceTable(definition, freeze);
        var writer = new LinkTemplateWriter(XModelSurfsAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteUInt16(definition.NumSurfs);
        writer.WriteUInt16(definition.Pad0A);
        foreach (uint value in definition.PartBits)
            writer.WriteUInt32(value);

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => surfaces is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    Direct(root, 0x04, surfaces.Value, "XModelSurfs.Surfaces")
                ]);
    }

    private static LinkStorageTarget? ResolveSurfaceTable(
        XModelSurfsAsset definition,
        LinkAssetFreezeScope freeze)
    {
        XPointerReference pointer = definition.SurfsPointer.Untyped;
        int byteCount = checked(definition.Surfaces.Count * XSurface.SerializedSize);
        if (definition.Surfaces.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    "XModelSurfs cannot preserve a present-empty surface allocation.");
            }
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, "XModelSurfs.Surfaces");

        var children = new SurfaceChildren[definition.Surfaces.Count];
        var writer = new LinkTemplateWriter(byteCount);
        for (int index = 0; index < definition.Surfaces.Count; index++)
        {
            XSurface surface = definition.Surfaces[index] ?? throw new InvalidDataException(
                $"XModelSurfs.Surfaces[{index}] cannot be null.");
            children[index] = FreezeSurfaceChildren(
                surface,
                freeze,
                $"XModelSurfs.Surfaces[{index}]");
            WriteSurface(writer, surface, $"XModelSurfs.Surfaces[{index}]");
        }

        byte[] bytes = writer.Complete();
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => SurfaceOperations(owner, addend, children);
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                "XModelSurfs.Surfaces")
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                "XModelSurfs.Surfaces");
    }

    private static SurfaceChildren FreezeSurfaceChildren(
        XSurface surface,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        int blendCount = checked(
            surface.VertexInfo.Blend0 +
            surface.VertexInfo.Blend1 * 3 +
            surface.VertexInfo.Blend2 * 5 +
            surface.VertexInfo.Blend3 * 7);
        LinkStorageTarget? blend = ResolveUshorts(
            surface.VertexInfo.VertsBlendPointer.Untyped,
            surface.VertexInfo.VertsBlend,
            blendCount,
            XFileBlockType.LARGE,
            alignment: 2,
            freeze,
            $"{fieldPath}.VertexInfo.VertsBlend");
        LinkStorageTarget? verts0 = ResolveBytes(
            surface.Verts0Pointer.Untyped,
            surface.Verts0,
            checked(surface.VertCount * 0x10),
            (surface.StreamFlags & 0x01) == 0
                ? XFileBlockType.PHYSICAL
                : XFileBlockType.LARGE,
            alignment: 16,
            freeze,
            $"{fieldPath}.Verts0");
        LinkStorageTarget? verts1 = ResolveBytes(
            surface.Verts1Pointer.Untyped,
            surface.Verts1,
            checked(surface.VertCount * 0x10),
            (surface.StreamFlags & 0x02) == 0
                ? XFileBlockType.PHYSICAL
                : XFileBlockType.LARGE,
            alignment: 16,
            freeze,
            $"{fieldPath}.Verts1");
        LinkStorageTarget? rigid = ResolveRigidTable(surface, freeze, $"{fieldPath}.VertList");
        LinkStorageTarget? triangles = ResolveUshorts(
            surface.TriIndicesPointer.Untyped,
            surface.TriIndices,
            checked(surface.TriCount * 3),
            (surface.StreamFlags & 0x04) == 0
                ? XFileBlockType.PHYSICAL
                : XFileBlockType.LARGE,
            alignment: 16,
            freeze,
            $"{fieldPath}.TriIndices");

        if (surface.TriIndices.Any(index => index >= surface.VertCount))
            throw new InvalidDataException($"{fieldPath}.TriIndices contains an out-of-range vertex index.");
        if (surface.PartBits.Count != 6)
            throw new InvalidDataException($"{fieldPath}.PartBits requires exactly six words.");
        return new SurfaceChildren(blend, verts0, verts1, rigid, triangles);
    }

    private static IEnumerable<LinkOperation> SurfaceOperations(
        LinkStorageSymbol owner,
        int addend,
        IReadOnlyList<SurfaceChildren> children)
    {
        for (int index = 0; index < children.Count; index++)
        {
            int row = checked(addend + index * XSurface.SerializedSize);
            SurfaceChildren value = children[index];
            if (value.Blend is { } blend)
                yield return Direct(owner, row + 0x14, blend, $"XSurface[{index}].VertsBlend");
            if (value.Verts0 is { } verts0)
                yield return Direct(owner, row + 0x18, verts0, $"XSurface[{index}].Verts0");
            if (value.Verts1 is { } verts1)
                yield return Direct(owner, row + 0x24, verts1, $"XSurface[{index}].Verts1");
            if (value.Rigid is { } rigid)
                yield return Direct(owner, row + 0x34, rigid, $"XSurface[{index}].VertList");
            if (value.Triangles is { } triangles)
                yield return Direct(owner, row + 0x08, triangles, $"XSurface[{index}].TriIndices");
        }
    }

    private static LinkStorageTarget? ResolveRigidTable(
        XSurface surface,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (surface.VertListCount < 0)
            throw new InvalidDataException($"{fieldPath} count cannot be negative.");
        XPointerReference pointer = surface.VertListPointer.Untyped;
        int byteCount = checked(surface.VertListCount * XRigidVertList.SerializedSize);
        if (pointer.Type == PointerType.Offset &&
            surface.VertList.Count != surface.VertListCount)
        {
            return freeze.ResolveStorage(
                pointer,
                byteCount,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (surface.VertList.Count == 0)
        {
            if (surface.VertListCount != 0 || pointer.Type != PointerType.Null)
                throw new InvalidDataException($"{fieldPath} has no retained semantic rows.");
            return null;
        }
        if (surface.VertList.Count != surface.VertListCount)
            throw new InvalidDataException($"{fieldPath} count disagrees with VertListCount.");
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);

        var trees = new LinkStorageTarget?[surface.VertList.Count];
        var writer = new LinkTemplateWriter(byteCount);
        int rigidVertexCount = 0;
        for (int index = 0; index < surface.VertList.Count; index++)
        {
            XRigidVertList rigid = surface.VertList[index] ?? throw new InvalidDataException(
                $"{fieldPath}[{index}] cannot be null.");
            if ((rigid.BoneOffset & 0x3f) != 0)
                throw new InvalidDataException($"{fieldPath}[{index}].BoneOffset must be 64-byte aligned.");
            if ((int)rigid.TriOffset + rigid.TriCount > surface.TriCount)
                throw new InvalidDataException($"{fieldPath}[{index}] triangle range exceeds its surface.");
            rigidVertexCount = checked(rigidVertexCount + rigid.VertCount);
            trees[index] = ResolveTree(rigid, freeze, $"{fieldPath}[{index}].CollisionTree");
            writer.WriteUInt16(rigid.BoneOffset);
            writer.WriteUInt16(rigid.VertCount);
            writer.WriteUInt16(rigid.TriOffset);
            writer.WriteUInt16(rigid.TriCount);
            writer.Skip(sizeof(int));
        }
        if (rigidVertexCount > surface.VertCount)
            throw new InvalidDataException($"{fieldPath} rigid vertex counts exceed VertCount.");

        byte[] bytes = writer.Complete();
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => trees
                .Select((target, index) => (target, index))
                .Where(item => item.target is not null)
                .Select(item => Direct(
                    owner,
                    checked(addend + item.index * XRigidVertList.SerializedSize + 0x08),
                    item.target!.Value,
                    $"{fieldPath}[{item.index}].CollisionTree"));
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath);
    }

    private static LinkStorageTarget? ResolveTree(
        XRigidVertList rigid,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        XPointerReference pointer = rigid.CollisionTreePointer.Untyped;
        if (pointer.Type == PointerType.Offset && rigid.CollisionTree is null)
        {
            return freeze.ResolveStorage(
                pointer,
                XSurfaceCollisionTree.SerializedSize,
                XFileBlockType.LARGE,
                fieldPath);
        }
        if (rigid.CollisionTree is null)
        {
            if (pointer.Type != PointerType.Null)
                throw new InvalidDataException($"{fieldPath} has no retained semantic tree.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        XSurfaceCollisionTree tree = rigid.CollisionTree;
        if (tree.NodeCount < 0 || tree.NodeCount != tree.Nodes.Count ||
            tree.LeafCount < 0 || tree.LeafCount != tree.Leafs.Count)
        {
            throw new InvalidDataException($"{fieldPath} counts must equal retained node and leaf rows.");
        }

        LinkStorageTarget? nodes = ResolveNodes(tree, freeze, $"{fieldPath}.Nodes");
        LinkStorageTarget? leafs = ResolveLeafs(tree, freeze, $"{fieldPath}.Leafs");
        ValidateTree(tree, fieldPath);
        var writer = new LinkTemplateWriter(XSurfaceCollisionTree.SerializedSize);
        WriteVec3(writer, tree.Trans, $"{fieldPath}.Trans", allowPositiveInfinity: false);
        WriteVec3(writer, tree.Scale, $"{fieldPath}.Scale", allowPositiveInfinity: true);
        writer.WriteInt32(tree.NodeCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(tree.LeafCount);
        writer.Skip(sizeof(int));
        byte[] bytes = writer.Complete();
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => OrderedDirectOperations(
                owner,
                (addend + 0x1c, nodes, $"{fieldPath}.Nodes"),
                (addend + 0x24, leafs, $"{fieldPath}.Leafs"));
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                fieldPath);
    }

    private static LinkStorageTarget? ResolveNodes(
        XSurfaceCollisionTree tree,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        XPointerReference pointer = tree.NodesPointer.Untyped;
        int byteCount = checked(tree.NodeCount * XSurfaceCollisionNode.SerializedSize);
        if (pointer.Type == PointerType.Offset && tree.Nodes.Count != tree.NodeCount)
            return freeze.ResolveStorage(pointer, byteCount, XFileBlockType.LARGE, fieldPath);
        if (tree.Nodes.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException($"{fieldPath} cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        var writer = new LinkTemplateWriter(byteCount);
        foreach (XSurfaceCollisionNode node in tree.Nodes)
        {
            writer.WriteUInt16(node.Aabb.MinsX);
            writer.WriteUInt16(node.Aabb.MinsY);
            writer.WriteUInt16(node.Aabb.MinsZ);
            writer.WriteUInt16(node.Aabb.MaxsX);
            writer.WriteUInt16(node.Aabb.MaxsY);
            writer.WriteUInt16(node.Aabb.MaxsZ);
            writer.WriteUInt16(node.ChildBeginIndex);
            writer.WriteUInt16(node.ChildCount);
        }
        byte[] bytes = writer.Complete();
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 16,
                operations: null,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 16,
                operations: null,
                fieldPath);
    }

    private static LinkStorageTarget? ResolveLeafs(
        XSurfaceCollisionTree tree,
        LinkAssetFreezeScope freeze,
        string fieldPath) =>
        ResolveUshorts(
            tree.LeafsPointer.Untyped,
            tree.Leafs.Select(value => value.TriangleBeginIndex).ToArray(),
            tree.LeafCount,
            XFileBlockType.LARGE,
            alignment: 2,
            freeze,
            fieldPath);

    private static void ValidateTree(XSurfaceCollisionTree tree, string fieldPath)
    {
        foreach (XSurfaceCollisionNode node in tree.Nodes)
        {
            bool targetsLeafs = (node.ChildCount & 0x8000) != 0;
            int count = node.ChildCount & 0x7fff;
            int available = targetsLeafs ? tree.LeafCount : tree.NodeCount;
            if ((int)node.ChildBeginIndex + count > available)
                throw new InvalidDataException($"{fieldPath} contains an out-of-range child span.");
        }
    }

    private static LinkStorageTarget? ResolveBytes(
        XPointerReference pointer,
        IReadOnlyList<byte> values,
        int expectedCount,
        XFileBlockType block,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (pointer.Type == PointerType.Offset && values.Count != expectedCount)
            return freeze.ResolveStorage(pointer, expectedCount, block, fieldPath);
        if (values.Count != expectedCount)
            throw new InvalidDataException($"{fieldPath} requires exactly {expectedCount} bytes.");
        if (values.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException($"{fieldPath} cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        byte[] bytes = values.ToArray();
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                block,
                alignment,
                operations: null,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                block,
                alignment,
                operations: null,
                fieldPath);
    }

    private static LinkStorageTarget? ResolveUshorts<T>(
        XPointerReference pointer,
        IReadOnlyList<T> values,
        int expectedCount,
        XFileBlockType block,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        int byteCount = checked(expectedCount * sizeof(ushort));
        if (pointer.Type == PointerType.Offset && values.Count != expectedCount)
            return freeze.ResolveStorage(pointer, byteCount, block, fieldPath);
        if (values.Count != expectedCount)
            throw new InvalidDataException($"{fieldPath} requires exactly {expectedCount} UInt16 values.");
        if (values.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException($"{fieldPath} cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        var writer = new LinkTemplateWriter(byteCount);
        foreach (T value in values)
        {
            ushort raw = value switch
            {
                ushort typed => typed,
                XSurfaceCollisionLeaf typed => typed.TriangleBeginIndex,
                _ => throw new InvalidOperationException(
                    $"Unsupported UInt16 stream value {typeof(T).Name}.")
            };
            writer.WriteUInt16(raw);
        }
        byte[] bytes = writer.Complete();
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                block,
                alignment,
                operations: null,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                block,
                alignment,
                operations: null,
                fieldPath);
    }

    private static void WriteSurface(
        LinkTemplateWriter writer,
        XSurface surface,
        string fieldPath)
    {
        writer.WriteUInt16(surface.FlagsOrPad00);
        writer.WriteByte(surface.StreamFlags);
        writer.WriteByte(surface.Pad03);
        writer.WriteUInt16(surface.VertCount);
        writer.WriteUInt16(surface.TriCount);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(surface.VertexInfo.Blend0);
        writer.WriteUInt16(surface.VertexInfo.Blend1);
        writer.WriteUInt16(surface.VertexInfo.Blend2);
        writer.WriteUInt16(surface.VertexInfo.Blend3);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32(surface.Vb0.StreamSource);
        writer.WriteInt32(surface.Vb0.DataOffset);
        writer.Skip(sizeof(int));
        writer.WriteInt32(surface.Vb1.StreamSource);
        writer.WriteInt32(surface.Vb1.DataOffset);
        writer.WriteInt32(surface.VertListCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(surface.IndexBuffer.DataOffset);
        foreach (uint value in surface.PartBits)
            writer.WriteUInt32(value);
        if (writer.Position % XSurface.SerializedSize != 0)
            throw new InvalidOperationException($"{fieldPath} did not serialize a complete XSurface row.");
    }

    private static IEnumerable<LinkOperation> OrderedDirectOperations(
        LinkStorageSymbol owner,
        params (int Offset, LinkStorageTarget? Target, string Path)[] values)
    {
        foreach ((int offset, LinkStorageTarget? target, string path) in values)
        {
            if (target is { } value)
                yield return Direct(owner, offset, value, path);
        }
    }

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int offset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, offset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private static void RequireAuthoredOrInline(
        XPointerReference pointer,
        string fieldPath)
    {
        if (pointer.Type is not (PointerType.Null or PointerType.Inline))
            throw new NotSupportedException($"{fieldPath} uses unsupported source form {pointer.Type}.");
    }

    private static void WriteVec3(
        LinkTemplateWriter writer,
        Vec3 value,
        string fieldPath,
        bool allowPositiveInfinity)
    {
        WriteSingle(writer, value.X, $"{fieldPath}.X", allowPositiveInfinity);
        WriteSingle(writer, value.Y, $"{fieldPath}.Y", allowPositiveInfinity);
        WriteSingle(writer, value.Z, $"{fieldPath}.Z", allowPositiveInfinity);
    }

    private static void WriteSingle(
        LinkTemplateWriter writer,
        float value,
        string fieldPath,
        bool allowPositiveInfinity)
    {
        if (!float.IsFinite(value) && !(allowPositiveInfinity && float.IsPositiveInfinity(value)))
            throw new InvalidDataException($"{fieldPath} has an unsupported floating-point value.");
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));
    }

    private readonly record struct SurfaceChildren(
        LinkStorageTarget? Blend,
        LinkStorageTarget? Verts0,
        LinkStorageTarget? Verts1,
        LinkStorageTarget? Rigid,
        LinkStorageTarget? Triangles);
}
