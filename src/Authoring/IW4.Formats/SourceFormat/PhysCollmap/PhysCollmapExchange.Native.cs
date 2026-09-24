using System.Text.Json;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.PhysCollmap;

public sealed partial class PhysCollmapExchange
{
    private const string NativeFormat = "iw4-ps3-phys-collmap";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string WriteNative(PhysCollmapAsset asset, string name)
    {
        if (asset.Count < 0 || asset.Geoms.Count != asset.Count ||
            (asset.Count == 0 && asset.GeomsPointer.Type != PointerType.Null))
            throw new InvalidDataException($"PhysCollmap '{name}' has an invalid geometry allocation.");
        ValidateMass(asset.Mass, name);
        ValidateBounds(asset.Bounds, $"PhysCollmap '{name}'.Bounds");
        var document = new NativeDocument
        {
            Format = NativeFormat,
            Version = 1,
            Name = name,
            Mass = asset.Mass,
            Bounds = asset.Bounds,
            Geoms = asset.Geoms.Select((geom, index) =>
                WriteGeom(geom, $"PhysCollmap '{name}'.Geoms[{index}]")).ToArray()
        };
        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public PhysCollmapAsset Link(string sourceDirectory, string assetName)
    {
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "PhysCollmap");
        string path = NativeSourcePath.Resolve(
            sourceDirectory, "phys_collmaps", name, ".phys_collmap.json");
        using FileStream stream = File.OpenRead(path);
        NativeDocument document = JsonSerializer.Deserialize<NativeDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException($"PhysCollmap '{name}' has an empty native source document.");
        if (document.Format != NativeFormat || document.Version != 1 || document.Name != name ||
            document.Geoms is null || document.Mass is null || document.Bounds is null)
            throw new InvalidDataException($"PhysCollmap '{name}' has invalid native source fields.");
        ValidateMass(document.Mass, name);
        ValidateBounds(document.Bounds, $"PhysCollmap '{name}'.Bounds");
        PhysGeomInfo[] geoms = document.Geoms.Select((geom, index) =>
            ReadGeom(geom, $"PhysCollmap '{name}'.Geoms[{index}]")).ToArray();
        return new PhysCollmapAsset
        {
            Name = name,
            Count = geoms.Length,
            Geoms = geoms,
            Mass = document.Mass,
            Bounds = document.Bounds
        };
    }

    private static GeomDocument WriteGeom(PhysGeomInfo geom, string field)
    {
        if (geom is null || geom.Orientation.Count != 3)
            throw new InvalidDataException($"{field} requires exactly three orientation vectors.");
        foreach (Vec3 orientation in geom.Orientation)
            ValidateVec3(orientation, $"{field}.Orientation");
        ValidateBounds(geom.Bounds, $"{field}.Bounds");
        if (geom.BrushWrapper is null && geom.BrushWrapperPointer.Type != PointerType.Null)
            throw new InvalidDataException($"{field}.BrushWrapper has no materialized body.");
        return new GeomDocument
        {
            Type = geom.Type,
            Orientation = geom.Orientation.ToArray(),
            Bounds = geom.Bounds,
            BrushWrapper = geom.BrushWrapper is null ? null :
                WriteWrapper(geom.BrushWrapper, $"{field}.BrushWrapper")
        };
    }

    private static PhysGeomInfo ReadGeom(GeomDocument geom, string field)
    {
        if (geom is null || geom.Orientation is not { Length: 3 } || geom.Bounds is null)
            throw new InvalidDataException($"{field} requires bounds and three orientation vectors.");
        foreach (Vec3 orientation in geom.Orientation)
            ValidateVec3(orientation, $"{field}.Orientation");
        ValidateBounds(geom.Bounds, $"{field}.Bounds");
        return new PhysGeomInfo
        {
            Type = geom.Type,
            Orientation = geom.Orientation,
            Bounds = geom.Bounds,
            BrushWrapper = geom.BrushWrapper is null ? null :
                ReadWrapper(geom.BrushWrapper, $"{field}.BrushWrapper")
        };
    }

    private static WrapperDocument WriteWrapper(BrushWrapper wrapper, string field)
    {
        CBrush brush = wrapper.Brush ?? throw new InvalidDataException($"{field}.Brush is null.");
        ValidateBounds(wrapper.Bounds, $"{field}.Bounds");
        if (brush.Sides.Count != brush.NumSides ||
            (brush.NumSides == 0 && brush.SidesPointer.Type != PointerType.Null) ||
            brush.AxialMaterialNum.Count != 6 ||
            brush.FirstAdjacentSideOffsets.Count != 6 || brush.EdgeCount.Count != 6 ||
            wrapper.TotalEdgeCount < 0 ||
            brush.BaseAdjacentSide.Count != wrapper.TotalEdgeCount ||
            (wrapper.TotalEdgeCount == 0 && brush.BaseAdjacentSidePointer.Type != PointerType.Null) ||
            (wrapper.Planes.Count != 0 && wrapper.Planes.Count != brush.NumSides) ||
            (wrapper.Planes.Count == 0 && wrapper.PlanesPointer.Type != PointerType.Null))
            throw new InvalidDataException($"{field} has incomplete brush, adjacency, or plane arrays.");
        var sides = brush.Sides.Select((side, index) =>
            WriteSide(side, $"{field}.Brush.Sides[{index}]")).ToArray();
        var planes = wrapper.Planes.Select((plane, index) =>
            ValidatePlane(plane, $"{field}.Planes[{index}]")).ToArray();
        return new WrapperDocument
        {
            Bounds = wrapper.Bounds,
            TotalEdgeCount = wrapper.TotalEdgeCount,
            NumSides = brush.NumSides,
            GlassPieceIndex = brush.GlassPieceIndex,
            Sides = sides,
            BaseAdjacentSide = brush.BaseAdjacentSide.ToArray(),
            AxialMaterialNum = brush.AxialMaterialNum.ToArray(),
            FirstAdjacentSideOffsets = brush.FirstAdjacentSideOffsets.ToArray(),
            EdgeCount = brush.EdgeCount.ToArray(),
            Planes = planes
        };
    }

    private static BrushWrapper ReadWrapper(WrapperDocument wrapper, string field)
    {
        if (wrapper.Bounds is null || wrapper.Sides is null || wrapper.Planes is null ||
            wrapper.BaseAdjacentSide is null || wrapper.AxialMaterialNum is not { Length: 6 } ||
            wrapper.FirstAdjacentSideOffsets is not { Length: 6 } ||
            wrapper.EdgeCount is not { Length: 6 } ||
            wrapper.Sides.Length != wrapper.NumSides ||
            wrapper.TotalEdgeCount < 0 || wrapper.BaseAdjacentSide.Length != wrapper.TotalEdgeCount ||
            (wrapper.Planes.Length != 0 && wrapper.Planes.Length != wrapper.NumSides))
            throw new InvalidDataException($"{field} has incomplete brush, adjacency, or plane arrays.");
        ValidateBounds(wrapper.Bounds, $"{field}.Bounds");
        CPlane[] planes = wrapper.Planes.Select((plane, index) =>
            ValidatePlane(plane, $"{field}.Planes[{index}]")).ToArray();
        CBrushSide[] sides = wrapper.Sides.Select((side, index) =>
            ReadSide(side, $"{field}.Brush.Sides[{index}]")).ToArray();
        return new BrushWrapper
        {
            Bounds = wrapper.Bounds,
            TotalEdgeCount = wrapper.TotalEdgeCount,
            Planes = planes,
            Brush = new CBrush
            {
                NumSides = wrapper.NumSides,
                GlassPieceIndex = wrapper.GlassPieceIndex,
                Sides = sides,
                BaseAdjacentSide = wrapper.BaseAdjacentSide,
                AxialMaterialNum = wrapper.AxialMaterialNum,
                FirstAdjacentSideOffsets = wrapper.FirstAdjacentSideOffsets,
                EdgeCount = wrapper.EdgeCount
            }
        };
    }

    private static SideDocument WriteSide(CBrushSide side, string field)
    {
        if (side is null || side.Plane is null && side.PlanePointer.Type != PointerType.Null)
            throw new InvalidDataException($"{field}.Plane has no materialized body.");
        return new SideDocument
        {
            Plane = side.Plane is null ? null : ValidatePlane(side.Plane, $"{field}.Plane"),
            MaterialNum = side.MaterialNum,
            FirstAdjacentSideOffset = side.FirstAdjacentSideOffset,
            EdgeCount = side.EdgeCount
        };
    }

    private static CBrushSide ReadSide(SideDocument side, string field)
    {
        if (side is null)
            throw new InvalidDataException($"{field} is null.");
        return new CBrushSide
        {
            Plane = side.Plane is null ? null : ValidatePlane(side.Plane, $"{field}.Plane"),
            MaterialNum = side.MaterialNum,
            FirstAdjacentSideOffset = side.FirstAdjacentSideOffset,
            EdgeCount = side.EdgeCount
        };
    }

    private static CPlane ValidatePlane(CPlane plane, string field)
    {
        if (plane is null || plane.Pad12.Count != 2 || !float.IsFinite(plane.Dist))
            throw new InvalidDataException($"{field} has invalid plane bytes or distance.");
        ValidateVec3(plane.Normal, $"{field}.Normal");
        return plane;
    }

    private static void ValidateMass(PhysMass mass, string name)
    {
        if (mass is null)
            throw new InvalidDataException($"PhysCollmap '{name}' has no mass body.");
        ValidateVec3(mass.CenterOfMass, $"PhysCollmap '{name}'.Mass.CenterOfMass");
        ValidateVec3(mass.MomentsOfInertia, $"PhysCollmap '{name}'.Mass.MomentsOfInertia");
        ValidateVec3(mass.ProductsOfInertia, $"PhysCollmap '{name}'.Mass.ProductsOfInertia");
    }

    private static void ValidateBounds(Bounds bounds, string field)
    {
        if (bounds is null)
            throw new InvalidDataException($"{field} is null.");
        ValidateVec3(bounds.MidPoint, $"{field}.MidPoint");
        ValidateVec3(bounds.HalfSize, $"{field}.HalfSize");
    }

    private static void ValidateVec3(Vec3 value, string field)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidDataException($"{field} must be finite.");
    }

    private sealed class NativeDocument
    {
        public NativeDocument() { }

        public string? Format { get; init; }
        public int Version { get; init; }
        public string? Name { get; init; }
        public PhysMass? Mass { get; init; }
        public Bounds? Bounds { get; init; }
        public GeomDocument[]? Geoms { get; init; }
    }

    private sealed class GeomDocument
    {
        public GeomDocument() { }

        public int Type { get; init; }
        public Vec3[]? Orientation { get; init; }
        public Bounds? Bounds { get; init; }
        public WrapperDocument? BrushWrapper { get; init; }
    }

    private sealed class WrapperDocument
    {
        public WrapperDocument() { }

        public Bounds? Bounds { get; init; }
        public int TotalEdgeCount { get; init; }
        public ushort NumSides { get; init; }
        public ushort GlassPieceIndex { get; init; }
        public SideDocument[]? Sides { get; init; }
        public byte[]? BaseAdjacentSide { get; init; }
        public short[]? AxialMaterialNum { get; init; }
        public byte[]? FirstAdjacentSideOffsets { get; init; }
        public byte[]? EdgeCount { get; init; }
        public CPlane[]? Planes { get; init; }
    }

    private sealed class SideDocument
    {
        public SideDocument() { }

        public CPlane? Plane { get; init; }
        public ushort MaterialNum { get; init; }
        public byte FirstAdjacentSideOffset { get; init; }
        public byte EdgeCount { get; init; }
    }
}
