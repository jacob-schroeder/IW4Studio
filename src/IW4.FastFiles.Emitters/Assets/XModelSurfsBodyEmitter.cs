using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Emits XModelSurfs headers, surface payloads, collision trees, and
/// the LARGE/PHYSICAL vertex-index stream transitions used by the loader.</summary>
public sealed class XModelSurfsBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.XModelSurfs;
    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IXModelSurfsBuildData data) { errors.Add(Error("body", "XModelSurfs build data does not implement IXModelSurfsBuildData.", rowIndex)); return errors; }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) errors.Add(Error("name", "Name must be a Latin-1 C string.", rowIndex));
        if (data.PartBits.Count != 6) errors.Add(Error("partBits", "XModelSurfs requires exactly six part-bit words.", rowIndex));
        if (data.Surfaces.Count > ushort.MaxValue) errors.Add(Error("surfaces", "Surface count exceeds UInt16.", rowIndex));
        if (data.ImportedSurfacesPackedRaw is { } packedRaw &&
            IW4.FastFiles.Pointers.XPointerCodec.GetType(packedRaw) !=
            IW4.FastFiles.Pointers.PointerType.Offset)
        {
            errors.Add(Error(
                "surfacesPointer",
                "Imported XSurface-table raw value is not a packed offset pointer.",
                rowIndex));
        }
        for (int index = 0; index < data.Surfaces.Count; index++) CheckSurface(data.Surfaces[index], index, errors, rowIndex);
        return errors;
    }
    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null) { ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); return PlanNested((IXModelSurfsBuildData)buildData, plan); }

    internal static AssetBodyEmission PlanNested(IXModelSurfsBuildData data, EmissionPlan plan)
    {
        var all = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x24, 4); plan.Push(XFileBlockType.LARGE);
        int beforeName = all.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, all, plan.StringAliases); int afterName = all.Count;
        EmissionBlockSegment? surfaces = null; var surfaceSources = new List<EmissionBlockSegment>();
        int? importedSurfacesRaw =
            plan.PreserveImportedXAssetPointerValues
                ? data.ImportedSurfacesPackedRaw
                : null;
        if (importedSurfacesRaw is null && data.Surfaces.Count != 0)
        {
            EmissionAddress address = plan.Allocate(checked(data.Surfaces.Count * 0x54), 4);
            SurfacePlan[] planned = data.Surfaces
                .Select(surface => PlanSurface(surface, plan, all))
                .ToArray();
            surfaces = new EmissionBlockSegment(
                address,
                BuildSurfaceTable(data.Surfaces, planned));
            all.Add(surfaces);
            foreach (SurfacePlan surface in planned)
                surfaceSources.AddRange(surface.Source);
        }
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var writer = new XSourceWriter(); writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); writer.WriteInt32(importedSurfacesRaw ?? Pointer(surfaces)); writer.WriteUInt16(data.NumSurfs); writer.WriteUInt16(data.Pad0A); foreach (uint value in data.PartBits) writer.WriteUInt32(value); var rootSegment = new EmissionBlockSegment(root, writer.ToArray()); all.Add(rootSegment); source.Add(rootSegment); source.AddRange(all.Skip(beforeName).Take(afterName - beforeName)); Add(source, surfaces); source.AddRange(surfaceSources);
        return new AssetBodyEmission(XAssetType.XModelSurfs, root, all, source);
    }
    private static byte[] BuildSurfaceTable(
        IReadOnlyList<XModelSurfaceBuildData> values,
        IReadOnlyList<SurfacePlan> plans)
    {
        if (values.Count != plans.Count)
            throw new InvalidDataException("XSurface values and plans have different counts.");
        var writer = new XSourceWriter();
        for (int index = 0; index < values.Count; index++)
        {
            XModelSurfaceBuildData value = values[index];
            SurfacePlan plan = plans[index];
            writer.WriteUInt16(value.FlagsOrPad00); writer.WriteByte(value.StreamFlags); writer.WriteByte(value.Pad03); writer.WriteUInt16(value.VertCount); writer.WriteUInt16(value.TriCount); writer.WriteInt32(plan.TriIndices.PointerRaw); writer.WriteUInt16(value.Blend0); writer.WriteUInt16(value.Blend1); writer.WriteUInt16(value.Blend2); writer.WriteUInt16(value.Blend3); writer.WriteInt32(Pointer(value.VertsBlend)); writer.WriteInt32(plan.Verts0.PointerRaw); writer.WriteInt32(value.Vb0StreamSource); writer.WriteInt32(value.Vb0DataOffset); writer.WriteInt32(plan.Verts1.PointerRaw); writer.WriteInt32(value.Vb1StreamSource); writer.WriteInt32(value.Vb1DataOffset); writer.WriteInt32(value.RigidVertLists.Count); writer.WriteInt32(Pointer(value.RigidVertLists)); writer.WriteInt32(value.IndexBufferDataOffset); foreach (uint partBit in value.PartBits) writer.WriteUInt32(partBit);
        }
        return writer.ToArray();
    }
    private static SurfacePlan PlanSurface(XModelSurfaceBuildData data, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        var source = new List<EmissionBlockSegment>(); EmissionBlockSegment? blend = PlanUshorts(data.VertsBlend, 2, plan, all); Add(source, blend); PlannedRegion verts0 = PlanStream(data.Verts0, (data.StreamFlags & 0x01) == 0, data.LinkerProvenance.Verts0Storage, plan, all); source.AddRange(verts0.Source); PlannedRegion verts1 = PlanStream(data.Verts1, (data.StreamFlags & 0x02) == 0, data.LinkerProvenance.Verts1Storage, plan, all); source.AddRange(verts1.Source);
        EmissionBlockSegment? rigidTable = null; var treeSources = new List<EmissionBlockSegment>(); if (data.RigidVertLists.Count != 0) { EmissionAddress address = plan.Allocate(checked(data.RigidVertLists.Count * 0x0c), 4); rigidTable = new EmissionBlockSegment(address, BuildRigidTable(data.RigidVertLists)); all.Add(rigidTable); foreach (XModelRigidVertListBuildData rigid in data.RigidVertLists) if (rigid.CollisionTree is { } tree) treeSources.AddRange(PlanTree(tree, plan, all)); }
        Add(source, rigidTable); source.AddRange(treeSources); PlannedRegion triangles = PlanUshortStream(data.TriIndices, (data.StreamFlags & 0x04) == 0, data.LinkerProvenance.TriIndicesStorage, plan, all); source.AddRange(triangles.Source); return new SurfacePlan(verts0, verts1, triangles, source);
    }
    private static byte[] BuildRigidTable(IReadOnlyList<XModelRigidVertListBuildData> values) { var writer = new XSourceWriter(); foreach (XModelRigidVertListBuildData value in values) { writer.WriteUInt16(value.BoneOffset); writer.WriteUInt16(value.VertCount); writer.WriteUInt16(value.TriOffset); writer.WriteUInt16(value.TriCount); writer.WriteInt32(value.CollisionTree is null ? 0 : -1); } return writer.ToArray(); }
    private static IEnumerable<EmissionBlockSegment> PlanTree(XModelCollisionTreeBuildData data, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        EmissionAddress root = plan.Allocate(0x28, 4); EmissionBlockSegment? nodes = null; EmissionBlockSegment? leafs = null; if (data.Nodes.Count != 0) { EmissionAddress address = plan.Allocate(checked(data.Nodes.Count * 0x10), 16); var writer = new XSourceWriter(); foreach (XModelCollisionNodeBuildData value in data.Nodes) { writer.WriteUInt16(value.MinsX); writer.WriteUInt16(value.MinsY); writer.WriteUInt16(value.MinsZ); writer.WriteUInt16(value.MaxsX); writer.WriteUInt16(value.MaxsY); writer.WriteUInt16(value.MaxsZ); writer.WriteUInt16(value.ChildBeginIndex); writer.WriteUInt16(value.ChildCount); } nodes = new EmissionBlockSegment(address, writer.ToArray()); all.Add(nodes); } if (data.Leafs.Count != 0) leafs = PlanUshorts(data.Leafs, 2, plan, all); var rootWriter = new XSourceWriter(); WriteVector(rootWriter, data.Trans); WriteVector(rootWriter, data.Scale); rootWriter.WriteInt32(data.Nodes.Count); rootWriter.WriteInt32(Pointer(nodes)); rootWriter.WriteInt32(data.Leafs.Count); rootWriter.WriteInt32(Pointer(leafs)); var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray()); all.Add(rootSegment); var source = new List<EmissionBlockSegment> { rootSegment }; Add(source, nodes); Add(source, leafs); return source;
    }
    private static PlannedRegion PlanStream(IReadOnlyList<byte> data, bool physical, XModelReusableStorageToken? storage, EmissionPlan plan, List<EmissionBlockSegment> all) => PlanStreamBytes(data.ToArray(), physical, storage, plan, all);
    private static PlannedRegion PlanUshortStream(IReadOnlyList<ushort> data, bool physical, XModelReusableStorageToken? storage, EmissionPlan plan, List<EmissionBlockSegment> all) { var writer = new XSourceWriter(); foreach (ushort value in data) writer.WriteUInt16(value); return PlanStreamBytes(writer.ToArray(), physical, storage, plan, all); }
    private static PlannedRegion PlanStreamBytes(byte[] bytes, bool physical, XModelReusableStorageToken? storage, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (bytes.Length == 0) return new PlannedRegion(null, 0, []);
        if (storage is { } existingToken &&
            plan.TryGetReusableStorage(existingToken.Value, bytes, out EmissionAddress existing))
            return new PlannedRegion(null, existing.ToPackedPointer(), []);
        if (physical) plan.Push(XFileBlockType.PHYSICAL);
        try
        {
            EmissionAddress address = plan.Allocate(bytes.Length, 16);
            var segment = new EmissionBlockSegment(address, bytes);
            all.Add(segment);
            if (storage is { } createdToken)
                plan.RegisterReusableStorage(createdToken.Value, bytes, address);
            return new PlannedRegion(segment, -1, [segment]);
        }
        finally { if (physical) plan.Pop(XFileBlockType.PHYSICAL); }
    }
    private static EmissionBlockSegment? PlanUshorts(IReadOnlyList<ushort> data, int alignment, EmissionPlan plan, List<EmissionBlockSegment> all) { if (data.Count == 0) return null; EmissionAddress address = plan.Allocate(checked(data.Count * 2), alignment); var writer = new XSourceWriter(); foreach (ushort value in data) writer.WriteUInt16(value); var segment = new EmissionBlockSegment(address, writer.ToArray()); all.Add(segment); return segment; }
    private static int Pointer<T>(IReadOnlyList<T> values) => values.Count == 0 ? 0 : -1; private static int Pointer(EmissionBlockSegment? segment) => segment is null ? 0 : -1; private static void Add(List<EmissionBlockSegment> source, EmissionBlockSegment? segment) { if (segment is not null) source.Add(segment); } private static void WriteVector(XSourceWriter writer, Float3BuildData value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static void CheckSurface(XModelSurfaceBuildData data, int index, List<EmissionError> errors, int? rowIndex)
    {
        string path = $"surfaces[{index}]"; int blendCount = checked(data.Blend0 + (data.Blend1 * 3) + (data.Blend2 * 5) + (data.Blend3 * 7)); if (data.VertsBlend.Count != blendCount) errors.Add(Error($"{path}.vertsBlend", "Blend stream length must match the four blend counters.", rowIndex)); if (data.Verts0.Count != data.VertCount * 0x10 || data.Verts1.Count != data.VertCount * 0x10) errors.Add(Error($"{path}.verts", "Each vertex stream must contain exactly VertCount * 16 bytes.", rowIndex)); if (data.TriIndices.Count != data.TriCount * 3) errors.Add(Error($"{path}.triIndices", "Triangle index count must equal TriCount * 3.", rowIndex)); if (data.TriIndices.Any(value => value >= data.VertCount)) errors.Add(Error($"{path}.triIndices", "Triangle indices must be inside the vertex range.", rowIndex)); if (data.PartBits.Count != 6) errors.Add(Error($"{path}.partBits", "Surface requires exactly six part-bit words.", rowIndex));
        int rigidVertexCount = 0;
        for (int rigidIndex = 0; rigidIndex < data.RigidVertLists.Count; rigidIndex++)
        {
            XModelRigidVertListBuildData rigid = data.RigidVertLists[rigidIndex];
            rigidVertexCount = checked(rigidVertexCount + rigid.VertCount);
            // boneOffset is a byte offset into the model's bone matrix table
            // (native construction uses boneIndex << 6); it is not a vertex
            // start index. triOffset, however, is an index into this surface's
            // triangle table.
            if ((rigid.BoneOffset & 0x3f) != 0)
                errors.Add(Error($"{path}.rigidVertLists[{rigidIndex}].boneOffset", "Rigid bone offsets must be 64-byte aligned.", rowIndex));
            if (rigid.TriCount + rigid.TriOffset > data.TriCount)
                errors.Add(Error($"{path}.rigidVertLists[{rigidIndex}]", "Rigid triangle range must fit its surface.", rowIndex));
            if (rigid.CollisionTree is { } tree)
                CheckTree(tree, $"{path}.rigidVertLists[{rigidIndex}].collisionTree", errors, rowIndex);
        }
        if (rigidVertexCount > data.VertCount)
            errors.Add(Error($"{path}.rigidVertLists", "Rigid vertex counts exceed the surface vertex count.", rowIndex));
    }
    private static void CheckTree(XModelCollisionTreeBuildData data, string path, List<EmissionError> errors, int? rowIndex)
    {
        CheckVector(data.Trans, $"{path}.trans", errors, rowIndex);
        CheckCollisionScale(data.Scale, $"{path}.scale", errors, rowIndex);
        foreach (XModelCollisionNodeBuildData node in data.Nodes)
        {
            bool targetsLeafs = (node.ChildCount & 0x8000) != 0;
            int childCount = node.ChildCount & 0x7fff;
            int available = targetsLeafs ? data.Leafs.Count : data.Nodes.Count;
            if (node.ChildBeginIndex + childCount > available)
            {
                errors.Add(Error(
                    $"{path}.nodes",
                    targetsLeafs
                        ? "Collision leaf child range exceeds available leaves."
                        : "Collision node child range exceeds available nodes.",
                    rowIndex));
            }
        }
    }
    private static void CheckCollisionScale(Float3BuildData value, string path, List<EmissionError> errors, int? rowIndex)
    {
        // Native XSurface collision quantization uses +infinity as the
        // reciprocal-scale sentinel for a zero-width axis. Preserve that
        // exact IEEE-754 value; NaN and negative infinity remain invalid.
        if (!ValidCollisionScale(value.X) ||
            !ValidCollisionScale(value.Y) ||
            !ValidCollisionScale(value.Z))
        {
            errors.Add(Error(
                path,
                "Collision scale values must be finite or positive infinity.",
                rowIndex));
        }
    }
    private static bool ValidCollisionScale(float value) =>
        float.IsFinite(value) || float.IsPositiveInfinity(value);
    private static void CheckVector(Float3BuildData value, string path, List<EmissionError> errors, int? rowIndex) { if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) errors.Add(Error(path, "Vector values must be finite.", rowIndex)); }
    private static EmissionError Error(string path, string message, int? rowIndex) => new(path, message, rowIndex, XAssetType.XModelSurfs);

    private sealed record PlannedRegion(
        EmissionBlockSegment? Segment,
        int PointerRaw,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record SurfacePlan(
        PlannedRegion Verts0,
        PlannedRegion Verts1,
        PlannedRegion TriIndices,
        IReadOnlyList<EmissionBlockSegment> Source);
}
