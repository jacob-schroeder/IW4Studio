using IW4.Game.Assets.XModel;
using IW4.Game.IO;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using ModelVec3 = IW4.Game.Math.Vec3;
using XModelSurfsAssetModel = IW4.Game.Assets.XModel.XModelSurfsAsset;
using XString = IW4.Game.Pointers.XPointer<string>;
using static IW4.Loaders.Assets.XModel.XModelPayloadReader;

namespace IW4.Loaders.Assets.XModel;

public sealed class XModelSurfsLoader : XAssetLoader<XModelSurfsAssetModel>
{
    private const int XModelSurfsSize = 0x24;
    private const int XSurfaceSize = 0x54;
    private const int XRigidVertListSize = 0x0c;
    private const int XSurfaceCollisionTreeSize = 0x28;
    private readonly ushort? _lodNumSurfs;

    // Nested loads retain the owning LOD's count; standalone assets use
    // the count in their own serialized header.
    public XModelSurfsLoader(ushort? lodNumSurfs = null)
        : base(XAssetType.XModelSurfs, XModelSurfsSize, "XModelSurfs")
    {
        _lodNumSurfs = lodNumSurfs;
    }

    // Copy only the 0x24-byte header into TEMP; the XSurface array and its
    // children are materialized in LARGE.
    protected override XModelSurfsAssetModel ReadBody(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, XModelSurfsSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
            throw new InvalidDataException($"XModelSurfs pointer patched to {rootAddress}, but Load_Stream wrote its root at {loadedAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XString namePointer = ReadXStringPointer(rootCursor, context);
        XPointer<byte[]> surfsPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        ushort numSurfs = rootCursor.ReadUInt16();
        ushort pad0A = rootCursor.ReadUInt16();
        var partBits = new uint[6];
        for (int i = 0; i < partBits.Length; i++)
            partBits[i] = rootCursor.ReadUInt32();

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            string? name = ReadXString(cursor, namePointer, context);
            // Nested bodies use the owning XModelLodInfo count exactly as the
            // native walk does. Canonical top-level XModelSurfs rows emitted by
            // the linker are self-contained and use their validated header
            // count.
            IReadOnlyList<XSurface> surfaces = ReadXSurfaceArray(
                cursor,
                surfsPointer.Untyped,
                _lodNumSurfs ?? numSurfs,
                context);

            return new XModelSurfsAssetModel
            {
                Offset = sourceOffset,
                RuntimeAddress = rootAddress,
                NamePointer = namePointer,
                Name = name,
                SurfsPointer = surfsPointer,
                NumSurfs = numSurfs,
                Pad0A = pad0A,
                PartBits = partBits,
                Surfaces = surfaces
            };
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private IReadOnlyList<XSurface> ReadXSurfaceArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count <= 0 || pointer.Type == PointerType.Null)
            return [];

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XSurface[]>(pointer, checked(count * XSurfaceSize), "XSurface[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] surfaceBytes = context.Blocks.Load(cursor, checked(count * XSurfaceSize), out XBlockAddress arrayAddress);
        var surfaces = new XSurface[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * XSurfaceSize;
            var surfaceCursor = new FastFileCursor(surfaceBytes.AsSpan(offset, XSurfaceSize).ToArray(), arrayAddress with { Offset = arrayAddress.Offset + offset });
            surfaces[i] = ReadXSurfaceChildren(cursor, surfaceCursor, context);
        }

        return surfaces;
    }

    private XSurface ReadXSurfaceChildren(
        FastFileCursor cursor,
        FastFileCursor surfaceCursor,
        DbLoadExecutionContext context)
    {
        XSurfaceTileMode tileMode =
            (XSurfaceTileMode)surfaceCursor.ReadByte();
        byte deformedRaw = surfaceCursor.ReadByte();
        XSurfaceStreamFlags streamFlags =
            (XSurfaceStreamFlags)surfaceCursor.ReadByte();
        byte pad03 = surfaceCursor.ReadByte();
        ushort vertCount = surfaceCursor.ReadUInt16();
        ushort triCount = surfaceCursor.ReadUInt16();
        XPointer<ushort[]> triIndicesPointer = ReadPointer<ushort[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        ushort blend0 = surfaceCursor.ReadUInt16();
        ushort blend1 = surfaceCursor.ReadUInt16();
        ushort blend2 = surfaceCursor.ReadUInt16();
        ushort blend3 = surfaceCursor.ReadUInt16();
        XPointer<ushort[]> vertsBlendPointer = ReadPointer<ushort[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> verts0Pointer = ReadPointer<byte[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        GfxVertexBuffer vb0 = ReadGfxVertexBuffer(surfaceCursor);
        XPointer<byte[]> verts1Pointer = ReadPointer<byte[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        GfxVertexBuffer vb1 = ReadGfxVertexBuffer(surfaceCursor);
        int vertListCount = surfaceCursor.ReadInt32();
        XPointer<XRigidVertList[]> vertListPointer = ReadPointer<XRigidVertList[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        GfxIndexBuffer indexBuffer = ReadGfxIndexBuffer(surfaceCursor);
        var partBits = new uint[6];
        for (int i = 0; i < partBits.Length; i++)
            partBits[i] = surfaceCursor.ReadUInt32();

        int blendCount = blend0 + (blend1 * 3) + (blend2 * 5) + (blend3 * 7);

        IReadOnlyList<ushort> vertsBlend = ReadUInt16Array(cursor, vertsBlendPointer.Untyped, blendCount, context, out XBlockAddress? vertsBlendAddress);
        IReadOnlyList<byte> verts0 = ReadSurfaceStreamBytes(cursor, verts0Pointer.Untyped, checked(vertCount * 0x10), alignment: 16, pushPhysical: (streamFlags & XSurfaceStreamFlags.Verts0InLarge) == 0, context, out _);
        IReadOnlyList<byte> verts1 = ReadSurfaceStreamBytes(cursor, verts1Pointer.Untyped, checked(vertCount * 0x10), alignment: 16, pushPhysical: (streamFlags & XSurfaceStreamFlags.Verts1InLarge) == 0, context, out _);
        IReadOnlyList<XRigidVertList> vertList = ReadRigidVertListArray(cursor, vertListPointer.Untyped, vertListCount, context);
        IReadOnlyList<ushort> triIndices = ReadSurfaceStreamUshorts(cursor, triIndicesPointer.Untyped, checked(triCount * 3), alignment: 16, pushPhysical: (streamFlags & XSurfaceStreamFlags.TriIndicesInLarge) == 0, context, out _);

        return new XSurface
        {
            TileMode = tileMode,
            DeformedRaw = deformedRaw,
            StreamFlags = streamFlags,
            Pad03 = pad03,
            VertCount = vertCount,
            TriCount = triCount,
            TriIndicesPointer = triIndicesPointer,
            TriIndices = triIndices,
            VertexInfo = new XSurfaceVertexInfo
            {
                Blend0 = blend0,
                Blend1 = blend1,
                Blend2 = blend2,
                Blend3 = blend3,
                VertsBlendPointer = vertsBlendPointer,
                VertsBlendRuntimeAddress = vertsBlendAddress,
                VertsBlend = vertsBlend
            },
            Verts0Pointer = verts0Pointer,
            Verts0 = verts0,
            Vb0 = vb0,
            Verts1Pointer = verts1Pointer,
            Verts1 = verts1,
            Vb1 = vb1,
            VertListCount = vertListCount,
            VertListPointer = vertListPointer,
            VertList = vertList,
            IndexBuffer = indexBuffer,
            PartBits = partBits
        };
    }

    private static GfxVertexBuffer ReadGfxVertexBuffer(FastFileCursor cursor)
    {
        return new GfxVertexBuffer
        {
            StreamSource = cursor.ReadInt32(),
            DataOffset = cursor.ReadInt32()
        };
    }

    private static GfxIndexBuffer ReadGfxIndexBuffer(FastFileCursor cursor)
    {
        return new GfxIndexBuffer
        {
            DataOffset = cursor.ReadInt32()
        };
    }

    private IReadOnlyList<XRigidVertList> ReadRigidVertListArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count <= 0 || pointer.Type == PointerType.Null)
            return [];

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XRigidVertList[]>(pointer, checked(count * XRigidVertListSize), "XRigidVertList[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] listBytes = context.Blocks.Load(cursor, checked(count * XRigidVertListSize), out XBlockAddress listAddress);
        var lists = new XRigidVertList[count];
        for (int i = 0; i < count; i++)
        {
            int offset = i * XRigidVertListSize;
            var listCursor = new FastFileCursor(listBytes.AsSpan(offset, XRigidVertListSize).ToArray(), listAddress with { Offset = listAddress.Offset + offset });
            ushort boneOffset = listCursor.ReadUInt16();
            ushort vertCount = listCursor.ReadUInt16();
            ushort triOffset = listCursor.ReadUInt16();
            ushort triCount = listCursor.ReadUInt16();
            XPointer<XSurfaceCollisionTree> collisionTreePointer = ReadPointer<XSurfaceCollisionTree>(listCursor, context, XPointerResolutionMode.Direct);
            lists[i] = new XRigidVertList
            {
                BoneOffset = boneOffset,
                VertCount = vertCount,
                TriOffset = triOffset,
                TriCount = triCount,
                CollisionTreePointer = collisionTreePointer,
                CollisionTree = ReadXSurfaceCollisionTree(cursor, collisionTreePointer.Untyped, context)
            };
        }

        return lists;
    }

    private XSurfaceCollisionTree? ReadXSurfaceCollisionTree(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XSurfaceCollisionTree>(pointer, XSurfaceCollisionTreeSize, "XSurfaceCollisionTree");
            return null;
        }

        XBlockAddress runtimeAddress =
            context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] treeBytes = context.Blocks.Load(cursor, XSurfaceCollisionTreeSize, out XBlockAddress treeAddress);
        var treeCursor = new FastFileCursor(treeBytes, treeAddress);
        ModelVec3 trans = ReadVec3(treeCursor);
        ModelVec3 scale = ReadVec3(treeCursor);
        int nodeCount = treeCursor.ReadInt32();
        XPointer<XSurfaceCollisionNode[]> nodesPointer = ReadPointer<XSurfaceCollisionNode[]>(treeCursor, context, XPointerResolutionMode.Direct);
        int leafCount = treeCursor.ReadInt32();
        XPointer<XSurfaceCollisionLeaf[]> leafsPointer = ReadPointer<XSurfaceCollisionLeaf[]>(treeCursor, context, XPointerResolutionMode.Direct);

        IReadOnlyList<XSurfaceCollisionNode> nodes =
            ReadCollisionNodeArray(cursor, nodesPointer.Untyped, nodeCount, context, out XBlockAddress? nodesAddress);
        IReadOnlyList<XSurfaceCollisionLeaf> leafs =
            ReadCollisionLeafArray(cursor, leafsPointer.Untyped, leafCount, context, out XBlockAddress? leafsAddress);

        return new XSurfaceCollisionTree
        {
            RuntimeAddress = runtimeAddress,
            Trans = trans,
            Scale = scale,
            NodeCount = nodeCount,
            NodesPointer = nodesPointer,
            NodesRuntimeAddress = nodesAddress,
            Nodes = nodes,
            LeafCount = leafCount,
            LeafsPointer = leafsPointer,
            LeafsRuntimeAddress = leafsAddress,
            Leafs = leafs
        };
    }

    private IReadOnlyList<byte> ReadSurfaceStreamBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        bool pushPhysical,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        if (!pushPhysical)
        {
            return ReadRawBytes(cursor, pointer, byteCount, alignment, context, out runtimeAddress);
        }

        context.Blocks.Push(XFileBlockType.PHYSICAL);
        try
        {
            return ReadRawBytes(cursor, pointer, byteCount, alignment, context, out runtimeAddress);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private IReadOnlyList<ushort> ReadSurfaceStreamUshorts(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        bool pushPhysical,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadSurfaceStreamBytes(
            cursor,
            pointer,
            checked(count * sizeof(ushort)),
            alignment,
            pushPhysical,
            context,
            out runtimeAddress);

        return ReadUInt16Values(bytes);
    }

    private static IReadOnlyList<XSurfaceCollisionNode> ReadCollisionNodeArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadRawBytes(cursor, pointer, checked(count * XSurfaceCollisionNode.SerializedSize), alignment: 16, context, out runtimeAddress);
        if (bytes.Count == 0)
            return [];

        RequireExactByteCount(bytes, count, XSurfaceCollisionNode.SerializedSize, nameof(XSurfaceCollisionNode));
        var values = new XSurfaceCollisionNode[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadXSurfaceCollisionNode(bytes, i * XSurfaceCollisionNode.SerializedSize);

        return values;
    }

    private static XSurfaceCollisionNode ReadXSurfaceCollisionNode(IReadOnlyList<byte> bytes, int offset)
    {
        var cursor = new FastFileCursor(bytes.Skip(offset).Take(XSurfaceCollisionNode.SerializedSize).ToArray());
        var aabb = new XSurfaceCollisionAabb(
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16());

        return new XSurfaceCollisionNode(aabb, cursor.ReadUInt16(), cursor.ReadUInt16());
    }

    private static IReadOnlyList<XSurfaceCollisionLeaf> ReadCollisionLeafArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadUInt16Array(cursor, pointer, count, context, out runtimeAddress)
            .Select(value => new XSurfaceCollisionLeaf(value))
            .ToArray();
    }
}
