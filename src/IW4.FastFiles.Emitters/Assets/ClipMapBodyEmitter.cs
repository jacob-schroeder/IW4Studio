using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Loader-ordered body writer for the shared ColMapSp/ColMapMp wire format.
/// Its input is a fully detached authoring model. Loaded
/// XModel/Fx/PhysPreset/MapEnts objects are not acceptable build dependencies;
/// imported pointer forms and incoming bodies are represented by detached
/// nested build links.
/// </summary>
public sealed class ClipMapBodyEmitter : IXAssetBodyEmitter
{
    public ClipMapBodyEmitter(XAssetType assetType)
    {
        if (assetType is not (XAssetType.ColMapSp or XAssetType.ColMapMp))
            throw new ArgumentOutOfRangeException(nameof(assetType));
        AssetType = assetType;
    }

    public XAssetType AssetType { get; }

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IClipMapBuildData data)
        {
            errors.Add(Error("body", "ColMap build data does not implement IClipMapBuildData.", rowIndex));
            return errors;
        }

        ClipMapAsset value = data.Definition;
        ClipMapReferenceBuildData references = data.References;
        if (data.LinkerProvenance.ImportedPlanesPackedRaw is { } packedRaw &&
            XPointerCodec.GetType(packedRaw) != PointerType.Offset)
        {
            errors.Add(Error(
                "planesPointer",
                "Imported ColMap plane-table raw value is not a packed offset pointer.",
                rowIndex));
        }
        IReadOnlyList<int?> leafBrushPointerRaws =
            data.LinkerProvenance.LeafBrushNodeBrushesPointerRaws;
        if (leafBrushPointerRaws.Count != 0)
        {
            Count(
                value.LeafBrushNodes.Count,
                leafBrushPointerRaws.Count,
                "linkerProvenance.leafBrushNodeBrushesPointerRaws",
                errors,
                rowIndex);
        }
        for (int index = 0;
             index < Math.Min(
                 value.LeafBrushNodes.Count,
                 leafBrushPointerRaws.Count);
             index++)
        {
            if (leafBrushPointerRaws[index] is not { } raw)
                continue;
            PointerType pointerType = XPointerCodec.GetType(raw);
            if (value.LeafBrushNodes[index].LeafBrushCount <= 0 ||
                pointerType is not (PointerType.Inline or PointerType.Offset))
            {
                errors.Add(Error(
                    $"linkerProvenance.leafBrushNodeBrushesPointerRaws[{index}]",
                    "Imported positive-count leaf-brush payload pointers must be inline or packed offsets.",
                rowIndex));
            }
        }
        IReadOnlyList<int?> partitionBorderPointerRaws =
            data.LinkerProvenance.PartitionBordersPointerRaws;
        if (partitionBorderPointerRaws.Count != 0)
        {
            Count(
                value.Partitions.Count,
                partitionBorderPointerRaws.Count,
                "linkerProvenance.partitionBordersPointerRaws",
                errors,
                rowIndex);
        }
        for (int index = 0;
             index < Math.Min(
                 value.Partitions.Count,
                 partitionBorderPointerRaws.Count);
             index++)
        {
            if (partitionBorderPointerRaws[index] is { } raw &&
                XPointerCodec.GetType(raw) != PointerType.Offset)
            {
                errors.Add(Error(
                    $"linkerProvenance.partitionBordersPointerRaws[{index}]",
                    "Imported collision-partition border pointer must be a packed offset.",
                    rowIndex));
            }
        }
        if (data.SerializedType != AssetType || value.SerializedType != AssetType)
            errors.Add(Error("serializedType", "The build row and definition must retain the exact ColMapSp/ColMapMp serialized type.", rowIndex));
        String(value.Name, "name", errors, rowIndex);
        Count(value.PlaneCount, value.Planes.Count, "planes", errors, rowIndex);
        Count(value.NumStaticModels, value.StaticModelList.Count, "staticModelList", errors, rowIndex);
        Count(value.NumMaterials, value.Materials.Count, "materials", errors, rowIndex);
        Count(value.NumBrushSides, value.BrushSides.Count, "brushSides", errors, rowIndex);
        Count(value.NumBrushEdges, value.BrushEdges.Count, "brushEdges", errors, rowIndex);
        Count(value.NumNodes, value.Nodes.Count, "nodes", errors, rowIndex);
        Count(value.NumLeafs, value.Leafs.Count, "leafs", errors, rowIndex);
        Count(value.LeafBrushNodesCount, value.LeafBrushNodes.Count, "leafBrushNodes", errors, rowIndex);
        Count(value.NumLeafBrushes, value.LeafBrushes.Count, "leafBrushes", errors, rowIndex);
        Count(value.NumLeafSurfaces, value.LeafSurfaces.Count, "leafSurfaces", errors, rowIndex);
        Count(value.VertCount, value.Verts.Count, "verts", errors, rowIndex);
        Count(CheckedMultiply(value.TriCount, 3, "triIndices", errors, rowIndex), value.TriIndices.Count, "triIndices", errors, rowIndex);
        Count(TriEdgeBytes(value.TriCount, errors, rowIndex), value.TriEdgeIsWalkable.Count, "triEdgeIsWalkable", errors, rowIndex);
        Count(value.BorderCount, value.Borders.Count, "borders", errors, rowIndex);
        Count(value.PartitionCount, value.Partitions.Count, "partitions", errors, rowIndex);
        Count(value.AabbTreeCount, value.AabbTrees.Count, "aabbTrees", errors, rowIndex);
        Count(value.NumSubModels, value.CModels.Count, "cmodels", errors, rowIndex);
        Count(value.NumBrushes, value.Brushes.Count, "brushes", errors, rowIndex);
        Count(value.NumBrushes, value.BrushBounds.Count, "brushBounds", errors, rowIndex);
        Count(value.NumBrushes, value.BrushContents.Count, "brushContents", errors, rowIndex);
        Count(value.SModelNodeCount, value.SModelNodes.Count, "smodelNodes", errors, rowIndex);
        if (value.PadD0ToFF.Count != 0x30) errors.Add(Error("padD0ToFF", "ColMap root tail is exactly 0x30 bytes.", rowIndex));
        if (value.DynEntCount.Count != 2 || value.DynEntDefList.Count != 2 || value.DynEntPoseList.Count != 2 || value.DynEntClientList.Count != 2 || value.DynEntCollList.Count != 2)
            errors.Add(Error("dynEnt", "ColMap has exactly two dynamic-entity list slots.", rowIndex));
        Count(value.StaticModelList.Count, references.StaticModels.Count, "references.staticModels", errors, rowIndex);
        if (references.StaticModelLinks.Count != 0)
            Count(value.StaticModelList.Count, references.StaticModelLinks.Count, "references.staticModelLinks", errors, rowIndex);
        if (references.DynamicEntities.Count != 2) errors.Add(Error("references.dynamicEntities", "ColMap has exactly two dynamic-entity reference lists.", rowIndex));

        for (int index = 0; index < value.Planes.Count; index++) Plane(value.Planes[index], $"planes[{index}]", errors, rowIndex);

        for (int index = 0; index < value.StaticModelList.Count; index++)
        {
            if (value.StaticModelList[index].XModel is not null)
                errors.Add(Error($"staticModelList[{index}].xmodel", "Loaded XModel objects are not build input; use a detached symbolic reference.", rowIndex));
            SymbolicXAssetReference? reference =
                index < references.StaticModels.Count
                    ? references.StaticModels[index]
                    : null;
            Reference(reference, XAssetType.XModel, $"references.staticModels[{index}]", errors, rowIndex);
            Nested(
                LinkAt(references.StaticModelLinks, index),
                reference,
                XAssetType.XModel,
                $"references.staticModelLinks[{index}]",
                errors,
                rowIndex);
            Fixed(value.StaticModelList[index].InvScaledAxis.Count, 3, $"staticModelList[{index}].invScaledAxis", errors, rowIndex);
        }
        for (int index = 0; index < value.Materials.Count; index++) String(value.Materials[index].Name, $"materials[{index}].name", errors, rowIndex);
        for (int index = 0; index < value.BrushSides.Count; index++) Side(value.BrushSides[index], $"brushSides[{index}]", errors, rowIndex);
        for (int index = 0; index < value.Nodes.Count; index++)
        {
            Fixed(value.Nodes[index].Children.Count, 2, $"nodes[{index}].children", errors, rowIndex);
        }
        for (int index = 0; index < value.LeafBrushNodes.Count; index++)
        {
            CLeafBrushNode node = value.LeafBrushNodes[index];
            if (node.LeafBrushCount > 0)
            {
                Count(node.LeafBrushCount, node.Data.Brushes.Count, $"leafBrushNodes[{index}].brushes", errors, rowIndex);
                Fixed(node.Data.LeafUnionPad.Count, 8, $"leafBrushNodes[{index}].leafUnionPad", errors, rowIndex);
                if (node.Data.Children is not null) errors.Add(Error($"leafBrushNodes[{index}].children", "Positive LeafBrushCount selects the brush-list union arm only.", rowIndex));
            }
            else if (node.Data.Children is null ||
                     node.Data.Children.ChildOffsets.Count != 2)
                errors.Add(Error(
                    $"leafBrushNodes[{index}].children",
                    "Non-positive LeafBrushCount requires dist, range, and " +
                    "exactly two relative child offsets.",
                    rowIndex));
        }
        for (int index = 0; index < value.Borders.Count; index++) Fixed(value.Borders[index].DistEq.Count, 3, $"borders[{index}].distEq", errors, rowIndex);
        for (int index = 0; index < value.Partitions.Count; index++)
        {
            CollisionPartition partition = value.Partitions[index];
            Count(partition.BorderCount, partition.Borders.Count, $"partitions[{index}].borders", errors, rowIndex);
            for (int child = 0; child < partition.Borders.Count; child++) Fixed(partition.Borders[child].DistEq.Count, 3, $"partitions[{index}].borders[{child}].distEq", errors, rowIndex);
        }
        for (int index = 0; index < value.Brushes.Count; index++) Brush(value.Brushes[index], $"brushes[{index}]", errors, rowIndex);
        ValidateBrushAliases(value, errors, rowIndex);
        if (value.MapEnts is not null) errors.Add(Error("mapEnts", "Loaded MapEnts objects are not build input; use a detached symbolic reference.", rowIndex));
        Reference(references.MapEnts, XAssetType.MapEnts, "references.mapEnts", errors, rowIndex);
        Nested(
            references.MapEntsLink,
            references.MapEnts,
            XAssetType.MapEnts,
            "references.mapEntsLink",
            errors,
            rowIndex);
        for (int list = 0; list < Math.Min(2, value.DynEntDefList.Count); list++)
        {
            Count(value.DynEntCount.Count > list ? value.DynEntCount[list] : -1, value.DynEntDefList[list].Count, $"dynEntDefList[{list}]", errors, rowIndex);
            Count(value.DynEntDefList[list].Count, list < references.DynamicEntities.Count ? references.DynamicEntities[list].Count : -1, $"references.dynamicEntities[{list}]", errors, rowIndex);
            for (int index = 0; index < value.DynEntDefList[list].Count; index++)
            {
                DynEntityDef def = value.DynEntDefList[list][index];
                if (def.XModel is not null || def.DestroyFx is not null || def.PhysPreset is not null)
                    errors.Add(Error($"dynEntDefList[{list}]", "Loaded nested assets are not build input; use detached symbolic references.", rowIndex));
                Fixed(def.Pose.Quat.Count, 4, $"dynEntDefList[{list}].pose.quat", errors, rowIndex);
                ClipMapDynEntityReferenceBuildData? links = list < references.DynamicEntities.Count && index < references.DynamicEntities[list].Count ? references.DynamicEntities[list][index] : null;
                Reference(links?.XModel, XAssetType.XModel, $"references.dynamicEntities[{list}][{index}].xmodel", errors, rowIndex);
                Reference(links?.DestroyFx, XAssetType.Fx, $"references.dynamicEntities[{list}][{index}].destroyFx", errors, rowIndex);
                Reference(links?.PhysPreset, XAssetType.PhysPreset, $"references.dynamicEntities[{list}][{index}].physPreset", errors, rowIndex);
                Nested(links?.XModelLink, links?.XModel, XAssetType.XModel, $"references.dynamicEntities[{list}][{index}].xmodelLink", errors, rowIndex);
                Nested(links?.DestroyFxLink, links?.DestroyFx, XAssetType.Fx, $"references.dynamicEntities[{list}][{index}].destroyFxLink", errors, rowIndex);
                Nested(links?.PhysPresetLink, links?.PhysPreset, XAssetType.PhysPreset, $"references.dynamicEntities[{list}][{index}].physPresetLink", errors, rowIndex);
            }
        }
        for (int list = 0; list < Math.Min(2, value.DynEntPoseList.Count); list++)
        {
            int count = value.DynEntCount.Count > list ? value.DynEntCount[list] : -1;
            Count(count, value.DynEntPoseList[list].Count, $"dynEntPoseList[{list}]", errors, rowIndex);
            Count(count, value.DynEntClientList[list].Count, $"dynEntClientList[{list}]", errors, rowIndex);
            Count(count, value.DynEntCollList[list].Count, $"dynEntCollList[{list}]", errors, rowIndex);
            if (value.DynEntPoseList[list].Any(item => !Zero(item)) || value.DynEntClientList[list].Any(item => !Zero(item)) || value.DynEntCollList[list].Any(item => !Zero(item)))
                errors.Add(Error($"dynEntRuntime[{list}]", "Runtime dynamic-entity arrays are zero-filled and cannot carry authored source values.", rowIndex));
        }
        return errors;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IClipMapBuildData data = (IClipMapBuildData)buildData;
        ClipMapAsset value = data.Definition;
        ClipMapReferenceBuildData references = data.References;
        var all = new List<EmissionBlockSegment>();
        var source = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress rootAddress = plan.Allocate(ClipMapAsset.SerializedSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = String(value.Name, plan, all, source);
        int? importedPlanesRaw =
            plan.PreserveImportedXAssetPointerValues
                ? data.LinkerProvenance.ImportedPlanesPackedRaw
                : null;
        ArrayPlan<CPlane>? planes =
            importedPlanesRaw is null
                ? Array(
                    value.Planes,
                    CPlane.SerializedSize,
                    4,
                    plan,
                    all,
                    WritePlane)
                : null;
        var planeAliases = new Dictionary<CPlane, ChildPlan>(ReferenceEqualityComparer.Instance);
        if (planes is not null)
        {
            for (int index = 0; index < value.Planes.Count; index++)
                planeAliases.TryAdd(value.Planes[index], new ChildPlan(AddressAt(planes.Segment.Address, index * CPlane.SerializedSize), null));
        }
        else if (importedPlanesRaw is { } packedRaw)
        {
            XBlockAddress importedBase = XPointerCodec.Decode(packedRaw);
            var importedAddress = new EmissionAddress(
                importedBase.BlockType,
                importedBase.Offset);
            for (int index = 0; index < value.Planes.Count; index++)
            {
                planeAliases.TryAdd(
                    value.Planes[index],
                    new ChildPlan(
                        AddressAt(
                            importedAddress,
                            checked(index * CPlane.SerializedSize)),
                        null));
            }
        }
        ArrayPlan<ClipStaticModel>? staticModels = Array(value.StaticModelList, ClipStaticModel.SerializedSize, 4, plan, all, static (writer, _) => writer.Reserve(ClipStaticModel.SerializedSize));
        ExternalPlan?[] staticModelSources = references.StaticModels
            .Select((reference, index) => Nested(
                reference,
                LinkAt(references.StaticModelLinks, index),
                XAssetType.XModel,
                0x120,
                AddressAt(
                    staticModels!.Segment.Address,
                    checked(index * ClipStaticModel.SerializedSize)),
                plan,
                all))
            .ToArray();
        if (staticModels is not null) Replace(all, staticModels, WriteStaticModels(value.StaticModelList, staticModelSources));
        ArrayPlan<ClipMaterial>? materials = Array(value.Materials, ClipMaterial.SerializedSize, 4, plan, all, static (w, item) => { w.WriteInt32(0); w.WriteInt32(item.SurfaceFlags); w.WriteInt32(item.Contents); });
        var materialNameSource = new List<EmissionBlockSegment>();
        PlannedString?[] materialNames = value.Materials.Select(item => String(item.Name, plan, all, materialNameSource)).ToArray();
        if (materials is not null) Replace(all, materials, WriteMaterials(value.Materials, materialNames));
        ArrayPlan<CBrushSide>? brushSides = Array(value.BrushSides, CBrushSide.SerializedSize, 4, plan, all, static (w, item) => WriteSide(w, item, item.Plane is not null));
        var brushSidePlanes = value.BrushSides.Select(item => Plane(item.Plane, planeAliases, plan, all)).ToArray();
        if (brushSides is not null) Replace(all, brushSides, WriteSides(value.BrushSides, brushSidePlanes));
        ArrayPlan<byte>? brushEdges = Array(value.BrushEdges, 1, 1, plan, all, static (w, item) => w.WriteByte(item));
        ArrayPlan<CNode>? nodes = Array(value.Nodes, CNode.SerializedSize, 4, plan, all, static (w, item) => { w.WriteInt32(0); w.WriteInt16(item.Children[0]); w.WriteInt16(item.Children[1]); });
        var nodePlanes = value.Nodes.Select(item => Plane(item.Plane, planeAliases, plan, all)).ToArray();
        if (nodes is not null) Replace(all, nodes, WriteNodes(value.Nodes, nodePlanes));
        ArrayPlan<CLeaf>? leafs = Array(value.Leafs, CLeaf.SerializedSize, 4, plan, all, WriteLeaf);
        ArrayPlan<ushort>? leafBrushes = Array(value.LeafBrushes, sizeof(ushort), 2, plan, all, static (w, item) => w.WriteUInt16(item));
        ArrayPlan<CLeafBrushNode>? leafBrushNodes = Array(value.LeafBrushNodes, CLeafBrushNode.SerializedSize, 4, plan, all, static (w, _) => w.Reserve(CLeafBrushNode.SerializedSize));
        NestedAliasPlan[] leafNodeBrushes = PlanLeafBrushPayloads(
            value.LeafBrushNodes,
            value.LeafBrushes,
            leafBrushes,
            plan.PreserveImportedXAssetPointerValues
                ? data.LinkerProvenance
                    .LeafBrushNodeBrushesPointerRaws
                : [],
            plan,
            all);
        if (leafBrushNodes is not null) Replace(all, leafBrushNodes, WriteLeafBrushNodes(value.LeafBrushNodes, leafNodeBrushes));
        ArrayPlan<uint>? leafSurfaces = Array(value.LeafSurfaces, sizeof(uint), 4, plan, all, static (w, item) => w.WriteUInt32(item));
        ArrayPlan<Vec3>? verts = Array(value.Verts, 0x0c, 4, plan, all, WriteVec3);
        ArrayPlan<ushort>? triIndices = Array(value.TriIndices, sizeof(ushort), 2, plan, all, static (w, item) => w.WriteUInt16(item));
        ArrayPlan<byte>? triEdges = Array(value.TriEdgeIsWalkable, 1, 1, plan, all, static (w, item) => w.WriteByte(item));
        ArrayPlan<CollisionBorder>? borders = Array(value.Borders, CollisionBorder.SerializedSize, 4, plan, all, WriteBorder);
        ArrayPlan<CollisionPartition>? partitions = Array(value.Partitions, CollisionPartition.SerializedSize, 4, plan, all, static (w, item) => { w.WriteByte(item.TriCount); w.WriteByte(item.BorderCount); w.WriteByte(item.FirstVertSegment); w.WriteByte(item.Pad03); w.WriteInt32(item.FirstTri); w.WriteInt32(item.Borders.Count == 0 ? 0 : -1); });
        NestedAliasPlan[] partitionBorders = PlanPartitionBorderPayloads(
            value.Partitions,
            value.Borders,
            borders,
            plan.PreserveImportedXAssetPointerValues
                ? data.LinkerProvenance.PartitionBordersPointerRaws
                : [],
            plan,
            all);
        if (partitions is not null) Replace(all, partitions, WritePartitions(value.Partitions, partitionBorders));
        ArrayPlan<CollisionAabbTree>? aabbTrees = Array(value.AabbTrees, CollisionAabbTree.SerializedSize, 16, plan, all, WriteAabbTree);
        ArrayPlan<CModel>? cmodels = Array(value.CModels, CModel.SerializedSize, 4, plan, all, WriteCModel);
        ArrayPlan<CBrush>? brushes = Array(value.Brushes, CBrush.SerializedSize, 128, plan, all, static (w, _) => w.Reserve(CBrush.SerializedSize));
        BrushAliasPlan[] brushAliases = PlanBrushAliases(value.Brushes, brushSides, brushEdges);
        if (brushes is not null) Replace(all, brushes, WriteBrushes(value.Brushes, brushAliases));
        ArrayPlan<Bounds>? brushBounds = Array(value.BrushBounds, 0x18, 128, plan, all, WriteBounds);
        ArrayPlan<uint>? brushContents = Array(value.BrushContents, sizeof(uint), 4, plan, all, static (w, item) => w.WriteUInt32(item));
        ArrayPlan<SModelAabbNode>? smodelNodes = Array(value.SModelNodes, SModelAabbNode.SerializedSize, 4, plan, all, WriteSModelNode);
        ExternalPlan? mapEnts = Nested(
            references.MapEnts,
            references.MapEntsLink,
            XAssetType.MapEnts,
            0x2c,
            AddressAt(rootAddress, 0x9c),
            plan,
            all);
        ArrayPlan<DynEntityDef>? dynDefs0 = Array(value.DynEntDefList[0], DynEntityDef.SerializedSize, 4, plan, all, static (writer, _) => writer.Reserve(DynEntityDef.SerializedSize));
        ExternalPlan?[][] dynamicSources0 = PlanDynamicSources(
            references.DynamicEntities[0],
            dynDefs0,
            plan,
            all);
        if (dynDefs0 is not null) Replace(all, dynDefs0, WriteDynDefs(value.DynEntDefList[0], dynamicSources0));
        ArrayPlan<DynEntityDef>? dynDefs1 = Array(value.DynEntDefList[1], DynEntityDef.SerializedSize, 4, plan, all, static (writer, _) => writer.Reserve(DynEntityDef.SerializedSize));
        ExternalPlan?[][] dynamicSources1 = PlanDynamicSources(
            references.DynamicEntities[1],
            dynDefs1,
            plan,
            all);
        if (dynDefs1 is not null) Replace(all, dynDefs1, WriteDynDefs(value.DynEntDefList[1], dynamicSources1));
        bool runtimePose0 = Runtime(value.DynEntPoseList[0].Count, DynEntityPose.SerializedSize, 4, plan);
        bool runtimePose1 = Runtime(value.DynEntPoseList[1].Count, DynEntityPose.SerializedSize, 4, plan);
        bool runtimeClient0 = Runtime(value.DynEntClientList[0].Count, DynEntityClient.SerializedSize, 4, plan);
        bool runtimeClient1 = Runtime(value.DynEntClientList[1].Count, DynEntityClient.SerializedSize, 4, plan);
        bool runtimeColl0 = Runtime(value.DynEntCollList[0].Count, DynEntityColl.SerializedSize, 4, plan);
        bool runtimeColl1 = Runtime(value.DynEntCollList[1].Count, DynEntityColl.SerializedSize, 4, plan);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var root = new XSourceWriter();
        root.WriteInt32(Pointer(name));
        root.WriteInt32(
            plan.PreserveImportedXAssetPointerValues
                ? data.LinkerProvenance.ImportedIsInUse ?? value.IsInUse
                : value.IsInUse);
        root.WriteInt32(value.Planes.Count);
        root.WriteInt32(importedPlanesRaw ?? Pointer(planes));
        root.WriteInt32(value.StaticModelList.Count); root.WriteInt32(Pointer(staticModels));
        root.WriteInt32(value.Materials.Count); root.WriteInt32(Pointer(materials));
        root.WriteInt32(value.BrushSides.Count); root.WriteInt32(Pointer(brushSides));
        root.WriteInt32(value.BrushEdges.Count); root.WriteInt32(Pointer(brushEdges));
        root.WriteInt32(value.Nodes.Count); root.WriteInt32(Pointer(nodes));
        root.WriteInt32(value.Leafs.Count); root.WriteInt32(Pointer(leafs));
        root.WriteInt32(value.LeafBrushNodes.Count); root.WriteInt32(Pointer(leafBrushNodes));
        root.WriteInt32(value.LeafBrushes.Count); root.WriteInt32(Pointer(leafBrushes));
        root.WriteInt32(value.LeafSurfaces.Count); root.WriteInt32(Pointer(leafSurfaces));
        root.WriteInt32(value.Verts.Count); root.WriteInt32(Pointer(verts));
        root.WriteInt32(value.TriCount); root.WriteInt32(Pointer(triIndices)); root.WriteInt32(Pointer(triEdges));
        root.WriteInt32(value.Borders.Count); root.WriteInt32(Pointer(borders));
        root.WriteInt32(value.Partitions.Count); root.WriteInt32(Pointer(partitions));
        root.WriteInt32(value.AabbTrees.Count); root.WriteInt32(Pointer(aabbTrees));
        root.WriteInt32(value.CModels.Count); root.WriteInt32(Pointer(cmodels));
        root.WriteUInt16(value.NumBrushes); root.WriteUInt16(value.Pad8ETo8F);
        root.WriteInt32(Pointer(brushes)); root.WriteInt32(Pointer(brushBounds)); root.WriteInt32(Pointer(brushContents)); root.WriteInt32(Pointer(mapEnts));
        root.WriteUInt16(value.SModelNodeCount); root.WriteUInt16(value.PadA2ToA3); root.WriteInt32(Pointer(smodelNodes));
        root.WriteUInt16(value.DynEntCount[0]); root.WriteUInt16(value.DynEntCount[1]);
        root.WriteInt32(Pointer(dynDefs0)); root.WriteInt32(Pointer(dynDefs1));
        root.WriteInt32(Pointer(runtimePose0)); root.WriteInt32(Pointer(runtimePose1));
        root.WriteInt32(Pointer(runtimeClient0)); root.WriteInt32(Pointer(runtimeClient1));
        root.WriteInt32(Pointer(runtimeColl0)); root.WriteInt32(Pointer(runtimeColl1));
        root.WriteUInt32(value.Checksum); root.WriteBytes(value.PadD0ToFF.ToArray());
        Exact(root, ClipMapAsset.SerializedSize, "ColMap root");
        var rootSegment = new EmissionBlockSegment(rootAddress, root.ToArray()); all.Add(rootSegment);

        source.Insert(0, rootSegment);
        Add(source, planes); Add(source, staticModels); foreach (ExternalPlan? item in staticModelSources) Add(source, item); Add(source, materials); source.AddRange(materialNameSource); Add(source, brushSides); foreach (ChildPlan? item in brushSidePlanes) Add(source, item); Add(source, brushEdges); Add(source, nodes); foreach (ChildPlan? item in nodePlanes) Add(source, item); Add(source, leafs); Add(source, leafBrushes); Add(source, leafBrushNodes); foreach (NestedAliasPlan item in leafNodeBrushes) Add(source, item); Add(source, leafSurfaces); Add(source, verts); Add(source, triIndices); Add(source, triEdges); Add(source, borders); Add(source, partitions); foreach (NestedAliasPlan item in partitionBorders) Add(source, item); Add(source, aabbTrees); Add(source, cmodels); Add(source, brushes); Add(source, brushBounds); Add(source, brushContents); Add(source, smodelNodes); Add(source, mapEnts); Add(source, dynDefs0); foreach (ExternalPlan?[] item in dynamicSources0) foreach (ExternalPlan? sourceItem in item) Add(source, sourceItem); Add(source, dynDefs1); foreach (ExternalPlan?[] item in dynamicSources1) foreach (ExternalPlan? sourceItem in item) Add(source, sourceItem);
        return new AssetBodyEmission(AssetType, rootAddress, all, source);
    }

    private static ArrayPlan<T>? Array<T>(IReadOnlyList<T> values, int stride, int alignment, EmissionPlan plan, List<EmissionBlockSegment> all, Action<XSourceWriter, T> write)
    {
        if (values.Count == 0) return null;
        EmissionAddress address = plan.Allocate(checked(values.Count * stride), alignment);
        var writer = new XSourceWriter(); foreach (T value in values) write(writer, value); Exact(writer, checked(values.Count * stride), typeof(T).Name);
        var segment = new EmissionBlockSegment(address, writer.ToArray()); all.Add(segment); return new ArrayPlan<T>(segment);
    }

    private static ChildPlan? Plane(CPlane? value, Dictionary<CPlane, ChildPlan> aliases, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (value is null) return null;
        if (aliases.TryGetValue(value, out ChildPlan? existing)) return existing;
        ArrayPlan<CPlane> emitted = Array([value], CPlane.SerializedSize, 4, plan, all, WritePlane)!;
        var child = new ChildPlan(emitted.Segment.Address, emitted.Segment);
        aliases.Add(value, child);
        return child;
    }
    private static bool Runtime(int count, int stride, int alignment, EmissionPlan plan) { if (count == 0) return false; plan.Push(XFileBlockType.RUNTIME); plan.Allocate(checked(count * stride), alignment); plan.Pop(XFileBlockType.RUNTIME); return true; }
    private static void Replace<T>(List<EmissionBlockSegment> all, ArrayPlan<T> plan, byte[] bytes) { int index = all.FindIndex(item => item.Address == plan.Segment.Address); if (index < 0) throw new InvalidDataException("Emission segment replacement target is missing."); var replacement = new EmissionBlockSegment(plan.Segment.Address, bytes); all[index] = replacement; plan.Segment = replacement; }
    private static byte[] WriteMaterials(IReadOnlyList<ClipMaterial> values, IReadOnlyList<PlannedString?> names) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { writer.WriteInt32(Pointer(names[index])); writer.WriteInt32(values[index].SurfaceFlags); writer.WriteInt32(values[index].Contents); } return writer.ToArray(); }
    private static byte[] WriteSides(IReadOnlyList<CBrushSide> values, IReadOnlyList<ChildPlan?> planes) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) WriteSide(writer, values[index], planes[index]); return writer.ToArray(); }
    private static byte[] WriteNodes(IReadOnlyList<CNode> values, IReadOnlyList<ChildPlan?> planes) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { writer.WriteInt32(Pointer(planes[index])); writer.WriteInt16(values[index].Children[0]); writer.WriteInt16(values[index].Children[1]); } return writer.ToArray(); }
    private static byte[] WriteLeafBrushNodes(IReadOnlyList<CLeafBrushNode> values, IReadOnlyList<NestedAliasPlan> brushes) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { CLeafBrushNode value = values[index]; writer.WriteByte(value.Axis); writer.WriteByte(value.Pad01); writer.WriteInt16(value.LeafBrushCount); writer.WriteInt32(value.Contents); if (value.LeafBrushCount > 0) { writer.WriteInt32(brushes[index].Pointer); writer.WriteBytes(value.Data.LeafUnionPad.ToArray()); } else { writer.WriteSingle(value.Data.Children!.Dist); writer.WriteSingle(value.Data.Children.Range); foreach (ushort offset in value.Data.Children.ChildOffsets) writer.WriteUInt16(offset); } } return writer.ToArray(); }
    private static byte[] WritePartitions(IReadOnlyList<CollisionPartition> values, IReadOnlyList<NestedAliasPlan> borders) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { CollisionPartition value = values[index]; writer.WriteByte(value.TriCount); writer.WriteByte(value.BorderCount); writer.WriteByte(value.FirstVertSegment); writer.WriteByte(value.Pad03); writer.WriteInt32(value.FirstTri); writer.WriteInt32(borders[index].Pointer); } return writer.ToArray(); }
    private static byte[] WriteBrushes(IReadOnlyList<CBrush> values, IReadOnlyList<BrushAliasPlan> aliases) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { CBrush value = values[index]; writer.WriteUInt16(value.NumSides); writer.WriteUInt16(value.GlassPieceIndex); writer.WriteInt32(aliases[index].SidesPointer); writer.WriteInt32(aliases[index].BaseAdjacentSidePointer); foreach (short item in value.AxialMaterialNum) writer.WriteInt16(item); writer.WriteBytes(value.FirstAdjacentSideOffsets.ToArray()); writer.WriteBytes(value.EdgeCount.ToArray()); } return writer.ToArray(); }
    private static byte[] WriteStaticModels(IReadOnlyList<ClipStaticModel> values, IReadOnlyList<ExternalPlan?> references) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { writer.WriteInt32(Pointer(references[index])); WriteVec3(writer, values[index].Origin); foreach (Vec3 axis in values[index].InvScaledAxis) WriteVec3(writer, axis); WriteVec3(writer, values[index].AbsMin); WriteVec3(writer, values[index].AbsMax); } return writer.ToArray(); }
    private static void WritePlane(XSourceWriter writer, CPlane value) { WriteVec3(writer, value.Normal); writer.WriteSingle(value.Dist); writer.WriteByte(value.Type); writer.WriteByte(value.SignBits); writer.WriteBytes(value.Pad12.ToArray()); }
    private static void WriteSide(XSourceWriter writer, CBrushSide value, bool hasPlane) { writer.WriteInt32(Pointer(hasPlane)); writer.WriteUInt16(value.MaterialNum); writer.WriteByte(value.FirstAdjacentSideOffset); writer.WriteByte(value.EdgeCount); }
    private static void WriteSide(XSourceWriter writer, CBrushSide value, ChildPlan? plane) { writer.WriteInt32(Pointer(plane)); writer.WriteUInt16(value.MaterialNum); writer.WriteByte(value.FirstAdjacentSideOffset); writer.WriteByte(value.EdgeCount); }
    private static void WriteLeaf(XSourceWriter writer, CLeaf value) { writer.WriteUInt16(value.FirstCollAabbIndex); writer.WriteUInt16(value.CollAabbCount); writer.WriteInt32(value.BrushContents); writer.WriteInt32(value.TerrainContents); WriteVec3(writer, value.Mins); WriteVec3(writer, value.Maxs); writer.WriteInt32(value.LeafBrushNode); }
    private static void WriteBorder(XSourceWriter writer, CollisionBorder value) { foreach (float item in value.DistEq) writer.WriteSingle(item); writer.WriteSingle(value.ZBase); writer.WriteSingle(value.ZSlope); writer.WriteSingle(value.Start); writer.WriteSingle(value.Length); }
    private static void WriteAabbTree(XSourceWriter writer, CollisionAabbTree value) { WriteVec3(writer, value.Origin); writer.WriteUInt16(value.MaterialIndex); writer.WriteUInt16(value.ChildCount); WriteVec3(writer, value.HalfSize); writer.WriteInt32(value.FirstChildOrPartitionIndex); }
    private static void WriteCModel(XSourceWriter writer, CModel value) { WriteVec3(writer, value.Mins); WriteVec3(writer, value.Maxs); writer.WriteSingle(value.Radius); WriteLeaf(writer, value.Leaf); }
    private static void WriteBounds(XSourceWriter writer, Bounds value) { WriteVec3(writer, value.MidPoint); WriteVec3(writer, value.HalfSize); }
    private static void WriteSModelNode(XSourceWriter writer, SModelAabbNode value) { WriteBounds(writer, value.Bounds); writer.WriteUInt16(value.FirstChild); writer.WriteUInt16(value.ChildCount); }
    private static byte[] WriteDynDefs(IReadOnlyList<DynEntityDef> values, IReadOnlyList<ExternalPlan?[]> references) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { DynEntityDef value = values[index]; writer.WriteInt32(value.Type); WritePlacement(writer, value.Pose); writer.WriteInt32(Pointer(references[index][0])); writer.WriteUInt16(value.BrushModel); writer.WriteUInt16(value.PhysicsBrushModel); writer.WriteInt32(Pointer(references[index][1])); writer.WriteInt32(Pointer(references[index][2])); writer.WriteInt32(value.Health); WriteMass(writer, value.Mass); writer.WriteInt32(value.Contents); } return writer.ToArray(); }
    private static void WritePlacement(XSourceWriter writer, GfxPlacement value) { foreach (float item in value.Quat) writer.WriteSingle(item); WriteVec3(writer, value.Origin); }
    private static void WriteMass(XSourceWriter writer, PhysMass value) { WriteVec3(writer, value.CenterOfMass); WriteVec3(writer, value.MomentsOfInertia); WriteVec3(writer, value.ProductsOfInertia); }
    private static void WriteVec3(XSourceWriter writer, Vec3 value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static int Pointer(PlannedString? value) => AssetBodyEmitterHelpers.SourcePointer(value);
    private static int Pointer<T>(ArrayPlan<T>? value) => value is null ? 0 : -1;
    private static int Pointer(ChildPlan? value) => value is null ? 0 : value.Source is null ? value.Address.ToPackedPointer() : -1;
    private static int Pointer(ExternalPlan? value) => value?.PointerRaw ?? 0;
    private static int Pointer(bool value) => value ? -1 : 0;
    private static void Add<T>(List<EmissionBlockSegment> source, ArrayPlan<T>? value) { if (value is not null) source.Add(value.Segment); }
    private static void Add(List<EmissionBlockSegment> source, ChildPlan? value) { if (value?.Source is { } segment) source.Add(segment); }
    private static void Add(List<EmissionBlockSegment> source, NestedAliasPlan value) { if (value.Source is { } segment) source.Add(segment); }
    private static void Add(List<EmissionBlockSegment> source, ExternalPlan? value) { if (value is not null) source.AddRange(value.SourceSegments); }
    private static EmissionAddress AddressAt(EmissionAddress address, int offset) => new(address.Block, checked(address.Offset + offset));
    private static void Exact(XSourceWriter writer, int expected, string name) { if (writer.Position != expected) throw new InvalidDataException($"{name} emitted 0x{writer.Position:X} bytes, expected 0x{expected:X}."); }

    private static NestedAliasPlan[] PlanLeafBrushPayloads(
        IReadOnlyList<CLeafBrushNode> nodes,
        IReadOnlyList<ushort> globalBrushes,
        ArrayPlan<ushort>? leafBrushes,
        IReadOnlyList<int?> importedPointerRaws,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        var payloads = new NestedAliasPlan[nodes.Count];
        for (int index = 0; index < nodes.Count; index++)
        {
            CLeafBrushNode node = nodes[index];
            if (node.LeafBrushCount <= 0)
            {
                payloads[index] = new NestedAliasPlan(0, null);
                continue;
            }

            int? importedRaw = index < importedPointerRaws.Count
                ? importedPointerRaws[index]
                : null;
            if (importedRaw is { } raw &&
                XPointerCodec.GetType(raw) == PointerType.Offset)
            {
                payloads[index] = new NestedAliasPlan(raw, null);
                continue;
            }

            bool forceInline =
                importedRaw is { } inlineRaw &&
                XPointerCodec.GetType(inlineRaw) == PointerType.Inline;
            int aliasIndex = FindValueSlice(globalBrushes, node.Data.Brushes);
            if (!forceInline &&
                aliasIndex >= 0 &&
                leafBrushes is not null)
            {
                int pointer = AddressAt(
                    leafBrushes.Segment.Address,
                    checked(aliasIndex * sizeof(ushort))).ToPackedPointer();
                payloads[index] = new NestedAliasPlan(pointer, null);
                continue;
            }

            ArrayPlan<ushort> child = Array(
                node.Data.Brushes,
                sizeof(ushort),
                2,
                plan,
                all,
                static (writer, value) => writer.WriteUInt16(value))
                ?? throw new InvalidDataException("A positive leaf brush count has no child payload.");
            payloads[index] = new NestedAliasPlan(-1, child.Segment);
        }

        return payloads;
    }

    private static NestedAliasPlan[] PlanPartitionBorderPayloads(
        IReadOnlyList<CollisionPartition> partitions,
        IReadOnlyList<CollisionBorder> globalBorders,
        ArrayPlan<CollisionBorder>? borders,
        IReadOnlyList<int?> importedPointerRaws,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        var payloads = new NestedAliasPlan[partitions.Count];
        for (int index = 0; index < partitions.Count; index++)
        {
            CollisionPartition partition = partitions[index];
            if (index < importedPointerRaws.Count &&
                importedPointerRaws[index] is { } importedRaw)
            {
                payloads[index] = new NestedAliasPlan(
                    importedRaw,
                    null);
                continue;
            }
            if (partition.BorderCount == 0)
            {
                payloads[index] = new NestedAliasPlan(0, null);
                continue;
            }

            int aliasIndex = FindReferenceSlice(globalBorders, partition.Borders);
            if (aliasIndex >= 0 && borders is not null)
            {
                int pointer = AddressAt(
                    borders.Segment.Address,
                    checked(aliasIndex * CollisionBorder.SerializedSize)).ToPackedPointer();
                payloads[index] = new NestedAliasPlan(pointer, null);
                continue;
            }

            ArrayPlan<CollisionBorder> child = Array(
                partition.Borders,
                CollisionBorder.SerializedSize,
                4,
                plan,
                all,
                WriteBorder)
                ?? throw new InvalidDataException("A positive partition border count has no child payload.");
            payloads[index] = new NestedAliasPlan(-1, child.Segment);
        }

        return payloads;
    }

    private static int FindValueSlice<T>(
        IReadOnlyList<T> haystack,
        IReadOnlyList<T> needle)
    {
        if (needle.Count == 0 || needle.Count > haystack.Count)
            return -1;

        var comparer = EqualityComparer<T>.Default;
        int lastStart = haystack.Count - needle.Count;
        for (int start = 0; start <= lastStart; start++)
        {
            if (!comparer.Equals(haystack[start], needle[0]))
                continue;

            int index = 1;
            while (index < needle.Count &&
                   comparer.Equals(haystack[start + index], needle[index]))
            {
                index++;
            }

            if (index == needle.Count)
                return start;
        }

        return -1;
    }

    private static int FindReferenceSlice<T>(
        IReadOnlyList<T> haystack,
        IReadOnlyList<T> needle)
        where T : class
    {
        if (needle.Count == 0 || needle.Count > haystack.Count)
            return -1;

        int lastStart = haystack.Count - needle.Count;
        for (int start = 0; start <= lastStart; start++)
        {
            if (!ReferenceEquals(haystack[start], needle[0]))
                continue;

            int index = 1;
            while (index < needle.Count &&
                   ReferenceEquals(haystack[start + index], needle[index]))
            {
                index++;
            }

            if (index == needle.Count)
                return start;
        }

        return -1;
    }

    private static BrushAliasPlan[] PlanBrushAliases(
        IReadOnlyList<CBrush> brushes,
        ArrayPlan<CBrushSide>? brushSides,
        ArrayPlan<byte>? brushEdges)
    {
        var aliases = new BrushAliasPlan[brushes.Count];
        int sideOffset = 0;
        int edgeOffset = 0;
        for (int index = 0; index < brushes.Count; index++)
        {
            CBrush brush = brushes[index];
            int edgeCount = RequiredAdjacencyByteCount(brush);
            int sidesPointer = brush.NumSides == 0
                ? 0
                : AddressAt(
                    brushSides?.Segment.Address
                        ?? throw new InvalidDataException("A brush side alias has no global brush-side table."),
                    checked(sideOffset * CBrushSide.SerializedSize)).ToPackedPointer();
            int adjacencyPointer = edgeCount == 0
                ? 0
                : AddressAt(
                    brushEdges?.Segment.Address
                        ?? throw new InvalidDataException("A brush adjacency alias has no global brush-edge table."),
                    edgeOffset).ToPackedPointer();
            aliases[index] = new BrushAliasPlan(sidesPointer, adjacencyPointer);
            sideOffset = checked(sideOffset + brush.NumSides);
            edgeOffset = checked(edgeOffset + edgeCount);
        }

        return aliases;
    }

    private void ValidateBrushAliases(
        ClipMapAsset value,
        List<EmissionError> errors,
        int? rowIndex)
    {
        long sideOffset = 0;
        long edgeOffset = 0;
        for (int index = 0; index < value.Brushes.Count; index++)
        {
            CBrush brush = value.Brushes[index];
            string path = $"brushes[{index}]";
            int edgeCount = RequiredAdjacencyByteCount(brush);

            if (brush.Sides.Count == brush.NumSides)
            {
                if (sideOffset + brush.NumSides > value.BrushSides.Count)
                {
                    errors.Add(Error(
                        $"{path}.sides",
                        "Brush side aliases extend beyond the global brush-side table.",
                        rowIndex));
                }
                else
                {
                    for (int side = 0; side < brush.Sides.Count; side++)
                    {
                        if (ReferenceEquals(
                                brush.Sides[side],
                                value.BrushSides[checked((int)sideOffset + side)]))
                        {
                            continue;
                        }

                        errors.Add(Error(
                            $"{path}.sides[{side}]",
                            "Brush sides must alias the corresponding contiguous entries in the global brush-side table.",
                            rowIndex));
                        break;
                    }
                }
            }

            if (brush.BaseAdjacentSide.Count == edgeCount)
            {
                if (edgeOffset + edgeCount > value.BrushEdges.Count)
                {
                    errors.Add(Error(
                        $"{path}.baseAdjacentSide",
                        "Brush adjacency aliases extend beyond the global brush-edge table.",
                        rowIndex));
                }
                else
                {
                    for (int edge = 0; edge < edgeCount; edge++)
                    {
                        if (brush.BaseAdjacentSide[edge] ==
                            value.BrushEdges[checked((int)edgeOffset + edge)])
                        {
                            continue;
                        }

                        errors.Add(Error(
                            $"{path}.baseAdjacentSide[{edge}]",
                            "Brush adjacency bytes must equal the corresponding contiguous slice of the global brush-edge table.",
                            rowIndex));
                        break;
                    }
                }
            }

            sideOffset += brush.NumSides;
            edgeOffset += edgeCount;
        }
    }

    private static int RequiredAdjacencyByteCount(CBrush brush)
    {
        int byteCount = 0;
        int axialCount = Math.Min(
            brush.FirstAdjacentSideOffsets.Count,
            brush.EdgeCount.Count);
        for (int index = 0; index < axialCount; index++)
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

    private void Count(long expected, int actual, string path, List<EmissionError> errors, int? rowIndex) { if (expected < 0 || expected != actual) errors.Add(Error(path, $"Serialized count {expected} must equal detached list length {actual}.", rowIndex)); }
    private long CheckedMultiply(int left, int right, string path, List<EmissionError> errors, int? rowIndex) { try { return checked(left * right); } catch (OverflowException) { errors.Add(Error(path, "Serialized count overflows Int32.", rowIndex)); return -1; } }
    private long TriEdgeBytes(int triangleCount, List<EmissionError> errors, int? rowIndex) { if (triangleCount < 0) { errors.Add(Error("triCount", "Triangle count cannot be negative.", rowIndex)); return -1; } try { return checked(((triangleCount * 3 + 0x1f) >> 5) << 2); } catch (OverflowException) { errors.Add(Error("triEdgeIsWalkable", "Packed tri-edge size overflows Int32.", rowIndex)); return -1; } }
    private void Fixed(int actual, int expected, string path, List<EmissionError> errors, int? rowIndex) { if (actual != expected) errors.Add(Error(path, $"Requires exactly {expected} values.", rowIndex)); }
    private void String(string? value, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) errors.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex)); }
    private void Plane(CPlane value, string path, List<EmissionError> errors, int? rowIndex) { Fixed(value.Pad12.Count, 2, $"{path}.pad12", errors, rowIndex); }
    private void Side(CBrushSide side, string path, List<EmissionError> errors, int? rowIndex) { if (side.Plane is { } plane) Plane(plane, $"{path}.plane", errors, rowIndex); }
    private void Brush(CBrush value, string path, List<EmissionError> errors, int? rowIndex) { Count(value.NumSides, value.Sides.Count, $"{path}.sides", errors, rowIndex); Count(RequiredAdjacencyByteCount(value), value.BaseAdjacentSide.Count, $"{path}.baseAdjacentSide", errors, rowIndex); Fixed(value.AxialMaterialNum.Count, 6, $"{path}.axialMaterialNum", errors, rowIndex); Fixed(value.FirstAdjacentSideOffsets.Count, 6, $"{path}.firstAdjacentSideOffsets", errors, rowIndex); Fixed(value.EdgeCount.Count, 6, $"{path}.edgeCount", errors, rowIndex); for (int index = 0; index < value.Sides.Count; index++) Side(value.Sides[index], $"{path}.sides[{index}]", errors, rowIndex); }
    private static PlannedString? String(string? value, EmissionPlan plan, List<EmissionBlockSegment> all, List<EmissionBlockSegment> source) { int before = all.Count; PlannedString? result = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases); source.AddRange(all.Skip(before)); return result; }
    private static bool Zero(DynEntityPose value) => value.Pose.Quat.All(item => BitConverter.SingleToInt32Bits(item) == 0) && Zero(value.Pose.Origin) && BitConverter.SingleToInt32Bits(value.Radius) == 0;
    private static bool Zero(DynEntityClient value) => value.PhysObjId == 0 && value.Flags == 0 && value.LightingHandle == 0 && value.Health == 0;
    private static bool Zero(DynEntityColl value) => value.Sector == 0 && value.NextEntInSector == 0 && BitConverter.SingleToInt32Bits(value.LinkMins.a) == 0 && BitConverter.SingleToInt32Bits(value.LinkMins.b) == 0 && BitConverter.SingleToInt32Bits(value.LinkMaxs.a) == 0 && BitConverter.SingleToInt32Bits(value.LinkMaxs.b) == 0;
    private static bool Zero(Vec3 value) => BitConverter.SingleToInt32Bits(value.X) == 0 && BitConverter.SingleToInt32Bits(value.Y) == 0 && BitConverter.SingleToInt32Bits(value.Z) == 0;
    private static ExternalPlan?[][] PlanDynamicSources(
        IReadOnlyList<ClipMapDynEntityReferenceBuildData> references,
        ArrayPlan<DynEntityDef>? definitions,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (references.Count == 0)
            return [];
        if (definitions is null)
            throw new InvalidDataException(
                "Non-empty dynamic-entity references require a persistent definition table.");

        return references.Select((reference, index) =>
        {
            EmissionAddress rowAddress = AddressAt(
                definitions.Segment.Address,
                checked(index * DynEntityDef.SerializedSize));
            return new[]
            {
                Nested(
                    reference.XModel,
                    reference.XModelLink,
                    XAssetType.XModel,
                    0x120,
                    AddressAt(rowAddress, 0x20),
                    plan,
                    all),
                Nested(
                    reference.DestroyFx,
                    reference.DestroyFxLink,
                    XAssetType.Fx,
                    0x20,
                    AddressAt(rowAddress, 0x28),
                    plan,
                    all),
                Nested(
                    reference.PhysPreset,
                    reference.PhysPresetLink,
                    XAssetType.PhysPreset,
                    0x2c,
                    AddressAt(rowAddress, 0x2c),
                    plan,
                    all)
            };
        }).ToArray();
    }

    private static ExternalPlan? Nested(
        SymbolicXAssetReference? reference,
        NestedXAssetBuildLink? link,
        XAssetType type,
        int rootSize,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (link is not null)
        {
            NestedXAssetPlan nested = NestedXAssetEmission.Plan(
                link,
                plan,
                all,
                ownerCell,
                "ClipMap");
            return new ExternalPlan(
                nested.PointerRaw,
                nested.Source);
        }
        return External(
            reference,
            type,
            rootSize,
            ownerCell,
            plan,
            all);
    }

    private static ExternalPlan? External(
        SymbolicXAssetReference? reference,
        XAssetType type,
        int rootSize,
        EmissionAddress ownerCell,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        if (reference is null) return null;
        string aliasKey = AssetBodyEmitterHelpers.XAssetAliasKey(
            type,
            reference.OriginalSerializedName);
        if (plan.PersistentXAssetAliasCells.TryGetValue(
                aliasKey,
                out EmissionAddress existingCell))
        {
            return new ExternalPlan(existingCell.ToPackedPointer(), []);
        }
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(rootSize, 4);
        plan.Push(XFileBlockType.LARGE);
        int beforeName = all.Count;
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(reference.OriginalSerializedName, plan, all, plan.StringAliases);
        EmissionBlockSegment[] nameSegments = all.Skip(beforeName).ToArray();
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(Pointer(name)); writer.Reserve(rootSize - sizeof(int)); Exact(writer, rootSize, $"external {type}");
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(rootSegment);
        if (ownerCell.Block != XFileBlockType.TEMP)
            plan.PersistentXAssetAliasCells.TryAdd(aliasKey, ownerCell);
        return new ExternalPlan(-1, [rootSegment, .. nameSegments]);
    }
    private void Reference(SymbolicXAssetReference? value, XAssetType expected, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && (value.AssetType != expected || !value.IsExternalReference || !AssetBodyEmitterHelpers.IsLatin1CString(value.OriginalSerializedName))) errors.Add(Error(path, $"Requires a Latin-1 comma-prefixed external {expected} identity.", rowIndex)); }
    private void Nested(NestedXAssetBuildLink? link, SymbolicXAssetReference? reference, XAssetType expected, string path, List<EmissionError> errors, int? rowIndex)
    {
        errors.AddRange(NestedXAssetEmission.Validate(link, expected, path, rowIndex, AssetType));
        if (link is not null && link.Reference != reference)
            errors.Add(Error(path, "Nested link identity must equal the parallel symbolic reference.", rowIndex));
    }
    private static NestedXAssetBuildLink? LinkAt(IReadOnlyList<NestedXAssetBuildLink?> links, int index) =>
        links.Count == 0 || index >= links.Count ? null : links[index];
    private EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, AssetType);
    private sealed class ArrayPlan<T>
    {
        public ArrayPlan(EmissionBlockSegment segment) => Segment = segment;

        public EmissionBlockSegment Segment { get; set; }
    }
    private sealed record BrushAliasPlan(int SidesPointer, int BaseAdjacentSidePointer);
    private sealed record ChildPlan(EmissionAddress Address, EmissionBlockSegment? Source);
    private sealed record NestedAliasPlan(int Pointer, EmissionBlockSegment? Source);
    private sealed record ExternalPlan(
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> SourceSegments);
}
