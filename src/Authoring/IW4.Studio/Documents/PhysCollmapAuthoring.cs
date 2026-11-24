using System.Numerics;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

/// <summary>A detached collision geometry row. Resolved brushes support translation and
/// positive axis scaling; their implicit axial planes do not support rotation.</summary>
public sealed class PhysCollmapGeomDraft
{
    private readonly PhysGeomInfo? _source;
    private readonly Vec3[] _sourceOrientation;
    private readonly Matrix4x4 _originalOrientation;
    private readonly bool _canEditBrush;
    private readonly BrushWrapper? _originalPreviewBrush;
    private BrushWrapper? _previewBrush;

    internal PhysCollmapGeomDraft(PhysGeomInfo source)
    {
        _source = source;
        _sourceOrientation = source.Orientation.ToArray();
        Type = source.Type;
        Shape = source.EffectiveShape;
        Bounds bounds = Shape == PhysGeomShape.Brush && source.BrushWrapper is { } wrapper
            ? wrapper.Bounds : source.Bounds;
        Position = ToNumerics(bounds.MidPoint);
        Size = ToNumerics(bounds.HalfSize) * 2f;
        Orientation = Shape switch
        {
            PhysGeomShape.Brush => Matrix4x4.Identity,
            PhysGeomShape.Cylinder => CylinderOrientation(ToNumerics(_sourceOrientation[0])),
            _ => RowsToMatrix(_sourceOrientation)
        };
        _originalOrientation = Orientation;
        _canEditBrush = Shape == PhysGeomShape.Brush && CanEditBrush(source.BrushWrapper);
        _previewBrush = source.BrushWrapper is { } brush ? CopyBrush(brush) : null;
        _originalPreviewBrush = _previewBrush;
    }

    private PhysCollmapGeomDraft(PhysGeomShape shape, Vector3 position, Vector3 size)
    {
        Shape = shape;
        Type = shape == PhysGeomShape.Box ? PhysGeomPrimitive.Box : PhysGeomPrimitive.Cylinder;
        Position = position;
        Size = size;
        Orientation = Matrix4x4.Identity;
        _originalOrientation = Orientation;
        _canEditBrush = false;
        _originalPreviewBrush = null;
        _sourceOrientation = shape == PhysGeomShape.Cylinder
            ? [new Vec3 { Z = 1 }, new Vec3 { X = 1 }, new Vec3 { Y = 1 }]
            : [new Vec3 { X = 1 }, new Vec3 { Y = 1 }, new Vec3 { Z = 1 }];
    }

    private PhysCollmapGeomDraft(PhysCollmapGeomDraft source)
    {
        _source = source._source;
        _sourceOrientation = source._sourceOrientation.ToArray();
        Shape = source.Shape;
        Type = source.Type;
        Position = source.Position;
        Size = source.Size;
        Orientation = source.Orientation;
        _originalOrientation = source._originalOrientation;
        _canEditBrush = source._canEditBrush;
        _originalPreviewBrush = source._originalPreviewBrush;
        _previewBrush = source._previewBrush;
    }

    public PhysGeomShape Shape { get; }
    public PhysGeomPrimitive Type { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Size { get; private set; }
    /// <summary>Row basis. A cylinder's local Z row is its stock axial Orientation[0].</summary>
    public Matrix4x4 Orientation { get; private set; }
    public bool CanEditGeometry => Shape != PhysGeomShape.Brush || _canEditBrush;
    public bool CanRotateGeometry => Shape != PhysGeomShape.Brush;
    /// <summary>Detached preview payload. Treat as read-only; edits go through the draft.</summary>
    public BrushWrapper? Brush => _previewBrush;

    internal static PhysCollmapGeomDraft New(PhysGeomShape shape, Vector3 position, Vector3 size)
    {
        ValidatePosition(position);
        ValidateSize(shape, size);
        return new PhysCollmapGeomDraft(shape, position, size);
    }

    internal PhysCollmapGeomDraft Clone() => new(this);

    internal void SetPosition(Vector3 value)
    {
        RequirePrimitive();
        ValidatePosition(value);
        BrushWrapper? preview = Shape == PhysGeomShape.Brush
            ? CopyBrush(TransformBrush(_source!.BrushWrapper!, value, Size)) : null;
        Position = value;
        if (Shape == PhysGeomShape.Brush)
            _previewBrush = SameTransformAsSource() ? _originalPreviewBrush : preview;
    }

    internal void SetSize(Vector3 value)
    {
        RequirePrimitive();
        ValidateSize(Shape, value);
        BrushWrapper? preview = Shape == PhysGeomShape.Brush
            ? CopyBrush(TransformBrush(_source!.BrushWrapper!, Position, value)) : null;
        Size = value;
        if (Shape == PhysGeomShape.Brush)
            _previewBrush = SameTransformAsSource() ? _originalPreviewBrush : preview;
    }

    internal void SetOrientation(Matrix4x4 value)
    {
        RequirePrimitive();
        if (!CanRotateGeometry)
            throw new NotSupportedException("Brush rotation requires rebuilding implicit axial planes.");
        if (!IsRotation(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Orientation must be an orthonormal rotation matrix.");
        Orientation = value;
    }

    internal PhysGeomInfo ToGeom()
    {
        if (_source is not null && SameTransformAsSource())
            return _source;

        if (Shape == PhysGeomShape.Brush)
        {
            PhysGeomInfo source = _source ?? throw new InvalidOperationException("Brush has no source row.");
            return new PhysGeomInfo
            {
                BrushWrapperPointer = source.BrushWrapperPointer,
                BrushWrapper = TransformBrush(source.BrushWrapper!, Position, Size),
                Type = source.Type,
                Orientation = source.Orientation,
                Bounds = source.Bounds
            };
        }

        Vec3[] orientation = _sourceOrientation.ToArray();
        if (Shape == PhysGeomShape.Cylinder && !SameCylinderAxis(Orientation, _originalOrientation))
            orientation[0] = ToGame(new Vector3(Orientation.M31, Orientation.M32, Orientation.M33));
        else if (Shape == PhysGeomShape.Box && !Orientation.Equals(_originalOrientation))
        {
            orientation[0] = ToGame(new Vector3(Orientation.M11, Orientation.M12, Orientation.M13));
            orientation[1] = ToGame(new Vector3(Orientation.M21, Orientation.M22, Orientation.M23));
            orientation[2] = ToGame(new Vector3(Orientation.M31, Orientation.M32, Orientation.M33));
        }
        return new PhysGeomInfo
        {
            BrushWrapperPointer = _source?.BrushWrapperPointer ?? default,
            BrushWrapper = _source?.BrushWrapper,
            Type = Type,
            Orientation = orientation,
            Bounds = new Bounds { MidPoint = ToGame(Position), HalfSize = ToGame(Size / 2f) }
        };
    }

    internal bool Matches(PhysCollmapGeomDraft other) =>
        ReferenceEquals(_source, other._source) && Shape == other.Shape && Type == other.Type &&
        Same(Position, other.Position) && Same(Size, other.Size) &&
        (Shape == PhysGeomShape.Cylinder
            ? SameCylinderAxis(Orientation, other.Orientation)
            : Same(Orientation, other.Orientation));

    internal bool IsUnchangedSourceRow(PhysGeomInfo source) =>
        ReferenceEquals(_source, source) && SameTransformAsSource();

    private bool SameTransformAsSource()
    {
        if (_source is null)
            return false;
        Bounds bounds = Shape == PhysGeomShape.Brush && _source.BrushWrapper is { } wrapper
            ? wrapper.Bounds : _source.Bounds;
        return Same(Position, ToNumerics(bounds.MidPoint)) &&
            Same(Size, ToNumerics(bounds.HalfSize) * 2f) &&
            (Shape == PhysGeomShape.Cylinder
                ? SameCylinderAxis(Orientation, _originalOrientation)
                : Orientation.Equals(_originalOrientation));
    }

    private void RequirePrimitive()
    {
        if (!CanEditGeometry)
            throw new NotSupportedException("Brush geometry has unresolved sides or planes and cannot be transformed safely. Remove and replace it deliberately.");
    }

    internal static void ValidatePosition(Vector3 value)
    {
        if (!Finite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Position must be finite.");
    }

    private static void ValidateSize(PhysGeomShape shape, Vector3 value)
    {
        if (!Finite(value) || value.X <= 0 || value.Y <= 0 || value.Z <= 0 ||
            shape == PhysGeomShape.Cylinder && !Same(value.X, value.Y))
            throw new ArgumentOutOfRangeException(nameof(value), "Size must be positive and finite; cylinder X and Y must match.");
    }

    private static bool IsRotation(Matrix4x4 m)
    {
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);
        return Finite(x) && Finite(y) && Finite(z) &&
            MathF.Abs(x.LengthSquared() - 1) < 0.001f &&
            MathF.Abs(y.LengthSquared() - 1) < 0.001f &&
            MathF.Abs(z.LengthSquared() - 1) < 0.001f &&
            MathF.Abs(Vector3.Dot(x, y)) < 0.001f &&
            MathF.Abs(Vector3.Dot(y, z)) < 0.001f &&
            MathF.Abs(Vector3.Dot(z, x)) < 0.001f &&
            Vector3.Dot(Vector3.Cross(x, y), z) > 0.999f &&
            m.M14 == 0 && m.M24 == 0 && m.M34 == 0 &&
            m.M41 == 0 && m.M42 == 0 && m.M43 == 0 && m.M44 == 1;
    }

    private static Matrix4x4 RowsToMatrix(IReadOnlyList<Vec3> rows) => new(
        rows[0].X, rows[0].Y, rows[0].Z, 0,
        rows[1].X, rows[1].Y, rows[1].Z, 0,
        rows[2].X, rows[2].Y, rows[2].Z, 0,
        0, 0, 0, 1);

    private static Matrix4x4 CylinderOrientation(Vector3 axis)
    {
        if (!Finite(axis) || axis.LengthSquared() < 0.000001f)
            return Matrix4x4.Identity;
        axis = Vector3.Normalize(axis);
        Vector3 guide = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 x = Vector3.Normalize(Vector3.Cross(guide, axis));
        Vector3 y = Vector3.Cross(axis, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0,
            axis.X, axis.Y, axis.Z, 0, 0, 0, 0, 1);
    }

    internal static Vector3 ToNumerics(Vec3 value) => new(value.X, value.Y, value.Z);
    internal static Vec3 ToGame(Vector3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };
    internal static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    internal static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    internal static bool Same(Vector3 a, Vector3 b) => Same(a.X, b.X) && Same(a.Y, b.Y) && Same(a.Z, b.Z);
    private static bool Same(Matrix4x4 a, Matrix4x4 b) => a.Equals(b);
    private static bool SameCylinderAxis(Matrix4x4 a, Matrix4x4 b)
    {
        Vector3 delta = new(a.M31 - b.M31, a.M32 - b.M32, a.M33 - b.M33);
        return delta.LengthSquared() < 1e-10f;
    }

    private static bool CanEditBrush(BrushWrapper? wrapper)
    {
        if (wrapper?.Brush is not { } brush ||
            brush.Sides.Count != brush.NumSides ||
            brush.AxialMaterialNum.Count != 6 ||
            brush.FirstAdjacentSideOffsets.Count != 6 || brush.EdgeCount.Count != 6 ||
            wrapper.Planes.Count != 0 && wrapper.Planes.Count != brush.NumSides ||
            wrapper.Planes.Count == 0 && wrapper.PlanesPointer.Type != PointerType.Null ||
            brush.Sides.Any(side => side is null || side.Plane is null) ||
            !Finite(ToNumerics(wrapper.Bounds.MidPoint)) ||
            !Finite(ToNumerics(wrapper.Bounds.HalfSize)))
            return false;
        Vector3 half = ToNumerics(wrapper.Bounds.HalfSize);
        return half.X > 0 && half.Y > 0 && half.Z > 0 &&
            brush.Sides.All(side => side.Plane is { } plane && ValidPlane(plane)) &&
            wrapper.Planes.All(ValidPlane);
    }

    private static bool ValidPlane(CPlane plane) =>
        Finite(ToNumerics(plane.Normal)) && float.IsFinite(plane.Dist) &&
        ToNumerics(plane.Normal).LengthSquared() > 0;

    private BrushWrapper TransformBrush(BrushWrapper wrapper, Vector3 position, Vector3 size)
    {
        Vector3 oldCenter = ToNumerics(wrapper.Bounds.MidPoint);
        Vector3 oldSize = ToNumerics(wrapper.Bounds.HalfSize) * 2f;
        Vector3 scale = size / oldSize;
        Vector3 shift = position - scale * oldCenter;
        if (!Finite(scale) || !Finite(shift) ||
            scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Brush scale exceeds finite positive range.");
        var transformed = new Dictionary<CPlane, CPlane>(ReferenceEqualityComparer.Instance);
        CPlane Map(CPlane plane)
        {
            if (transformed.TryGetValue(plane, out CPlane? existing))
                return existing;
            Vector3 normal = ToNumerics(plane.Normal);
            Vector3 raw = normal / scale;
            float length = raw.Length();
            if (!float.IsFinite(length) || length <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Brush plane transform exceeds finite range.");
            Vector3 nextNormal = raw / length;
            float nextDistance = (plane.Dist + Vector3.Dot(raw, shift)) / length;
            if (!Finite(nextNormal) || !float.IsFinite(nextDistance))
                throw new ArgumentOutOfRangeException(nameof(size), "Brush plane transform exceeds finite range.");
            CPlane result = new()
            {
                Normal = ToGame(nextNormal), Dist = nextDistance,
                Type = plane.Type, SignBits = plane.SignBits, Pad12 = plane.Pad12.ToArray()
            };
            transformed.Add(plane, result);
            return result;
        }

        CBrush brush = wrapper.Brush;
        return new BrushWrapper
        {
            Bounds = new Bounds { MidPoint = ToGame(position), HalfSize = ToGame(size / 2f) },
            TotalEdgeCount = wrapper.TotalEdgeCount,
            PlanesPointer = wrapper.PlanesPointer,
            Planes = wrapper.Planes.Select(Map).ToArray(),
            Brush = new CBrush
            {
                NumSides = brush.NumSides, GlassPieceIndex = brush.GlassPieceIndex,
                SidesPointer = brush.SidesPointer,
                Sides = brush.Sides.Select(side => new CBrushSide
                {
                    PlanePointer = side.PlanePointer,
                    Plane = Map(side.Plane!), MaterialNum = side.MaterialNum,
                    FirstAdjacentSideOffset = side.FirstAdjacentSideOffset,
                    EdgeCount = side.EdgeCount
                }).ToArray(),
                BaseAdjacentSidePointer = brush.BaseAdjacentSidePointer,
                BaseAdjacentSide = brush.BaseAdjacentSide.ToArray(),
                AxialMaterialNum = brush.AxialMaterialNum.ToArray(),
                FirstAdjacentSideOffsets = brush.FirstAdjacentSideOffsets.ToArray(),
                EdgeCount = brush.EdgeCount.ToArray()
            }
        };
    }

    private static BrushWrapper CopyBrush(BrushWrapper value)
    {
        var planes = new Dictionary<CPlane, CPlane>(ReferenceEqualityComparer.Instance);
        CPlane Map(CPlane plane)
        {
            if (!planes.TryGetValue(plane, out CPlane? copy))
            {
                copy = CopyPlane(plane);
                planes.Add(plane, copy);
            }
            return copy;
        }
        return new BrushWrapper
        {
            Bounds = new Bounds { MidPoint = value.Bounds.MidPoint, HalfSize = value.Bounds.HalfSize },
            TotalEdgeCount = value.TotalEdgeCount,
            PlanesPointer = value.PlanesPointer,
            Planes = value.Planes.Select(Map).ToArray(),
            Brush = new CBrush
            {
                NumSides = value.Brush.NumSides, GlassPieceIndex = value.Brush.GlassPieceIndex,
                SidesPointer = value.Brush.SidesPointer,
                Sides = value.Brush.Sides.Select(side => new CBrushSide
                {
                    PlanePointer = side.PlanePointer,
                    Plane = side.Plane is null ? null : Map(side.Plane),
                    MaterialNum = side.MaterialNum,
                    FirstAdjacentSideOffset = side.FirstAdjacentSideOffset,
                    EdgeCount = side.EdgeCount
                }).ToArray(),
                BaseAdjacentSidePointer = value.Brush.BaseAdjacentSidePointer,
                BaseAdjacentSide = value.Brush.BaseAdjacentSide.ToArray(),
                AxialMaterialNum = value.Brush.AxialMaterialNum.ToArray(),
                FirstAdjacentSideOffsets = value.Brush.FirstAdjacentSideOffsets.ToArray(),
                EdgeCount = value.Brush.EdgeCount.ToArray()
            }
        };
    }

    private static CPlane CopyPlane(CPlane value) => new()
    {
        Normal = value.Normal, Dist = value.Dist, Type = value.Type,
        SignBits = value.SignBits, Pad12 = value.Pad12.ToArray()
    };
}

/// <summary>Editable detached PhysCollmap with original brush and inertia data retained.</summary>
public sealed class PhysCollmapDraft
{
    private readonly PhysCollmapAsset _source;
    private List<PhysCollmapGeomDraft> _geoms;
    private IReadOnlyList<PhysCollmapGeomDraft> _geomsView;
    private Bounds _bounds;
    private Vector3 _centerOfMass;

    public PhysCollmapDraft(PhysCollmapAsset source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.Count != source.Geoms.Count || source.Geoms.Any(geom => geom is null || geom.Orientation.Count != 3))
            throw new InvalidDataException("PhysCollmap has incomplete geometry rows.");
        _geoms = source.Geoms.Select(geom => new PhysCollmapGeomDraft(geom)).ToList();
        _geomsView = _geoms.AsReadOnly();
        _bounds = CopyBounds(source.Bounds);
        _centerOfMass = PhysCollmapGeomDraft.ToNumerics(source.Mass.CenterOfMass);
    }

    private PhysCollmapDraft(PhysCollmapDraft source)
    {
        _source = source._source;
        _geoms = source._geoms.Select(geom => geom.Clone()).ToList();
        _geomsView = _geoms.AsReadOnly();
        _bounds = CopyBounds(source._bounds);
        _centerOfMass = source._centerOfMass;
    }

    public string? Name => _source.Name;
    public int Count => _geoms.Count;
    public IReadOnlyList<PhysCollmapGeomDraft> Geoms => _geomsView;
    public Vector3 CenterOfMass
    {
        get => _centerOfMass;
        set
        {
            PhysCollmapGeomDraft.ValidatePosition(value);
            _centerOfMass = value;
        }
    }
    public Vector3 BoundsCenter => PhysCollmapGeomDraft.ToNumerics(_bounds.MidPoint);
    public Vector3 BoundsSize => PhysCollmapGeomDraft.ToNumerics(_bounds.HalfSize) * 2f;

    public PhysCollmapDraft Clone() => new(this);
    public bool Matches(PhysCollmapDraft other) =>
        other is not null && ReferenceEquals(_source, other._source) && Count == other.Count &&
        PhysCollmapGeomDraft.Same(_centerOfMass, other._centerOfMass) &&
        PhysCollmapGeomDraft.Same(BoundsCenter, other.BoundsCenter) &&
        PhysCollmapGeomDraft.Same(BoundsSize, other.BoundsSize) &&
        _geoms.Zip(other._geoms).All(pair => pair.First.Matches(pair.Second));

    public void ApplyEdits(PhysCollmapDraft original, PhysCollmapDraft working)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(working);
        if (!Matches(original))
            throw new InvalidOperationException("PhysCollmap changed after this edit was opened. Reopen the current draft.");
        if (!ReferenceEquals(_source, working._source))
            throw new ArgumentException("Drafts belong to different PhysCollmaps.", nameof(working));
        _geoms = working._geoms.Select(geom => geom.Clone()).ToList();
        _geomsView = _geoms.AsReadOnly();
        _bounds = CopyBounds(working._bounds);
        _centerOfMass = working._centerOfMass;
    }

    public void SetPosition(int index, Vector3 position) { _geoms[index].SetPosition(position); RecalculateBounds(); }
    public void SetSize(int index, Vector3 size) { _geoms[index].SetSize(size); RecalculateBounds(); }
    public void SetOrientation(int index, Matrix4x4 orientation) { _geoms[index].SetOrientation(orientation); RecalculateBounds(); }
    public int AddBox(Vector3 position, Vector3 size) => Add(PhysGeomShape.Box, position, size);
    public int AddCylinder(Vector3 position, Vector3 size) => Add(PhysGeomShape.Cylinder, position, size);

    public void Remove(int index)
    {
        _geoms.RemoveAt(index);
        RecalculateBounds();
    }

    private int Add(PhysGeomShape shape, Vector3 position, Vector3 size)
    {
        _geoms.Add(PhysCollmapGeomDraft.New(shape, position, size));
        RecalculateBounds();
        return _geoms.Count - 1;
    }

    internal PhysCollmapAsset ToAsset()
    {
        PhysGeomInfo[] geoms = _geoms.Select(geom => geom.ToGeom()).ToArray();
        return new PhysCollmapAsset
        {
            Name = Name,
            NamePointer = _source.NamePointer,
            Count = geoms.Length,
            GeomsPointer = geoms.Length == _source.Count ? _source.GeomsPointer : default,
            Geoms = geoms,
            Mass = new PhysMass
            {
                CenterOfMass = PhysCollmapGeomDraft.ToGame(_centerOfMass),
                MomentsOfInertia = _source.Mass.MomentsOfInertia,
                ProductsOfInertia = _source.Mass.ProductsOfInertia
            },
            Bounds = CopyBounds(_bounds)
        };
    }

    internal IReadOnlyList<AssetValidationIssue> Validate()
    {
        var issues = new List<AssetValidationIssue>();
        if (Name?.StartsWith(',') == true &&
            (Count != 0 || _centerOfMass != Vector3.Zero ||
             PhysCollmapGeomDraft.ToNumerics(_source.Mass.MomentsOfInertia) != Vector3.Zero ||
             PhysCollmapGeomDraft.ToNumerics(_source.Mass.ProductsOfInertia) != Vector3.Zero ||
             BoundsCenter != Vector3.Zero || BoundsSize != Vector3.Zero))
            issues.Add(Error(nameof(Name), "A referenced PhysCollmap must have a zero body."));
        if (!PhysCollmapGeomDraft.Finite(_centerOfMass))
            issues.Add(Error("Mass.CenterOfMass", "Center of mass must be finite."));
        if (!PhysCollmapGeomDraft.Finite(BoundsCenter) || !PhysCollmapGeomDraft.Finite(BoundsSize))
            issues.Add(Error("Bounds", "Bounds must be finite."));
        for (int index = 0; index < _geoms.Count; index++)
        {
            PhysCollmapGeomDraft geom = _geoms[index];
            if (!PhysCollmapGeomDraft.Finite(geom.Position) || !PhysCollmapGeomDraft.Finite(geom.Size))
                issues.Add(Error($"Geoms[{index}]", "Geometry position and size must be finite."));
        }
        return issues;
    }

    private static AssetValidationIssue Error(string path, string message) =>
        new(path, message, AssetValidationSeverity.Error);

    private void RecalculateBounds()
    {
        if (_geoms.Count == _source.Geoms.Count &&
            _geoms.Select((geom, index) => geom.IsUnchangedSourceRow(_source.Geoms[index])).All(same => same))
        {
            _bounds = CopyBounds(_source.Bounds);
            return;
        }
        if (_geoms.Count == 0)
        {
            _bounds = new Bounds();
            return;
        }
        Vector3 low = new(float.PositiveInfinity), high = new(float.NegativeInfinity);
        foreach (PhysCollmapGeomDraft geom in _geoms)
        {
            Vector3 half = geom.Size / 2f;
            Vector3 extents;
            if (geom.Shape == PhysGeomShape.Brush)
                extents = half;
            else if (geom.Shape == PhysGeomShape.Cylinder)
            {
                Vector3 axis = new(geom.Orientation.M31, geom.Orientation.M32, geom.Orientation.M33);
                extents = new Vector3(
                    half.X * MathF.Sqrt(MathF.Max(0, 1 - axis.X * axis.X)) + half.Z * MathF.Abs(axis.X),
                    half.X * MathF.Sqrt(MathF.Max(0, 1 - axis.Y * axis.Y)) + half.Z * MathF.Abs(axis.Y),
                    half.X * MathF.Sqrt(MathF.Max(0, 1 - axis.Z * axis.Z)) + half.Z * MathF.Abs(axis.Z));
            }
            else
            {
                Matrix4x4 m = geom.Orientation;
                extents = new Vector3(
                    MathF.Abs(m.M11) * half.X + MathF.Abs(m.M21) * half.Y + MathF.Abs(m.M31) * half.Z,
                    MathF.Abs(m.M12) * half.X + MathF.Abs(m.M22) * half.Y + MathF.Abs(m.M32) * half.Z,
                    MathF.Abs(m.M13) * half.X + MathF.Abs(m.M23) * half.Y + MathF.Abs(m.M33) * half.Z);
            }
            low = Vector3.Min(low, geom.Position - extents);
            high = Vector3.Max(high, geom.Position + extents);
        }
        _bounds = new Bounds
        {
            MidPoint = PhysCollmapGeomDraft.ToGame((low + high) / 2f),
            HalfSize = PhysCollmapGeomDraft.ToGame((high - low) / 2f)
        };
    }

    private static Bounds CopyBounds(Bounds value) => new()
    {
        MidPoint = value.MidPoint,
        HalfSize = value.HalfSize
    };
}

internal sealed class PhysCollmapAdapter : AssetAuthoringAdapter<PhysCollmapAsset, PhysCollmapDraft>
{
    public override XAssetType AssetType => XAssetType.PhysCollmap;
    public override PhysCollmapDraft CreateDraft(PhysCollmapAsset value) => new(value);
    public override PhysCollmapDraft CloneDraft(PhysCollmapDraft value) => value.Clone();
    public override PhysCollmapAsset CreateDefinition(PhysCollmapDraft value) => value.ToAsset();
    public override bool SemanticallyEquals(PhysCollmapDraft left, PhysCollmapDraft right) => left.Matches(right);
    public override IReadOnlyList<AssetValidationIssue> Validate(PhysCollmapDraft value) => value.Validate();
}
