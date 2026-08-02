using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached authored PhysCollmap geometry.  Every nested brush and
/// plane is copied by value so a draft never retains a loader allocation.</summary>
public sealed class PhysCollmapAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal PhysCollmapAuthoredSnapshot(PhysCollmapBuildData data) => Data = data.Copy();
    internal PhysCollmapBuildData Data { get; }
    public XAssetType AssetType => XAssetType.PhysCollmap;

    internal static PhysCollmapAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is PhysCollmapAuthoredSnapshot snapshot
        ? snapshot
        : throw new InvalidDataException("PhysCollmap editing requires a capture-time detached semantic snapshot because its nested pointers may be aliases.");

    internal static PhysCollmapAuthoredSnapshot FromLoaded(PhysCollmapAsset asset) => new(new PhysCollmapBuildData(
        asset.Name, asset.Geoms.Select(Geom), Vector(asset.Mass.CenterOfMass), Vector(asset.Mass.MomentsOfInertia),
        Vector(asset.Mass.ProductsOfInertia), Vector(asset.Bounds.MidPoint), Vector(asset.Bounds.HalfSize)));

    private static PhysGeomBuildData Geom(PhysGeomInfo value) => new(value.BrushWrapper is { } wrapper ? Wrapper(wrapper) : null, value.Type,
        value.Orientation.Select(Vector).ToArray(), Vector(value.Bounds.MidPoint), Vector(value.Bounds.HalfSize));
    private static PhysBrushWrapperBuildData Wrapper(BrushWrapper value) => new(Vector(value.Bounds.MidPoint), Vector(value.Bounds.HalfSize),
        new PhysBrushBuildData(value.Brush.GlassPieceIndex, value.Brush.Sides.Select(Side).ToArray(), value.Brush.BaseAdjacentSide,
            value.Brush.AxialMaterialNum, value.Brush.FirstAdjacentSideOffsets, value.Brush.EdgeCount,
            value.Brush.SidesPointer.Type == IW4.FastFiles.Pointers.PointerType.Offset
                ? value.Brush.SidesPointer.Raw
                : null),
        value.TotalEdgeCount, value.Planes.Select(Plane).ToArray(),
        value.PlanesPointer.Type == IW4.FastFiles.Pointers.PointerType.Offset
            ? value.PlanesPointer.Raw
            : null);
    private static PhysBrushSideBuildData Side(CBrushSide value) => new(value.Plane is { } plane ? Plane(plane) : null, value.MaterialNum, value.FirstAdjacentSideOffset, value.EdgeCount);
    private static PhysPlaneBuildData Plane(CPlane value) => new(Vector(value.Normal), value.Dist, value.Type, value.SignBits, value.Pad12.ToArray());
    private static Float3BuildData Vector(IW4.Assets.Math.Vec3 value) => new(value.X, value.Y, value.Z);
}

public sealed class PhysCollmapBuildData : IPhysCollmapBuildData
{
    private readonly PhysGeomBuildData[] _geoms;
    internal PhysCollmapBuildData(string? name, IEnumerable<PhysGeomBuildData> geoms, Float3BuildData centerOfMass, Float3BuildData momentsOfInertia, Float3BuildData productsOfInertia, Float3BuildData boundsMidpoint, Float3BuildData boundsHalfSize)
    {
        Name = name; _geoms = geoms.Select(Copy).ToArray(); CenterOfMass = centerOfMass; MomentsOfInertia = momentsOfInertia;
        ProductsOfInertia = productsOfInertia; BoundsMidpoint = boundsMidpoint; BoundsHalfSize = boundsHalfSize;
    }
    public XAssetType AssetType => XAssetType.PhysCollmap;
    public string? Name { get; }
    public IReadOnlyList<PhysGeomBuildData> Geoms => Array.AsReadOnly(_geoms.Select(Copy).ToArray());
    public Float3BuildData CenterOfMass { get; }
    public Float3BuildData MomentsOfInertia { get; }
    public Float3BuildData ProductsOfInertia { get; }
    public Float3BuildData BoundsMidpoint { get; }
    public Float3BuildData BoundsHalfSize { get; }
    internal PhysCollmapBuildData Copy() => new(Name, _geoms, CenterOfMass, MomentsOfInertia, ProductsOfInertia, BoundsMidpoint, BoundsHalfSize);
    internal static PhysCollmapBuildData FromLoaded(PhysCollmapAsset asset) =>
        PhysCollmapAuthoredSnapshot.FromLoaded(asset).Data.Copy();
    internal static PhysGeomBuildData Copy(PhysGeomBuildData value) => new(value.BrushWrapper is { } wrapper ? Copy(wrapper) : null, value.Type, value.Orientation.ToArray(), value.Midpoint, value.HalfSize);
    private static PhysBrushWrapperBuildData Copy(PhysBrushWrapperBuildData value) => new(value.Midpoint, value.HalfSize, Copy(value.Brush), value.TotalEdgeCount, value.Planes.Select(Copy).ToArray(), value.ImportedPlanesPackedRaw);
    private static PhysBrushBuildData Copy(PhysBrushBuildData value) => new(value.GlassPieceIndex, value.Sides.Select(Copy).ToArray(), value.BaseAdjacentSide.ToArray(), value.AxialMaterialNum.ToArray(), value.FirstAdjacentSideOffsets.ToArray(), value.EdgeCount.ToArray(), value.ImportedSidesPackedRaw);
    private static PhysBrushSideBuildData Copy(PhysBrushSideBuildData value) => new(value.Plane is { } plane ? Copy(plane) : null, value.MaterialNum, value.FirstAdjacentSideOffset, value.EdgeCount);
    private static PhysPlaneBuildData Copy(PhysPlaneBuildData value) => new(value.Normal, value.Dist, value.Type, value.SignBits, value.Pad12);
}

public sealed class PhysCollmapDraft
{
    private PhysCollmapBuildData _data;
    internal PhysCollmapDraft(PhysCollmapBuildData data) => _data = data.Copy();
    public PhysCollmapBuildData Data => _data.Copy();
    public void SetMass(Float3BuildData centerOfMass, Float3BuildData momentsOfInertia, Float3BuildData productsOfInertia) => _data = Replace(centerOfMass: centerOfMass, momentsOfInertia: momentsOfInertia, productsOfInertia: productsOfInertia);
    public void SetBounds(Float3BuildData midpoint, Float3BuildData halfSize) => _data = Replace(boundsMidpoint: midpoint, boundsHalfSize: halfSize);
    public void ReplaceGeoms(IEnumerable<PhysGeomBuildData> geoms) { ArgumentNullException.ThrowIfNull(geoms); _data = Replace(geoms: geoms); }
    internal PhysCollmapDraft Clone() => new(_data);
    private PhysCollmapBuildData Replace(IEnumerable<PhysGeomBuildData>? geoms = null, Float3BuildData? centerOfMass = null, Float3BuildData? momentsOfInertia = null, Float3BuildData? productsOfInertia = null, Float3BuildData? boundsMidpoint = null, Float3BuildData? boundsHalfSize = null) => new(_data.Name, geoms ?? _data.Geoms, centerOfMass ?? _data.CenterOfMass, momentsOfInertia ?? _data.MomentsOfInertia, productsOfInertia ?? _data.ProductsOfInertia, boundsMidpoint ?? _data.BoundsMidpoint, boundsHalfSize ?? _data.BoundsHalfSize);
}

public sealed class PhysCollmapAuthoringAdapter : AssetAuthoringAdapter<PhysCollmapAuthoredSnapshot, PhysCollmapDraft, PhysCollmapBuildData>
{
    private static readonly PhysCollmapBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.PhysCollmap;
    public override PhysCollmapAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => PhysCollmapAuthoredSnapshot.Import(source);
    public override PhysCollmapDraft CreateDraft(PhysCollmapAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override PhysCollmapDraft CloneDraft(PhysCollmapDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(PhysCollmapDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(PhysCollmapDraft left, PhysCollmapDraft right) => Same(left.Data, right.Data);
    public override PhysCollmapBuildData ExportBuildData(PhysCollmapDraft draft) { PhysCollmapBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("PhysCollmap draft has validation errors and cannot produce build data."); return data; }

    private static bool Same(PhysCollmapBuildData left, PhysCollmapBuildData right) => left.Name == right.Name && Same(left.CenterOfMass, right.CenterOfMass) && Same(left.MomentsOfInertia, right.MomentsOfInertia) && Same(left.ProductsOfInertia, right.ProductsOfInertia) && Same(left.BoundsMidpoint, right.BoundsMidpoint) && Same(left.BoundsHalfSize, right.BoundsHalfSize) && left.Geoms.Count == right.Geoms.Count && left.Geoms.Zip(right.Geoms).All(pair => Same(pair.First, pair.Second));
    private static bool Same(PhysGeomBuildData left, PhysGeomBuildData right) => left.Type == right.Type && Same(left.Midpoint, right.Midpoint) && Same(left.HalfSize, right.HalfSize) && left.Orientation.Count == right.Orientation.Count && left.Orientation.Zip(right.Orientation).All(pair => Same(pair.First, pair.Second)) && ((left.BrushWrapper is null && right.BrushWrapper is null) || (left.BrushWrapper is { } a && right.BrushWrapper is { } b && Same(a, b)));
    private static bool Same(PhysBrushWrapperBuildData left, PhysBrushWrapperBuildData right) => Same(left.Midpoint, right.Midpoint) && Same(left.HalfSize, right.HalfSize) && left.TotalEdgeCount == right.TotalEdgeCount && Same(left.Brush, right.Brush) && left.Planes.Count == right.Planes.Count && left.Planes.Zip(right.Planes).All(pair => Same(pair.First, pair.Second));
    private static bool Same(PhysBrushBuildData left, PhysBrushBuildData right) => left.GlassPieceIndex == right.GlassPieceIndex && left.BaseAdjacentSide.SequenceEqual(right.BaseAdjacentSide) && left.AxialMaterialNum.SequenceEqual(right.AxialMaterialNum) && left.FirstAdjacentSideOffsets.SequenceEqual(right.FirstAdjacentSideOffsets) && left.EdgeCount.SequenceEqual(right.EdgeCount) && left.Sides.Count == right.Sides.Count && left.Sides.Zip(right.Sides).All(pair => Same(pair.First, pair.Second));
    private static bool Same(PhysBrushSideBuildData left, PhysBrushSideBuildData right) => left.MaterialNum == right.MaterialNum && left.FirstAdjacentSideOffset == right.FirstAdjacentSideOffset && left.EdgeCount == right.EdgeCount && ((left.Plane is null && right.Plane is null) || (left.Plane is { } a && right.Plane is { } b && Same(a, b)));
    private static bool Same(PhysPlaneBuildData left, PhysPlaneBuildData right) => Same(left.Normal, right.Normal) && Bits(left.Dist, right.Dist) && left.Type == right.Type && left.SignBits == right.SignBits && left.Pad12.SequenceEqual(right.Pad12);
    private static bool Same(Float3BuildData left, Float3BuildData right) => Bits(left.X, right.X) && Bits(left.Y, right.Y) && Bits(left.Z, right.Z);
    private static bool Bits(float left, float right) => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
}
