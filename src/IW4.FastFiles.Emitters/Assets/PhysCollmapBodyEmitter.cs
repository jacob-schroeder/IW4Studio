using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Emitter for the fully loader-materialized PhysCollmap tree. All
/// geometry blocks are consumed while the loader is in LARGE.</summary>
public sealed class PhysCollmapBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.PhysCollmap;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not IPhysCollmapBuildData data) { diagnostics.Add(new("body", "PhysCollmap build data does not implement IPhysCollmapBuildData.", rowIndex, AssetType)); return diagnostics; }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "Name must be a Latin-1 C string.", rowIndex, AssetType));
        CheckVector(data.CenterOfMass, "mass.centerOfMass", diagnostics, rowIndex); CheckVector(data.MomentsOfInertia, "mass.momentsOfInertia", diagnostics, rowIndex); CheckVector(data.ProductsOfInertia, "mass.productsOfInertia", diagnostics, rowIndex); CheckVector(data.BoundsMidpoint, "bounds.midpoint", diagnostics, rowIndex); CheckVector(data.BoundsHalfSize, "bounds.halfSize", diagnostics, rowIndex);
        for (int index = 0; index < data.Geoms.Count; index++) CheckGeom(data.Geoms[index], index, diagnostics, rowIndex);
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex)); IPhysCollmapBuildData data = (IPhysCollmapBuildData)buildData;
        var segments = new List<EmissionBlockSegment>(); var source = new List<EmissionBlockSegment>();
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(0x48, 4); plan.Push(XFileBlockType.LARGE);
        int beforeName = segments.Count; PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases); int afterName = segments.Count;
        EmissionBlockSegment? geomTable = null; var geomSources = new List<EmissionBlockSegment>();
        if (data.Geoms.Count != 0)
        {
            EmissionAddress address = plan.Allocate(checked(data.Geoms.Count * 0x44), 4);
            geomTable = new EmissionBlockSegment(address, BuildGeomTable(data.Geoms)); segments.Add(geomTable);
            foreach (PhysGeomBuildData geom in data.Geoms) geomSources.AddRange(PlanGeom(geom, plan, segments));
        }
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter(); rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); rootWriter.WriteInt32(data.Geoms.Count); rootWriter.WriteInt32(geomTable is null ? 0 : -1); WriteVector(rootWriter, data.CenterOfMass); WriteVector(rootWriter, data.MomentsOfInertia); WriteVector(rootWriter, data.ProductsOfInertia); WriteVector(rootWriter, data.BoundsMidpoint); WriteVector(rootWriter, data.BoundsHalfSize);
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray()); segments.Add(rootSegment); source.Add(rootSegment); source.AddRange(segments.Skip(beforeName).Take(afterName - beforeName)); if (geomTable is not null) source.Add(geomTable); source.AddRange(geomSources);
        return new AssetBodyEmission(AssetType, root, segments, source);
    }

    private static byte[] BuildGeomTable(IReadOnlyList<PhysGeomBuildData> values)
    {
        var writer = new XSourceWriter();
        foreach (PhysGeomBuildData value in values) { writer.WriteInt32(value.BrushWrapper is null ? 0 : -1); writer.WriteInt32(value.Type); foreach (Float3BuildData orientation in value.Orientation) WriteVector(writer, orientation); WriteVector(writer, value.Midpoint); WriteVector(writer, value.HalfSize); }
        return writer.ToArray();
    }

    private static IEnumerable<EmissionBlockSegment> PlanGeom(PhysGeomBuildData geom, EmissionPlan plan, List<EmissionBlockSegment> all)
    {
        if (geom.BrushWrapper is null) return []; PhysBrushWrapperBuildData wrapper = geom.BrushWrapper;
        EmissionAddress root = plan.Allocate(0x44, 4); PhysBrushBuildData brush = wrapper.Brush;
        EmissionBlockSegment? sideTable = null; var sidePlanes = new List<EmissionBlockSegment>();
        int? importedSidesRaw =
            plan.PreserveImportedXAssetPointerValues
                ? brush.ImportedSidesPackedRaw
                : null;
        if (brush.Sides.Count != 0 && importedSidesRaw is null) { EmissionAddress address = plan.Allocate(checked(brush.Sides.Count * 8), 4); sideTable = new EmissionBlockSegment(address, BuildSideTable(brush.Sides)); all.Add(sideTable); foreach (PhysBrushSideBuildData side in brush.Sides) if (side.Plane is { } plane) sidePlanes.Add(PlanPlane(plane, plan, all)); }
        EmissionBlockSegment? adjacent = null;
        if (brush.BaseAdjacentSide.Count != 0) { EmissionAddress address = plan.Allocate(brush.BaseAdjacentSide.Count); adjacent = new EmissionBlockSegment(address, brush.BaseAdjacentSide.ToArray()); all.Add(adjacent); }
        EmissionBlockSegment? planeTable = null;
        int? importedPlanesRaw =
            plan.PreserveImportedXAssetPointerValues
                ? wrapper.ImportedPlanesPackedRaw
                : null;
        if (wrapper.Planes.Count != 0 && importedPlanesRaw is null) { EmissionAddress address = plan.Allocate(checked(wrapper.Planes.Count * 0x14), 4); var writer = new XSourceWriter(); foreach (PhysPlaneBuildData plane in wrapper.Planes) WritePlane(writer, plane); planeTable = new EmissionBlockSegment(address, writer.ToArray()); all.Add(planeTable); }
        var rootWriter = new XSourceWriter(); WriteVector(rootWriter, wrapper.Midpoint); WriteVector(rootWriter, wrapper.HalfSize); rootWriter.WriteUInt16((ushort)brush.Sides.Count); rootWriter.WriteUInt16(brush.GlassPieceIndex); rootWriter.WriteInt32(importedSidesRaw ?? (sideTable is null ? 0 : -1)); rootWriter.WriteInt32(adjacent is null ? 0 : -1); foreach (short value in brush.AxialMaterialNum) rootWriter.WriteInt16(value); rootWriter.WriteBytes(brush.FirstAdjacentSideOffsets.ToArray()); rootWriter.WriteBytes(brush.EdgeCount.ToArray()); rootWriter.WriteInt32(wrapper.TotalEdgeCount); rootWriter.WriteInt32(importedPlanesRaw ?? (planeTable is null ? 0 : -1));
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray()); all.Add(rootSegment); var source = new List<EmissionBlockSegment> { rootSegment }; if (sideTable is not null) source.Add(sideTable); source.AddRange(sidePlanes); if (adjacent is not null) source.Add(adjacent); if (planeTable is not null) source.Add(planeTable); return source;
    }

    private static byte[] BuildSideTable(IReadOnlyList<PhysBrushSideBuildData> values)
    {
        var writer = new XSourceWriter(); foreach (PhysBrushSideBuildData side in values) { writer.WriteInt32(side.Plane is null ? 0 : -1); writer.WriteUInt16(side.MaterialNum); writer.WriteByte(side.FirstAdjacentSideOffset); writer.WriteByte(side.EdgeCount); } return writer.ToArray();
    }
    private static EmissionBlockSegment PlanPlane(PhysPlaneBuildData value, EmissionPlan plan, List<EmissionBlockSegment> all) { EmissionAddress address = plan.Allocate(0x14, 4); var writer = new XSourceWriter(); WritePlane(writer, value); var segment = new EmissionBlockSegment(address, writer.ToArray()); all.Add(segment); return segment; }
    private static void WritePlane(XSourceWriter writer, PhysPlaneBuildData value) { WriteVector(writer, value.Normal); writer.WriteSingle(value.Dist); writer.WriteByte(value.Type); writer.WriteByte(value.SignBits); writer.WriteBytes(value.Pad12); }
    private static void WriteVector(XSourceWriter writer, Float3BuildData value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static void CheckGeom(PhysGeomBuildData value, int index, List<EmissionError> diagnostics, int? rowIndex)
    {
        string path = $"geoms[{index}]"; if (value.Orientation.Count != 3) diagnostics.Add(new($"{path}.orientation", "PhysGeomInfo requires exactly three orientation vectors.", rowIndex, XAssetType.PhysCollmap)); foreach (Float3BuildData orientation in value.Orientation) CheckVector(orientation, $"{path}.orientation", diagnostics, rowIndex); CheckVector(value.Midpoint, $"{path}.bounds.midpoint", diagnostics, rowIndex); CheckVector(value.HalfSize, $"{path}.bounds.halfSize", diagnostics, rowIndex);
        if (value.BrushWrapper is not { } wrapper) return; CheckVector(wrapper.Midpoint, $"{path}.wrapper.bounds.midpoint", diagnostics, rowIndex); CheckVector(wrapper.HalfSize, $"{path}.wrapper.bounds.halfSize", diagnostics, rowIndex); PhysBrushBuildData brush = wrapper.Brush; if (brush.Sides.Count > ushort.MaxValue) diagnostics.Add(new($"{path}.wrapper.brush.sides", "Brush side count exceeds UInt16.", rowIndex, XAssetType.PhysCollmap)); if (brush.BaseAdjacentSide.Count != wrapper.TotalEdgeCount || wrapper.TotalEdgeCount < 0) diagnostics.Add(new($"{path}.wrapper.baseAdjacentSide", "Base-adjacent bytes must equal the nonnegative total edge count.", rowIndex, XAssetType.PhysCollmap)); if (brush.AxialMaterialNum.Count != 6 || brush.FirstAdjacentSideOffsets.Count != 6 || brush.EdgeCount.Count != 6) diagnostics.Add(new($"{path}.wrapper.brush.fixedArrays", "Brush axial materials and adjacency arrays each require six entries.", rowIndex, XAssetType.PhysCollmap)); if (wrapper.Planes.Count is not (0) && wrapper.Planes.Count != brush.Sides.Count) diagnostics.Add(new($"{path}.wrapper.planes", "Inline plane array is absent or has one plane per brush side.", rowIndex, XAssetType.PhysCollmap)); if (brush.ImportedSidesPackedRaw is { } sidesRaw && IW4.FastFiles.Pointers.XPointerCodec.GetType(sidesRaw) != IW4.FastFiles.Pointers.PointerType.Offset) diagnostics.Add(new($"{path}.wrapper.brush.sidesPointer", "Imported brush-side pointer must be a packed offset.", rowIndex, XAssetType.PhysCollmap)); if (wrapper.ImportedPlanesPackedRaw is { } packedRaw && IW4.FastFiles.Pointers.XPointerCodec.GetType(packedRaw) != IW4.FastFiles.Pointers.PointerType.Offset) diagnostics.Add(new($"{path}.wrapper.planesPointer", "Imported brush-wrapper plane pointer must be a packed offset.", rowIndex, XAssetType.PhysCollmap));
        for (int sideIndex = 0; sideIndex < brush.Sides.Count; sideIndex++) if (brush.Sides[sideIndex].Plane is { } plane) CheckPlane(plane, $"{path}.wrapper.brush.sides[{sideIndex}].plane", diagnostics, rowIndex); for (int planeIndex = 0; planeIndex < wrapper.Planes.Count; planeIndex++) CheckPlane(wrapper.Planes[planeIndex], $"{path}.wrapper.planes[{planeIndex}]", diagnostics, rowIndex);
    }
    private static void CheckPlane(PhysPlaneBuildData value, string path, List<EmissionError> diagnostics, int? rowIndex) { CheckVector(value.Normal, $"{path}.normal", diagnostics, rowIndex); if (!float.IsFinite(value.Dist)) diagnostics.Add(new($"{path}.dist", "Plane distance must be finite.", rowIndex, XAssetType.PhysCollmap)); if (value.Pad12.Length != 2) diagnostics.Add(new($"{path}.pad12", "Plane padding is exactly two bytes.", rowIndex, XAssetType.PhysCollmap)); }
    private static void CheckVector(Float3BuildData value, string path, List<EmissionError> diagnostics, int? rowIndex) { if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) diagnostics.Add(new(path, "Vector values must be finite.", rowIndex, XAssetType.PhysCollmap)); }
}
