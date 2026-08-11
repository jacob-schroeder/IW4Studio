using IW4.Assets.Assets.GameMap;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen GameMapSp provider. Path trees and vehicle branches use declared
/// storage symbols so forward and cyclic direct references remain symbolic.
/// </summary>
internal sealed class GameWorldSpLinkRecipe : AssetLinkRecipe
{
    private GameWorldSpLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        GameWorldSpAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        PathStorage path = CreatePath(definition.Path, freeze);
        DirectTarget? vehicle = CreateVehicleTrack(definition.VehicleTrack, freeze);
        LinkStorageSymbol? glass = definition.GlassData is null
            ? null
            : GameWorldGlassLinkStorage.Create(definition.GlassData, freeze);

        var writer = new LinkTemplateWriter(GameWorldSpAsset.SerializedSize);
        writer.Skip(sizeof(int));
        WritePathHeader(writer, definition.Path);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.VehicleTrack.SegmentCount);
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateRootOperations(root, path, vehicle, glass));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        GameWorldSpAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Path);
        ArgumentNullException.ThrowIfNull(definition.VehicleTrack);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.GameMapSp,
                originalSerializedName,
                freeze);
        }

        ValidatePath(definition.Path);
        ValidateVehicleTrack(definition.VehicleTrack);
        if (definition.GlassData is not null)
            GameWorldGlassLinkStorage.Validate(definition.GlassData, "GameWorldSp.GlassData");
        return new GameWorldSpLinkRecipe(key, originalSerializedName, definition, freeze);
    }

    private IEnumerable<LinkOperation> CreateRootOperations(
        LinkStorageSymbol root,
        PathStorage path,
        DirectTarget? vehicle,
        LinkStorageSymbol? glass)
    {
        yield return NameOperation(root, 0);
        foreach (LinkOperation operation in path.CreateRootOperations(root))
            yield return operation;
        if (vehicle is { } segments)
            yield return Direct(root, 0x2c, segments, "GameWorldSp.VehicleTrack.Segments");
        if (glass is not null)
        {
            yield return PresenceOperation(
                root,
                0x34,
                glass,
                "GameWorldSp.GlassData");
        }
    }

    private static PathStorage CreatePath(
        PathData path,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageSymbol? nodes = CreatePathNodes(
            path.Nodes,
            path.NodesPointer.Untyped,
            "GameWorldSp.Path.Nodes");
        LinkStorageSymbol? baseNodes = CreateRuntimeBaseNodes(path);
        LinkStorageSymbol? chainForNode = CreateUInt16Storage(
            path.ChainNodeForNode,
            path.ChainNodeForNodePointer.Untyped,
            "GameWorldSp.Path.ChainNodeForNode");
        LinkStorageSymbol? nodeForChain = CreateUInt16Storage(
            path.NodeForChainNode,
            path.NodeForChainNodePointer.Untyped,
            "GameWorldSp.Path.NodeForChainNode");
        LinkStorageSymbol? pathVis = CreateByteStorage(
            path.PathVis,
            path.PathVisPointer.Untyped,
            "GameWorldSp.Path.PathVis");
        LinkStorageTarget? trees = CreateTreeGraph(path, freeze);
        return new PathStorage(
            nodes,
            baseNodes,
            chainForNode,
            nodeForChain,
            pathVis,
            trees);
    }

    private static LinkStorageSymbol? CreatePathNodes(
        IReadOnlyList<PathNode> nodes,
        XPointerReference pointer,
        string fieldPath)
    {
        if (nodes.Count == 0 && pointer.Type == PointerType.Null)
            return null;

        var links = new LinkStorageSymbol?[nodes.Count];
        var writer = new LinkTemplateWriter(
            checked(nodes.Count * PathNode.SerializedSize));
        for (int index = 0; index < nodes.Count; index++)
        {
            PathNode node = nodes[index];
            PathNodeConstant constant = node.Constant;
            PathNodeDynamic dynamic = node.Dynamic;
            PathNodeTransient transient = node.Transient;
            links[index] = CreatePathLinks(
                constant.Links,
                constant.LinksPointer.Untyped,
                $"{fieldPath}[{index}].Links");

            writer.WriteInt32(constant.NodeType);
            writer.WriteUInt16(constant.SpawnFlags);
            writer.Skip(5 * sizeof(ushort));
            writer.WriteInt32(constant.AnimScriptFunc);
            WriteVec3(writer, constant.Origin);
            WriteSingle(writer, constant.Angle);
            WriteSingle(writer, constant.ForwardX);
            WriteSingle(writer, constant.ForwardY);
            WriteSingle(writer, constant.Radius);
            WriteSingle(writer, constant.MinUseDistSq);
            writer.WriteUInt16(unchecked((ushort)constant.OverlapNode0));
            writer.WriteUInt16(unchecked((ushort)constant.OverlapNode1));
            writer.WriteUInt16(constant.TotalLinkCount);
            writer.WriteUInt16(constant.Pad3A);
            writer.Skip(sizeof(int));

            writer.WriteUInt16(dynamic.OwnerHandle);
            writer.WriteUInt16(dynamic.Pad42);
            writer.WriteInt32(dynamic.FreeTime);
            foreach (int value in dynamic.ValidTimes)
                writer.WriteInt32(value);
            foreach (int value in dynamic.DangerousNodeTimes)
                writer.WriteInt32(value);
            writer.WriteInt32(dynamic.InPlayerLosTime);
            writer.WriteUInt16(unchecked((ushort)dynamic.LinkCount));
            writer.WriteUInt16(unchecked((ushort)dynamic.OverlapCount));
            writer.WriteUInt16(unchecked((ushort)dynamic.TurretEntityNumber));
            writer.WriteByte(dynamic.UserCount);
            writer.WriteByte(dynamic.HasBadPlaceLink ? (byte)1 : (byte)0);

            writer.WriteInt32(transient.SearchFrame);
            writer.WriteUInt32(transient.NextOpenRuntimePointer);
            writer.WriteUInt32(transient.PreviousOpenRuntimePointer);
            writer.WriteUInt32(transient.ParentRuntimePointer);
            WriteSingle(writer, transient.Cost);
            WriteSingle(writer, transient.Heuristic);
            writer.WriteUInt32(transient.NodeCostOrLinkIndexBits);
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => CreatePathNodeOperations(table, nodes, links, fieldPath));
    }

    private static IEnumerable<LinkOperation> CreatePathNodeOperations(
        LinkStorageSymbol table,
        IReadOnlyList<PathNode> nodes,
        IReadOnlyList<LinkStorageSymbol?> links,
        string fieldPath)
    {
        for (int index = 0; index < nodes.Count; index++)
        {
            int row = checked(index * PathNode.SerializedSize);
            PathNodeConstant constant = nodes[index].Constant;
            yield return Script(table, row + 0x06, constant.TargetName, $"{fieldPath}[{index}].TargetName");
            yield return Script(table, row + 0x08, constant.ScriptLinkName, $"{fieldPath}[{index}].ScriptLinkName");
            yield return Script(table, row + 0x0a, constant.ScriptNoteworthy, $"{fieldPath}[{index}].ScriptNoteworthy");
            yield return Script(table, row + 0x0c, constant.Target, $"{fieldPath}[{index}].Target");
            yield return Script(table, row + 0x0e, constant.AnimScript, $"{fieldPath}[{index}].AnimScript");
            if (links[index] is { } linkStorage)
            {
                yield return PresenceOperation(
                    table,
                    checked(row + 0x3c),
                    linkStorage,
                    $"{fieldPath}[{index}].Links");
            }
        }
    }

    private static LinkStorageSymbol? CreatePathLinks(
        IReadOnlyList<PathLink> values,
        XPointerReference pointer,
        string fieldPath)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(
            checked(values.Count * PathLink.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            PathLink value = values[index];
            WriteSingle(writer, value.Distance);
            writer.WriteUInt16(value.NodeNumber);
            writer.WriteByte(value.DisconnectCount);
            writer.WriteByte(value.NegotiationLink);
            writer.WriteByte(value.BadPlaceCount0);
            writer.WriteByte(value.BadPlaceCount1);
            writer.WriteByte(value.BadPlaceCount2);
            writer.WriteByte(value.BadPlaceCount3);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    private static LinkStorageSymbol? CreateRuntimeBaseNodes(PathData path)
    {
        if (path.BaseNodes.Count == 0 &&
            path.BaseNodesPointer.Type == PointerType.Null)
        {
            return null;
        }
        return LinkStorageSymbol.SourceFree(
            XFileBlockType.RUNTIME,
            checked(path.BaseNodes.Count * PathBaseNode.SerializedSize),
            alignment: 16,
            LinkMaterializationKind.RuntimeZeroFill);
    }

    private static LinkStorageTarget? CreateTreeGraph(
        PathData path,
        LinkAssetFreezeScope freeze)
    {
        IReadOnlyList<PathNodeTree> roots = path.NodeTree;
        if (roots.Count == 0 && path.NodeTreePointer.Type == PointerType.Null)
            return null;

        var rootIndices = new Dictionary<PathNodeTree, int>(
            ReferenceEqualityComparer.Instance);
        var writer = new LinkTemplateWriter(
            checked(roots.Count * PathNodeTree.SerializedSize));
        for (int index = 0; index < roots.Count; index++)
        {
            PathNodeTree root = roots[index];
            if (!rootIndices.TryAdd(root, index))
                throw new InvalidDataException("GameWorldSp.Path.NodeTree requires distinct root rows.");
            WriteTreeTemplate(writer, root);
        }

        var targets = new Dictionary<PathNodeTree, DirectTarget>(
            ReferenceEqualityComparer.Instance);
        return freeze.FreezeStorage(
            path.NodeTreePointer.Untyped,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, baseAddend) => CreateOperations(table, baseAddend),
            "GameWorldSp.Path.NodeTree");

        IEnumerable<LinkOperation> CreateOperations(
            LinkStorageSymbol table,
            int baseAddend)
        {
            for (int index = 0; index < roots.Count; index++)
            {
                targets.Add(
                    roots[index],
                    new DirectTarget(
                        new LinkStorageView(
                            table,
                            checked(baseAddend + index * PathNodeTree.SerializedSize),
                            PathNodeTree.SerializedSize),
                        CanMaterialize: false));
            }

            for (int index = 0; index < roots.Count; index++)
            {
                foreach (LinkOperation operation in CreateTreeOperations(
                    table,
                    checked(baseAddend + index * PathNodeTree.SerializedSize),
                    roots[index],
                    targets,
                    path.NodeCount,
                    freeze,
                    $"GameWorldSp.Path.NodeTree[{index}]"))
                {
                    yield return operation;
                }
            }
        }
    }

    private static IEnumerable<LinkOperation> CreateTreeOperations(
        LinkStorageSymbol owner,
        int baseOffset,
        PathNodeTree tree,
        IDictionary<PathNodeTree, DirectTarget> targets,
        uint pathNodeCount,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (tree.Axis < 0)
        {
            LinkStorageSymbol? nodes = CreateUInt16Storage(
                tree.Nodes,
                tree.NodesPointer.Untyped,
                $"{fieldPath}.Nodes");
            if (nodes is not null)
            {
                yield return PresenceOperation(
                    owner,
                    checked(baseOffset + 0x0c),
                    nodes,
                    $"{fieldPath}.Nodes");
            }
            yield break;
        }

        foreach ((PathNodeTree child, XPointerReference pointer, int pointerOffset, string childPath) in new[]
        {
            (tree.Child0!, tree.Child0Pointer.Untyped, 0x08, $"{fieldPath}.Child0"),
            (tree.Child1!, tree.Child1Pointer.Untyped, 0x0c, $"{fieldPath}.Child1")
        })
        {
            DirectTarget target = EnsureTreeTarget(
                child,
                pointer,
                targets,
                pathNodeCount,
                freeze,
                childPath);
            yield return Direct(
                owner,
                checked(baseOffset + pointerOffset),
                target,
                childPath);
        }
    }

    private static DirectTarget EnsureTreeTarget(
        PathNodeTree tree,
        XPointerReference pointer,
        IDictionary<PathNodeTree, DirectTarget> targets,
        uint pathNodeCount,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (targets.TryGetValue(tree, out DirectTarget existing))
            return existing;

        var writer = new LinkTemplateWriter(PathNodeTree.SerializedSize);
        WriteTreeTemplate(writer, tree);
        DirectTarget? published = null;
        LinkStorageTarget storage = freeze.FreezeStorageView(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (owner, baseAddend) => PublishAndCreateOperations(owner, baseAddend),
            fieldPath,
            allowStandaloneDetach: true);
        if (published is null)
            throw new InvalidOperationException(
                $"{fieldPath} did not publish its recursive direct target.");
        var target = new DirectTarget(storage.View, storage.CanMaterializeRoot);
        targets[tree] = target;
        return target;

        IEnumerable<LinkOperation> PublishAndCreateOperations(
            LinkStorageSymbol owner,
            int baseAddend)
        {
            published = new DirectTarget(
                new LinkStorageView(
                    owner,
                    baseAddend,
                    PathNodeTree.SerializedSize),
                baseAddend == 0 &&
                owner.Definition.ByteLength == PathNodeTree.SerializedSize);
            targets.Add(tree, published.Value);
            return CreateTreeOperations(
                owner,
                baseAddend,
                tree,
                targets,
                pathNodeCount,
                freeze,
                fieldPath);
        }
    }

    private static void WriteTreeTemplate(
        LinkTemplateWriter writer,
        PathNodeTree tree)
    {
        writer.WriteInt32(tree.Axis);
        WriteSingle(writer, tree.Distance);
        if (tree.Axis < 0)
        {
            writer.WriteInt32(tree.NodeCount);
            writer.Skip(sizeof(int));
        }
        else
        {
            writer.Skip(2 * sizeof(int));
        }
    }

    private static DirectTarget? CreateVehicleTrack(
        VehicleTrack track,
        LinkAssetFreezeScope freeze)
    {
        IReadOnlyList<VehicleTrackSegment> roots = track.Segments;
        if (roots.Count == 0 && track.SegmentsPointer.Type == PointerType.Null)
            return null;

        var rootIndices = new Dictionary<VehicleTrackSegment, int>(
            ReferenceEqualityComparer.Instance);
        var writer = new LinkTemplateWriter(
            checked(roots.Count * VehicleTrackSegment.SerializedSize));
        for (int index = 0; index < roots.Count; index++)
        {
            VehicleTrackSegment root = roots[index];
            if (!rootIndices.TryAdd(root, index))
                throw new InvalidDataException("GameWorldSp.VehicleTrack.Segments requires distinct root rows.");
            WriteVehicleSegmentTemplate(writer, root);
        }

        var targets = new Dictionary<VehicleTrackSegment, DirectTarget>(
            ReferenceEqualityComparer.Instance);
        LinkStorageTarget table = freeze.FreezeStorage(
            track.SegmentsPointer.Untyped,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (owner, baseAddend) => CreateOperations(owner, baseAddend),
            "GameWorldSp.VehicleTrack.Segments");
        return new DirectTarget(table.View, table.CanMaterializeRoot);

        IEnumerable<LinkOperation> CreateOperations(
            LinkStorageSymbol owner,
            int baseAddend)
        {
            for (int index = 0; index < roots.Count; index++)
            {
                targets.Add(
                    roots[index],
                    new DirectTarget(
                        new LinkStorageView(
                            owner,
                            checked(baseAddend + index * VehicleTrackSegment.SerializedSize),
                            VehicleTrackSegment.SerializedSize),
                        CanMaterialize: false));
            }

            for (int index = 0; index < roots.Count; index++)
            {
                foreach (LinkOperation operation in CreateVehicleSegmentOperations(
                    owner,
                    checked(baseAddend + index * VehicleTrackSegment.SerializedSize),
                    roots[index],
                    targets,
                    freeze,
                    $"GameWorldSp.VehicleTrack.Segments[{index}]"))
                {
                    yield return operation;
                }
            }
        }
    }

    private static IEnumerable<LinkOperation> CreateVehicleSegmentOperations(
        LinkStorageSymbol owner,
        int baseOffset,
        VehicleTrackSegment segment,
        IDictionary<VehicleTrackSegment, DirectTarget> targets,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        LinkStorageSymbol? name = freeze.FreezeOptionalXString(
            segment.Name,
            segment.NamePointer.Untyped,
            $"{fieldPath}.Name");
        if (name is not null)
        {
            yield return XStringOperation(
                owner,
                baseOffset,
                name,
                $"{fieldPath}.Name");
        }

        LinkStorageSymbol? sectors = CreateVehicleSectors(
            segment.Sectors,
            segment.SectorsPointer.Untyped,
            $"{fieldPath}.Sectors");
        if (sectors is not null)
        {
            yield return PresenceOperation(
                owner,
                checked(baseOffset + 0x04),
                sectors,
                $"{fieldPath}.Sectors");
        }

        LinkStorageTarget? next = CreateVehicleBranchTable(
            segment.NextBranches,
            segment.NextBranchPointers,
            segment.NextBranchesPointer.Untyped,
            targets,
            freeze,
            $"{fieldPath}.NextBranches");
        if (next is { } nextTable)
        {
            yield return new DirectStorageLinkOperation(
                new LinkStorageCell(owner, checked(baseOffset + 0x0c)),
                nextTable.View,
                nextTable.CanMaterializeRoot,
                $"{fieldPath}.NextBranches");
        }

        LinkStorageTarget? previous = CreateVehicleBranchTable(
            segment.PreviousBranches,
            segment.PreviousBranchPointers,
            segment.PreviousBranchesPointer.Untyped,
            targets,
            freeze,
            $"{fieldPath}.PreviousBranches");
        if (previous is { } previousTable)
        {
            yield return new DirectStorageLinkOperation(
                new LinkStorageCell(owner, checked(baseOffset + 0x14)),
                previousTable.View,
                previousTable.CanMaterializeRoot,
                $"{fieldPath}.PreviousBranches");
        }
    }

    private static LinkStorageTarget? CreateVehicleBranchTable(
        IReadOnlyList<VehicleTrackSegment?> branches,
        IReadOnlyList<XPointer<VehicleTrackSegment>> branchPointers,
        XPointerReference pointer,
        IDictionary<VehicleTrackSegment, DirectTarget> targets,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (branches.Count == 0 && pointer.Type == PointerType.Null)
            return null;

        return freeze.FreezeStorage(
            pointer,
            new byte[checked(branches.Count * sizeof(int))],
            XFileBlockType.LARGE,
            alignment: 4,
            (table, baseAddend) => CreateOperations(table, baseAddend),
            fieldPath);

        IEnumerable<LinkOperation> CreateOperations(
            LinkStorageSymbol table,
            int baseAddend)
        {
            for (int index = 0; index < branches.Count; index++)
            {
                if (branches[index] is not { } branch)
                    continue;
                XPointerReference branchPointer = branchPointers.Count == 0
                    ? default
                    : branchPointers[index].Untyped;
                DirectTarget target = EnsureVehicleSegmentTarget(
                    branch,
                    branchPointer,
                    targets,
                    freeze,
                    $"{fieldPath}[{index}]");
                yield return Direct(
                    table,
                    checked(baseAddend + index * sizeof(int)),
                    target,
                    $"{fieldPath}[{index}]");
            }
        }
    }

    private static DirectTarget EnsureVehicleSegmentTarget(
        VehicleTrackSegment segment,
        XPointerReference pointer,
        IDictionary<VehicleTrackSegment, DirectTarget> targets,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (targets.TryGetValue(segment, out DirectTarget existing))
            return existing;

        var writer = new LinkTemplateWriter(VehicleTrackSegment.SerializedSize);
        WriteVehicleSegmentTemplate(writer, segment);
        DirectTarget? published = null;
        LinkStorageTarget storage = freeze.FreezeStorageView(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (owner, baseAddend) => PublishAndCreateOperations(owner, baseAddend),
            fieldPath,
            allowStandaloneDetach: true);
        if (published is null)
            throw new InvalidOperationException(
                $"{fieldPath} did not publish its recursive direct target.");
        var target = new DirectTarget(storage.View, storage.CanMaterializeRoot);
        targets[segment] = target;
        return target;

        IEnumerable<LinkOperation> PublishAndCreateOperations(
            LinkStorageSymbol owner,
            int baseAddend)
        {
            published = new DirectTarget(
                new LinkStorageView(
                    owner,
                    baseAddend,
                    VehicleTrackSegment.SerializedSize),
                baseAddend == 0 &&
                owner.Definition.ByteLength == VehicleTrackSegment.SerializedSize);
            targets.Add(segment, published.Value);
            return CreateVehicleSegmentOperations(
                owner,
                baseAddend,
                segment,
                targets,
                freeze,
                fieldPath);
        }
    }

    private static void WriteVehicleSegmentTemplate(
        LinkTemplateWriter writer,
        VehicleTrackSegment segment)
    {
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32(segment.SectorCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(segment.NextBranchCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(segment.PreviousBranchCount);
        foreach (float value in segment.EndEdgeDirection)
            WriteSingle(writer, value);
        WriteSingle(writer, segment.EndEdgeDistance);
        WriteSingle(writer, segment.TotalLength);
    }

    private static LinkStorageSymbol? CreateVehicleSectors(
        IReadOnlyList<VehicleTrackSector> sectors,
        XPointerReference pointer,
        string fieldPath)
    {
        if (sectors.Count == 0 && pointer.Type == PointerType.Null)
            return null;

        var obstacles = new LinkStorageSymbol?[sectors.Count];
        var writer = new LinkTemplateWriter(
            checked(sectors.Count * VehicleTrackSector.SerializedSize));
        for (int index = 0; index < sectors.Count; index++)
        {
            VehicleTrackSector sector = sectors[index];
            obstacles[index] = CreateVehicleObstacles(
                sector.Obstacles,
                sector.ObstaclesPointer.Untyped,
                $"{fieldPath}[{index}].Obstacles");
            foreach (float value in sector.StartEdgeDirection)
                WriteSingle(writer, value);
            WriteSingle(writer, sector.StartEdgeDistance);
            foreach (float value in sector.LeftEdgeDirection)
                WriteSingle(writer, value);
            WriteSingle(writer, sector.LeftEdgeDistance);
            foreach (float value in sector.RightEdgeDirection)
                WriteSingle(writer, value);
            WriteSingle(writer, sector.RightEdgeDistance);
            WriteSingle(writer, sector.SectorLength);
            WriteSingle(writer, sector.SectorWidth);
            WriteSingle(writer, sector.TotalPriorLength);
            WriteSingle(writer, sector.TotalFollowingLength);
            writer.Skip(sizeof(int));
            writer.WriteInt32(sector.ObstacleCount);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => obstacles
                .Select((storage, index) => (storage, index))
                .Where(item => item.storage is not null)
                .Select(item => PresenceOperation(
                    table,
                    checked(item.index * VehicleTrackSector.SerializedSize + 0x34),
                    item.storage!,
                    $"{fieldPath}[{item.index}].Obstacles")));
    }

    private static LinkStorageSymbol? CreateVehicleObstacles(
        IReadOnlyList<VehicleTrackObstacle> obstacles,
        XPointerReference pointer,
        string fieldPath)
    {
        if (obstacles.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(
            checked(obstacles.Count * VehicleTrackObstacle.SerializedSize));
        foreach (VehicleTrackObstacle obstacle in obstacles)
        {
            foreach (float value in obstacle.Origin)
                WriteSingle(writer, value);
            WriteSingle(writer, obstacle.Radius);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    private static LinkStorageSymbol? CreateUInt16Storage(
        IReadOnlyList<ushort> values,
        XPointerReference pointer,
        string fieldPath)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 2);
    }

    private static LinkStorageSymbol? CreateByteStorage(
        IReadOnlyList<byte> values,
        XPointerReference pointer,
        string fieldPath)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            values.ToArray(),
            alignment: 1);
    }

    private static void ValidatePath(PathData path)
    {
        if (path.NodeCount > int.MaxValue ||
            path.Nodes.Count != (int)path.NodeCount ||
            path.BaseNodes.Count != (int)path.NodeCount)
        {
            throw new InvalidDataException(
                "GameWorldSp.Path.NodeCount must equal the node and RUNTIME base-node rows.");
        }
        if (path.ChainNodeCount > path.NodeCount)
        {
            throw new InvalidDataException(
                "GameWorldSp.Path.ChainNodeCount cannot exceed NodeCount.");
        }
        bool requireChainMaps = path.ChainNodeCount != 0;
        ValidateChainMap(
            path.ChainNodeForNode,
            path.ChainNodeForNodePointer.Untyped,
            path.NodeCount,
            requireChainMaps,
            "GameWorldSp.Path.ChainNodeForNode");
        ValidateChainMap(
            path.NodeForChainNode,
            path.NodeForChainNodePointer.Untyped,
            path.NodeCount,
            requireChainMaps,
            "GameWorldSp.Path.NodeForChainNode");
        if (path.VisBytes < 0 || path.PathVis.Count != path.VisBytes)
            throw new InvalidDataException("GameWorldSp.Path.VisBytes must equal PathVis.Count.");
        if (path.NodeTreeCount < 0 || path.NodeTree.Count != path.NodeTreeCount)
            throw new InvalidDataException("GameWorldSp.Path.NodeTreeCount must equal NodeTree.Count.");

        for (int index = 0; index < path.BaseNodes.Count; index++)
        {
            PathBaseNode value = path.BaseNodes[index] ?? throw new InvalidDataException(
                $"GameWorldSp.Path.BaseNodes[{index}] cannot be null.");
            if (!IsZero(value.Origin) || value.Type != 0)
            {
                throw new InvalidDataException(
                    "GameWorldSp.Path.BaseNodes is source-free RUNTIME storage and must be zero initialized.");
            }
        }

        for (int index = 0; index < path.Nodes.Count; index++)
        {
            PathNode node = path.Nodes[index] ?? throw new InvalidDataException(
                $"GameWorldSp.Path.Nodes[{index}] cannot be null.");
            ArgumentNullException.ThrowIfNull(node.Constant);
            ArgumentNullException.ThrowIfNull(node.Dynamic);
            ArgumentNullException.ThrowIfNull(node.Transient);
            if (node.Constant.TotalLinkCount != node.Constant.Links.Count)
                throw new InvalidDataException($"GameWorldSp.Path.Nodes[{index}] link count disagrees with Links.");
            if (node.Dynamic.ValidTimes.Count != 3 ||
                node.Dynamic.DangerousNodeTimes.Count != 3)
            {
                throw new InvalidDataException(
                    $"GameWorldSp.Path.Nodes[{index}].Dynamic requires three valid and danger times.");
            }
            for (int link = 0; link < node.Constant.Links.Count; link++)
            {
                if (node.Constant.Links[link] is null)
                    throw new InvalidDataException($"GameWorldSp.Path.Nodes[{index}].Links[{link}] cannot be null.");
            }
        }

        var roots = new HashSet<PathNodeTree>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<PathNodeTree>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < path.NodeTree.Count; index++)
        {
            PathNodeTree tree = path.NodeTree[index] ?? throw new InvalidDataException(
                $"GameWorldSp.Path.NodeTree[{index}] cannot be null.");
            if (!roots.Add(tree))
                throw new InvalidDataException("GameWorldSp.Path.NodeTree requires distinct root rows.");
            ValidateTree(tree, path.NodeCount, visited, $"GameWorldSp.Path.NodeTree[{index}]");
        }
    }

    private static void ValidateChainMap(
        IReadOnlyList<ushort> values,
        XPointerReference pointer,
        uint nodeCount,
        bool required,
        string fieldPath)
    {
        bool present = values.Count != 0 || pointer.Type != PointerType.Null;
        if (!present)
        {
            if (required)
                throw new InvalidDataException($"{fieldPath} is required when ChainNodeCount is nonzero.");
            return;
        }

        if (values.Count != (int)nodeCount)
        {
            throw new InvalidDataException(
                $"{fieldPath} must contain one UInt16 per path node when present.");
        }
    }

    private static void ValidateTree(
        PathNodeTree tree,
        uint pathNodeCount,
        ISet<PathNodeTree> visited,
        string fieldPath)
    {
        if (!visited.Add(tree))
            return;
        if (tree.Axis < 0)
        {
            if (tree.NodeCount < 0 || tree.NodeCount != tree.Nodes.Count)
                throw new InvalidDataException($"{fieldPath}.NodeCount must equal Nodes.Count.");
            if (tree.Nodes.Any(index => index >= pathNodeCount))
                throw new InvalidDataException($"{fieldPath}.Nodes references a path node outside the table.");
            return;
        }
        if (tree.Child0 is null || tree.Child1 is null)
            throw new InvalidDataException($"{fieldPath} branch nodes require both children.");
        ValidateTree(tree.Child0, pathNodeCount, visited, $"{fieldPath}.Child0");
        ValidateTree(tree.Child1, pathNodeCount, visited, $"{fieldPath}.Child1");
    }

    private static void ValidateVehicleTrack(VehicleTrack track)
    {
        if (track.SegmentCount < 0 || track.SegmentCount != track.Segments.Count)
        {
            throw new InvalidDataException(
                "GameWorldSp.VehicleTrack.SegmentCount must equal Segments.Count.");
        }
        var roots = new HashSet<VehicleTrackSegment>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<VehicleTrackSegment>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < track.Segments.Count; index++)
        {
            VehicleTrackSegment segment = track.Segments[index] ?? throw new InvalidDataException(
                $"GameWorldSp.VehicleTrack.Segments[{index}] cannot be null.");
            if (!roots.Add(segment))
                throw new InvalidDataException("GameWorldSp.VehicleTrack.Segments requires distinct root rows.");
            ValidateVehicleSegment(
                segment,
                visited,
                $"GameWorldSp.VehicleTrack.Segments[{index}]");
        }
    }

    private static void ValidateVehicleSegment(
        VehicleTrackSegment segment,
        ISet<VehicleTrackSegment> visited,
        string fieldPath)
    {
        if (!visited.Add(segment))
            return;
        if (segment.SectorCount < 0 || segment.SectorCount != segment.Sectors.Count)
            throw new InvalidDataException($"{fieldPath}.SectorCount must equal Sectors.Count.");
        if (segment.NextBranchCount < 0 ||
            segment.NextBranchCount != segment.NextBranches.Count ||
            segment.PreviousBranchCount < 0 ||
            segment.PreviousBranchCount != segment.PreviousBranches.Count)
        {
            throw new InvalidDataException($"{fieldPath} branch counts must equal their semantic tables.");
        }
        if (segment.NextBranchPointers.Count is not 0 &&
            segment.NextBranchPointers.Count != segment.NextBranches.Count)
        {
            throw new InvalidDataException($"{fieldPath}.NextBranchPointers count is inconsistent.");
        }
        if (segment.PreviousBranchPointers.Count is not 0 &&
            segment.PreviousBranchPointers.Count != segment.PreviousBranches.Count)
        {
            throw new InvalidDataException($"{fieldPath}.PreviousBranchPointers count is inconsistent.");
        }
        if (segment.EndEdgeDirection.Count != 2)
            throw new InvalidDataException($"{fieldPath}.EndEdgeDirection requires two floats.");

        for (int index = 0; index < segment.Sectors.Count; index++)
        {
            VehicleTrackSector sector = segment.Sectors[index] ?? throw new InvalidDataException(
                $"{fieldPath}.Sectors[{index}] cannot be null.");
            if (sector.StartEdgeDirection.Count != 2 ||
                sector.LeftEdgeDirection.Count != 2 ||
                sector.RightEdgeDirection.Count != 2)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.Sectors[{index}] edge directions require two floats each.");
            }
            if (sector.ObstacleCount < 0 || sector.ObstacleCount != sector.Obstacles.Count)
                throw new InvalidDataException($"{fieldPath}.Sectors[{index}].ObstacleCount is inconsistent.");
            for (int obstacle = 0; obstacle < sector.Obstacles.Count; obstacle++)
            {
                VehicleTrackObstacle value = sector.Obstacles[obstacle] ?? throw new InvalidDataException(
                    $"{fieldPath}.Sectors[{index}].Obstacles[{obstacle}] cannot be null.");
                if (value.Origin.Count != 2)
                {
                    throw new InvalidDataException(
                        $"{fieldPath}.Sectors[{index}].Obstacles[{obstacle}].Origin requires two floats.");
                }
            }
        }

        foreach (VehicleTrackSegment? branch in segment.NextBranches)
        {
            if (branch is not null)
                ValidateVehicleSegment(branch, visited, $"{fieldPath}.NextBranches");
        }
        foreach (VehicleTrackSegment? branch in segment.PreviousBranches)
        {
            if (branch is not null)
                ValidateVehicleSegment(branch, visited, $"{fieldPath}.PreviousBranches");
        }
    }

    private static void ValidateReferenceShape(GameWorldSpAsset definition)
    {
        PathData path = definition.Path;
        VehicleTrack track = definition.VehicleTrack;
        if (path.NodeCount != 0 || path.NodesPointer.Raw != 0 || path.Nodes.Count != 0 ||
            path.BaseNodesPointer.Raw != 0 || path.BaseNodes.Count != 0 ||
            path.ChainNodeCount != 0 || path.ChainNodeForNodePointer.Raw != 0 ||
            path.ChainNodeForNode.Count != 0 || path.NodeForChainNodePointer.Raw != 0 ||
            path.NodeForChainNode.Count != 0 || path.VisBytes != 0 ||
            path.PathVisPointer.Raw != 0 || path.PathVis.Count != 0 ||
            path.NodeTreeCount != 0 || path.NodeTreePointer.Raw != 0 ||
            path.NodeTree.Count != 0 || track.SegmentsPointer.Raw != 0 ||
            track.SegmentCount != 0 || track.Segments.Count != 0 ||
            definition.GlassDataPointer.Raw != 0 || definition.GlassData is not null)
        {
            throw new InvalidDataException(
                "A comma-prefixed GameMapSp provider must have a zeroed reference body.");
        }
    }

    private static void WritePathHeader(LinkTemplateWriter writer, PathData path)
    {
        writer.WriteUInt32(path.NodeCount);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteUInt32(path.ChainNodeCount);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32(path.VisBytes);
        writer.Skip(sizeof(int));
        writer.WriteInt32(path.NodeTreeCount);
        writer.Skip(sizeof(int));
    }

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        DirectTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            target.View,
            target.CanMaterialize,
            fieldPath);

    private static ScriptStringLinkOperation Script(
        LinkStorageSymbol owner,
        int offset,
        ScriptStringReference value,
        string fieldPath) =>
        new(new LinkStorageCell(owner, offset), value, fieldPath);

    private static void WriteVec3(LinkTemplateWriter writer, Vec3 value)
    {
        WriteSingle(writer, value.X);
        WriteSingle(writer, value.Y);
        WriteSingle(writer, value.Z);
    }

    private static void WriteSingle(LinkTemplateWriter writer, float value) =>
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));

    private static bool IsZero(Vec3 value) =>
        BitConverter.SingleToInt32Bits(value.X) == 0 &&
        BitConverter.SingleToInt32Bits(value.Y) == 0 &&
        BitConverter.SingleToInt32Bits(value.Z) == 0;

    private readonly record struct DirectTarget(
        LinkStorageView View,
        bool CanMaterialize);

    private sealed record PathStorage(
        LinkStorageSymbol? Nodes,
        LinkStorageSymbol? BaseNodes,
        LinkStorageSymbol? ChainForNode,
        LinkStorageSymbol? NodeForChain,
        LinkStorageSymbol? PathVis,
        LinkStorageTarget? NodeTrees)
    {
        public IEnumerable<LinkOperation> CreateRootOperations(
            LinkStorageSymbol root)
        {
            if (Nodes is not null)
                yield return PresenceOperation(root, 0x08, Nodes, "GameWorldSp.Path.Nodes");
            if (BaseNodes is not null)
                yield return PresenceOperation(root, 0x0c, BaseNodes, "GameWorldSp.Path.BaseNodes");
            if (ChainForNode is not null)
                yield return PresenceOperation(root, 0x14, ChainForNode, "GameWorldSp.Path.ChainNodeForNode");
            if (NodeForChain is not null)
                yield return PresenceOperation(root, 0x18, NodeForChain, "GameWorldSp.Path.NodeForChainNode");
            if (PathVis is not null)
                yield return PresenceOperation(root, 0x20, PathVis, "GameWorldSp.Path.PathVis");
            if (NodeTrees is { } nodeTrees)
            {
                yield return new PresenceStorageLinkOperation(
                    new LinkStorageCell(root, 0x28),
                    nodeTrees.View,
                    "GameWorldSp.Path.NodeTree");
            }
        }
    }
}
