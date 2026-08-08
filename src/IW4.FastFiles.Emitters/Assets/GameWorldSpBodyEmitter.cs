using IW4.Assets.Assets.GameMap;
using IW4.Assets.Math;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Serializer for the GameMapSp root. Its important invariant is that
/// PathData.baseNodes is a RUNTIME allocation: it receives a packed
/// destination address but owns no source bytes.  Path/tree aliases are
/// planned by object identity so an already materialized target is emitted as
/// a packed block address rather than duplicated inline payload.
/// </summary>
public sealed class GameWorldSpBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.GameMapSp;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IGameWorldSpBuildData data)
        {
            errors.Add(Error("body", "GameMapSp build data does not implement IGameWorldSpBuildData.", rowIndex));
            return errors;
        }
        String(data.Name, "name", errors, rowIndex);
        ValidatePath(data.Path, errors, rowIndex);
        ValidateVehicleTrack(data.VehicleTrack, errors, rowIndex);
        ValidateGlass(data.GlassData, errors, rowIndex);
        return errors;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        IGameWorldSpBuildData data = (IGameWorldSpBuildData)buildData;
        var all = new List<EmissionBlockSegment>();
        var source = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress rootAddress = plan.Allocate(GameWorldSpAsset.SerializedSize, 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = PlanString(data.Name, plan, all, source);
        PathPlan path = PlanPath(data.Path, plan, all);
        VehiclePlan vehicle = PlanVehicleTrack(data.VehicleTrack, plan, all);
        GlassPlan? glass = data.GlassData is null ? null : PlanGlass(data.GlassData, plan, all);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var root = new XSourceWriter();
        root.WriteInt32(Pointer(name));
        WritePathHeader(root, data.Path, path);
        root.WriteInt32(Pointer(vehicle.SegmentTable)); root.WriteInt32(data.VehicleTrack.SegmentCount);
        root.WriteInt32(glass is null ? 0 : -1);
        Exact(root, GameWorldSpAsset.SerializedSize, "GameWorldSp");
        var rootSegment = new EmissionBlockSegment(rootAddress, root.ToArray()); all.Add(rootSegment);
        var ordered = new List<EmissionBlockSegment> { rootSegment };
        ordered.AddRange(source);
        ordered.AddRange(path.Source);
        ordered.AddRange(vehicle.Source);
        if (glass is not null) ordered.AddRange(glass.Source);
        return new AssetBodyEmission(AssetType, rootAddress, all, ordered);
    }

    private static PathPlan PlanPath(PathData path, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        var source = new List<EmissionBlockSegment>();
        EmissionBlockSegment? nodes = null;
        var nodeChildren = new List<EmissionBlockSegment>();
        if (path.Nodes.Count != 0)
        {
            EmissionAddress address = plan.Allocate(checked(path.Nodes.Count * PathNode.SerializedSize), 4);
            var linkPlans = new EmissionBlockSegment?[path.Nodes.Count];
            for (int index = 0; index < path.Nodes.Count; index++)
                linkPlans[index] = Array(path.Nodes[index].Constant.Links, PathLink.SerializedSize, 4, plan, all, WritePathLink);
            var writer = new XSourceWriter();
            for (int index = 0; index < path.Nodes.Count; index++)
                WritePathNode(writer, path.Nodes[index], linkPlans[index], index);
            Exact(writer, checked(path.Nodes.Count * PathNode.SerializedSize), "PathNode array");
            nodes = new EmissionBlockSegment(address, writer.ToArray()); all.Add(nodes);
            foreach (EmissionBlockSegment? child in linkPlans) Add(nodeChildren, child);
        }

        // Native Load_Stream zero-fills this RUNTIME payload and advances its
        // destination cursor; no bytes may enter the compact source stream.
        bool baseNodes = RuntimeArray(path.BaseNodes.Count, PathBaseNode.SerializedSize, 16, plan);
        EmissionBlockSegment? chainForNode = Array(path.ChainNodeForNode, sizeof(ushort), 2, plan, all, static (writer, value) => writer.WriteUInt16(value));
        EmissionBlockSegment? nodeForChain = Array(path.NodeForChainNode, sizeof(ushort), 2, plan, all, static (writer, value) => writer.WriteUInt16(value));
        EmissionBlockSegment? pathVis = Array(path.PathVis, sizeof(byte), 1, plan, all, static (writer, value) => writer.WriteByte(value));

        EmissionBlockSegment? treeTable = null;
        var treeChildren = new List<EmissionBlockSegment>();
        if (path.NodeTree.Count != 0)
        {
            EmissionAddress tableAddress = plan.Allocate(checked(path.NodeTree.Count * PathNodeTree.SerializedSize), 4);
            var treePlans = new Dictionary<PathNodeTree, TreePlan>(ReferenceEqualityComparer.Instance);
            for (int index = 0; index < path.NodeTree.Count; index++)
                treePlans.Add(path.NodeTree[index], new TreePlan(new EmissionAddress(tableAddress.Block, checked(tableAddress.Offset + index * PathNodeTree.SerializedSize)), isBaseTableMember: true));
            var writer = new XSourceWriter();
            foreach (PathNodeTree value in path.NodeTree)
            {
                TreePlan body = PlanTreeBody(value, plan, all, treePlans, out _);
                writer.WriteBytes(body.Root!.Bytes.Span);
                treeChildren.AddRange(body.Source!.Skip(1));
            }
            treeTable = new EmissionBlockSegment(tableAddress, writer.ToArray()); all.Add(treeTable);
        }

        Add(source, nodes); source.AddRange(nodeChildren);
        Add(source, chainForNode); Add(source, nodeForChain); Add(source, pathVis);
        Add(source, treeTable); source.AddRange(treeChildren);
        return new PathPlan(nodes, baseNodes, chainForNode, nodeForChain, pathVis, treeTable, source);
    }

    private static TreePlan PlanTreeBody(PathNodeTree value, EmissionPlan plan, List<EmissionBlockSegment> all, Dictionary<PathNodeTree, TreePlan> known, out bool isInlinePayload)
    {
        if (!known.TryGetValue(value, out TreePlan? existing))
        {
            EmissionAddress address = plan.Allocate(PathNodeTree.SerializedSize, 4);
            existing = new TreePlan(address, isBaseTableMember: false);
            known.Add(value, existing);
        }
        if (existing.Planning || existing.Planned)
        {
            isInlinePayload = false;
            return existing;
        }
        isInlinePayload = !existing.IsBaseTableMember;
        existing.Planning = true;

        EmissionBlockSegment? leafIndices = null;
        TreePlan? child0 = null;
        TreePlan? child1 = null;
        bool child0Inline = false;
        bool child1Inline = false;
        if (value.Axis < 0)
            leafIndices = Array(value.Nodes, sizeof(ushort), 2, plan, all, static (writer, index) => writer.WriteUInt16(index));
        else
        {
            PlanTreeChild(value.Child0, out child0, out child0Inline);
            PlanTreeChild(value.Child1, out child1, out child1Inline);
        }

        var writer = new XSourceWriter();
        writer.WriteInt32(value.Axis); writer.WriteSingle(value.Distance);
        if (value.Axis < 0)
        {
            writer.WriteInt32(value.NodeCount); writer.WriteInt32(Pointer(leafIndices));
        }
        else
        {
            writer.WriteInt32(TreePointer(child0, child0Inline)); writer.WriteInt32(TreePointer(child1, child1Inline));
        }
        Exact(writer, PathNodeTree.SerializedSize, "PathNodeTree");
        var root = new EmissionBlockSegment(existing.Address, writer.ToArray());
        var source = new List<EmissionBlockSegment> { root };
        Add(source, leafIndices);
        if (child0Inline && child0?.Source is not null) source.AddRange(child0.Source);
        if (child1Inline && child1?.Source is not null) source.AddRange(child1.Source);
        existing.Root = root; existing.Source = source; existing.Planning = false; existing.Planned = true;
        if (!existing.IsBaseTableMember) all.Add(root);
        return existing;

        void PlanTreeChild(
            PathNodeTree? child,
            out TreePlan? childPlan,
            out bool childInline)
        {
            if (child is null)
            {
                childPlan = null;
                childInline = false;
                return;
            }

            // A pointer to another member of the already allocated root table
            // is packed and consumes no inline child here. Defer planning that
            // member's own children until its table slot is traversed.
            if (known.TryGetValue(child, out TreePlan? tableMember) &&
                tableMember.IsBaseTableMember)
            {
                childPlan = tableMember;
                childInline = false;
                return;
            }

            childPlan = PlanTreeBody(child, plan, all, known, out childInline);
        }
    }

    private static int TreePointer(TreePlan? planValue, bool isInlinePayload)
    {
        if (planValue is null) return 0;
        return isInlinePayload ? -1 : planValue.Address.ToPackedPointer();
    }

    private static VehiclePlan PlanVehicleTrack(VehicleTrack value, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (value.Segments.Count == 0) return new VehiclePlan(null, []);
        EmissionAddress tableAddress = plan.Allocate(checked(value.Segments.Count * VehicleTrackSegment.SerializedSize), 4);
        var known = new Dictionary<VehicleTrackSegment, SegmentPlan>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < value.Segments.Count; index++)
            known.Add(value.Segments[index], new SegmentPlan(new EmissionAddress(tableAddress.Block, checked(tableAddress.Offset + index * VehicleTrackSegment.SerializedSize)), isBaseTableMember: true));
        var tableWriter = new XSourceWriter();
        var children = new List<EmissionBlockSegment>();
        foreach (VehicleTrackSegment segment in value.Segments)
        {
            SegmentPlan body = PlanVehicleSegment(segment, plan, all, known, out _);
            tableWriter.WriteBytes(body.Root!.Bytes.Span);
            children.AddRange(body.Source!.Skip(1));
        }
        var table = new EmissionBlockSegment(tableAddress, tableWriter.ToArray()); all.Add(table);
        return new VehiclePlan(table, [table, .. children]);
    }

    private static SegmentPlan PlanVehicleSegment(VehicleTrackSegment value, EmissionPlan plan, List<EmissionBlockSegment> all, Dictionary<VehicleTrackSegment, SegmentPlan> known, out bool isInlinePayload)
    {
        if (!known.TryGetValue(value, out SegmentPlan? existing))
        {
            existing = new SegmentPlan(plan.Allocate(VehicleTrackSegment.SerializedSize, 4), isBaseTableMember: false);
            known.Add(value, existing);
        }
        if (existing.Planning || existing.Planned) { isInlinePayload = false; return existing; }
        isInlinePayload = !existing.IsBaseTableMember;
        existing.Planning = true;

        var source = new List<EmissionBlockSegment>();
        PlannedString? name = PlanString(value.Name, plan, all, source);
        EmissionBlockSegment? sectors = null;
        var sectorChildren = new List<EmissionBlockSegment>();
        if (value.Sectors.Count != 0)
        {
            EmissionAddress sectorsAddress = plan.Allocate(checked(value.Sectors.Count * VehicleTrackSector.SerializedSize), 4);
            var obstacles = new EmissionBlockSegment?[value.Sectors.Count];
            for (int index = 0; index < value.Sectors.Count; index++)
                obstacles[index] = Array(value.Sectors[index].Obstacles, VehicleTrackObstacle.SerializedSize, 4, plan, all, WriteVehicleObstacle);
            var sectorWriter = new XSourceWriter();
            for (int index = 0; index < value.Sectors.Count; index++)
                WriteVehicleSector(sectorWriter, value.Sectors[index], obstacles[index]);
            sectors = new EmissionBlockSegment(sectorsAddress, sectorWriter.ToArray()); all.Add(sectors);
            foreach (EmissionBlockSegment? obstacle in obstacles) Add(sectorChildren, obstacle);
        }

        PointerTablePlan next = PlanVehicleBranchPointers(value.NextBranches, plan, all, known);
        PointerTablePlan previous = PlanVehicleBranchPointers(value.PreviousBranches, plan, all, known);
        var writer = new XSourceWriter();
        writer.WriteInt32(Pointer(name)); writer.WriteInt32(Pointer(sectors)); writer.WriteInt32(value.SectorCount); writer.WriteInt32(Pointer(next.Table)); writer.WriteInt32(value.NextBranchCount); writer.WriteInt32(Pointer(previous.Table)); writer.WriteInt32(value.PreviousBranchCount);
        WriteFloats(writer, value.EndEdgeDirection, 2, "VehicleTrackSegment.endEdgeDirection"); writer.WriteSingle(value.EndEdgeDistance); writer.WriteSingle(value.TotalLength);
        Exact(writer, VehicleTrackSegment.SerializedSize, "VehicleTrackSegment");
        var root = new EmissionBlockSegment(existing.Address, writer.ToArray());
        var ordered = new List<EmissionBlockSegment> { root };
        ordered.AddRange(source); Add(ordered, sectors); ordered.AddRange(sectorChildren);
        Add(ordered, next.Table); ordered.AddRange(next.Children);
        Add(ordered, previous.Table); ordered.AddRange(previous.Children);
        existing.Root = root; existing.Source = ordered; existing.Planning = false; existing.Planned = true;
        if (!existing.IsBaseTableMember) all.Add(root);
        return existing;
    }

    private static PointerTablePlan PlanVehicleBranchPointers(IReadOnlyList<VehicleTrackSegment?> values, EmissionPlan plan, List<EmissionBlockSegment> all, Dictionary<VehicleTrackSegment, SegmentPlan> known)
    {
        if (values.Count == 0) return new PointerTablePlan(null, []);
        EmissionAddress address = plan.Allocate(checked(values.Count * sizeof(int)), 4);
        var writer = new XSourceWriter(); var children = new List<EmissionBlockSegment>();
        foreach (VehicleTrackSegment? value in values)
        {
            if (value is null) { writer.WriteInt32(0); continue; }
            SegmentPlan target;
            bool inline;
            if (known.TryGetValue(value, out SegmentPlan? tableMember) &&
                tableMember.IsBaseTableMember)
            {
                target = tableMember;
                inline = false;
            }
            else
            {
                target = PlanVehicleSegment(value, plan, all, known, out inline);
            }
            writer.WriteInt32(inline ? -1 : target.Address.ToPackedPointer());
            if (inline && target.Source is not null) children.AddRange(target.Source);
        }
        var table = new EmissionBlockSegment(address, writer.ToArray()); all.Add(table);
        return new PointerTablePlan(table, children);
    }

    private static GlassPlan PlanGlass(GGlassData value, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        EmissionAddress rootAddress = plan.Allocate(GGlassData.SerializedSize, 4);
        EmissionBlockSegment? pieces = Array(value.GlassPieces, GGlassPiece.SerializedSize, 4, plan, all, static (writer, piece) => { writer.WriteUInt16(piece.DamageTaken); writer.WriteUInt16(piece.CollapseTime); writer.WriteInt32(piece.LastStateChangeTime); writer.WriteUInt16(piece.PackedImpactDir); writer.WriteUInt16(piece.PackedImpactPos); });
        EmissionAddress? namesAddress = value.GlassNames.Count == 0
            ? null
            : plan.Allocate(checked(value.GlassNames.Count * GGlassName.SerializedSize), 4);
        EmissionBlockSegment? names = null;
        var nameStrings = new PlannedString?[value.GlassNames.Count];
        var indices = new EmissionBlockSegment?[value.GlassNames.Count];
        var children = new List<EmissionBlockSegment>();
        for (int index = 0; index < value.GlassNames.Count; index++)
        {
            int before = all.Count;
            nameStrings[index] = AssetBodyEmitterHelpers.PlanString(value.GlassNames[index].NameStr, plan, all, plan.StringAliases);
            children.AddRange(all.Skip(before));
            indices[index] = Array(value.GlassNames[index].PieceIndices, sizeof(ushort), 2, plan, all, static (writer, pieceIndex) => writer.WriteUInt16(pieceIndex));
            Add(children, indices[index]);
        }
        if (namesAddress is EmissionAddress address)
        {
            var writer = new XSourceWriter();
            for (int index = 0; index < value.GlassNames.Count; index++)
            {
                GGlassName name = value.GlassNames[index];
                writer.WriteInt32(Pointer(nameStrings[index])); writer.WriteUInt16(name.Name); writer.WriteUInt16(checked((ushort)name.PieceIndices.Count)); writer.WriteInt32(Pointer(indices[index]));
            }
            names = new EmissionBlockSegment(address, writer.ToArray()); all.Add(names);
        }
        var root = new XSourceWriter();
        root.WriteInt32(Pointer(pieces)); root.WriteInt32(value.PieceCount); root.WriteUInt16(value.DamageToWeaken); root.WriteUInt16(value.DamageToDestroy); root.WriteInt32(value.GlassNameCount); root.WriteInt32(Pointer(names)); root.WriteBytes(value.Pad14To7F.ToArray());
        Exact(root, GGlassData.SerializedSize, "G_GlassData");
        var rootSegment = new EmissionBlockSegment(rootAddress, root.ToArray()); all.Add(rootSegment);
        var source = new List<EmissionBlockSegment> { rootSegment }; Add(source, pieces); Add(source, names); source.AddRange(children);
        return new GlassPlan(source);
    }

    private static void WritePathHeader(XSourceWriter writer, PathData path, PathPlan plan)
    {
        writer.WriteUInt32(path.NodeCount); writer.WriteInt32(Pointer(plan.Nodes)); writer.WriteInt32(Pointer(plan.BaseNodes)); writer.WriteUInt32(path.ChainNodeCount); writer.WriteInt32(Pointer(plan.ChainForNode)); writer.WriteInt32(Pointer(plan.NodeForChain)); writer.WriteInt32(path.VisBytes); writer.WriteInt32(Pointer(plan.PathVis)); writer.WriteInt32(path.NodeTreeCount); writer.WriteInt32(Pointer(plan.TreeTable));
    }
    private static void WritePathNode(
        XSourceWriter writer,
        PathNode value,
        EmissionBlockSegment? links,
        int nodeIndex)
    {
        int startPosition = writer.Position;
        PathNodeConstant constant = value.Constant; PathNodeDynamic dynamic = value.Dynamic; PathNodeTransient transient = value.Transient;
        string prefix = $"path.nodes[{nodeIndex}].constant";
        writer.WriteInt32(constant.NodeType); writer.WriteUInt16(constant.SpawnFlags); Script(writer, constant.TargetName, $"{prefix}.targetName"); Script(writer, constant.ScriptLinkName, $"{prefix}.scriptLinkName"); Script(writer, constant.ScriptNoteworthy, $"{prefix}.scriptNoteworthy"); Script(writer, constant.Target, $"{prefix}.target"); Script(writer, constant.AnimScript, $"{prefix}.animScript"); writer.WriteInt32(constant.AnimScriptFunc); WriteVec3(writer, constant.Origin); writer.WriteSingle(constant.Angle); writer.WriteSingle(constant.ForwardX); writer.WriteSingle(constant.ForwardY); writer.WriteSingle(constant.Radius); writer.WriteSingle(constant.MinUseDistSq); writer.WriteUInt16(unchecked((ushort)constant.OverlapNode0)); writer.WriteUInt16(unchecked((ushort)constant.OverlapNode1)); writer.WriteUInt16(constant.TotalLinkCount); writer.WriteUInt16(constant.Pad3A); writer.WriteInt32(Pointer(links));
        writer.WriteUInt16(dynamic.OwnerHandle); writer.WriteUInt16(dynamic.Pad42); writer.WriteInt32(dynamic.FreeTime); WriteInts(writer, dynamic.ValidTimes, 3, "PathNode.dynamic.validTimes"); WriteInts(writer, dynamic.DangerousNodeTimes, 3, "PathNode.dynamic.dangerousNodeTimes"); writer.WriteInt32(dynamic.InPlayerLosTime); writer.WriteUInt16(unchecked((ushort)dynamic.LinkCount)); writer.WriteUInt16(unchecked((ushort)dynamic.OverlapCount)); writer.WriteUInt16(unchecked((ushort)dynamic.TurretEntityNumber)); writer.WriteByte(dynamic.UserCount); writer.WriteByte(dynamic.HasBadPlaceLink ? (byte)1 : (byte)0);
        writer.WriteInt32(transient.SearchFrame); writer.WriteUInt32(transient.NextOpenRuntimePointer); writer.WriteUInt32(transient.PreviousOpenRuntimePointer); writer.WriteUInt32(transient.ParentRuntimePointer); writer.WriteSingle(transient.Cost); writer.WriteSingle(transient.Heuristic); writer.WriteUInt32(transient.NodeCostOrLinkIndexBits);
        Exact(writer.Position - startPosition, PathNode.SerializedSize, "PathNode");
    }
    private static void WritePathLink(XSourceWriter writer, PathLink value) { writer.WriteSingle(value.Distance); writer.WriteUInt16(value.NodeNumber); writer.WriteByte(value.DisconnectCount); writer.WriteByte(value.NegotiationLink); writer.WriteByte(value.BadPlaceCount0); writer.WriteByte(value.BadPlaceCount1); writer.WriteByte(value.BadPlaceCount2); writer.WriteByte(value.BadPlaceCount3); }
    private static void WriteVehicleSector(XSourceWriter writer, VehicleTrackSector value, EmissionBlockSegment? obstacles)
    {
        WriteFloats(writer, value.StartEdgeDirection, 2, "VehicleTrackSector.startEdgeDirection"); writer.WriteSingle(value.StartEdgeDistance); WriteFloats(writer, value.LeftEdgeDirection, 2, "VehicleTrackSector.leftEdgeDirection"); writer.WriteSingle(value.LeftEdgeDistance); WriteFloats(writer, value.RightEdgeDirection, 2, "VehicleTrackSector.rightEdgeDirection"); writer.WriteSingle(value.RightEdgeDistance); writer.WriteSingle(value.SectorLength); writer.WriteSingle(value.SectorWidth); writer.WriteSingle(value.TotalPriorLength); writer.WriteSingle(value.TotalFollowingLength); writer.WriteInt32(Pointer(obstacles)); writer.WriteInt32(value.ObstacleCount);
    }
    private static void WriteVehicleObstacle(XSourceWriter writer, VehicleTrackObstacle value) { WriteFloats(writer, value.Origin, 2, "VehicleTrackObstacle.origin"); writer.WriteSingle(value.Radius); }
    private static void WriteVec3(XSourceWriter writer, Vec3 value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static void Script(XSourceWriter writer, ScriptStringReference value, string path) =>
        writer.WriteUInt16(ScriptStringEmissionScope.Resolve(value, path));
    private static void WriteInts(XSourceWriter writer, IReadOnlyList<int> values, int count, string path) { if (values.Count != count) throw new InvalidDataException($"{path} requires {count} values."); foreach (int value in values) writer.WriteInt32(value); }
    private static void WriteFloats(XSourceWriter writer, IReadOnlyList<float> values, int count, string path) { if (values.Count != count) throw new InvalidDataException($"{path} requires {count} values."); foreach (float value in values) writer.WriteSingle(value); }
    private static PlannedString? PlanString(string? value, EmissionPlan plan, List<EmissionBlockSegment> all, List<EmissionBlockSegment> source) { int before = all.Count; PlannedString? result = AssetBodyEmitterHelpers.PlanString(value, plan, all, plan.StringAliases); source.AddRange(all.Skip(before)); return result; }
    private static EmissionBlockSegment? Array<T>(IReadOnlyList<T> values, int stride, int alignment, EmissionPlan plan, List<EmissionBlockSegment> all, Action<XSourceWriter, T> write) { if (values.Count == 0) return null; EmissionAddress address = plan.Allocate(checked(values.Count * stride), alignment); var writer = new XSourceWriter(); foreach (T value in values) write(writer, value); Exact(writer, checked(values.Count * stride), "GameMapSp array"); var result = new EmissionBlockSegment(address, writer.ToArray()); all.Add(result); return result; }
    private static bool RuntimeArray(int count, int stride, int alignment, EmissionPlan plan) { if (count == 0) return false; plan.Push(XFileBlockType.RUNTIME); try { plan.Allocate(checked(count * stride), alignment); return true; } finally { plan.Pop(XFileBlockType.RUNTIME); } }
    private static int Pointer(PlannedString? value) => AssetBodyEmitterHelpers.SourcePointer(value);
    private static int Pointer(EmissionBlockSegment? value) => value is null ? 0 : -1;
    private static int Pointer(bool present) => present ? -1 : 0;
    private static void Add(List<EmissionBlockSegment> values, EmissionBlockSegment? value) { if (value is not null) values.Add(value); }
    private static void Exact(int actual, int expected, string name) { if (actual != expected) throw new InvalidDataException($"{name} emission produced 0x{actual:X} bytes instead of 0x{expected:X}."); }
    private static void Exact(XSourceWriter writer, int expected, string name) { if (writer.Position != expected) throw new InvalidDataException($"{name} emission produced 0x{writer.Position:X} bytes instead of 0x{expected:X}."); }
    private static void String(string? value, string path, List<EmissionError> errors, int? rowIndex) { if (value is not null && !AssetBodyEmitterHelpers.IsLatin1CString(value)) errors.Add(Error(path, "XString must be a Latin-1 C string.", rowIndex)); }
    private static void ValidatePath(PathData path, List<EmissionError> errors, int? rowIndex)
    {
        if (path.NodeCount > int.MaxValue || path.NodeCount != (uint)path.Nodes.Count || path.NodeCount != (uint)path.BaseNodes.Count) errors.Add(Error("path.nodeCount", "Path node/base-node count must equal the serialized node count.", rowIndex));
        if (path.ChainNodeCount > int.MaxValue || path.ChainNodeCount != (uint)path.ChainNodeForNode.Count || path.ChainNodeCount != (uint)path.NodeForChainNode.Count) errors.Add(Error("path.chainNodes", "Path node-index maps must each match the serialized chain-node count.", rowIndex));
        if (path.VisBytes < 0 || path.VisBytes != path.PathVis.Count) errors.Add(Error("path.visBytes", "Path visibility byte count does not match its array.", rowIndex));
        if (path.NodeTreeCount < 0 || path.NodeTreeCount != path.NodeTree.Count) errors.Add(Error("path.nodeTreeCount", "Path tree count does not match its array.", rowIndex));
        if (path.BaseNodes.Any(value => value.Origin.X != 0 || value.Origin.Y != 0 || value.Origin.Z != 0 || value.Type != 0)) errors.Add(Error("path.baseNodes", "RUNTIME base-node storage must be zero-initialized.", rowIndex));
        for (int index = 0; index < path.Nodes.Count; index++)
        {
            PathNode node = path.Nodes[index];
            if (node.Constant.TotalLinkCount != node.Constant.Links.Count) errors.Add(Error($"path.nodes[{index}].links", "Link count does not match the link array.", rowIndex));
            if (node.Dynamic.ValidTimes.Count != 3 || node.Dynamic.DangerousNodeTimes.Count != 3) errors.Add(Error($"path.nodes[{index}].dynamic", "Dynamic path-node tables require three values each.", rowIndex));
        }
        var baseTrees = new HashSet<PathNodeTree>(ReferenceEqualityComparer.Instance);
        foreach (PathNodeTree tree in path.NodeTree)
            if (!baseTrees.Add(tree)) errors.Add(Error("path.nodeTree", "The serialized tree table requires distinct root identities.", rowIndex));
        var visitedTrees = new HashSet<PathNodeTree>(ReferenceEqualityComparer.Instance);
        foreach (PathNodeTree tree in path.NodeTree) ValidateTree(tree, errors, rowIndex, visitedTrees);
    }
    private static void ValidateGlass(GGlassData? value, List<EmissionError> errors, int? rowIndex)
    {
        if (value is null) return;
        if (value.PieceCount != value.GlassPieces.Count || value.GlassNameCount != value.GlassNames.Count) errors.Add(Error("glassData", "Glass counts do not match detached arrays.", rowIndex));
        if (value.Pad14To7F.Count != 0x6c) errors.Add(Error("glassData.pad14To7F", "G_GlassData requires exactly 0x6c pad bytes.", rowIndex));
        for (int index = 0; index < value.GlassNames.Count; index++)
        {
            GGlassName name = value.GlassNames[index];
            String(name.NameStr, $"glassData.names[{index}].name", errors, rowIndex);
            if (name.PieceIndices.Count > ushort.MaxValue || name.PieceIndices.Any(piece => piece >= value.GlassPieces.Count)) errors.Add(Error($"glassData.names[{index}].pieceIndices", "Glass-name piece indices are outside the piece array or serialized ushort range.", rowIndex));
        }
    }
    private static void ValidateTree(PathNodeTree tree, List<EmissionError> errors, int? rowIndex, HashSet<PathNodeTree> visited)
    {
        if (!visited.Add(tree)) return;
        if (tree.Axis < 0) { if (tree.NodeCount < 0 || tree.NodeCount != tree.Nodes.Count) errors.Add(Error("path.nodeTree.nodes", "Leaf tree count does not match its node-index list.", rowIndex)); }
        else { if (tree.Child0 is null || tree.Child1 is null) errors.Add(Error("path.nodeTree.children", "Branch tree nodes require both children in the detached graph.", rowIndex)); else { ValidateTree(tree.Child0, errors, rowIndex, visited); ValidateTree(tree.Child1, errors, rowIndex, visited); } }
    }
    private static void ValidateVehicleTrack(VehicleTrack value, List<EmissionError> errors, int? rowIndex)
    {
        if (value.SegmentCount != value.Segments.Count) errors.Add(Error("vehicleTrack.segmentCount", "Vehicle-track segment count does not match its detached list.", rowIndex));
        var visited = new HashSet<VehicleTrackSegment>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < value.Segments.Count; index++) ValidateVehicleSegment(value.Segments[index], $"vehicleTrack.segments[{index}]", errors, rowIndex, visited);
    }
    private static void ValidateVehicleSegment(VehicleTrackSegment value, string path, List<EmissionError> errors, int? rowIndex, HashSet<VehicleTrackSegment> visited)
    {
        if (!visited.Add(value)) return;
        String(value.Name, $"{path}.name", errors, rowIndex);
        if (value.SectorCount != value.Sectors.Count) errors.Add(Error($"{path}.sectorCount", "Sector count does not match the detached sector list.", rowIndex));
        if (value.NextBranchCount != value.NextBranches.Count || value.PreviousBranchCount != value.PreviousBranches.Count) errors.Add(Error(path, "Branch counts do not match detached branch lists.", rowIndex));
        if (value.NextBranchPointers.Count != 0 && value.NextBranchPointers.Count != value.NextBranches.Count) errors.Add(Error($"{path}.nextBranchPointers", "Captured next-branch pointer cells do not match the detached topology.", rowIndex));
        if (value.PreviousBranchPointers.Count != 0 && value.PreviousBranchPointers.Count != value.PreviousBranches.Count) errors.Add(Error($"{path}.previousBranchPointers", "Captured previous-branch pointer cells do not match the detached topology.", rowIndex));
        if (value.NextBranchPointers.Zip(value.NextBranches).Any(pair => pair.First.Raw != 0 && pair.Second is null)) errors.Add(Error($"{path}.nextBranches", "A non-null next-branch pointer has no detached target identity.", rowIndex));
        if (value.PreviousBranchPointers.Zip(value.PreviousBranches).Any(pair => pair.First.Raw != 0 && pair.Second is null)) errors.Add(Error($"{path}.previousBranches", "A non-null previous-branch pointer has no detached target identity.", rowIndex));
        ValidateFiniteList(value.EndEdgeDirection, 2, $"{path}.endEdgeDirection", errors, rowIndex); Finite(value.EndEdgeDistance, $"{path}.endEdgeDistance", errors, rowIndex); Finite(value.TotalLength, $"{path}.totalLength", errors, rowIndex);
        for (int index = 0; index < value.Sectors.Count; index++)
        {
            VehicleTrackSector sector = value.Sectors[index]; string sectorPath = $"{path}.sectors[{index}]";
            if (sector.ObstacleCount != sector.Obstacles.Count) errors.Add(Error($"{sectorPath}.obstacleCount", "Obstacle count does not match the detached obstacle list.", rowIndex));
            ValidateFiniteList(sector.StartEdgeDirection, 2, $"{sectorPath}.startEdgeDirection", errors, rowIndex); ValidateFiniteList(sector.LeftEdgeDirection, 2, $"{sectorPath}.leftEdgeDirection", errors, rowIndex); ValidateFiniteList(sector.RightEdgeDirection, 2, $"{sectorPath}.rightEdgeDirection", errors, rowIndex);
            foreach (float number in new[] { sector.StartEdgeDistance, sector.LeftEdgeDistance, sector.RightEdgeDistance, sector.SectorLength, sector.SectorWidth, sector.TotalPriorLength, sector.TotalFollowingLength }) Finite(number, sectorPath, errors, rowIndex);
            foreach (VehicleTrackObstacle obstacle in sector.Obstacles) { ValidateFiniteList(obstacle.Origin, 2, $"{sectorPath}.obstacle.origin", errors, rowIndex); Finite(obstacle.Radius, $"{sectorPath}.obstacle.radius", errors, rowIndex); }
        }
        for (int index = 0; index < value.NextBranches.Count; index++) if (value.NextBranches[index] is { } child) ValidateVehicleSegment(child, $"{path}.nextBranches[{index}]", errors, rowIndex, visited);
        for (int index = 0; index < value.PreviousBranches.Count; index++) if (value.PreviousBranches[index] is { } child) ValidateVehicleSegment(child, $"{path}.previousBranches[{index}]", errors, rowIndex, visited);
    }
    private static void ValidateFiniteList(IReadOnlyList<float> values, int expected, string path, List<EmissionError> errors, int? rowIndex) { if (values.Count != expected) { errors.Add(Error(path, $"Requires exactly {expected} values.", rowIndex)); return; } foreach (float value in values) Finite(value, path, errors, rowIndex); }
    private static void Finite(float value, string path, List<EmissionError> errors, int? rowIndex) { if (!float.IsFinite(value)) errors.Add(Error(path, "Value must be finite.", rowIndex)); }
    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.GameMapSp);
    private sealed record PathPlan(EmissionBlockSegment? Nodes, bool BaseNodes, EmissionBlockSegment? ChainForNode, EmissionBlockSegment? NodeForChain, EmissionBlockSegment? PathVis, EmissionBlockSegment? TreeTable, IReadOnlyList<EmissionBlockSegment> Source);
    private sealed class TreePlan
    {
        public TreePlan(EmissionAddress address, bool isBaseTableMember) { Address = address; IsBaseTableMember = isBaseTableMember; }
        public EmissionAddress Address { get; }
        public bool IsBaseTableMember { get; }
        public bool Planning { get; set; }
        public bool Planned { get; set; }
        public EmissionBlockSegment? Root { get; set; }
        public IReadOnlyList<EmissionBlockSegment>? Source { get; set; }
    }
    private sealed class SegmentPlan
    {
        public SegmentPlan(EmissionAddress address, bool isBaseTableMember) { Address = address; IsBaseTableMember = isBaseTableMember; }
        public EmissionAddress Address { get; }
        public bool IsBaseTableMember { get; }
        public bool Planning { get; set; }
        public bool Planned { get; set; }
        public EmissionBlockSegment? Root { get; set; }
        public IReadOnlyList<EmissionBlockSegment>? Source { get; set; }
    }
    private sealed record PointerTablePlan(EmissionBlockSegment? Table, IReadOnlyList<EmissionBlockSegment> Children);
    private sealed record VehiclePlan(EmissionBlockSegment? SegmentTable, IReadOnlyList<EmissionBlockSegment> Source);
    private sealed record GlassPlan(IReadOnlyList<EmissionBlockSegment> Source);
}
