using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.GameMap;

public sealed class PathDataLoader
{
    // Embedded 0x28-byte PathData header.
    public PathData ReadHeader(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        uint nodeCount = cursor.ReadUInt32();
        XPointer<PathNode[]> nodesPointer = ReadPresencePointer<PathNode[]>(cursor, context);
        XPointer<PathBaseNode[]> baseNodesPointer = ReadPresencePointer<PathBaseNode[]>(cursor, context);
        uint chainNodeCount = cursor.ReadUInt32();
        XPointer<ushort[]> chainNodeForNodePointer = ReadPresencePointer<ushort[]>(cursor, context);
        XPointer<ushort[]> nodeForChainNodePointer = ReadPresencePointer<ushort[]>(cursor, context);
        int visBytes = cursor.ReadInt32();
        XPointer<byte[]> pathVisPointer = ReadPresencePointer<byte[]>(cursor, context);
        int nodeTreeCount = cursor.ReadInt32();
        XPointer<PathNodeTree[]> nodeTreePointer = ReadPresencePointer<PathNodeTree[]>(cursor, context);

        return new PathData
        {
            NodeCount = nodeCount,
            NodesPointer = nodesPointer,
            BaseNodesPointer = baseNodesPointer,
            ChainNodeCount = chainNodeCount,
            ChainNodeForNodePointer = chainNodeForNodePointer,
            NodeForChainNodePointer = nodeForChainNodePointer,
            VisBytes = visBytes,
            PathVisPointer = pathVisPointer,
            NodeTreeCount = nodeTreeCount,
            NodeTreePointer = nodeTreePointer
        };
    }

    public PathData LoadPayloads(
        FastFileCursor cursor,
        PathData header,
        DbLoadExecutionContext context)
    {
        int nodeCount = Count(header.NodeCount, "PathData.nodeCount");
        IReadOnlyList<PathNode> nodes = ReadPathNodes(
            cursor,
            header.NodesPointer,
            nodeCount,
            context);

        IReadOnlyList<PathBaseNode> baseNodes;
        // Base nodes use RUNTIME storage: loading zero-fills and advances the
        // destination without consuming source bytes.
        context.Blocks.Push(XFileBlockType.RUNTIME);
        try
        {
            baseNodes = ReadBaseNodes(
                cursor,
                header.BaseNodesPointer,
                nodeCount,
                context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        IReadOnlyList<ushort> chainNodeForNode = ReadUInt16Array(
            cursor,
            header.ChainNodeForNodePointer,
            nodeCount,
            context,
            "PathData.chainNodeForNode");
        IReadOnlyList<ushort> nodeForChainNode = ReadUInt16Array(
            cursor,
            header.NodeForChainNodePointer,
            nodeCount,
            context,
            "PathData.nodeForChainNode");
        IReadOnlyList<byte> pathVis = ReadByteArray(
            cursor,
            header.PathVisPointer,
            NonNegative(header.VisBytes, "PathData.visBytes"),
            context,
            "PathData.pathVis");
        IReadOnlyList<PathNodeTree> nodeTree = ReadNodeTrees(
            cursor,
            header.NodeTreePointer,
            NonNegative(header.NodeTreeCount, "PathData.nodeTreeCount"),
            context);

        return new PathData
        {
            NodeCount = header.NodeCount,
            NodesPointer = header.NodesPointer,
            Nodes = nodes,
            BaseNodesPointer = header.BaseNodesPointer,
            BaseNodes = baseNodes,
            ChainNodeCount = header.ChainNodeCount,
            ChainNodeForNodePointer = header.ChainNodeForNodePointer,
            ChainNodeForNode = chainNodeForNode,
            NodeForChainNodePointer = header.NodeForChainNodePointer,
            NodeForChainNode = nodeForChainNode,
            VisBytes = header.VisBytes,
            PathVisPointer = header.PathVisPointer,
            PathVis = pathVis,
            NodeTreeCount = header.NodeTreeCount,
            NodeTreePointer = header.NodeTreePointer,
            NodeTree = nodeTree
        };
    }

    private static IReadOnlyList<PathNode> ReadPathNodes(
        FastFileCursor cursor,
        XPointer<PathNode[]> pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            PathNode.SerializedSize,
            alignment: 4,
            context,
            "PathData.nodes");
        if (!hasPayload)
            return [];

        var nodes = new PathNode[count];
        for (int index = 0; index < count; index++)
        {
            var rowCursor = RowCursor(bytes, address, index, PathNode.SerializedSize);
            XBlockAddress rowAddress = address.Add(checked(index * PathNode.SerializedSize));
            nodes[index] = ReadPathNode(cursor, rowCursor, rowAddress, context, index);
        }

        return nodes;
    }

    // A path-node row is 0x88 bytes with a 0x40-byte constant prefix and five
    // ScriptString fixups.
    private static PathNode ReadPathNode(
        FastFileCursor cursor,
        FastFileCursor rowCursor,
        XBlockAddress rowAddress,
        DbLoadExecutionContext context,
        int index)
    {
        int nodeType = rowCursor.ReadInt32();
        ushort spawnFlags = rowCursor.ReadUInt16();
        ScriptStringReference targetName = ReadScriptString(rowCursor, rowAddress, 0x06, context, $"PathData.nodes[{index}].targetName");
        ScriptStringReference scriptLinkName = ReadScriptString(rowCursor, rowAddress, 0x08, context, $"PathData.nodes[{index}].scriptLinkName");
        ScriptStringReference scriptNoteworthy = ReadScriptString(rowCursor, rowAddress, 0x0A, context, $"PathData.nodes[{index}].scriptNoteworthy");
        ScriptStringReference target = ReadScriptString(rowCursor, rowAddress, 0x0C, context, $"PathData.nodes[{index}].target");
        ScriptStringReference animScript = ReadScriptString(rowCursor, rowAddress, 0x0E, context, $"PathData.nodes[{index}].animScript");
        int animScriptFunc = rowCursor.ReadInt32();
        Vec3 origin = ReadVec3(rowCursor);
        float angle = ReadSingle(rowCursor);
        float forwardX = ReadSingle(rowCursor);
        float forwardY = ReadSingle(rowCursor);
        float radius = ReadSingle(rowCursor);
        float minUseDistSq = ReadSingle(rowCursor);
        short overlapNode0 = unchecked((short)rowCursor.ReadUInt16());
        short overlapNode1 = unchecked((short)rowCursor.ReadUInt16());
        ushort totalLinkCount = rowCursor.ReadUInt16();
        ushort pad3A = rowCursor.ReadUInt16();
        XPointer<PathLink[]> linksPointer = ReadPresencePointer<PathLink[]>(rowCursor, context);
        IReadOnlyList<PathLink> links = ReadPathLinks(
            cursor,
            linksPointer,
            totalLinkCount,
            context,
            $"PathData.nodes[{index}].links");

        var dynamic = new PathNodeDynamic
        {
            OwnerHandle = rowCursor.ReadUInt16(),
            Pad42 = rowCursor.ReadUInt16(),
            FreeTime = rowCursor.ReadInt32(),
            ValidTimes = [rowCursor.ReadInt32(), rowCursor.ReadInt32(), rowCursor.ReadInt32()],
            DangerousNodeTimes = [rowCursor.ReadInt32(), rowCursor.ReadInt32(), rowCursor.ReadInt32()],
            InPlayerLosTime = rowCursor.ReadInt32(),
            LinkCount = unchecked((short)rowCursor.ReadUInt16()),
            OverlapCount = unchecked((short)rowCursor.ReadUInt16()),
            TurretEntityNumber = unchecked((short)rowCursor.ReadUInt16()),
            UserCount = rowCursor.ReadByte(),
            HasBadPlaceLink = rowCursor.ReadByte() != 0
        };
        var transient = new PathNodeTransient
        {
            SearchFrame = rowCursor.ReadInt32(),
            NextOpenRuntimePointer = rowCursor.ReadUInt32(),
            PreviousOpenRuntimePointer = rowCursor.ReadUInt32(),
            ParentRuntimePointer = rowCursor.ReadUInt32(),
            Cost = ReadSingle(rowCursor),
            Heuristic = ReadSingle(rowCursor),
            NodeCostOrLinkIndexBits = rowCursor.ReadUInt32()
        };

        if (rowCursor.Offset != PathNode.SerializedSize)
        {
            throw new InvalidDataException(
                $"PathData.nodes[{index}] consumed 0x{rowCursor.Offset:X} bytes instead of 0x{PathNode.SerializedSize:X}.");
        }

        return new PathNode
        {
            Offset = rowAddress.Offset,
            Constant = new PathNodeConstant
            {
                NodeType = nodeType,
                SpawnFlags = spawnFlags,
                TargetName = targetName,
                ScriptLinkName = scriptLinkName,
                ScriptNoteworthy = scriptNoteworthy,
                Target = target,
                AnimScript = animScript,
                AnimScriptFunc = animScriptFunc,
                Origin = origin,
                Angle = angle,
                ForwardX = forwardX,
                ForwardY = forwardY,
                Radius = radius,
                MinUseDistSq = minUseDistSq,
                OverlapNode0 = overlapNode0,
                OverlapNode1 = overlapNode1,
                TotalLinkCount = totalLinkCount,
                Pad3A = pad3A,
                LinksPointer = linksPointer,
                Links = links
            },
            Dynamic = dynamic,
            Transient = transient
        };
    }

    private static ScriptStringReference ReadScriptString(
        FastFileCursor cursor,
        XBlockAddress rowAddress,
        int expectedOffset,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (cursor.Offset != expectedOffset)
        {
            throw new InvalidDataException(
                $"{memberName} is at row+0x{cursor.Offset:X}, expected row+0x{expectedOffset:X}.");
        }

        ushort raw = cursor.ReadUInt16();
        XBlockAddress destinationCell = rowAddress.Add(expectedOffset);
        ScriptStringReference resolved = context.ZoneScriptStrings.Resolve(raw, destinationCell, memberName);
        context.Blocks.WriteUInt16(destinationCell, resolved.RuntimeHandle.Value);
        return resolved;
    }

    private static IReadOnlyList<PathLink> ReadPathLinks(
        FastFileCursor cursor,
        XPointer<PathLink[]> pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            PathLink.SerializedSize,
            alignment: 4,
            context,
            memberName);
        if (!hasPayload)
            return [];

        var links = new PathLink[count];
        var linkCursor = new FastFileCursor(bytes, address);
        for (int index = 0; index < links.Length; index++)
        {
            links[index] = new PathLink
            {
                Distance = ReadSingle(linkCursor),
                NodeNumber = linkCursor.ReadUInt16(),
                DisconnectCount = linkCursor.ReadByte(),
                NegotiationLink = linkCursor.ReadByte(),
                BadPlaceCount0 = linkCursor.ReadByte(),
                BadPlaceCount1 = linkCursor.ReadByte(),
                BadPlaceCount2 = linkCursor.ReadByte(),
                BadPlaceCount3 = linkCursor.ReadByte()
            };
        }

        return links;
    }

    private static IReadOnlyList<PathBaseNode> ReadBaseNodes(
        FastFileCursor cursor,
        XPointer<PathBaseNode[]> pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            PathBaseNode.SerializedSize,
            alignment: 16,
            context,
            "PathData.baseNodes");
        if (!hasPayload)
            return [];

        var rows = new PathBaseNode[count];
        var rowCursor = new FastFileCursor(bytes, address);
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = new PathBaseNode
            {
                Origin = ReadVec3(rowCursor),
                Type = rowCursor.ReadUInt32()
            };
        }

        return rows;
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointer<ushort[]> pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            sizeof(ushort),
            alignment: 2,
            context,
            memberName);
        if (!hasPayload)
            return [];

        var values = new ushort[count];
        var valueCursor = new FastFileCursor(bytes, address);
        for (int index = 0; index < values.Length; index++)
            values[index] = valueCursor.ReadUInt16();
        return values;
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointer<byte[]> pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, _, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            sizeof(byte),
            alignment: 1,
            context,
            memberName);
        return hasPayload ? bytes : [];
    }

    private static IReadOnlyList<PathNodeTree> ReadNodeTrees(
        FastFileCursor cursor,
        XPointer<PathNodeTree[]> pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            PathNodeTree.SerializedSize,
            alignment: 4,
            context,
            "PathData.nodeTree");
        if (!hasPayload)
            return [];


        var trees = new PathNodeTree[count];
        var byAddress = new Dictionary<XBlockAddress, PathNodeTree>();
        var materialized = new HashSet<XBlockAddress>();
        var active = new HashSet<XBlockAddress>();
        // Allocate identities for every table member before decoding a body,
        // so a packed pointer to an earlier or later table row retains its
        // graph relationship instead of being silently collapsed to null.
        for (int index = 0; index < trees.Length; index++)
        {
            XBlockAddress rowAddress = address.Add(checked(index * PathNodeTree.SerializedSize));
            trees[index] = new PathNodeTree { Offset = rowAddress.Offset };
            byAddress.Add(rowAddress, trees[index]);
        }
        for (int index = 0; index < trees.Length; index++)
        {
            var rowCursor = RowCursor(bytes, address, index, PathNodeTree.SerializedSize);
            XBlockAddress rowAddress = address.Add(checked(index * PathNodeTree.SerializedSize));
            ReadNodeTreeBody(cursor, rowCursor, rowAddress, context, $"PathData.nodeTree[{index}]", trees[index], byAddress, materialized, active);
        }

        XBlockAddress[] unresolved = byAddress.Keys.Where(value => !materialized.Contains(value)).OrderBy(value => value.BlockType).ThenBy(value => value.Offset).ToArray();
        if (unresolved.Length != 0)
            throw new InvalidDataException($"PathData.nodeTree contains packed references to {unresolved.Length} tree payload(s) that were never materialized inline.");

        return trees;
    }

    private static void ReadNodeTreeBody(
        FastFileCursor cursor,
        FastFileCursor rowCursor,
        XBlockAddress rowAddress,
        DbLoadExecutionContext context,
        string memberName,
        PathNodeTree target,
        IDictionary<XBlockAddress, PathNodeTree> byAddress,
        ISet<XBlockAddress> materialized,
        ISet<XBlockAddress> active)
    {
        if (!active.Add(rowAddress))
            throw new InvalidDataException($"{memberName} recursively materializes tree payload {rowAddress}.");
        if (!materialized.Add(rowAddress))
            throw new InvalidDataException($"{memberName} materializes tree payload {rowAddress} more than once.");
        int axis = rowCursor.ReadInt32();
        float distance = ReadSingle(rowCursor);
        target.Offset = rowAddress.Offset;
        target.Axis = axis;
        target.Distance = distance;
        if (axis < 0)
        {
            int nodeCount = rowCursor.ReadInt32();
            XPointer<ushort[]> nodesPointer = ReadPresencePointer<ushort[]>(rowCursor, context);
            IReadOnlyList<ushort> nodes = ReadUInt16Array(
                cursor,
                nodesPointer,
                NonNegative(nodeCount, $"{memberName}.nodeCount"),
                context,
                $"{memberName}.nodes");
            target.NodeCount = nodeCount;
            target.NodesPointer = nodesPointer;
            target.Nodes = nodes;
            active.Remove(rowAddress);
            return;
        }

        XPointer<PathNodeTree> child0Pointer = ReadTreePointerCell(rowCursor, context);
        XPointer<PathNodeTree> child1Pointer = ReadTreePointerCell(rowCursor, context);
        target.Child0Pointer = child0Pointer;
        target.Child0 = ReadNodeTreePointer(cursor, child0Pointer, context, $"{memberName}.child[0]", byAddress, materialized, active);
        target.Child1Pointer = child1Pointer;
        target.Child1 = ReadNodeTreePointer(cursor, child1Pointer, context, $"{memberName}.child[1]", byAddress, materialized, active);
        active.Remove(rowAddress);
    }

    // Node-tree pointers may be null, inline (-1), or packed.
    private static PathNodeTree? ReadNodeTreePointer(
        FastFileCursor cursor,
        XPointer<PathNodeTree> pointer,
        DbLoadExecutionContext context,
        string memberName,
        IDictionary<XBlockAddress, PathNodeTree> byAddress,
        ISet<XBlockAddress> materialized,
        ISet<XBlockAddress> active)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            try
            {
                context.PointerReader.ValidateOffsetPointerRange<PathNodeTree>(
                    pointer.Untyped,
                    PathNodeTree.SerializedSize,
                    memberName);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"{memberName} has invalid packed pointer 0x{unchecked((uint)pointer.Raw):X8}.",
                    exception);
            }
            XBlockAddress address = pointer.PackedAddress
                ?? throw new InvalidDataException($"{memberName} packed pointer has no decoded block address.");
            return GetOrCreateTree(address, byAddress);
        }

        if (pointer.Type != PointerType.Inline)
        {
            throw new InvalidDataException(
                $"{memberName} pointer 0x{pointer.Raw:X8} is not null, inline -1, or packed.");
        }

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        if (active.Contains(targetAddress))
            throw new InvalidDataException($"{memberName} recursively materializes inline tree payload {targetAddress}.");
        byte[] bytes = context.Blocks.Load(
            cursor,
            PathNodeTree.SerializedSize,
            out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} patched to {targetAddress}, but loaded at {loadedAddress}.");

        PathNodeTree target = GetOrCreateTree(targetAddress, byAddress);
        ReadNodeTreeBody(
            cursor,
            new FastFileCursor(bytes, targetAddress),
            targetAddress,
            context,
            memberName,
            target,
            byAddress,
            materialized,
            active);
        return target;
    }

    private static PathNodeTree GetOrCreateTree(XBlockAddress address, IDictionary<XBlockAddress, PathNodeTree> byAddress)
    {
        if (byAddress.TryGetValue(address, out PathNodeTree? tree))
            return tree;
        tree = new PathNodeTree { Offset = address.Offset };
        byAddress.Add(address, tree);
        return tree;
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadPresenceArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        int byteCount = checked(NonNegative(count, memberName) * stride);
        if (pointer.Raw == 0)
            return ([], context.Blocks.CurrentAddress, HasPayload: false);

        XBlockAddress targetAddress = PatchPresenceCell(pointer, alignment, context, memberName);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} patched to {targetAddress}, but loaded at {loadedAddress}.");
        return (bytes, targetAddress, HasPayload: true);
    }

    private static XBlockAddress PatchPresenceCell(
        XPointerReference pointer,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Raw == 0)
            throw new InvalidDataException($"{memberName} is null and cannot be materialized.");
        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"{memberName} has no destination cell.");

        context.Blocks.AlignCurrent(alignment);
        XBlockAddress targetAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(targetAddress));
        return targetAddress;
    }

    private static XPointer<T> ReadPresencePointer<T>(FastFileCursor cursor, DbLoadExecutionContext context) =>
        context.PointerReader.ReadPointer<T>(cursor, XPointerResolutionMode.Direct);

    private static XPointer<PathNodeTree> ReadTreePointerCell(FastFileCursor cursor, DbLoadExecutionContext context) =>
        context.PointerReader.ReadPointer<PathNodeTree>(cursor, XPointerResolutionMode.Direct);

    private static FastFileCursor RowCursor(
        byte[] bytes,
        XBlockAddress address,
        int index,
        int stride)
    {
        int offset = checked(index * stride);
        return new FastFileCursor(bytes.AsSpan(offset, stride).ToArray(), address.Add(offset));
    }

    private static Vec3 ReadVec3(FastFileCursor cursor) => new()
    {
        X = ReadSingle(cursor),
        Y = ReadSingle(cursor),
        Z = ReadSingle(cursor)
    };

    private static float ReadSingle(FastFileCursor cursor) =>
        BitConverter.Int32BitsToSingle(cursor.ReadInt32());

    private static int Count(uint count, string name)
    {
        if (count > int.MaxValue)
            throw new InvalidDataException($"{name} exceeds Int32: 0x{count:X8}.");
        return (int)count;
    }

    private static int NonNegative(int count, string name)
    {
        if (count < 0)
            throw new InvalidDataException($"{name} is negative: {count}.");
        return count;
    }
}
