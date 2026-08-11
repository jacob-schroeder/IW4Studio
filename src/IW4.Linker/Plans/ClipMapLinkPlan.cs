using IW4.Assets.Assets;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen shared ColMapSp/ColMapMp provider. Persistent collision tables are
/// rebuilt in native loader order; captured direct views remain physical
/// symbols, while nested XAssets remain logical provider dependencies.
/// </summary>
internal sealed class ClipMapLinkPlan : AssetLinkPlan
{
    private ClipMapLinkPlan(
        AssetKey key,
        string originalSerializedName,
        ClipMapAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var storage = new StorageFreezer(freeze).Freeze(definition);
        Root = CreateRoot(definition, storage);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        ClipMapAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        if (definition.SerializedType is not (
            XAssetType.ColMapSp or XAssetType.ColMapMp))
        {
            throw new InvalidDataException(
                "ClipMap serialized type must remain ColMapSp or ColMapMp.");
        }

        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                definition.SerializedType,
                originalSerializedName,
                freeze);
        }

        ValidateOwned(definition);
        return new ClipMapLinkPlan(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private LinkStorageSymbol CreateRoot(
        ClipMapAsset definition,
        FrozenStorage storage)
    {
        var writer = new LinkTemplateWriter(ClipMapAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.SerializedIsInUse ?? definition.IsInUse);
        writer.WriteInt32(definition.PlaneCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumStaticModels);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumMaterials);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumBrushSides);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumBrushEdges);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumNodes);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumLeafs);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.LeafBrushNodesCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumLeafBrushes);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumLeafSurfaces);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.VertCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.TriCount);
        writer.Skip(2 * sizeof(int));
        writer.WriteInt32(definition.BorderCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.PartitionCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.AabbTreeCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumSubModels);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(definition.NumBrushes);
        writer.WriteUInt16(definition.Pad8ETo8F);
        writer.Skip(4 * sizeof(int));
        writer.WriteUInt16(definition.SModelNodeCount);
        writer.WriteUInt16(definition.PadA2ToA3);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(definition.DynEntCount[0]);
        writer.WriteUInt16(definition.DynEntCount[1]);
        writer.Skip(8 * sizeof(int));
        writer.WriteUInt32(definition.Checksum);
        if (definition.PadD0ToFF.Count == 0)
            writer.Skip(0x30);
        else
            writer.WriteBytes(definition.PadD0ToFF.ToArray());

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateRootOperations(root, storage));
    }

    private IEnumerable<LinkOperation> CreateRootOperations(
        LinkStorageSymbol root,
        FrozenStorage storage)
    {
        yield return NameOperation(root, 0x00);
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x0c,
            storage.Planes,
            "ClipMap.Planes"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x14,
            storage.StaticModels,
            "ClipMap.StaticModelList"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x1c,
            storage.Materials,
            "ClipMap.Materials"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x24,
            storage.BrushSides,
            "ClipMap.BrushSides"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x2c,
            storage.BrushEdges,
            "ClipMap.BrushEdges"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x34,
            storage.Nodes,
            "ClipMap.Nodes"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x3c,
            storage.Leafs,
            "ClipMap.Leafs"))
        {
            yield return operation;
        }

        // Native Load_clipMap_t visits leafBrushes before leafBrushNodes even
        // though their root cells occur in the opposite field order.
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x4c,
            storage.LeafBrushes,
            "ClipMap.LeafBrushes"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x44,
            storage.LeafBrushNodes,
            "ClipMap.LeafBrushNodes"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x54,
            storage.LeafSurfaces,
            "ClipMap.LeafSurfaces"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x5c,
            storage.Verts,
            "ClipMap.Verts"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x64,
            storage.TriIndices,
            "ClipMap.TriIndices"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x68,
            storage.TriEdgeIsWalkable,
            "ClipMap.TriEdgeIsWalkable"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x70,
            storage.Borders,
            "ClipMap.Borders"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x78,
            storage.Partitions,
            "ClipMap.Partitions"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x80,
            storage.AabbTrees,
            "ClipMap.AabbTrees"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x88,
            storage.CModels,
            "ClipMap.CModels"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x90,
            storage.Brushes,
            "ClipMap.Brushes"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x94,
            storage.BrushBounds,
            "ClipMap.BrushBounds"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0x98,
            storage.BrushContents,
            "ClipMap.BrushContents"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0xa4,
            storage.SModelNodes,
            "ClipMap.SModelNodes"))
        {
            yield return operation;
        }
        if (storage.MapEnts is { } mapEnts)
            yield return ProviderOperation(root, 0x9c, mapEnts);
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0xac,
            storage.DynEntDefinitions[0],
            "ClipMap.DynEntDefList[0]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in DirectIfPresent(
            root,
            0xb0,
            storage.DynEntDefinitions[1],
            "ClipMap.DynEntDefList[1]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in PresenceIfPresent(
            root,
            0xb4,
            storage.DynEntPoses[0],
            "ClipMap.DynEntPoseList[0]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in PresenceIfPresent(
            root,
            0xb8,
            storage.DynEntPoses[1],
            "ClipMap.DynEntPoseList[1]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in PresenceIfPresent(
            root,
            0xbc,
            storage.DynEntClients[0],
            "ClipMap.DynEntClientList[0]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in PresenceIfPresent(
            root,
            0xc0,
            storage.DynEntClients[1],
            "ClipMap.DynEntClientList[1]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in PresenceIfPresent(
            root,
            0xc4,
            storage.DynEntCollisions[0],
            "ClipMap.DynEntCollList[0]"))
        {
            yield return operation;
        }
        foreach (LinkOperation operation in PresenceIfPresent(
            root,
            0xc8,
            storage.DynEntCollisions[1],
            "ClipMap.DynEntCollList[1]"))
        {
            yield return operation;
        }
    }

    private static IEnumerable<LinkOperation> DirectIfPresent(
        LinkStorageSymbol owner,
        int offset,
        LinkStorageTarget? target,
        string fieldPath)
    {
        if (target is { } value)
        {
            yield return new DirectStorageLinkOperation(
                new LinkStorageCell(owner, offset),
                value.View,
                value.CanMaterializeRoot,
                fieldPath);
        }
    }

    private static IEnumerable<LinkOperation> PresenceIfPresent(
        LinkStorageSymbol owner,
        int offset,
        LinkStorageSymbol? target,
        string fieldPath)
    {
        if (target is not null)
        {
            yield return new PresenceStorageLinkOperation(
                new LinkStorageCell(owner, offset),
                LinkStorageView.Whole(target),
                fieldPath);
        }
    }

    private sealed class StorageFreezer
    {
        private readonly LinkAssetFreezeScope _freeze;

        public StorageFreezer(LinkAssetFreezeScope freeze) =>
            _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));

        public FrozenStorage Freeze(ClipMapAsset value)
        {
            LinkStorageTarget? planes = FreezeArray(
                value.PlanesPointer.Untyped,
                value.Planes,
                CPlane.SerializedSize,
                4,
                WritePlane,
                "ClipMap.Planes",
                allowInteriorView: true,
                allowStandaloneDetach: true);
            LinkStorageTarget? staticModels = FreezeStaticModels(value);
            LinkStorageTarget? materials = FreezeMaterials(value);
            LinkStorageTarget? brushSides = FreezeBrushSides(
                value,
                out LinkStorageTarget?[] brushSidePlanes);
            LinkStorageTarget? brushEdges = FreezeArray(
                value.BrushEdgesPointer.Untyped,
                value.BrushEdges,
                sizeof(byte),
                1,
                static (writer, item, _) => writer.WriteByte(item),
                "ClipMap.BrushEdges");
            LinkStorageTarget? nodes = FreezeNodes(value);
            LinkStorageTarget? leafs = FreezeArray(
                value.LeafsPointer.Untyped,
                value.Leafs,
                CLeaf.SerializedSize,
                4,
                WriteLeaf,
                "ClipMap.Leafs");
            LinkStorageTarget? leafBrushes = FreezeArray(
                value.LeafBrushesPointer.Untyped,
                value.LeafBrushes,
                sizeof(ushort),
                2,
                static (writer, item, _) => writer.WriteUInt16(item),
                "ClipMap.LeafBrushes",
                allowInteriorView: true,
                allowStandaloneDetach: true);
            LinkStorageTarget? leafBrushNodes = FreezeLeafBrushNodes(value);
            LinkStorageTarget? leafSurfaces = FreezeArray(
                value.LeafSurfacesPointer.Untyped,
                value.LeafSurfaces,
                sizeof(uint),
                4,
                static (writer, item, _) => writer.WriteUInt32(item),
                "ClipMap.LeafSurfaces");
            LinkStorageTarget? verts = FreezeArray(
                value.VertsPointer.Untyped,
                value.Verts,
                0x0c,
                4,
                static (writer, item, _) => WriteVec3(writer, item),
                "ClipMap.Verts");
            LinkStorageTarget? triIndices = FreezeArray(
                value.TriIndicesPointer.Untyped,
                value.TriIndices,
                sizeof(ushort),
                2,
                static (writer, item, _) => writer.WriteUInt16(item),
                "ClipMap.TriIndices");
            LinkStorageTarget? triEdges = FreezeArray(
                value.TriEdgeIsWalkablePointer.Untyped,
                value.TriEdgeIsWalkable,
                sizeof(byte),
                1,
                static (writer, item, _) => writer.WriteByte(item),
                "ClipMap.TriEdgeIsWalkable");
            LinkStorageTarget? borders = FreezeBorders(
                value.BordersPointer.Untyped,
                value.Borders,
                "ClipMap.Borders");
            LinkStorageTarget? partitions = FreezePartitions(value, borders);
            LinkStorageTarget? aabbTrees = FreezeArray(
                value.AabbTreesPointer.Untyped,
                value.AabbTrees,
                CollisionAabbTree.SerializedSize,
                16,
                WriteAabbTree,
                "ClipMap.AabbTrees");
            LinkStorageTarget? cmodels = FreezeArray(
                value.CModelsPointer.Untyped,
                value.CModels,
                CModel.SerializedSize,
                4,
                WriteCModel,
                "ClipMap.CModels");
            LinkStorageTarget? brushes = FreezeBrushes(
                value,
                brushSides,
                brushEdges,
                brushSidePlanes);
            LinkStorageTarget? brushBounds = FreezeArray(
                value.BrushBoundsPointer.Untyped,
                value.BrushBounds,
                0x18,
                128,
                static (writer, item, _) => WriteBounds(writer, item),
                "ClipMap.BrushBounds");
            LinkStorageTarget? brushContents = FreezeArray(
                value.BrushContentsPointer.Untyped,
                value.BrushContents,
                sizeof(uint),
                4,
                static (writer, item, _) => writer.WriteUInt32(item),
                "ClipMap.BrushContents");
            LinkStorageTarget? smodelNodes = FreezeArray(
                value.SModelNodesPointer.Untyped,
                value.SModelNodes,
                SModelAabbNode.SerializedSize,
                4,
                WriteSModelNode,
                "ClipMap.SModelNodes");
            AssetDependency? mapEnts = FreezeProviderDependency(
                value.MapEntsPointer.Untyped,
                value.MapEnts,
                XAssetType.MapEnts,
                "ClipMap.MapEnts");
            LinkStorageTarget?[] dynDefinitions =
            [
                FreezeDynDefinitions(value, 0),
                FreezeDynDefinitions(value, 1)
            ];
            LinkStorageSymbol?[] dynPoses =
            [
                FreezeRuntime(
                    value.DynEntCount[0],
                    DynEntityPose.SerializedSize,
                    value.DynEntPoseListPointers[0].Untyped,
                    "ClipMap.DynEntPoseList[0]"),
                FreezeRuntime(
                    value.DynEntCount[1],
                    DynEntityPose.SerializedSize,
                    value.DynEntPoseListPointers[1].Untyped,
                    "ClipMap.DynEntPoseList[1]")
            ];
            LinkStorageSymbol?[] dynClients =
            [
                FreezeRuntime(
                    value.DynEntCount[0],
                    DynEntityClient.SerializedSize,
                    value.DynEntClientListPointers[0].Untyped,
                    "ClipMap.DynEntClientList[0]"),
                FreezeRuntime(
                    value.DynEntCount[1],
                    DynEntityClient.SerializedSize,
                    value.DynEntClientListPointers[1].Untyped,
                    "ClipMap.DynEntClientList[1]")
            ];
            LinkStorageSymbol?[] dynCollisions =
            [
                FreezeRuntime(
                    value.DynEntCount[0],
                    DynEntityColl.SerializedSize,
                    value.DynEntCollListPointers[0].Untyped,
                    "ClipMap.DynEntCollList[0]"),
                FreezeRuntime(
                    value.DynEntCount[1],
                    DynEntityColl.SerializedSize,
                    value.DynEntCollListPointers[1].Untyped,
                    "ClipMap.DynEntCollList[1]")
            ];

            return new FrozenStorage(
                planes,
                staticModels,
                materials,
                brushSides,
                brushEdges,
                nodes,
                leafs,
                leafBrushNodes,
                leafBrushes,
                leafSurfaces,
                verts,
                triIndices,
                triEdges,
                borders,
                partitions,
                aabbTrees,
                cmodels,
                brushes,
                brushBounds,
                brushContents,
                smodelNodes,
                mapEnts,
                Array.AsReadOnly(dynDefinitions),
                Array.AsReadOnly(dynPoses),
                Array.AsReadOnly(dynClients),
                Array.AsReadOnly(dynCollisions));
        }

        private LinkStorageTarget? FreezeStaticModels(ClipMapAsset value)
        {
            var dependencies = new AssetDependency?[value.StaticModelList.Count];
            for (int index = 0; index < dependencies.Length; index++)
            {
                ClipStaticModel model = value.StaticModelList[index] ??
                    throw NullRow("ClipMap.StaticModelList", index);
                dependencies[index] = FreezeProviderDependency(
                    model.XModelPointer.Untyped,
                    model.XModel,
                    XAssetType.XModel,
                    $"ClipMap.StaticModelList[{index}].XModel");
            }

            return FreezeArray(
                value.StaticModelListPointer.Untyped,
                value.StaticModelList,
                ClipStaticModel.SerializedSize,
                4,
                static (writer, item, index) =>
                {
                    writer.Skip(sizeof(int));
                    WriteVec3(writer, item.Origin);
                    RequireCount(
                        item.InvScaledAxis,
                        3,
                        $"ClipMap.StaticModelList[{index}].InvScaledAxis");
                    foreach (Vec3 axis in item.InvScaledAxis)
                        WriteVec3(writer, axis);
                    WriteVec3(writer, item.AbsMin);
                    WriteVec3(writer, item.AbsMax);
                },
                "ClipMap.StaticModelList",
                operations: (table, addend) => dependencies
                    .Select((dependency, index) => (dependency, index))
                    .Where(item => item.dependency is not null)
                    .Select(item => (LinkOperation)new ProviderLinkOperation(
                        new LinkStorageCell(
                            table,
                            checked(addend + item.index * ClipStaticModel.SerializedSize)),
                        item.dependency!.Value)));
        }

        private LinkStorageTarget? FreezeMaterials(ClipMapAsset value)
        {
            var names = new LinkStorageSymbol?[value.Materials.Count];
            for (int index = 0; index < names.Length; index++)
            {
                ClipMaterial material = value.Materials[index] ??
                    throw NullRow("ClipMap.Materials", index);
                names[index] = FreezeOptionalXString(
                    material.Name,
                    material.NamePointer.Untyped,
                    $"ClipMap.Materials[{index}].Name");
            }

            return FreezeArray(
                value.MaterialsPointer.Untyped,
                value.Materials,
                ClipMaterial.SerializedSize,
                4,
                static (writer, item, _) =>
                {
                    writer.Skip(sizeof(int));
                    writer.WriteInt32(item.SurfaceFlags);
                    writer.WriteInt32(item.Contents);
                },
                "ClipMap.Materials",
                operations: (table, addend) => names
                    .Select((name, index) => (name, index))
                    .Where(item => item.name is not null)
                    .Select(item => (LinkOperation)new XStringLinkOperation(
                        new LinkStorageCell(
                            table,
                            checked(addend + item.index * ClipMaterial.SerializedSize)),
                        LinkStorageView.Whole(item.name!),
                        CanMaterializeRoot: true,
                        $"ClipMap.Materials[{item.index}].Name")));
        }

        private LinkStorageTarget? FreezeBrushSides(
            ClipMapAsset value,
            out LinkStorageTarget?[] planes)
        {
            planes = value.BrushSides
                .Select((side, index) => FreezePlane(
                    side ?? throw NullRow("ClipMap.BrushSides", index),
                    $"ClipMap.BrushSides[{index}].Plane"))
                .ToArray();
            return FreezeSideArray(
                value.BrushSidesPointer.Untyped,
                value.BrushSides,
                planes,
                "ClipMap.BrushSides",
                allowInteriorView: true,
                allowStandaloneDetach: true);
        }

        private LinkStorageTarget? FreezeNodes(ClipMapAsset value)
        {
            LinkStorageTarget?[] planes = value.Nodes
                .Select((node, index) => FreezePlane(
                    node ?? throw NullRow("ClipMap.Nodes", index),
                    $"ClipMap.Nodes[{index}].Plane"))
                .ToArray();
            return FreezeArray(
                value.NodesPointer.Untyped,
                value.Nodes,
                CNode.SerializedSize,
                4,
                static (writer, item, index) =>
                {
                    writer.Skip(sizeof(int));
                    RequireCount(
                        item.Children,
                        2,
                        $"ClipMap.Nodes[{index}].Children");
                    writer.WriteUInt16(unchecked((ushort)item.Children[0]));
                    writer.WriteUInt16(unchecked((ushort)item.Children[1]));
                },
                "ClipMap.Nodes",
                operations: (table, addend) => CreateDirectRowOperations(
                    table,
                    addend,
                    CNode.SerializedSize,
                    0,
                    planes,
                    "ClipMap.Nodes"));
        }

        private LinkStorageTarget? FreezeLeafBrushNodes(ClipMapAsset value)
        {
            var children = new LinkStorageTarget?[value.LeafBrushNodes.Count];
            for (int index = 0; index < children.Length; index++)
            {
                CLeafBrushNode node = value.LeafBrushNodes[index] ??
                    throw NullRow("ClipMap.LeafBrushNodes", index);
                if (node.LeafBrushCount <= 0)
                    continue;
                children[index] = FreezeArray(
                    node.Data.BrushesPointer.Untyped,
                    node.Data.Brushes,
                    sizeof(ushort),
                    2,
                    static (writer, item, _) => writer.WriteUInt16(item),
                    $"ClipMap.LeafBrushNodes[{index}].Brushes",
                    allowInteriorView: true,
                    allowStandaloneDetach: true);
            }

            return FreezeArray(
                value.LeafBrushNodesPointer.Untyped,
                value.LeafBrushNodes,
                CLeafBrushNode.SerializedSize,
                4,
                WriteLeafBrushNode,
                "ClipMap.LeafBrushNodes",
                operations: (table, addend) => CreateDirectRowOperations(
                    table,
                    addend,
                    CLeafBrushNode.SerializedSize,
                    0x08,
                    children,
                    "ClipMap.LeafBrushNodes"));
        }

        private LinkStorageTarget? FreezeBorders(
            XPointerReference pointer,
            IReadOnlyList<CollisionBorder> values,
            string fieldPath) =>
            FreezeArray(
                pointer,
                values,
                CollisionBorder.SerializedSize,
                4,
                WriteBorder,
                fieldPath,
                allowInteriorView: true,
                allowStandaloneDetach: true);

        private LinkStorageTarget? FreezePartitions(
            ClipMapAsset value,
            LinkStorageTarget? rootBorders)
        {
            var borders = new LinkStorageTarget?[value.Partitions.Count];
            int borderOffset = 0;
            for (int index = 0; index < borders.Length; index++)
            {
                CollisionPartition partition = value.Partitions[index] ??
                    throw NullRow("ClipMap.Partitions", index);
                string fieldPath = $"ClipMap.Partitions[{index}].Borders";
                int byteOffset = checked(
                    borderOffset * CollisionBorder.SerializedSize);
                int byteCount = checked(
                    partition.Borders.Count * CollisionBorder.SerializedSize);
                if (byteCount == 0 &&
                    partition.BordersPointer.Type == PointerType.Null)
                {
                    borders[index] = null;
                }
                else
                {
                    LinkStorageTarget expected = ContainedView(
                        rootBorders,
                        byteOffset,
                        byteCount,
                        fieldPath);
                    var writer = new LinkTemplateWriter(byteCount);
                    for (int border = 0; border < partition.Borders.Count; border++)
                    {
                        CollisionBorder item = partition.Borders[border] ??
                            throw NullRow(fieldPath, border);
                        WriteBorder(writer, item, border);
                    }
                    borders[index] = _freeze.FreezeContainedStorageView(
                        partition.BordersPointer.Untyped,
                        expected,
                        writer.Complete(),
                        XFileBlockType.LARGE,
                        alignment: 4,
                        operations: null,
                        fieldPath,
                        allowCapturedEndBoundary: true);
                }
                borderOffset = checked(borderOffset + partition.BorderCount);
            }

            if (borderOffset != value.Borders.Count)
            {
                throw new InvalidDataException(
                    "ClipMap.Partitions border slices do not exactly cover ClipMap.Borders.");
            }

            return FreezeArray(
                value.PartitionsPointer.Untyped,
                value.Partitions,
                CollisionPartition.SerializedSize,
                4,
                static (writer, item, _) =>
                {
                    writer.WriteByte(item.TriCount);
                    writer.WriteByte(item.BorderCount);
                    writer.WriteByte(item.FirstVertSegment);
                    writer.WriteByte(item.Pad03);
                    writer.WriteInt32(item.FirstTri);
                    writer.Skip(sizeof(int));
                },
                "ClipMap.Partitions",
                operations: (table, addend) => CreateDirectRowOperations(
                    table,
                    addend,
                    CollisionPartition.SerializedSize,
                    0x08,
                    borders,
                    "ClipMap.Partitions"));
        }

        private LinkStorageTarget? FreezeBrushes(
            ClipMapAsset value,
            LinkStorageTarget? brushSides,
            LinkStorageTarget? brushEdges,
            IReadOnlyList<LinkStorageTarget?> brushSidePlanes)
        {
            var sideViews = new LinkStorageTarget?[value.Brushes.Count];
            var edgeViews = new LinkStorageTarget?[value.Brushes.Count];
            int sideOffset = 0;
            int edgeOffset = 0;
            for (int index = 0; index < value.Brushes.Count; index++)
            {
                CBrush brush = value.Brushes[index] ??
                    throw NullRow("ClipMap.Brushes", index);
                int sideByteCount = checked(brush.Sides.Count * CBrushSide.SerializedSize);
                int edgeByteCount = RequiredAdjacencyByteCount(brush);
                sideViews[index] = FreezeBrushSideView(
                    brush,
                    index,
                    brushSides,
                    brushSidePlanes,
                    sideOffset,
                    sideByteCount);
                edgeViews[index] = FreezeBrushEdgeView(
                    brush,
                    index,
                    brushEdges,
                    edgeOffset,
                    edgeByteCount);
                sideOffset = checked(sideOffset + brush.NumSides);
                edgeOffset = checked(edgeOffset + edgeByteCount);
            }

            return FreezeArray(
                value.BrushesPointer.Untyped,
                value.Brushes,
                CBrush.SerializedSize,
                128,
                WriteBrush,
                "ClipMap.Brushes",
                operations: (table, addend) => value.Brushes
                    .SelectMany((_, index) =>
                    {
                        int row = checked(addend + index * CBrush.SerializedSize);
                        return DirectOperations(
                            table,
                            (row + 0x04, sideViews[index], $"ClipMap.Brushes[{index}].Sides"),
                            (row + 0x08, edgeViews[index], $"ClipMap.Brushes[{index}].BaseAdjacentSide"));
                    }));
        }

        private LinkStorageTarget? FreezeBrushSideView(
            CBrush brush,
            int brushIndex,
            LinkStorageTarget? root,
            IReadOnlyList<LinkStorageTarget?> rootPlanes,
            int sideOffset,
            int byteCount)
        {
            string fieldPath = $"ClipMap.Brushes[{brushIndex}].Sides";
            if (byteCount == 0)
            {
                if (brush.SidesPointer.CellAddress is null ||
                    brush.SidesPointer.Type == PointerType.Null)
                {
                    return null;
                }
            }
            int byteOffset = checked(sideOffset * CBrushSide.SerializedSize);
            LinkStorageTarget expected = ContainedView(
                root,
                byteOffset,
                byteCount,
                fieldPath);
            if (sideOffset < 0 ||
                sideOffset > rootPlanes.Count - brush.Sides.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPath} lies outside the canonical BrushSides plane targets.");
            }
            LinkStorageTarget?[] planes = rootPlanes
                .Skip(sideOffset)
                .Take(brush.Sides.Count)
                .ToArray();
            var writer = new LinkTemplateWriter(byteCount);
            for (int index = 0; index < brush.Sides.Count; index++)
            {
                CBrushSide side = brush.Sides[index] ??
                    throw NullRow(fieldPath, index);
                WriteSide(writer, side);
            }
            return _freeze.FreezeContainedStorageView(
                brush.SidesPointer.Untyped,
                expected,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                operations: (table, addend) => CreateDirectRowOperations(
                    table,
                    addend,
                    CBrushSide.SerializedSize,
                    0,
                    planes,
                    fieldPath),
                fieldPath);
        }

        private LinkStorageTarget? FreezeBrushEdgeView(
            CBrush brush,
            int brushIndex,
            LinkStorageTarget? root,
            int byteOffset,
            int byteCount)
        {
            string fieldPath =
                $"ClipMap.Brushes[{brushIndex}].BaseAdjacentSide";
            if (byteCount == 0)
            {
                if (brush.BaseAdjacentSidePointer.CellAddress is null ||
                    brush.BaseAdjacentSidePointer.Type == PointerType.Null)
                {
                    return null;
                }
            }
            LinkStorageTarget expected = ContainedView(
                root,
                byteOffset,
                byteCount,
                fieldPath);
            return _freeze.FreezeContainedStorageView(
                brush.BaseAdjacentSidePointer.Untyped,
                expected,
                brush.BaseAdjacentSide.ToArray(),
                XFileBlockType.LARGE,
                alignment: 1,
                operations: null,
                fieldPath);
        }

        private LinkStorageTarget? FreezeSideArray(
            XPointerReference pointer,
            IReadOnlyList<CBrushSide> values,
            IReadOnlyList<LinkStorageTarget?> planes,
            string fieldPath,
            bool allowInteriorView,
            bool allowStandaloneDetach = false)
        {
            return FreezeArray(
                pointer,
                values,
                CBrushSide.SerializedSize,
                4,
                static (writer, item, _) => WriteSide(writer, item),
                fieldPath,
                operations: (table, addend) => CreateDirectRowOperations(
                    table,
                    addend,
                    CBrushSide.SerializedSize,
                    0,
                    planes,
                    fieldPath),
                allowInteriorView,
                allowStandaloneDetach);
        }

        private LinkStorageTarget? FreezePlane(CBrushSide side, string fieldPath) =>
            FreezePlane(side.PlanePointer.Untyped, side.Plane, fieldPath);

        private LinkStorageTarget? FreezePlane(CNode node, string fieldPath) =>
            FreezePlane(node.PlanePointer.Untyped, node.Plane, fieldPath);

        private LinkStorageTarget? FreezePlane(
            XPointerReference pointer,
            CPlane? plane,
            string fieldPath)
        {
            if (plane is null)
            {
                EnsureAbsentCapturedPointer(pointer, fieldPath);
                return null;
            }

            var writer = new LinkTemplateWriter(CPlane.SerializedSize);
            WritePlane(writer, plane, 0);
            return _freeze.FreezeStorageView(
                pointer,
                writer.Complete(),
                XFileBlockType.LARGE,
                alignment: 4,
                operations: null,
                fieldPath,
                allowStandaloneDetach: true);
        }

        private LinkStorageTarget? FreezeDynDefinitions(
            ClipMapAsset value,
            int listIndex)
        {
            IReadOnlyList<DynEntityDef> definitions = value.DynEntDefList[listIndex];
            var dependencies = new DynDependencies[definitions.Count];
            for (int index = 0; index < definitions.Count; index++)
            {
                DynEntityDef definition = definitions[index] ??
                    throw NullRow($"ClipMap.DynEntDefList[{listIndex}]", index);
                dependencies[index] = new DynDependencies(
                    FreezeProviderDependency(
                        definition.XModelPointer.Untyped,
                        definition.XModel,
                        XAssetType.XModel,
                        $"ClipMap.DynEntDefList[{listIndex}][{index}].XModel"),
                    FreezeProviderDependency(
                        definition.DestroyFxPointer.Untyped,
                        definition.DestroyFx,
                        XAssetType.Fx,
                        $"ClipMap.DynEntDefList[{listIndex}][{index}].DestroyFx"),
                    FreezeProviderDependency(
                        definition.PhysPresetPointer.Untyped,
                        definition.PhysPreset,
                        XAssetType.PhysPreset,
                        $"ClipMap.DynEntDefList[{listIndex}][{index}].PhysPreset"));
            }

            return FreezeArray(
                value.DynEntDefListPointers[listIndex].Untyped,
                definitions,
                DynEntityDef.SerializedSize,
                4,
                WriteDynEntityDef,
                $"ClipMap.DynEntDefList[{listIndex}]",
                operations: (table, addend) => definitions
                    .SelectMany((_, index) =>
                    {
                        int row = checked(addend + index * DynEntityDef.SerializedSize);
                        DynDependencies item = dependencies[index];
                        return ProviderOperations(
                            table,
                            (row + 0x20, item.XModel),
                            (row + 0x28, item.DestroyFx),
                            (row + 0x2c, item.PhysPreset));
                    }));
        }

        private LinkStorageSymbol? FreezeRuntime(
            int count,
            int stride,
            XPointerReference pointer,
            string fieldPath)
        {
            if (count == 0)
            {
                EnsureAbsentCapturedPointer(pointer, fieldPath);
                return null;
            }
            if (pointer.CellAddress is not null &&
                pointer.Type is not (PointerType.Inline or PointerType.Insert))
            {
                throw new InvalidDataException(
                    $"{fieldPath} retains unsupported captured {pointer.Type} runtime storage.");
            }
            return LinkStorageSymbol.SourceFree(
                XFileBlockType.RUNTIME,
                checked(count * stride),
                alignment: 4,
                LinkMaterializationKind.RuntimeZeroFill);
        }

        private LinkStorageTarget? FreezeArray<T>(
            XPointerReference pointer,
            IReadOnlyList<T> values,
            int stride,
            int alignment,
            Action<LinkTemplateWriter, T, int> write,
            string fieldPath,
            Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>>? operations = null,
            bool allowInteriorView = false,
            bool allowStandaloneDetach = false)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 && pointer.Type == PointerType.Null)
                return null;

            var writer = new LinkTemplateWriter(checked(values.Count * stride));
            for (int index = 0; index < values.Count; index++)
            {
                T item = values[index] ?? throw NullRow(fieldPath, index);
                write(writer, item, index);
            }
            byte[] bytes = writer.Complete();
            return allowInteriorView
                ? _freeze.FreezeStorageView(
                    pointer,
                    bytes,
                    XFileBlockType.LARGE,
                    alignment,
                    operations,
                    fieldPath,
                    allowStandaloneDetach)
                : _freeze.FreezeStorage(
                    pointer,
                    bytes,
                    XFileBlockType.LARGE,
                    alignment,
                    operations,
                    fieldPath);
        }

        private LinkStorageSymbol? FreezeOptionalXString(
            string? value,
            XPointerReference pointer,
            string fieldPath)
        {
            if (value is null)
            {
                EnsureAbsentCapturedPointer(pointer, fieldPath);
                return null;
            }
            return _freeze.FreezeRequiredXString(value, pointer, fieldPath);
        }

        private static void EnsureAbsentCapturedPointer(
            XPointerReference pointer,
            string fieldPath)
        {
            if (pointer.CellAddress is not null && pointer.Type != PointerType.Null)
            {
                throw new InvalidDataException(
                    $"{fieldPath} retains captured non-null pointer storage without semantic data.");
            }
        }

        private static LinkStorageTarget ContainedView(
            LinkStorageTarget? root,
            int relativeAddend,
            int byteCount,
            string fieldPath)
        {
            if (root is not { } owner)
                throw new InvalidDataException($"{fieldPath} has no earlier root storage.");
            if (relativeAddend < 0 || byteCount < 0 ||
                relativeAddend > owner.View.Length - byteCount)
            {
                throw new InvalidDataException(
                    $"{fieldPath} lies outside the shared root storage view.");
            }
            return new LinkStorageTarget(
                new LinkStorageView(
                    owner.View.Storage,
                    checked(owner.View.Addend + relativeAddend),
                    byteCount),
                CanMaterializeRoot: false);
        }

        private static IEnumerable<LinkOperation> CreateDirectRowOperations(
            LinkStorageSymbol table,
            int addend,
            int stride,
            int pointerOffset,
            IReadOnlyList<LinkStorageTarget?> targets,
            string fieldPath) =>
            targets
                .Select((target, index) => (target, index))
                .Where(item => item.target is not null)
                .Select(item => (LinkOperation)new DirectStorageLinkOperation(
                    new LinkStorageCell(
                        table,
                        checked(addend + item.index * stride + pointerOffset)),
                    item.target!.Value.View,
                    item.target.Value.CanMaterializeRoot,
                    $"{fieldPath}[{item.index}]"));

        private static IEnumerable<LinkOperation> DirectOperations(
            LinkStorageSymbol owner,
            params (int Offset, LinkStorageTarget? Target, string FieldPath)[] values)
        {
            foreach ((int offset, LinkStorageTarget? target, string fieldPath) in values)
            {
                if (target is { } value)
                {
                    yield return new DirectStorageLinkOperation(
                        new LinkStorageCell(owner, offset),
                        value.View,
                        value.CanMaterializeRoot,
                        fieldPath);
                }
            }
        }

        private static IEnumerable<LinkOperation> ProviderOperations(
            LinkStorageSymbol owner,
            params (int Offset, AssetDependency? Dependency)[] values)
        {
            foreach ((int offset, AssetDependency? dependency) in values)
            {
                if (dependency is { } value)
                {
                    yield return new ProviderLinkOperation(
                        new LinkStorageCell(owner, offset),
                        value);
                }
            }
        }
    }

    private sealed record FrozenStorage(
        LinkStorageTarget? Planes,
        LinkStorageTarget? StaticModels,
        LinkStorageTarget? Materials,
        LinkStorageTarget? BrushSides,
        LinkStorageTarget? BrushEdges,
        LinkStorageTarget? Nodes,
        LinkStorageTarget? Leafs,
        LinkStorageTarget? LeafBrushNodes,
        LinkStorageTarget? LeafBrushes,
        LinkStorageTarget? LeafSurfaces,
        LinkStorageTarget? Verts,
        LinkStorageTarget? TriIndices,
        LinkStorageTarget? TriEdgeIsWalkable,
        LinkStorageTarget? Borders,
        LinkStorageTarget? Partitions,
        LinkStorageTarget? AabbTrees,
        LinkStorageTarget? CModels,
        LinkStorageTarget? Brushes,
        LinkStorageTarget? BrushBounds,
        LinkStorageTarget? BrushContents,
        LinkStorageTarget? SModelNodes,
        AssetDependency? MapEnts,
        IReadOnlyList<LinkStorageTarget?> DynEntDefinitions,
        IReadOnlyList<LinkStorageSymbol?> DynEntPoses,
        IReadOnlyList<LinkStorageSymbol?> DynEntClients,
        IReadOnlyList<LinkStorageSymbol?> DynEntCollisions);

    private readonly record struct DynDependencies(
        AssetDependency? XModel,
        AssetDependency? DestroyFx,
        AssetDependency? PhysPreset);

    private static void WritePlane(
        LinkTemplateWriter writer,
        CPlane value,
        int index)
    {
        WriteVec3(writer, value.Normal);
        WriteSingle(writer, value.Dist);
        writer.WriteByte(value.Type);
        writer.WriteByte(value.SignBits);
        RequireOptionalFixedCount(value.Pad12, 2, $"CPlane[{index}].Pad12");
        if (value.Pad12.Count == 0)
            writer.Skip(2);
        else
            writer.WriteBytes(value.Pad12.ToArray());
    }

    private static void WriteSide(LinkTemplateWriter writer, CBrushSide value)
    {
        writer.Skip(sizeof(int));
        writer.WriteUInt16(value.MaterialNum);
        writer.WriteByte(value.FirstAdjacentSideOffset);
        writer.WriteByte(value.EdgeCount);
    }

    private static void WriteLeaf(
        LinkTemplateWriter writer,
        CLeaf value,
        int index)
    {
        writer.WriteUInt16(value.FirstCollAabbIndex);
        writer.WriteUInt16(value.CollAabbCount);
        writer.WriteInt32(value.BrushContents);
        writer.WriteInt32(value.TerrainContents);
        WriteVec3(writer, value.Mins);
        WriteVec3(writer, value.Maxs);
        writer.WriteInt32(value.LeafBrushNode);
    }

    private static void WriteLeafBrushNode(
        LinkTemplateWriter writer,
        CLeafBrushNode value,
        int index)
    {
        writer.WriteByte(value.Axis);
        writer.WriteByte(value.Pad01);
        writer.WriteUInt16(unchecked((ushort)value.LeafBrushCount));
        writer.WriteInt32(value.Contents);
        if (value.LeafBrushCount > 0)
        {
            writer.Skip(sizeof(int));
            if (value.Data.LeafUnionPad.Count == 0)
                writer.Skip(8);
            else
                writer.WriteBytes(value.Data.LeafUnionPad.ToArray());
            return;
        }

        CLeafBrushNodeChildren children = value.Data.Children ??
            throw new InvalidDataException(
                $"ClipMap.LeafBrushNodes[{index}] requires the child union arm.");
        WriteSingle(writer, children.Dist);
        WriteSingle(writer, children.Range);
        foreach (ushort offset in children.ChildOffsets)
            writer.WriteUInt16(offset);
    }

    private static void WriteBorder(
        LinkTemplateWriter writer,
        CollisionBorder value,
        int index)
    {
        RequireCount(value.DistEq, 3, $"CollisionBorder[{index}].DistEq");
        foreach (float item in value.DistEq)
            WriteSingle(writer, item);
        WriteSingle(writer, value.ZBase);
        WriteSingle(writer, value.ZSlope);
        WriteSingle(writer, value.Start);
        WriteSingle(writer, value.Length);
    }

    private static void WriteAabbTree(
        LinkTemplateWriter writer,
        CollisionAabbTree value,
        int index)
    {
        WriteVec3(writer, value.Origin);
        writer.WriteUInt16(value.MaterialIndex);
        writer.WriteUInt16(value.ChildCount);
        WriteVec3(writer, value.HalfSize);
        writer.WriteInt32(value.FirstChildOrPartitionIndex);
    }

    private static void WriteCModel(
        LinkTemplateWriter writer,
        CModel value,
        int index)
    {
        WriteVec3(writer, value.Mins);
        WriteVec3(writer, value.Maxs);
        WriteSingle(writer, value.Radius);
        WriteLeaf(writer, value.Leaf, index);
    }

    private static void WriteBrush(
        LinkTemplateWriter writer,
        CBrush value,
        int index)
    {
        writer.WriteUInt16(value.NumSides);
        writer.WriteUInt16(value.GlassPieceIndex);
        writer.Skip(2 * sizeof(int));
        foreach (short item in value.AxialMaterialNum)
            writer.WriteUInt16(unchecked((ushort)item));
        writer.WriteBytes(value.FirstAdjacentSideOffsets.ToArray());
        writer.WriteBytes(value.EdgeCount.ToArray());
    }

    private static void WriteBounds(LinkTemplateWriter writer, Bounds value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteVec3(writer, value.MidPoint);
        WriteVec3(writer, value.HalfSize);
    }

    private static void WriteSModelNode(
        LinkTemplateWriter writer,
        SModelAabbNode value,
        int index)
    {
        WriteBounds(writer, value.Bounds);
        writer.WriteUInt16(value.FirstChild);
        writer.WriteUInt16(value.ChildCount);
    }

    private static void WriteDynEntityDef(
        LinkTemplateWriter writer,
        DynEntityDef value,
        int index)
    {
        writer.WriteInt32(value.Type);
        RequireCount(value.Pose.Quat, 4, $"DynEntityDef[{index}].Pose.Quat");
        foreach (float item in value.Pose.Quat)
            WriteSingle(writer, item);
        WriteVec3(writer, value.Pose.Origin);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(value.BrushModel);
        writer.WriteUInt16(value.PhysicsBrushModel);
        writer.Skip(2 * sizeof(int));
        writer.WriteInt32(value.Health);
        WriteVec3(writer, value.Mass.CenterOfMass);
        WriteVec3(writer, value.Mass.MomentsOfInertia);
        WriteVec3(writer, value.Mass.ProductsOfInertia);
        writer.WriteInt32(value.Contents);
    }

    private static void WriteVec3(LinkTemplateWriter writer, Vec3 value)
    {
        WriteSingle(writer, value.X);
        WriteSingle(writer, value.Y);
        WriteSingle(writer, value.Z);
    }

    private static void WriteSingle(LinkTemplateWriter writer, float value) =>
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));

    private static int RequiredAdjacencyByteCount(CBrush brush)
    {
        int byteCount = 0;
        for (int index = 0; index < brush.FirstAdjacentSideOffsets.Count; index++)
        {
            byteCount = Math.Max(
                byteCount,
                checked(brush.FirstAdjacentSideOffsets[index] + brush.EdgeCount[index]));
        }
        foreach (CBrushSide side in brush.Sides)
        {
            byteCount = Math.Max(
                byteCount,
                checked(side.FirstAdjacentSideOffset + side.EdgeCount));
        }
        return byteCount;
    }

    private static int TriEdgeByteCount(int triCount)
    {
        if (triCount < 0)
            throw new InvalidDataException("ClipMap.TriCount cannot be negative.");
        return checked(((triCount * 3 + 0x1f) >> 5) << 2);
    }

    private static void ValidateOwned(ClipMapAsset value)
    {
        RequireCount(value.Planes, value.PlaneCount, "ClipMap.Planes");
        RequireCount(value.StaticModelList, value.NumStaticModels, "ClipMap.StaticModelList");
        RequireCount(value.Materials, value.NumMaterials, "ClipMap.Materials");
        RequireCount(value.BrushSides, value.NumBrushSides, "ClipMap.BrushSides");
        RequireCount(value.BrushEdges, value.NumBrushEdges, "ClipMap.BrushEdges");
        RequireCount(value.Nodes, value.NumNodes, "ClipMap.Nodes");
        RequireCount(value.Leafs, value.NumLeafs, "ClipMap.Leafs");
        RequireCount(value.LeafBrushNodes, value.LeafBrushNodesCount, "ClipMap.LeafBrushNodes");
        RequireCount(value.LeafBrushes, value.NumLeafBrushes, "ClipMap.LeafBrushes");
        RequireCount(value.LeafSurfaces, value.NumLeafSurfaces, "ClipMap.LeafSurfaces");
        RequireCount(value.Verts, value.VertCount, "ClipMap.Verts");
        RequireCount(value.TriIndices, checked(value.TriCount * 3), "ClipMap.TriIndices");
        RequireCount(
            value.TriEdgeIsWalkable,
            TriEdgeByteCount(value.TriCount),
            "ClipMap.TriEdgeIsWalkable");
        RequireCount(value.Borders, value.BorderCount, "ClipMap.Borders");
        RequireCount(value.Partitions, value.PartitionCount, "ClipMap.Partitions");
        RequireCount(value.AabbTrees, value.AabbTreeCount, "ClipMap.AabbTrees");
        RequireCount(value.CModels, value.NumSubModels, "ClipMap.CModels");
        RequireCount(value.Brushes, value.NumBrushes, "ClipMap.Brushes");
        RequireCount(value.BrushBounds, value.NumBrushes, "ClipMap.BrushBounds");
        RequireCount(value.BrushContents, value.NumBrushes, "ClipMap.BrushContents");
        RequireCount(value.SModelNodes, value.SModelNodeCount, "ClipMap.SModelNodes");
        RequireCount(value.DynEntCount, 2, "ClipMap.DynEntCount");
        RequireCount(value.DynEntDefListPointers, 2, "ClipMap.DynEntDefListPointers");
        RequireCount(value.DynEntDefList, 2, "ClipMap.DynEntDefList");
        RequireCount(value.DynEntPoseListPointers, 2, "ClipMap.DynEntPoseListPointers");
        RequireCount(value.DynEntPoseList, 2, "ClipMap.DynEntPoseList");
        RequireCount(value.DynEntClientListPointers, 2, "ClipMap.DynEntClientListPointers");
        RequireCount(value.DynEntClientList, 2, "ClipMap.DynEntClientList");
        RequireCount(value.DynEntCollListPointers, 2, "ClipMap.DynEntCollListPointers");
        RequireCount(value.DynEntCollList, 2, "ClipMap.DynEntCollList");
        RequireOptionalFixedCount(value.PadD0ToFF, 0x30, "ClipMap.PadD0ToFF");

        for (int index = 0; index < value.Planes.Count; index++)
            ValidatePlane(value.Planes[index], $"ClipMap.Planes[{index}]");
        for (int index = 0; index < value.StaticModelList.Count; index++)
        {
            ClipStaticModel item = value.StaticModelList[index] ??
                throw NullRow("ClipMap.StaticModelList", index);
            RequireCount(
                item.InvScaledAxis,
                3,
                $"ClipMap.StaticModelList[{index}].InvScaledAxis");
        }
        for (int index = 0; index < value.BrushSides.Count; index++)
            ValidateSide(value.BrushSides[index], $"ClipMap.BrushSides[{index}]");
        for (int index = 0; index < value.Nodes.Count; index++)
        {
            CNode node = value.Nodes[index] ?? throw NullRow("ClipMap.Nodes", index);
            RequireCount(node.Children, 2, $"ClipMap.Nodes[{index}].Children");
            if (node.Plane is not null)
                ValidatePlane(node.Plane, $"ClipMap.Nodes[{index}].Plane");
        }
        for (int index = 0; index < value.LeafBrushNodes.Count; index++)
        {
            CLeafBrushNode node = value.LeafBrushNodes[index] ??
                throw NullRow("ClipMap.LeafBrushNodes", index);
            if (node.Data is null)
            {
                throw new InvalidDataException(
                    $"ClipMap.LeafBrushNodes[{index}].Data cannot be null.");
            }
            if (node.LeafBrushCount > 0)
            {
                RequireCount(
                    node.Data.Brushes,
                    node.LeafBrushCount,
                    $"ClipMap.LeafBrushNodes[{index}].Brushes");
                RequireOptionalFixedCount(
                    node.Data.LeafUnionPad,
                    8,
                    $"ClipMap.LeafBrushNodes[{index}].LeafUnionPad");
                if (node.Data.Children is not null)
                {
                    throw new InvalidDataException(
                        $"ClipMap.LeafBrushNodes[{index}] selects both union arms.");
                }
            }
            else
            {
                if (node.Data.Brushes.Count != 0 || node.Data.Children is null)
                {
                    throw new InvalidDataException(
                        $"ClipMap.LeafBrushNodes[{index}] requires only the child union arm.");
                }
                RequireCount(
                    node.Data.Children.ChildOffsets,
                    2,
                    $"ClipMap.LeafBrushNodes[{index}].Children.ChildOffsets");
            }
        }
        for (int index = 0; index < value.Borders.Count; index++)
            ValidateBorder(value.Borders[index], $"ClipMap.Borders[{index}]");
        for (int index = 0; index < value.Partitions.Count; index++)
        {
            CollisionPartition partition = value.Partitions[index] ??
                throw NullRow("ClipMap.Partitions", index);
            RequireCount(
                partition.Borders,
                partition.BorderCount,
                $"ClipMap.Partitions[{index}].Borders");
            for (int border = 0; border < partition.Borders.Count; border++)
            {
                ValidateBorder(
                    partition.Borders[border],
                    $"ClipMap.Partitions[{index}].Borders[{border}]");
            }
        }
        for (int index = 0; index < value.CModels.Count; index++)
        {
            CModel model = value.CModels[index] ??
                throw NullRow("ClipMap.CModels", index);
            if (model.Leaf is null)
            {
                throw new InvalidDataException(
                    $"ClipMap.CModels[{index}].Leaf cannot be null.");
            }
        }
        for (int index = 0; index < value.Brushes.Count; index++)
            ValidateBrush(value.Brushes[index], index);
        for (int index = 0; index < value.SModelNodes.Count; index++)
        {
            SModelAabbNode node = value.SModelNodes[index] ??
                throw NullRow("ClipMap.SModelNodes", index);
            if (node.Bounds is null)
            {
                throw new InvalidDataException(
                    $"ClipMap.SModelNodes[{index}].Bounds cannot be null.");
            }
        }

        int sideOffset = 0;
        int edgeOffset = 0;
        foreach ((CBrush brush, int index) in
            value.Brushes.Select((brush, index) => (brush, index)))
        {
            int edgeCount = RequiredAdjacencyByteCount(brush);
            if (sideOffset > value.BrushSides.Count - brush.Sides.Count ||
                !brush.Sides.Zip(
                    value.BrushSides.Skip(sideOffset),
                    SideEquals).All(equal => equal))
            {
                throw new InvalidDataException(
                    $"ClipMap.Brushes[{index}].Sides is not its ordered shared BrushSides view.");
            }
            if (edgeOffset > value.BrushEdges.Count - edgeCount ||
                !brush.BaseAdjacentSide.SequenceEqual(
                    value.BrushEdges.Skip(edgeOffset).Take(edgeCount)))
            {
                throw new InvalidDataException(
                    $"ClipMap.Brushes[{index}].BaseAdjacentSide is not its ordered shared BrushEdges view.");
            }
            sideOffset = checked(sideOffset + brush.NumSides);
            edgeOffset = checked(edgeOffset + edgeCount);
        }

        for (int list = 0; list < 2; list++)
        {
            int count = value.DynEntCount[list];
            RequireCount(value.DynEntDefList[list], count, $"ClipMap.DynEntDefList[{list}]");
            RequireOptionalCount(value.DynEntPoseList[list], count, $"ClipMap.DynEntPoseList[{list}]");
            RequireOptionalCount(value.DynEntClientList[list], count, $"ClipMap.DynEntClientList[{list}]");
            RequireOptionalCount(value.DynEntCollList[list], count, $"ClipMap.DynEntCollList[{list}]");
            if (value.DynEntPoseList[list].Any(item => !IsZero(item)) ||
                value.DynEntClientList[list].Any(item => !IsZero(item)) ||
                value.DynEntCollList[list].Any(item => !IsZero(item)))
            {
                throw new InvalidDataException(
                    $"ClipMap dynamic runtime list {list} must remain source-free zero storage.");
            }
            for (int index = 0; index < value.DynEntDefList[list].Count; index++)
            {
                DynEntityDef definition = value.DynEntDefList[list][index] ??
                    throw NullRow($"ClipMap.DynEntDefList[{list}]", index);
                if (definition.Pose is null || definition.Mass is null)
                {
                    throw new InvalidDataException(
                        $"ClipMap.DynEntDefList[{list}][{index}] requires Pose and Mass.");
                }
                RequireCount(
                    definition.Pose.Quat,
                    4,
                    $"ClipMap.DynEntDefList[{list}][{index}].Pose.Quat");
            }
        }
    }

    private static void ValidateReferenceShape(ClipMapAsset value)
    {
        RequireCount(value.DynEntCount, 2, "ClipMap.DynEntCount");
        RequireOptionalFixedCount(value.PadD0ToFF, 0x30, "ClipMap.PadD0ToFF");
        bool hasPointer = EnumerateRootPointers(value).Any(pointer => pointer.Raw != 0);
        bool hasPayload = value.Planes.Count != 0 ||
            value.StaticModelList.Count != 0 || value.Materials.Count != 0 ||
            value.BrushSides.Count != 0 || value.BrushEdges.Count != 0 ||
            value.Nodes.Count != 0 || value.Leafs.Count != 0 ||
            value.LeafBrushNodes.Count != 0 || value.LeafBrushes.Count != 0 ||
            value.LeafSurfaces.Count != 0 || value.Verts.Count != 0 ||
            value.TriIndices.Count != 0 || value.TriEdgeIsWalkable.Count != 0 ||
            value.Borders.Count != 0 || value.Partitions.Count != 0 ||
            value.AabbTrees.Count != 0 || value.CModels.Count != 0 ||
            value.Brushes.Count != 0 || value.BrushBounds.Count != 0 ||
            value.BrushContents.Count != 0 || value.SModelNodes.Count != 0 ||
            value.MapEnts is not null ||
            value.DynEntDefList.Any(list => list.Count != 0) ||
            value.DynEntPoseList.Any(list => list.Count != 0) ||
            value.DynEntClientList.Any(list => list.Count != 0) ||
            value.DynEntCollList.Any(list => list.Count != 0);
        byte[] root = BuildReferenceScalarRoot(value);
        if (hasPointer || hasPayload || root.Any(item => item != 0))
        {
            throw new InvalidDataException(
                "A comma-prefixed ClipMap provider must have a zeroed reference body.");
        }
    }

    private static byte[] BuildReferenceScalarRoot(ClipMapAsset value)
    {
        var writer = new LinkTemplateWriter(ClipMapAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.SerializedIsInUse ?? value.IsInUse);
        writer.WriteInt32(value.PlaneCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumStaticModels);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumMaterials);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumBrushSides);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumBrushEdges);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumNodes);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumLeafs);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.LeafBrushNodesCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumLeafBrushes);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumLeafSurfaces);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.VertCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.TriCount);
        writer.Skip(2 * sizeof(int));
        writer.WriteInt32(value.BorderCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.PartitionCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.AabbTreeCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.NumSubModels);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(value.NumBrushes);
        writer.WriteUInt16(value.Pad8ETo8F);
        writer.Skip(4 * sizeof(int));
        writer.WriteUInt16(value.SModelNodeCount);
        writer.WriteUInt16(value.PadA2ToA3);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(value.DynEntCount[0]);
        writer.WriteUInt16(value.DynEntCount[1]);
        writer.Skip(8 * sizeof(int));
        writer.WriteUInt32(value.Checksum);
        if (value.PadD0ToFF.Count == 0)
            writer.Skip(0x30);
        else
            writer.WriteBytes(value.PadD0ToFF.ToArray());
        return writer.Complete();
    }

    private static IEnumerable<XPointerReference> EnumerateRootPointers(
        ClipMapAsset value)
    {
        yield return value.PlanesPointer.Untyped;
        yield return value.StaticModelListPointer.Untyped;
        yield return value.MaterialsPointer.Untyped;
        yield return value.BrushSidesPointer.Untyped;
        yield return value.BrushEdgesPointer.Untyped;
        yield return value.NodesPointer.Untyped;
        yield return value.LeafsPointer.Untyped;
        yield return value.LeafBrushNodesPointer.Untyped;
        yield return value.LeafBrushesPointer.Untyped;
        yield return value.LeafSurfacesPointer.Untyped;
        yield return value.VertsPointer.Untyped;
        yield return value.TriIndicesPointer.Untyped;
        yield return value.TriEdgeIsWalkablePointer.Untyped;
        yield return value.BordersPointer.Untyped;
        yield return value.PartitionsPointer.Untyped;
        yield return value.AabbTreesPointer.Untyped;
        yield return value.CModelsPointer.Untyped;
        yield return value.BrushesPointer.Untyped;
        yield return value.BrushBoundsPointer.Untyped;
        yield return value.BrushContentsPointer.Untyped;
        yield return value.MapEntsPointer.Untyped;
        yield return value.SModelNodesPointer.Untyped;
        foreach (XPointer<DynEntityDef[]> pointer in value.DynEntDefListPointers)
            yield return pointer.Untyped;
        foreach (XPointer<DynEntityPose[]> pointer in value.DynEntPoseListPointers)
            yield return pointer.Untyped;
        foreach (XPointer<DynEntityClient[]> pointer in value.DynEntClientListPointers)
            yield return pointer.Untyped;
        foreach (XPointer<DynEntityColl[]> pointer in value.DynEntCollListPointers)
            yield return pointer.Untyped;
    }

    private static void ValidatePlane(CPlane? value, string fieldPath)
    {
        if (value is null)
            throw new InvalidDataException($"{fieldPath} cannot be null.");
        RequireOptionalFixedCount(value.Pad12, 2, $"{fieldPath}.Pad12");
    }

    private static void ValidateSide(CBrushSide? value, string fieldPath)
    {
        if (value is null)
            throw new InvalidDataException($"{fieldPath} cannot be null.");
        if (value.Plane is not null)
            ValidatePlane(value.Plane, $"{fieldPath}.Plane");
    }

    private static void ValidateBorder(CollisionBorder? value, string fieldPath)
    {
        if (value is null)
            throw new InvalidDataException($"{fieldPath} cannot be null.");
        RequireCount(value.DistEq, 3, $"{fieldPath}.DistEq");
    }

    private static void ValidateBrush(CBrush? value, int index)
    {
        if (value is null)
            throw NullRow("ClipMap.Brushes", index);
        RequireCount(value.Sides, value.NumSides, $"ClipMap.Brushes[{index}].Sides");
        RequireCount(
            value.BaseAdjacentSide,
            RequiredAdjacencyByteCount(value),
            $"ClipMap.Brushes[{index}].BaseAdjacentSide");
        RequireCount(value.AxialMaterialNum, 6, $"ClipMap.Brushes[{index}].AxialMaterialNum");
        RequireCount(
            value.FirstAdjacentSideOffsets,
            6,
            $"ClipMap.Brushes[{index}].FirstAdjacentSideOffsets");
        RequireCount(value.EdgeCount, 6, $"ClipMap.Brushes[{index}].EdgeCount");
        for (int side = 0; side < value.Sides.Count; side++)
            ValidateSide(value.Sides[side], $"ClipMap.Brushes[{index}].Sides[{side}]");
    }

    private static bool SideEquals(CBrushSide left, CBrushSide right) =>
        left.MaterialNum == right.MaterialNum &&
        left.FirstAdjacentSideOffset == right.FirstAdjacentSideOffset &&
        left.EdgeCount == right.EdgeCount &&
        PlaneEquals(left.Plane, right.Plane);

    private static bool PlaneEquals(CPlane? left, CPlane? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return BitConverter.SingleToInt32Bits(left.Normal.X) ==
                BitConverter.SingleToInt32Bits(right.Normal.X) &&
            BitConverter.SingleToInt32Bits(left.Normal.Y) ==
                BitConverter.SingleToInt32Bits(right.Normal.Y) &&
            BitConverter.SingleToInt32Bits(left.Normal.Z) ==
                BitConverter.SingleToInt32Bits(right.Normal.Z) &&
            BitConverter.SingleToInt32Bits(left.Dist) ==
                BitConverter.SingleToInt32Bits(right.Dist) &&
            left.Type == right.Type && left.SignBits == right.SignBits &&
            OptionalPaddingEquals(left.Pad12, right.Pad12, 2);
    }

    private static bool OptionalPaddingEquals(
        IReadOnlyList<byte> left,
        IReadOnlyList<byte> right,
        int width)
    {
        for (int index = 0; index < width; index++)
        {
            byte leftValue = left.Count == 0 ? (byte)0 : left[index];
            byte rightValue = right.Count == 0 ? (byte)0 : right[index];
            if (leftValue != rightValue)
                return false;
        }
        return true;
    }

    private static bool IsZero(DynEntityPose? value) =>
        value is not null && value.Pose is not null &&
        value.Pose.Quat.All(item => BitConverter.SingleToInt32Bits(item) == 0) &&
        IsZero(value.Pose.Origin) &&
        BitConverter.SingleToInt32Bits(value.Radius) == 0;

    private static bool IsZero(DynEntityClient? value) =>
        value is not null && value.PhysObjId == 0 && value.Flags == 0 &&
        value.LightingHandle == 0 && value.Health == 0;

    private static bool IsZero(DynEntityColl? value) =>
        value is not null && value.Sector == 0 && value.NextEntInSector == 0 &&
        BitConverter.SingleToInt32Bits(value.LinkMins.a) == 0 &&
        BitConverter.SingleToInt32Bits(value.LinkMins.b) == 0 &&
        BitConverter.SingleToInt32Bits(value.LinkMaxs.a) == 0 &&
        BitConverter.SingleToInt32Bits(value.LinkMaxs.b) == 0;

    private static bool IsZero(Vec3 value) =>
        BitConverter.SingleToInt32Bits(value.X) == 0 &&
        BitConverter.SingleToInt32Bits(value.Y) == 0 &&
        BitConverter.SingleToInt32Bits(value.Z) == 0;

    private static void RequireCount<T>(
        IReadOnlyList<T> values,
        int count,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (count < 0 || values.Count != count)
        {
            throw new InvalidDataException(
                $"{fieldPath} requires exactly {count} values.");
        }
    }

    private static void RequireOptionalCount<T>(
        IReadOnlyList<T> values,
        int count,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != 0 && values.Count != count)
        {
            throw new InvalidDataException(
                $"{fieldPath} must be absent or contain exactly {count} values.");
        }
    }

    private static void RequireOptionalFixedCount<T>(
        IReadOnlyList<T> values,
        int count,
        string fieldPath) =>
        RequireOptionalCount(values, count, fieldPath);

    private static InvalidDataException NullRow(string fieldPath, int index) =>
        new($"{fieldPath}[{index}] cannot be null.");
}
