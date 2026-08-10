using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Fx;
using IW4.FastFiles.Loaders.Assets.MapEnts;
using IW4.FastFiles.Loaders.Assets.Physics;
using IW4.FastFiles.Loaders.Assets.XModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.FastFiles.Loaders.Assets.ColMap;

public sealed class ClipMapLoader
{
    private readonly XModelLoader _xmodelLoader = new();
    private readonly FxEffectDefLoader _fxLoader = new();
    private readonly PhysPresetLoader _physPresetLoader = new();
    private readonly MapEntsLoader _mapEntsLoader = new();

    // ColMapSp and ColMapMp share this serialized loader.
    public ClipMapAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        XAssetType serializedType = XAssetType.ColMapMp)
    {
        if (serializedType is not (XAssetType.ColMapSp or XAssetType.ColMapMp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedType),
                serializedType,
                "ClipMapLoader only accepts serialized ColMapSp or ColMapMp assets.");
        }

        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException($"Top-level {serializedType} pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<ClipMapAsset>(
                pointer,
                ClipMapAsset.SerializedSize,
                "clipMap_t");
            ClipMapAsset canonical = context.ResolveCanonicalAsset<ClipMapAsset>(
                    pointer,
                    serializedType)
                ?? throw new InvalidDataException(
                    $"Top-level {serializedType} pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to the canonical ColMapMp asset family.");
            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Top-level ColMap pointer 0x{pointer.Raw:X8} does not reference inline/insert payload data.");

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            ClipMapAsset asset = ReadClipMap(cursor, rootAddress, serializedType, context);
            ClipMapAsset canonical = context.DB_AddXAsset(
                serializedType,
                asset,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private ClipMapAsset ReadClipMap(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        XAssetType serializedType,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, ClipMapAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"ColMap pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = ReadPointer<string>(rootCursor, context, XPointerResolutionMode.Direct);
        int isInUse = rootCursor.ReadInt32();
        int planeCount = rootCursor.ReadInt32();
        XPointer<CPlane[]> planesPointer = ReadPointer<CPlane[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numStaticModels = rootCursor.ReadInt32();
        XPointer<ClipStaticModel[]> staticModelListPointer = ReadPointer<ClipStaticModel[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numMaterials = rootCursor.ReadInt32();
        XPointer<ClipMaterial[]> materialsPointer = ReadPointer<ClipMaterial[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numBrushSides = rootCursor.ReadInt32();
        XPointer<CBrushSide[]> brushSidesPointer = ReadPointer<CBrushSide[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numBrushEdges = rootCursor.ReadInt32();
        XPointer<byte[]> brushEdgesPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numNodes = rootCursor.ReadInt32();
        XPointer<CNode[]> nodesPointer = ReadPointer<CNode[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numLeafs = rootCursor.ReadInt32();
        XPointer<CLeaf[]> leafsPointer = ReadPointer<CLeaf[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int leafBrushNodesCount = rootCursor.ReadInt32();
        XPointer<CLeafBrushNode[]> leafBrushNodesPointer = ReadPointer<CLeafBrushNode[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numLeafBrushes = rootCursor.ReadInt32();
        XPointer<ushort[]> leafBrushesPointer = ReadPointer<ushort[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numLeafSurfaces = rootCursor.ReadInt32();
        XPointer<uint[]> leafSurfacesPointer = ReadPointer<uint[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int vertCount = rootCursor.ReadInt32();
        XPointer<ModelVec3[]> vertsPointer = ReadPointer<ModelVec3[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int triCount = rootCursor.ReadInt32();
        XPointer<ushort[]> triIndicesPointer = ReadPointer<ushort[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> triEdgeIsWalkablePointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int borderCount = rootCursor.ReadInt32();
        XPointer<CollisionBorder[]> bordersPointer = ReadPointer<CollisionBorder[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int partitionCount = rootCursor.ReadInt32();
        XPointer<CollisionPartition[]> partitionsPointer = ReadPointer<CollisionPartition[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int aabbTreeCount = rootCursor.ReadInt32();
        XPointer<CollisionAabbTree[]> aabbTreesPointer = ReadPointer<CollisionAabbTree[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numSubModels = rootCursor.ReadInt32();
        XPointer<CModel[]> cmodelsPointer = ReadPointer<CModel[]>(rootCursor, context, XPointerResolutionMode.Direct);
        ushort numBrushes = rootCursor.ReadUInt16();
        ushort pad8ETo8F = rootCursor.ReadUInt16();
        XPointer<CBrush[]> brushesPointer = ReadPointer<CBrush[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<ModelBounds[]> brushBoundsPointer = ReadPointer<ModelBounds[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<uint[]> brushContentsPointer = ReadPointer<uint[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<MapEntsAsset> mapEntsPointer = ReadPointer<MapEntsAsset>(rootCursor, context, XPointerResolutionMode.AliasCell);
        ushort smodelNodeCount = rootCursor.ReadUInt16();
        ushort padA2ToA3 = rootCursor.ReadUInt16();
        XPointer<SModelAabbNode[]> smodelNodesPointer = ReadPointer<SModelAabbNode[]>(rootCursor, context, XPointerResolutionMode.Direct);
        ushort[] dynEntCount = [rootCursor.ReadUInt16(), rootCursor.ReadUInt16()];
        XPointer<DynEntityDef[]>[] dynEntDefListPointers =
        [
            ReadPointer<DynEntityDef[]>(rootCursor, context, XPointerResolutionMode.Direct),
            ReadPointer<DynEntityDef[]>(rootCursor, context, XPointerResolutionMode.Direct)
        ];
        XPointer<DynEntityPose[]>[] dynEntPoseListPointers =
        [
            ReadPointer<DynEntityPose[]>(rootCursor, context, XPointerResolutionMode.Direct),
            ReadPointer<DynEntityPose[]>(rootCursor, context, XPointerResolutionMode.Direct)
        ];
        XPointer<DynEntityClient[]>[] dynEntClientListPointers =
        [
            ReadPointer<DynEntityClient[]>(rootCursor, context, XPointerResolutionMode.Direct),
            ReadPointer<DynEntityClient[]>(rootCursor, context, XPointerResolutionMode.Direct)
        ];
        XPointer<DynEntityColl[]>[] dynEntCollListPointers =
        [
            ReadPointer<DynEntityColl[]>(rootCursor, context, XPointerResolutionMode.Direct),
            ReadPointer<DynEntityColl[]>(rootCursor, context, XPointerResolutionMode.Direct)
        ];
        uint checksum = rootCursor.ReadUInt32();
        byte[] padD0ToFF = rootCursor.ReadBytes(0x30);

        if (rootCursor.Offset != ClipMapAsset.SerializedSize)
            throw new InvalidDataException($"ColMap consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ClipMapAsset.SerializedSize:X}.");

        string? name;
        IReadOnlyList<CPlane> planes;
        IReadOnlyList<ClipStaticModel> staticModelList;
        IReadOnlyList<ClipMaterial> materials;
        IReadOnlyList<CBrushSide> brushSides;
        IReadOnlyList<byte> brushEdges;
        IReadOnlyList<CNode> nodes;
        IReadOnlyList<CLeaf> leafs;
        IReadOnlyList<CLeafBrushNode> leafBrushNodes;
        IReadOnlyList<ushort> leafBrushes;
        IReadOnlyList<uint> leafSurfaces;
        IReadOnlyList<ModelVec3> verts;
        IReadOnlyList<ushort> triIndices;
        IReadOnlyList<byte> triEdgeIsWalkable;
        IReadOnlyList<CollisionBorder> borders;
        IReadOnlyList<CollisionPartition> partitions;
        IReadOnlyList<CollisionAabbTree> aabbTrees;
        IReadOnlyList<CModel> cmodels;
        IReadOnlyList<CBrush> brushes;
        IReadOnlyList<ModelBounds> brushBounds;
        IReadOnlyList<uint> brushContents;
        MapEntsAsset? mapEnts;
        MapEntsPointerLoadResult mapEntsResult;
        IReadOnlyList<SModelAabbNode> smodelNodes;
        IReadOnlyList<DynEntityDef>[] dynEntDefList = new IReadOnlyList<DynEntityDef>[2];
        IReadOnlyList<DynEntityPose>[] dynEntPoseList = new IReadOnlyList<DynEntityPose>[2];
        IReadOnlyList<DynEntityClient>[] dynEntClientList = new IReadOnlyList<DynEntityClient>[2];
        IReadOnlyList<DynEntityColl>[] dynEntCollList = new IReadOnlyList<DynEntityColl>[2];

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            planes = ReadCPlaneArray(cursor, planesPointer.Untyped, planeCount, context, allowOffset: true, "clipMap_t.planes");
            staticModelList = ReadStaticModelArray(cursor, staticModelListPointer.Untyped, numStaticModels, context);
            materials = ReadClipMaterialArray(cursor, materialsPointer.Untyped, numMaterials, context);
            brushSides = ReadCBrushSideArray(cursor, brushSidesPointer.Untyped, numBrushSides, context, "clipMap_t.brushsides");
            brushEdges = ReadByteArray(cursor, brushEdgesPointer.Untyped, numBrushEdges, 1, context, "clipMap_t.brushEdges");
            nodes = ReadNodeArray(cursor, nodesPointer.Untyped, numNodes, context);
            leafs = ReadLeafArray(cursor, leafsPointer.Untyped, numLeafs, context);
            leafBrushes = ReadUInt16Array(cursor, leafBrushesPointer.Untyped, numLeafBrushes, 2, context, "clipMap_t.leafbrushes");
            leafBrushNodes = ReadLeafBrushNodeArray(cursor, leafBrushNodesPointer.Untyped, leafBrushNodesCount, context);
            leafSurfaces = ReadUInt32Array(cursor, leafSurfacesPointer.Untyped, numLeafSurfaces, 4, context, "clipMap_t.leafsurfaces");
            verts = ReadVec3Array(cursor, vertsPointer.Untyped, vertCount, context, "clipMap_t.verts");
            triIndices = ReadUInt16Array(cursor, triIndicesPointer.Untyped, checked(NonNegative(triCount, "triCount") * 3), 2, context, "clipMap_t.triIndices");
            triEdgeIsWalkable = ReadByteArray(cursor, triEdgeIsWalkablePointer.Untyped, TriEdgeWalkableByteCount(triCount), 1, context, "clipMap_t.triEdgeIsWalkable");
            borders = ReadCollisionBorderArray(cursor, bordersPointer.Untyped, borderCount, context, "clipMap_t.borders", allowOffset: false);
            partitions = ReadCollisionPartitionArray(cursor, partitionsPointer.Untyped, partitionCount, context);
            aabbTrees = ReadCollisionAabbTreeArray(cursor, aabbTreesPointer.Untyped, aabbTreeCount, context);
            cmodels = ReadCModelArray(cursor, cmodelsPointer.Untyped, numSubModels, context);
            brushes = ReadCBrushArray(cursor, brushesPointer.Untyped, numBrushes, context);
            brushBounds = ReadBoundsArray(cursor, brushBoundsPointer.Untyped, numBrushes, 128, context, "clipMap_t.brushBounds");
            brushContents = ReadUInt32Array(cursor, brushContentsPointer.Untyped, numBrushes, 4, context, "clipMap_t.brushContents");
            smodelNodes = ReadSModelAabbNodeArray(cursor, smodelNodesPointer.Untyped, smodelNodeCount, context);
            mapEntsResult = _mapEntsLoader.LoadFromPointerWithMaterialization(
                cursor,
                mapEntsPointer.Untyped,
                context);
            mapEnts = mapEntsResult.Canonical;
            dynEntDefList[0] = ReadDynEntityDefArray(cursor, dynEntDefListPointers[0].Untyped, dynEntCount[0], context, "clipMap_t.dynEntDefList[0]");
            dynEntDefList[1] = ReadDynEntityDefArray(cursor, dynEntDefListPointers[1].Untyped, dynEntCount[1], context, "clipMap_t.dynEntDefList[1]");
            dynEntPoseList[0] = ReadRuntimeArray(context, () => ReadDynEntityPoseArray(cursor, dynEntPoseListPointers[0].Untyped, dynEntCount[0], context, "clipMap_t.dynEntPoseList[0]"));
            dynEntPoseList[1] = ReadRuntimeArray(context, () => ReadDynEntityPoseArray(cursor, dynEntPoseListPointers[1].Untyped, dynEntCount[1], context, "clipMap_t.dynEntPoseList[1]"));
            dynEntClientList[0] = ReadRuntimeArray(context, () => ReadDynEntityClientArray(cursor, dynEntClientListPointers[0].Untyped, dynEntCount[0], context, "clipMap_t.dynEntClientList[0]"));
            dynEntClientList[1] = ReadRuntimeArray(context, () => ReadDynEntityClientArray(cursor, dynEntClientListPointers[1].Untyped, dynEntCount[1], context, "clipMap_t.dynEntClientList[1]"));
            dynEntCollList[0] = ReadRuntimeArray(context, () => ReadDynEntityCollArray(cursor, dynEntCollListPointers[0].Untyped, dynEntCount[0], context, "clipMap_t.dynEntCollList[0]"));
            dynEntCollList[1] = ReadRuntimeArray(context, () => ReadDynEntityCollArray(cursor, dynEntCollListPointers[1].Untyped, dynEntCount[1], context, "clipMap_t.dynEntCollList[1]"));
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new ClipMapAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            SerializedType = serializedType,
            NamePointer = namePointer,
            Name = name,
            IsInUse = isInUse,
            SerializedIsInUse = isInUse,
            PlaneCount = planeCount,
            PlanesPointer = planesPointer,
            Planes = planes,
            NumStaticModels = numStaticModels,
            StaticModelListPointer = staticModelListPointer,
            StaticModelList = staticModelList,
            NumMaterials = numMaterials,
            MaterialsPointer = materialsPointer,
            Materials = materials,
            NumBrushSides = numBrushSides,
            BrushSidesPointer = brushSidesPointer,
            BrushSides = brushSides,
            NumBrushEdges = numBrushEdges,
            BrushEdgesPointer = brushEdgesPointer,
            BrushEdges = brushEdges,
            NumNodes = numNodes,
            NodesPointer = nodesPointer,
            Nodes = nodes,
            NumLeafs = numLeafs,
            LeafsPointer = leafsPointer,
            Leafs = leafs,
            LeafBrushNodesCount = leafBrushNodesCount,
            LeafBrushNodesPointer = leafBrushNodesPointer,
            LeafBrushNodes = leafBrushNodes,
            NumLeafBrushes = numLeafBrushes,
            LeafBrushesPointer = leafBrushesPointer,
            LeafBrushes = leafBrushes,
            NumLeafSurfaces = numLeafSurfaces,
            LeafSurfacesPointer = leafSurfacesPointer,
            LeafSurfaces = leafSurfaces,
            VertCount = vertCount,
            VertsPointer = vertsPointer,
            Verts = verts,
            TriCount = triCount,
            TriIndicesPointer = triIndicesPointer,
            TriIndices = triIndices,
            TriEdgeIsWalkablePointer = triEdgeIsWalkablePointer,
            TriEdgeIsWalkable = triEdgeIsWalkable,
            BorderCount = borderCount,
            BordersPointer = bordersPointer,
            Borders = borders,
            PartitionCount = partitionCount,
            PartitionsPointer = partitionsPointer,
            Partitions = partitions,
            AabbTreeCount = aabbTreeCount,
            AabbTreesPointer = aabbTreesPointer,
            AabbTrees = aabbTrees,
            NumSubModels = numSubModels,
            CModelsPointer = cmodelsPointer,
            CModels = cmodels,
            NumBrushes = numBrushes,
            Pad8ETo8F = pad8ETo8F,
            BrushesPointer = brushesPointer,
            Brushes = brushes,
            BrushBoundsPointer = brushBoundsPointer,
            BrushBounds = brushBounds,
            BrushContentsPointer = brushContentsPointer,
            BrushContents = brushContents,
            MapEntsPointer = mapEntsPointer,
            MapEnts = mapEnts,
            MapEntsIncomingDefinition = mapEntsResult.IncomingDefinition,
            SModelNodeCount = smodelNodeCount,
            PadA2ToA3 = padA2ToA3,
            SModelNodesPointer = smodelNodesPointer,
            SModelNodes = smodelNodes,
            DynEntCount = dynEntCount,
            DynEntDefListPointers = dynEntDefListPointers,
            DynEntDefList = dynEntDefList,
            DynEntPoseListPointers = dynEntPoseListPointers,
            DynEntPoseList = dynEntPoseList,
            DynEntClientListPointers = dynEntClientListPointers,
            DynEntClientList = dynEntClientList,
            DynEntCollListPointers = dynEntCollListPointers,
            DynEntCollList = dynEntCollList,
            Checksum = checksum,
            PadD0ToFF = padD0ToFF
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        ClipMapAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed ColMap pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical ColMap has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private IReadOnlyList<ClipStaticModel> ReadStaticModelArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<ClipStaticModel[]>(cursor, pointer, count, ClipStaticModel.SerializedSize, 4, context, "clipMap_t.staticModelList", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new ClipStaticModel[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, ClipStaticModel.SerializedSize);
            XPointer<XModelAsset> xmodelPointer = ReadPointer<XModelAsset>(rowCursor, context, XPointerResolutionMode.AliasCell);
            ModelVec3 origin = ReadVec3(rowCursor);
            ModelVec3[] invScaledAxis = [ReadVec3(rowCursor), ReadVec3(rowCursor), ReadVec3(rowCursor)];
            ModelVec3 absMin = ReadVec3(rowCursor);
            ModelVec3 absMax = ReadVec3(rowCursor);

            if (rowCursor.Offset != ClipStaticModel.SerializedSize)
                throw new InvalidDataException($"ClipStaticModel consumed 0x{rowCursor.Offset:X} bytes.");

            XModelPointerLoadResult xmodelResult =
                _xmodelLoader.LoadFromPointerWithMaterialization(
                    cursor,
                    xmodelPointer.Untyped,
                    context);
            rows[i] = new ClipStaticModel
            {
                XModelPointer = xmodelPointer,
                XModel = xmodelResult.Canonical,
                XModelIncomingDefinition = xmodelResult.IncomingDefinition,
                Origin = origin,
                InvScaledAxis = invScaledAxis,
                AbsMin = absMin,
                AbsMax = absMax
            };
        }

        return rows;
    }

    private static IReadOnlyList<ClipMaterial> ReadClipMaterialArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<ClipMaterial[]>(cursor, pointer, count, ClipMaterial.SerializedSize, 4, context, "clipMap_t.materials", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new ClipMaterial[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, ClipMaterial.SerializedSize);
            XPointer<string> namePointer = ReadPointer<string>(rowCursor, context, XPointerResolutionMode.Direct);
            int surfaceFlags = rowCursor.ReadInt32();
            int contents = rowCursor.ReadInt32();
            rows[i] = new ClipMaterial
            {
                NamePointer = namePointer,
                Name = context.PointerReader.LoadXString(cursor, namePointer),
                SurfaceFlags = surfaceFlags,
                Contents = contents
            };
        }

        return rows;
    }

    private static IReadOnlyList<CBrushSide> ReadCBrushSideArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CBrushSide[]>(cursor, pointer, count, CBrushSide.SerializedSize, 4, context, memberName, allowOffset: true);
        if (!hasPayload && pointer.Type == PointerType.Offset)
        {
            bytes = context.Blocks.ReadBytes(
                address,
                checked(NonNegative(count, memberName) * CBrushSide.SerializedSize));
            hasPayload = true;
        }
        if (!hasPayload)
            return [];

        var rows = new CBrushSide[count];
        for (int i = 0; i < rows.Length; i++)
        {
            XBlockAddress sideAddress = address.Add(checked(i * CBrushSide.SerializedSize));
            if (context.TryGetMaterialized(
                    sideAddress,
                    out CBrushSide? existing) &&
                existing is not null)
            {
                rows[i] = existing;
                continue;
            }

            CBrushSide side = ReadCBrushSide(
                cursor,
                RowCursor(bytes, address, i, CBrushSide.SerializedSize),
                context);
            rows[i] = context.RegisterMaterialized(sideAddress, side, "cbrushside_t");
        }

        return rows;
    }

    private static CBrushSide ReadCBrushSide(
        FastFileCursor cursor,
        FastFileCursor rowCursor,
        DbLoadExecutionContext context)
    {
        XPointer<CPlane> planePointer = ReadPointer<CPlane>(rowCursor, context, XPointerResolutionMode.Direct);
        return new CBrushSide
        {
            PlanePointer = planePointer,
            Plane = ReadCPlanePointer(cursor, planePointer.Untyped, context),
            MaterialNum = rowCursor.ReadUInt16(),
            FirstAdjacentSideOffset = rowCursor.ReadByte(),
            EdgeCount = rowCursor.ReadByte()
        };
    }

    private static IReadOnlyList<CNode> ReadNodeArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CNode[]>(cursor, pointer, count, CNode.SerializedSize, 4, context, "clipMap_t.nodes", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CNode[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, CNode.SerializedSize);
            XPointer<CPlane> planePointer = ReadPointer<CPlane>(rowCursor, context, XPointerResolutionMode.Direct);
            rows[i] = new CNode
            {
                PlanePointer = planePointer,
                Plane = ReadCPlanePointer(cursor, planePointer.Untyped, context),
                Children = [ReadInt16(rowCursor), ReadInt16(rowCursor)]
            };
        }

        return rows;
    }

    private static IReadOnlyList<CLeaf> ReadLeafArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CLeaf[]>(cursor, pointer, count, CLeaf.SerializedSize, 4, context, "clipMap_t.leafs", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CLeaf[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = ReadLeaf(RowCursor(bytes, address, i, CLeaf.SerializedSize));

        return rows;
    }

    private static IReadOnlyList<CLeafBrushNode> ReadLeafBrushNodeArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CLeafBrushNode[]>(cursor, pointer, count, CLeafBrushNode.SerializedSize, 4, context, "clipMap_t.leafbrushNodes", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CLeafBrushNode[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, CLeafBrushNode.SerializedSize);
            byte axis = rowCursor.ReadByte();
            byte pad01 = rowCursor.ReadByte();
            short leafBrushCount = ReadInt16(rowCursor);
            int contents = rowCursor.ReadInt32();
            CLeafBrushNodeData data = ReadLeafBrushNodeData(cursor, rowCursor, leafBrushCount, context);
            rows[i] = new CLeafBrushNode
            {
                Axis = axis,
                Pad01 = pad01,
                LeafBrushCount = leafBrushCount,
                Contents = contents,
                Data = data
            };
        }

        return rows;
    }

    private static CLeafBrushNodeData ReadLeafBrushNodeData(
        FastFileCursor cursor,
        FastFileCursor rowCursor,
        short leafBrushCount,
        DbLoadExecutionContext context)
    {
        if (leafBrushCount > 0)
        {
            XPointer<ushort[]> brushesPointer = ReadPointer<ushort[]>(rowCursor, context, XPointerResolutionMode.Direct);
            byte[] unionPad = rowCursor.ReadBytes(8);
            IReadOnlyList<ushort> brushes = ReadUInt16Array(cursor, brushesPointer.Untyped, leafBrushCount, 2, context, "cLeafBrushNodeLeaf_t.brushes", allowOffset: true);
            return new CLeafBrushNodeData
            {
                BrushesPointer = brushesPointer,
                Brushes = brushes,
                LeafUnionPad = unionPad
            };
        }

        float dist = ReadSingle(rowCursor);
        float range = ReadSingle(rowCursor);
        var childOffsets = new ushort[2];
        for (int i = 0; i < childOffsets.Length; i++)
            childOffsets[i] = rowCursor.ReadUInt16();

        return new CLeafBrushNodeData
        {
            Children = new CLeafBrushNodeChildren
            {
                Dist = dist,
                Range = range,
                ChildOffsets = childOffsets
            }
        };
    }

    private static IReadOnlyList<CollisionBorder> ReadCollisionBorderArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName,
        bool allowOffset)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CollisionBorder[]>(cursor, pointer, count, CollisionBorder.SerializedSize, 4, context, memberName, allowOffset);
        if (!hasPayload && pointer.Type == PointerType.Offset)
        {
            bytes = context.Blocks.ReadBytes(
                address,
                checked(NonNegative(count, memberName) * CollisionBorder.SerializedSize));
            hasPayload = true;
        }
        if (!hasPayload)
            return [];

        var rows = new CollisionBorder[count];
        for (int i = 0; i < rows.Length; i++)
        {
            XBlockAddress borderAddress = address.Add(checked(i * CollisionBorder.SerializedSize));
            if (context.TryGetMaterialized(
                    borderAddress,
                    out CollisionBorder? existing) &&
                existing is not null)
            {
                rows[i] = existing;
                continue;
            }

            CollisionBorder border = ReadCollisionBorder(
                RowCursor(bytes, address, i, CollisionBorder.SerializedSize));
            rows[i] = context.RegisterMaterialized(
                borderAddress,
                border,
                "CollisionBorder");
        }

        return rows;
    }

    private static IReadOnlyList<CollisionPartition> ReadCollisionPartitionArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CollisionPartition[]>(cursor, pointer, count, CollisionPartition.SerializedSize, 4, context, "clipMap_t.partitions", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CollisionPartition[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, CollisionPartition.SerializedSize);
            byte triCount = rowCursor.ReadByte();
            byte borderCount = rowCursor.ReadByte();
            byte firstVertSegment = rowCursor.ReadByte();
            byte pad03 = rowCursor.ReadByte();
            int firstTri = rowCursor.ReadInt32();
            XPointer<CollisionBorder[]> bordersPointer = ReadPointer<CollisionBorder[]>(rowCursor, context, XPointerResolutionMode.Direct);
            rows[i] = new CollisionPartition
            {
                TriCount = triCount,
                BorderCount = borderCount,
                FirstVertSegment = firstVertSegment,
                Pad03 = pad03,
                FirstTri = firstTri,
                BordersPointer = bordersPointer,
                Borders = ReadCollisionBorderArray(cursor, bordersPointer.Untyped, borderCount, context, "CollisionPartition.borders", allowOffset: true)
            };
        }

        return rows;
    }

    private static IReadOnlyList<CollisionAabbTree> ReadCollisionAabbTreeArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CollisionAabbTree[]>(cursor, pointer, count, CollisionAabbTree.SerializedSize, 16, context, "clipMap_t.aabbTrees", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CollisionAabbTree[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, CollisionAabbTree.SerializedSize);
            rows[i] = new CollisionAabbTree
            {
                Origin = ReadVec3(rowCursor),
                MaterialIndex = rowCursor.ReadUInt16(),
                ChildCount = rowCursor.ReadUInt16(),
                HalfSize = ReadVec3(rowCursor),
                FirstChildOrPartitionIndex = rowCursor.ReadInt32()
            };
        }

        return rows;
    }

    private static IReadOnlyList<CModel> ReadCModelArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CModel[]>(cursor, pointer, count, CModel.SerializedSize, 4, context, "clipMap_t.cmodels", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CModel[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, CModel.SerializedSize);
            rows[i] = new CModel
            {
                Mins = ReadVec3(rowCursor),
                Maxs = ReadVec3(rowCursor),
                Radius = ReadSingle(rowCursor),
                Leaf = ReadLeaf(rowCursor)
            };
        }

        return rows;
    }

    private static IReadOnlyList<CBrush> ReadCBrushArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CBrush[]>(cursor, pointer, count, CBrush.SerializedSize, 128, context, "clipMap_t.brushes", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new CBrush[count];
        for (int i = 0; i < rows.Length; i++)
        {
            CBrush root = ReadCBrushRoot(RowCursor(bytes, address, i, CBrush.SerializedSize), context);
            IReadOnlyList<CBrushSide> sides = ReadCBrushPointerSidePayload(cursor, root.SidesPointer.Untyped, root.NumSides, context);
            int adjacencyByteCount = RequiredAdjacencyByteCount(root, sides);
            IReadOnlyList<byte> baseAdjacentSide = ReadCBrushPointerBytePayload(
                cursor,
                root.BaseAdjacentSidePointer.Untyped,
                adjacencyByteCount,
                context);
            rows[i] = new CBrush
            {
                NumSides = root.NumSides,
                GlassPieceIndex = root.GlassPieceIndex,
                SidesPointer = root.SidesPointer,
                Sides = sides,
                BaseAdjacentSidePointer = root.BaseAdjacentSidePointer,
                BaseAdjacentSide = baseAdjacentSide,
                AxialMaterialNum = root.AxialMaterialNum,
                FirstAdjacentSideOffsets = root.FirstAdjacentSideOffsets,
                EdgeCount = root.EdgeCount
            };
        }

        return rows;
    }

    private static CBrush ReadCBrushRoot(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        ushort numSides = cursor.ReadUInt16();
        ushort glassPieceIndex = cursor.ReadUInt16();
        XPointer<CBrushSide[]> sidesPointer = ReadPointer<CBrushSide[]>(cursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> baseAdjacentSidePointer = ReadPointer<byte[]>(cursor, context, XPointerResolutionMode.Direct);
        var axialMaterialNum = new short[6];
        for (int i = 0; i < axialMaterialNum.Length; i++)
            axialMaterialNum[i] = ReadInt16(cursor);

        return new CBrush
        {
            NumSides = numSides,
            GlassPieceIndex = glassPieceIndex,
            SidesPointer = sidesPointer,
            BaseAdjacentSidePointer = baseAdjacentSidePointer,
            AxialMaterialNum = axialMaterialNum,
            FirstAdjacentSideOffsets = cursor.ReadBytes(6),
            EdgeCount = cursor.ReadBytes(6)
        };
    }

    private static IReadOnlyList<CBrushSide> ReadCBrushPointerSidePayload(
        FastFileCursor cursor,
        XPointerReference pointer,
        int numSides,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            int byteCount = checked(numSides * CBrushSide.SerializedSize);
            context.PointerReader.ValidateOffsetPointerRange<CBrushSide[]>(
                pointer,
                byteCount,
                "cbrush_t.sides");
            XBlockAddress address = pointer.PackedAddress
                ?? throw new InvalidDataException("Packed cbrush_t.sides pointer has no decoded block address.");
            var sides = new CBrushSide[numSides];
            for (int i = 0; i < sides.Length; i++)
            {
                XBlockAddress sideAddress = address.Add(checked(i * CBrushSide.SerializedSize));
                if (context.TryGetMaterialized(
                        sideAddress,
                        out CBrushSide? existing) &&
                    existing is not null)
                {
                    sides[i] = existing;
                    continue;
                }

                CBrushSide materialized = ReadCBrushSide(
                    cursor,
                    new FastFileCursor(
                        context.Blocks.ReadBytes(sideAddress, CBrushSide.SerializedSize),
                        sideAddress),
                    context);
                sides[i] = context.RegisterMaterialized(
                    sideAddress,
                    materialized,
                    "cbrush_t.sides");
            }

            return sides;
        }

        if (pointer.Type != PointerType.Inline)
            throw new InvalidDataException($"cbrush_t.sides pointer 0x{pointer.Raw:X8} is not null/inline/offset.");

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, CBrushSide.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"cbrush_t.sides pointer patched to {targetAddress}, but row loaded at {loadedAddress}.");

        CBrushSide side = ReadCBrushSide(
            cursor,
            new FastFileCursor(bytes, targetAddress),
            context);
        return [context.RegisterMaterialized(targetAddress, side, "cbrush_t.sides[0]")];
    }

    private static IReadOnlyList<byte> ReadCBrushPointerBytePayload(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<byte[]>(
                pointer,
                byteCount,
                "cbrush_t.baseAdjacentSide");
            XBlockAddress address = pointer.PackedAddress
                ?? throw new InvalidDataException("Packed cbrush_t.baseAdjacentSide pointer has no decoded block address.");
            return context.Blocks.ReadBytes(address, byteCount);
        }

        if (pointer.Type != PointerType.Inline)
            throw new InvalidDataException($"cbrush_t.baseAdjacentSide pointer 0x{pointer.Raw:X8} is not null/inline/offset.");

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 1);
        byte[] bytes = context.Blocks.Load(cursor, 1, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"cbrush_t.baseAdjacentSide pointer patched to {targetAddress}, but byte loaded at {loadedAddress}.");

        return bytes;
    }

    private static int RequiredAdjacencyByteCount(
        CBrush brush,
        IReadOnlyList<CBrushSide> sides)
    {
        int byteCount = 0;
        int axialCount = Math.Min(
            brush.FirstAdjacentSideOffsets.Count,
            brush.EdgeCount.Count);
        for (int i = 0; i < axialCount; i++)
        {
            byteCount = Math.Max(
                byteCount,
                checked(brush.FirstAdjacentSideOffsets[i] + brush.EdgeCount[i]));
        }

        foreach (CBrushSide side in sides)
        {
            byteCount = Math.Max(
                byteCount,
                checked(side.FirstAdjacentSideOffset + side.EdgeCount));
        }

        return byteCount;
    }

    private IReadOnlyList<DynEntityDef> ReadDynEntityDefArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<DynEntityDef[]>(cursor, pointer, count, DynEntityDef.SerializedSize, 4, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new DynEntityDef[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, DynEntityDef.SerializedSize);
            int type = rowCursor.ReadInt32();
            GfxPlacement pose = ReadGfxPlacement(rowCursor);
            XPointer<XModelAsset> xmodelPointer = ReadPointer<XModelAsset>(rowCursor, context, XPointerResolutionMode.AliasCell);
            XModelPointerLoadResult xmodelResult =
                _xmodelLoader.LoadFromPointerWithMaterialization(
                    cursor,
                    xmodelPointer.Untyped,
                    context);
            ushort brushModel = rowCursor.ReadUInt16();
            ushort physicsBrushModel = rowCursor.ReadUInt16();
            XPointer<FxEffectDefAsset> destroyFxPointer = ReadPointer<FxEffectDefAsset>(rowCursor, context, XPointerResolutionMode.AliasCell);
            FxEffectDefAsset? destroyFx = _fxLoader.LoadFromPointer(cursor, destroyFxPointer.Untyped, context);
            XPointer<PhysPresetAsset> physPresetPointer = ReadPointer<PhysPresetAsset>(rowCursor, context, XPointerResolutionMode.AliasCell);
            PhysPresetPointerLoadResult physPresetResult =
                _physPresetLoader.LoadFromPointerWithMaterialization(
                    cursor,
                    physPresetPointer.Untyped,
                    context);
            int health = rowCursor.ReadInt32();
            PhysMass mass = ReadPhysMass(rowCursor);
            int contents = rowCursor.ReadInt32();
            rows[i] = new DynEntityDef
            {
                Type = type,
                Pose = pose,
                XModelPointer = xmodelPointer,
                XModel = xmodelResult.Canonical,
                XModelIncomingDefinition = xmodelResult.IncomingDefinition,
                BrushModel = brushModel,
                PhysicsBrushModel = physicsBrushModel,
                DestroyFxPointer = destroyFxPointer,
                DestroyFx = destroyFx,
                PhysPresetPointer = physPresetPointer,
                PhysPreset = physPresetResult.Canonical,
                PhysPresetIncomingDefinition = physPresetResult.IncomingDefinition,
                Health = health,
                Mass = mass,
                Contents = contents
            };
        }

        return rows;
    }

    private static IReadOnlyList<DynEntityPose> ReadDynEntityPoseArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<DynEntityPose[]>(cursor, pointer, count, DynEntityPose.SerializedSize, 4, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new DynEntityPose[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, DynEntityPose.SerializedSize);
            rows[i] = new DynEntityPose
            {
                Pose = ReadGfxPlacement(rowCursor),
                Radius = ReadSingle(rowCursor)
            };
        }

        return rows;
    }

    private static IReadOnlyList<DynEntityClient> ReadDynEntityClientArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<DynEntityClient[]>(cursor, pointer, count, DynEntityClient.SerializedSize, 4, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new DynEntityClient[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, DynEntityClient.SerializedSize);
            rows[i] = new DynEntityClient
            {
                PhysObjId = rowCursor.ReadInt32(),
                Flags = rowCursor.ReadUInt16(),
                LightingHandle = rowCursor.ReadUInt16(),
                Health = rowCursor.ReadInt32()
            };
        }

        return rows;
    }

    private static IReadOnlyList<DynEntityColl> ReadDynEntityCollArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<DynEntityColl[]>(cursor, pointer, count, DynEntityColl.SerializedSize, 4, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new DynEntityColl[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, DynEntityColl.SerializedSize);
            rows[i] = new DynEntityColl
            {
                Sector = rowCursor.ReadUInt16(),
                NextEntInSector = rowCursor.ReadUInt16(),
                LinkMins = ReadVec2(rowCursor),
                LinkMaxs = ReadVec2(rowCursor)
            };
        }

        return rows;
    }

    private static IReadOnlyList<SModelAabbNode> ReadSModelAabbNodeArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<SModelAabbNode[]>(cursor, pointer, count, SModelAabbNode.SerializedSize, 4, context, "clipMap_t.smodelNodes", allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new SModelAabbNode[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, SModelAabbNode.SerializedSize);
            rows[i] = new SModelAabbNode
            {
                Bounds = ReadBounds(rowCursor),
                FirstChild = rowCursor.ReadUInt16(),
                ChildCount = rowCursor.ReadUInt16()
            };
        }

        return rows;
    }

    private static IReadOnlyList<CPlane> ReadCPlaneArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        bool allowOffset,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<CPlane[]>(cursor, pointer, count, CPlane.SerializedSize, 4, context, memberName, allowOffset);
        if (!hasPayload && pointer.Type == PointerType.Offset)
        {
            // The native map graph may reuse GfxWorld's already copied
            // dpvsPlanes array as clipMap_t.planes. Both views are the same
            // 0x14-byte PS3 record. Preserve the ColMap semantic view by
            // reading the previously materialized destination bytes without
            // consuming the source stream again.
            bytes = context.Blocks.ReadBytes(
                address,
                checked(NonNegative(count, memberName) * CPlane.SerializedSize));
            hasPayload = true;
        }
        if (!hasPayload)
            return [];

        var rows = new CPlane[count];
        for (int i = 0; i < rows.Length; i++)
        {
            XBlockAddress planeAddress = address.Add(checked(i * CPlane.SerializedSize));
            if (context.TryGetClipPlane(
                    planeAddress,
                    out CPlane? existing) &&
                existing is not null)
            {
                rows[i] = existing;
                continue;
            }

            FastFileCursor rowCursor = RowCursor(bytes, address, i, CPlane.SerializedSize);
            CPlane plane = ReadCPlane(rowCursor);
            context.RegisterClipPlane(planeAddress, plane);
            rows[i] = plane;
        }

        return rows;
    }

    private static CPlane? ReadCPlanePointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<CPlane>(pointer, CPlane.SerializedSize, "cplane_s");
            XBlockAddress address = pointer.PackedAddress
                ?? throw new InvalidDataException("Packed cplane_s pointer has no decoded block address.");
            if (context.TryGetClipPlane(address, out CPlane? existing))
                return existing;

            // A direct packed pointer is allowed to target any earlier
            // materialized cplane_s record, not only one reached through this
            // loader's primary clipMap_t.planes traversal. Boneyard uses that
            // native form for brush sides which alias a plane copied earlier
            // in LARGE. The range check above proves that the complete typed
            // record is resident, so materialize its semantic view directly
            // from block memory and retain it for subsequent aliases.
            CPlane materialized = ReadCPlane(
                new FastFileCursor(
                    context.Blocks.ReadBytes(address, CPlane.SerializedSize),
                    address));
            context.RegisterClipPlane(address, materialized);
            return materialized;
        }

        if (pointer.Type != PointerType.Inline)
            throw new InvalidDataException($"cplane_s pointer 0x{pointer.Raw:X8} is not null/inline/offset.");

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, CPlane.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"cplane_s pointer patched to {targetAddress}, but plane loaded at {loadedAddress}.");

        CPlane plane = ReadCPlane(new FastFileCursor(bytes, targetAddress));
        context.RegisterClipPlane(targetAddress, plane);
        return plane;
    }

    private static CPlane ReadCPlane(FastFileCursor cursor)
    {
        return new CPlane
        {
            Normal = ReadVec3(cursor),
            Dist = ReadSingle(cursor),
            Type = cursor.ReadByte(),
            SignBits = cursor.ReadByte(),
            Pad12 = cursor.ReadBytes(2)
        };
    }

    private static IReadOnlyList<ModelBounds> ReadBoundsArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<ModelBounds[]>(cursor, pointer, count, 0x18, alignment, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new ModelBounds[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = ReadBounds(RowCursor(bytes, address, i, 0x18));

        return rows;
    }

    private static IReadOnlyList<ModelVec3> ReadVec3Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<ModelVec3[]>(cursor, pointer, count, 0x0C, 4, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var rows = new ModelVec3[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = ReadVec3(RowCursor(bytes, address, i, 0x0C));

        return rows;
    }

    private static IReadOnlyList<uint> ReadUInt32Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, _, bool hasPayload) = LoadArray<uint[]>(cursor, pointer, count, sizeof(uint), alignment, context, memberName, allowOffset: false);
        if (!hasPayload)
            return [];

        var valueCursor = new FastFileCursor(bytes);
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = valueCursor.ReadUInt32();

        return values;
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        bool allowOffset = false)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<ushort[]>(cursor, pointer, count, sizeof(ushort), alignment, context, memberName, allowOffset);
        if (!hasPayload && pointer.Type == PointerType.Offset)
        {
            bytes = context.Blocks.ReadBytes(
                address,
                checked(NonNegative(count, memberName) * sizeof(ushort)));
            hasPayload = true;
        }
        if (!hasPayload)
            return [];

        var valueCursor = new FastFileCursor(bytes);
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = valueCursor.ReadUInt16();

        return values;
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, _, bool hasPayload) = LoadArray<byte[]>(cursor, pointer, count, 1, alignment, context, memberName, allowOffset: false);
        return hasPayload ? bytes : [];
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        bool allowOffset)
    {
        return LoadArray(cursor, pointer, count, stride, alignment, context, memberName, allowOffset, targetType: null);
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadArray<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        bool allowOffset)
    {
        return LoadArray(cursor, pointer, count, stride, alignment, context, memberName, allowOffset, typeof(T));
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        bool allowOffset,
        Type? targetType)
    {
        count = NonNegative(count, memberName);
        int byteCount = checked(count * stride);

        if (pointer.Type == PointerType.Null)
        {
            if (count != 0)
                throw new InvalidDataException($"{memberName} is null with non-zero count {count}.");

            return ([], context.Blocks.CurrentAddress, HasPayload: false);
        }

        if (pointer.Type == PointerType.Offset)
        {
            if (!allowOffset)
                throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is packed, but the PS3 loader path only proves null/non-null inline loading.");

            if (targetType is null)
                context.PointerReader.ValidateOffsetPointerRange(pointer, byteCount, memberName);
            else
                context.PointerReader.ValidateOffsetPointerRange(pointer, targetType, byteCount, memberName);

            return ([], pointer.PackedAddress ?? context.Blocks.CurrentAddress, HasPayload: false);
        }

        if (allowOffset && pointer.Type != PointerType.Inline)
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not null/inline/offset.");

        if (!allowOffset && pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not null/inline/insert.");

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} pointer patched to {targetAddress}, but data loaded at {loadedAddress}.");

        return (bytes, targetAddress, HasPayload: true);
    }

    private static T ReadRuntimeArray<T>(DbLoadExecutionContext context, Func<T> read)
    {
        context.Blocks.Push(XFileBlockType.RUNTIME);
        try
        {
            return read();
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static FastFileCursor RowCursor(byte[] bytes, XBlockAddress address, int index, int stride)
    {
        int offset = checked(index * stride);
        return new FastFileCursor(bytes.AsSpan(offset, stride).ToArray(), address.Add(offset));
    }

    private static CLeaf ReadLeaf(FastFileCursor cursor)
    {
        return new CLeaf
        {
            FirstCollAabbIndex = cursor.ReadUInt16(),
            CollAabbCount = cursor.ReadUInt16(),
            BrushContents = cursor.ReadInt32(),
            TerrainContents = cursor.ReadInt32(),
            Mins = ReadVec3(cursor),
            Maxs = ReadVec3(cursor),
            LeafBrushNode = cursor.ReadInt32()
        };
    }

    private static CollisionBorder ReadCollisionBorder(FastFileCursor cursor)
    {
        return new CollisionBorder
        {
            DistEq = [ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor)],
            ZBase = ReadSingle(cursor),
            ZSlope = ReadSingle(cursor),
            Start = ReadSingle(cursor),
            Length = ReadSingle(cursor)
        };
    }

    private static GfxPlacement ReadGfxPlacement(FastFileCursor cursor)
    {
        return new GfxPlacement
        {
            Quat = [ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor)],
            Origin = ReadVec3(cursor)
        };
    }

    private static PhysMass ReadPhysMass(FastFileCursor cursor)
    {
        return new PhysMass
        {
            CenterOfMass = ReadVec3(cursor),
            MomentsOfInertia = ReadVec3(cursor),
            ProductsOfInertia = ReadVec3(cursor)
        };
    }

    private static ModelBounds ReadBounds(FastFileCursor cursor)
    {
        return new ModelBounds
        {
            MidPoint = ReadVec3(cursor),
            HalfSize = ReadVec3(cursor)
        };
    }

    private static ModelVec3 ReadVec3(FastFileCursor cursor)
    {
        return new ModelVec3
        {
            X = ReadSingle(cursor),
            Y = ReadSingle(cursor),
            Z = ReadSingle(cursor)
        };
    }

    private static ModelVec2 ReadVec2(FastFileCursor cursor)
    {
        return new ModelVec2
        {
            a = ReadSingle(cursor),
            b = ReadSingle(cursor)
        };
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode)
    {
        return context.PointerReader.ReadPointer<T>(cursor, mode);
    }

    private static short ReadInt16(FastFileCursor cursor)
    {
        return unchecked((short)cursor.ReadUInt16());
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static int Count(uint count, string name)
    {
        if (count > int.MaxValue)
            throw new InvalidDataException($"{name} count {count} exceeds int.MaxValue.");

        return (int)count;
    }

    private static int NonNegative(int count, string name)
    {
        if (count < 0)
            throw new InvalidDataException($"{name} has negative count {count}.");

        return count;
    }

    private static int TriEdgeWalkableByteCount(int triCount)
    {
        triCount = NonNegative(triCount, "triCount");
        return checked(((triCount * 3 + 0x1F) >> 5) << 2);
    }
}
