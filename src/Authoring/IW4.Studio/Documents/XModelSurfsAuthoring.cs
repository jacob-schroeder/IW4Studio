using System.Buffers.Binary;
using System.Numerics;
using IW4.Game.Assets.XModel;
using IW4.Game.Codecs.XModel;
using IW4.Game.Math;
using IW4.Game.Zone;
using IW4.Formats.XModel;

namespace IW4.Studio.Documents;

/// <summary>A detached native surface. Translation is an offset from its source geometry;
/// scale is positive and applied about the source bounds center.</summary>
public sealed class XModelSurfaceDraft
{
    private readonly XSurface _original;
    private XSurface _geometry;
    private Vector3 _center;
    private Vector3 _size;
    private IReadOnlyList<Vector3> _positions = [];
    private IReadOnlyList<Vector3> _normals = [];
    private IReadOnlyList<Vector3> _tangents = [];
    private IReadOnlyList<ushort> _triangles = [];
    private Vector3 _translation;
    private Vector3 _scale = Vector3.One;

    internal XModelSurfaceDraft(XSurface surface)
    {
        _original = surface ?? throw new ArgumentNullException(nameof(surface));
        _geometry = surface;
        EditBlocker = Inspect(surface);
        CacheGeometry();
    }

    private XModelSurfaceDraft(XModelSurfaceDraft other)
    {
        _original = other._original;
        _geometry = other._geometry;
        _center = other._center;
        _size = other._size;
        _positions = other._positions;
        _normals = other._normals;
        _tangents = other._tangents;
        _triangles = other._triangles;
        _translation = other._translation;
        _scale = other._scale;
        EditBlocker = other.EditBlocker;
    }

    public bool CanEdit => EditBlocker is null;
    public string? EditBlocker { get; private set; }
    public Vector3 OriginalCenter => _center;
    public Vector3 OriginalSize => _size;
    public Vector3 Translation => _translation;
    public Vector3 Scale => _scale;
    public IReadOnlyList<Vector3> Positions => _positions;
    public IReadOnlyList<Vector3> Normals => _normals;
    public IReadOnlyList<ushort> TriangleIndices => _triangles;
    internal XSurface BaseSurface => _geometry;
    internal Vector3 TransformedPosition(int index) =>
        _positions[index] * _scale + _center * (Vector3.One - _scale) + _translation;

    internal XModelSurfaceDraft Clone() => new(this);

    internal void SetTranslation(Vector3 value)
    {
        RequireEditable();
        if (!Finite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Translation must be finite.");
        ValidateTransform(value, _scale);
        _translation = value;
    }

    internal void SetScale(Vector3 value)
    {
        RequireEditable();
        if (!Finite(value) || value.X <= 0 || value.Y <= 0 || value.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Scale must be positive and finite on every axis.");
        ValidateTransform(_translation, value);
        _scale = value;
    }

    internal void ReplaceFrom(XModelSurfaceDraft donor)
    {
        ArgumentNullException.ThrowIfNull(donor);
        RequireEditable();
        donor.RequireEditable();
        XSurface target = _original;
        XSurface source = donor.ToSurface();
        bool rigid = target.DeformedRaw == 0 && source.DeformedRaw == 0 &&
            target.VertListCount == 1 && source.VertListCount == 1 &&
            target.VertList[0].BoneOffset == source.VertList[0].BoneOffset &&
            target.VertList[0].VertCount == target.VertCount &&
            source.VertList[0].VertCount == source.VertCount &&
            target.VertList[0].TriOffset == 0 && source.VertList[0].TriOffset == 0 &&
            target.VertList[0].TriCount == target.TriCount &&
            source.VertList[0].TriCount == source.TriCount &&
            target.VertexInfo.VertsBlend.Count == 0 && source.VertexInfo.VertsBlend.Count == 0;
        bool identicalSkinning = target.VertCount == source.VertCount &&
            target.TriCount == source.TriCount &&
            SameVertexInfo(target.VertexInfo, source.VertexInfo) &&
            target.VertListCount == source.VertListCount &&
            target.VertList.Zip(source.VertList).All(pair =>
                pair.First.BoneOffset == pair.Second.BoneOffset &&
                pair.First.VertCount == pair.Second.VertCount &&
                pair.First.TriOffset == pair.Second.TriOffset &&
                pair.First.TriCount == pair.Second.TriCount);
        if (target.DeformedRaw != source.DeformedRaw || target.TileMode != source.TileMode ||
            target.StreamFlags != source.StreamFlags || !target.PartBits.SequenceEqual(source.PartBits) ||
            !(rigid || identicalSkinning))
        {
            throw new InvalidOperationException("Choose a surface attached to the same bones. The selected surface has different bone attachment, part bits, or vertex stream layout. Different triangle counts are supported for a single rigid bone.");
        }
        XSurface replacement = CopyWithGeometry(target, source);
        if (target.VertList.Any(row => row.CollisionTree is not null) &&
            replacement.VertList.Any(row => row.CollisionTree is null))
        {
            string? blocker = null;
            if (!rigid || !XModelCollisionTreeCompiler.TryAttach(
                    replacement, "Replacement surface", out replacement, out blocker))
                throw new InvalidOperationException(blocker ??
                    "Replacement cannot rebuild the target's collision tree for this skinning layout.");
        }
        if (Inspect(replacement) is { } invalid)
            throw new InvalidOperationException($"Replacement surface is incompatible: {invalid}");
        _geometry = replacement;
        CacheGeometry();
        _translation = Vector3.Zero;
        _scale = Vector3.One;
        EditBlocker = null;
    }

    internal XSurface ToSurface() =>
        _translation == Vector3.Zero && _scale == Vector3.One
            ? _geometry
            : Transform(_geometry, _center, _translation, _scale);

    internal bool HasSameStagedTransform(XModelSurfaceDraft other) =>
        ReferenceEquals(_geometry, other._geometry) &&
        _translation == other._translation && _scale == other._scale;

    internal bool Matches(XModelSurfaceDraft other)
    {
        if (!ReferenceEquals(_original, other._original)) return false;
        if (ReferenceEquals(_geometry, other._geometry))
            return HasSameStagedTransform(other);
        XSurface x = ToSurface(), y = other.ToSurface();
        return x.Verts0.SequenceEqual(y.Verts0) && x.Verts1.SequenceEqual(y.Verts1) &&
            x.TriIndices.SequenceEqual(y.TriIndices) && SameTrees(x, y);
    }

    private void CacheGeometry()
    {
        var positions = new Vector3[_geometry.VertCount];
        var normals = new Vector3[_geometry.VertCount];
        var tangents = new Vector3[_geometry.VertCount];
        for (int index = 0; index < positions.Length; index++)
        {
            XSurfaceVertexCodec.TryReadPosition(_geometry.Verts0, index, out positions[index]);
            XSurfaceVertexCodec.TryReadNormal(_geometry.Verts1, index, out normals[index]);
            XSurfaceVertexCodec.TryReadTangent(_geometry.Verts1, index, out tangents[index]);
        }
        _positions = Array.AsReadOnly(positions);
        _normals = Array.AsReadOnly(normals);
        _tangents = Array.AsReadOnly(tangents);
        _triangles = Array.AsReadOnly(_geometry.TriIndices.ToArray());
        (_center, _size) = Extents(positions);
    }

    private void ValidateTransform(Vector3 translation, Vector3 scale)
    {
        Vector3 shift = _center * (Vector3.One - scale) + translation;
        if (!Finite(shift))
            throw new ArgumentOutOfRangeException(nameof(scale), "Transform exceeds the finite vertex range.");
        foreach (Vector3 position in _positions)
            if (!Finite(position * scale + shift))
                throw new ArgumentOutOfRangeException(nameof(scale), "Transform exceeds the finite vertex range.");
        if (scale.X != scale.Y || scale.Y != scale.Z)
            for (int index = 0; index < _normals.Count; index++)
            {
                Vector3 normal = Normalize(_normals[index] / scale);
                Vector3 tangent = _tangents[index] * scale;
                _ = Normalize(tangent - Vector3.Dot(tangent, normal) * normal);
            }
        foreach (XRigidVertList rigid in _geometry.VertList)
            if (rigid.CollisionTree is { } tree)
            {
                Vector3 trans = ToVector(tree.Trans) * scale - shift;
                Vector3 quantization = ToVector(tree.Scale) / scale;
                if (!Finite(trans) || !ValidScale(quantization.X) ||
                    !ValidScale(quantization.Y) || !ValidScale(quantization.Z))
                    throw new ArgumentOutOfRangeException(nameof(scale),
                        "Collision-tree transform exceeds its representable range.");
            }
    }

    private void RequireEditable()
    {
        if (EditBlocker is { } reason)
            throw new NotSupportedException(reason);
    }

    private static string? Inspect(XSurface surface)
    {
        if (surface.Verts0.Count != surface.VertCount * XSurfaceVertexCodec.StreamStride ||
            surface.Verts1.Count != surface.VertCount * XSurfaceVertexCodec.StreamStride ||
            surface.TriIndices.Count != surface.TriCount * 3 ||
            surface.PartBits.Count != 6 || surface.VertList.Count != surface.VertListCount ||
            surface.VertexInfo.VertsBlend.Count !=
                surface.VertexInfo.Blend0 + surface.VertexInfo.Blend1 * 3 +
                surface.VertexInfo.Blend2 * 5 + surface.VertexInfo.Blend3 * 7)
            return "Surface geometry or skinning streams are incomplete.";
        for (int index = 0; index < surface.VertCount; index++)
            if (!XSurfaceVertexCodec.TryReadPosition(surface.Verts0, index, out _) ||
                !XSurfaceVertexCodec.TryReadNormal(surface.Verts1, index, out _) ||
                !XSurfaceVertexCodec.TryReadTangent(surface.Verts1, index, out _))
                return $"Vertex {index} has an invalid position or direction.";
        if (surface.TriIndices.Any(index => index >= surface.VertCount))
            return "Triangle indices leave the surface vertex range.";
        foreach (XRigidVertList rigid in surface.VertList)
        {
            if (rigid.CollisionTree is null && rigid.CollisionTreePointer.Value != 0)
                return "A collision tree is unresolved.";
            if (rigid.CollisionTree is { } tree &&
                !XModelCollisionTreeValidator.TryValidate(tree, rigid, surface, "Surface", out string? blocker))
                return blocker ?? "A collision tree is invalid.";
        }
        return null;
    }

    private static (Vector3 Center, Vector3 Size) Extents(IReadOnlyList<Vector3> positions)
    {
        if (positions.Count == 0)
            return (Vector3.Zero, Vector3.Zero);
        Vector3 min = positions[0], max = positions[0];
        for (int index = 1; index < positions.Count; index++)
        {
            min = Vector3.Min(min, positions[index]);
            max = Vector3.Max(max, positions[index]);
        }
        return ((min + max) * 0.5f, max - min);
    }

    private static XSurface Transform(XSurface surface, Vector3 center, Vector3 translation, Vector3 scale)
    {
        Vector3 shift = center * (Vector3.One - scale) + translation;
        if (!Finite(shift)) throw new ArgumentOutOfRangeException(nameof(scale), "Transform exceeds the finite vertex range.");
        byte[] verts0 = surface.Verts0.ToArray(), verts1 = surface.Verts1.ToArray();
        bool changeDirections = scale.X != scale.Y || scale.Y != scale.Z;
        for (int index = 0; index < surface.VertCount; index++)
        {
            XSurfaceVertexCodec.TryReadPosition(surface.Verts0, index, out Vector3 oldPosition);
            Vector3 position = oldPosition * scale + shift;
            if (!Finite(position)) throw new ArgumentOutOfRangeException(nameof(scale), "Transform exceeds the finite vertex range.");
            int offset = index * XSurfaceVertexCodec.StreamStride;
            WriteSingle(verts0, offset, position.X);
            WriteSingle(verts0, offset + 4, position.Y);
            WriteSingle(verts0, offset + 8, position.Z);
            if (!changeDirections) continue;
            XSurfaceVertexCodec.TryReadNormal(surface.Verts1, index, out Vector3 oldNormal);
            XSurfaceVertexCodec.TryReadTangent(surface.Verts1, index, out Vector3 oldTangent);
            Vector3 normal = Normalize(oldNormal / scale);
            Vector3 tangent = oldTangent * scale;
            tangent = Normalize(tangent - Vector3.Dot(tangent, normal) * normal);
            BinaryPrimitives.WriteUInt32BigEndian(verts1.AsSpan(offset + 8), XSurfaceVertexCodec.EncodeDirection(normal));
            BinaryPrimitives.WriteUInt32BigEndian(verts1.AsSpan(offset + 12), XSurfaceVertexCodec.EncodeDirection(tangent));
        }
        XRigidVertList[] rigidRows = surface.VertList.Select(row => new XRigidVertList
        {
            BoneOffset = row.BoneOffset, VertCount = row.VertCount, TriOffset = row.TriOffset,
            TriCount = row.TriCount, CollisionTreePointer = row.CollisionTreePointer,
            CollisionTree = row.CollisionTree is null ? null : TransformTree(row.CollisionTree, shift, scale)
        }).ToArray();
        return CopySurface(surface, verts0, verts1, surface.TriIndices, rigidRows);
    }

    private static XSurfaceCollisionTree TransformTree(XSurfaceCollisionTree tree, Vector3 shift, Vector3 scale)
    {
        Vector3 trans = ToVector(tree.Trans) * scale - shift;
        Vector3 quantization = ToVector(tree.Scale) / scale;
        if (!Finite(trans) || !(Finite(quantization) ||
              (ValidScale(quantization.X) && ValidScale(quantization.Y) && ValidScale(quantization.Z))))
            throw new ArgumentOutOfRangeException(nameof(scale), "Collision-tree transform exceeds its representable range.");
        return new XSurfaceCollisionTree
        {
            RuntimeAddress = tree.RuntimeAddress, Trans = ToGame(trans), Scale = ToGame(quantization),
            NodeCount = tree.NodeCount, NodesPointer = tree.NodesPointer, NodesRuntimeAddress = tree.NodesRuntimeAddress,
            Nodes = tree.Nodes, LeafCount = tree.LeafCount, LeafsPointer = tree.LeafsPointer,
            LeafsRuntimeAddress = tree.LeafsRuntimeAddress, Leafs = tree.Leafs
        };
    }

    private static XSurface CopyWithGeometry(XSurface target, XSurface source) => new()
    {
        TileMode = target.TileMode, DeformedRaw = target.DeformedRaw,
        StreamFlags = target.StreamFlags, Pad03 = target.Pad03,
        VertCount = source.VertCount, TriCount = source.TriCount,
        TriIndices = Array.AsReadOnly(source.TriIndices.ToArray()),
        VertexInfo = new XSurfaceVertexInfo
        {
            Blend0 = source.VertexInfo.Blend0, Blend1 = source.VertexInfo.Blend1,
            Blend2 = source.VertexInfo.Blend2, Blend3 = source.VertexInfo.Blend3,
            VertsBlend = Array.AsReadOnly(source.VertexInfo.VertsBlend.ToArray())
        },
        Verts0 = Array.AsReadOnly(source.Verts0.ToArray()), Vb0 = target.Vb0,
        Verts1 = Array.AsReadOnly(source.Verts1.ToArray()), Vb1 = target.Vb1,
        VertListCount = source.VertListCount,
        VertList = Array.AsReadOnly(source.VertList.Select((row, index) => new XRigidVertList
        {
            BoneOffset = target.VertList[index].BoneOffset,
            VertCount = row.VertCount, TriOffset = row.TriOffset, TriCount = row.TriCount,
            CollisionTree = row.CollisionTree is null ? null : DetachTree(row.CollisionTree)
        }).ToArray()),
        IndexBuffer = target.IndexBuffer, PartBits = target.PartBits.ToArray()
    };

    internal static XSurface DetachForNewCollection(XSurface source) =>
        CopyWithGeometry(source, source);

    private static XSurfaceCollisionTree DetachTree(XSurfaceCollisionTree value) => new()
    {
        Trans = value.Trans, Scale = value.Scale, NodeCount = value.NodeCount,
        Nodes = value.Nodes.ToArray(), LeafCount = value.LeafCount, Leafs = value.Leafs.ToArray()
    };

    private static XSurface CopySurface(XSurface value, IReadOnlyList<byte> verts0,
        IReadOnlyList<byte> verts1, IReadOnlyList<ushort> indices, IReadOnlyList<XRigidVertList> rigid) => new()
    {
        TileMode = value.TileMode, DeformedRaw = value.DeformedRaw, StreamFlags = value.StreamFlags,
        Pad03 = value.Pad03, VertCount = value.VertCount, TriCount = value.TriCount,
        TriIndicesPointer = value.TriIndicesPointer, TriIndices = Array.AsReadOnly(indices.ToArray()),
        VertexInfo = value.VertexInfo, Verts0Pointer = value.Verts0Pointer,
        Verts0 = Array.AsReadOnly(verts0.ToArray()), Vb0 = value.Vb0,
        Verts1Pointer = value.Verts1Pointer, Verts1 = Array.AsReadOnly(verts1.ToArray()), Vb1 = value.Vb1,
        VertListCount = value.VertListCount, VertListPointer = value.VertListPointer,
        VertList = Array.AsReadOnly(rigid.ToArray()), IndexBuffer = value.IndexBuffer,
        PartBits = value.PartBits
    };

    private static bool SameVertexInfo(XSurfaceVertexInfo a, XSurfaceVertexInfo b) =>
        a.Blend0 == b.Blend0 && a.Blend1 == b.Blend1 && a.Blend2 == b.Blend2 &&
        a.Blend3 == b.Blend3 && a.VertsBlend.SequenceEqual(b.VertsBlend);

    private static bool SameTrees(XSurface a, XSurface b) =>
        a.VertList.Zip(b.VertList).All(pair =>
            pair.First.CollisionTree == pair.Second.CollisionTree ||
            pair.First.CollisionTree is { } x && pair.Second.CollisionTree is { } y &&
            x.Trans.Equals(y.Trans) && x.Scale.Equals(y.Scale) &&
            x.Nodes.SequenceEqual(y.Nodes) && x.Leafs.SequenceEqual(y.Leafs));

    private static Vector3 Normalize(Vector3 value)
    {
        float length = value.Length();
        if (!Finite(value) || !float.IsFinite(length) || length <= 0f)
            throw new ArgumentOutOfRangeException(nameof(value), "Surface direction collapses under the selected scale.");
        Vector3 unit = value / length;
        if (!Finite(unit) || unit.LengthSquared() <= 0f)
            throw new ArgumentOutOfRangeException(nameof(value), "Surface direction collapses under the selected scale.");
        return unit;
    }

    private static void WriteSingle(Span<byte> bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32BigEndian(bytes[offset..], BitConverter.SingleToInt32Bits(value));
    private static bool ValidScale(float value) => float.IsFinite(value) || float.IsPositiveInfinity(value);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static Vector3 ToVector(Vec3 value) => new(value.X, value.Y, value.Z);
    private static Vec3 ToGame(Vector3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };
}

/// <summary>A detached XModelSurfs collection. Surface order and material slots are stable.</summary>
public sealed class XModelSurfsDraft
{
    private readonly XModelSurfsAsset _source;
    private IReadOnlyList<XModelSurfaceDraft>? _baselineSurfaces;
    private List<XModelSurfaceDraft>? _surfaces;
    private IReadOnlyList<XModelSurfaceDraft>? _surfacesView;
    private IReadOnlyList<uint> _partBits;

    public XModelSurfsDraft(XModelSurfsAsset source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (source.NumSurfs != source.Surfaces.Count || source.PartBits.Count != 6)
            throw new InvalidDataException("XModelSurfs has incomplete surface rows.");
        _partBits = source.PartBits.ToArray();
    }

    private XModelSurfsDraft(XModelSurfsDraft other)
    {
        _source = other._source;
        _baselineSurfaces = other._baselineSurfaces;
        if (other._surfaces is { } rows)
        {
            _surfaces = rows.Select(surface => surface.Clone()).ToList();
            _surfacesView = _surfaces.AsReadOnly();
        }
        _partBits = other._partBits.ToArray();
    }

    internal XModelSurfsAsset Source => _source;
    public IReadOnlyList<XModelSurfaceDraft> Surfaces => _surfacesView ?? GetSurfaces();
    private IReadOnlyList<XModelSurfaceDraft> GetSurfaces()
    {
        if (_surfacesView is not null) return _surfacesView;
        _surfaces = _source.Surfaces.Select(surface => new XModelSurfaceDraft(surface)).ToList();
        _baselineSurfaces = _surfaces.Select(surface => surface.Clone()).ToArray();
        return _surfacesView = _surfaces.AsReadOnly();
    }
    internal IEnumerable<int> ChangedSurfaceIndices()
    {
        if (_surfaces is null || _baselineSurfaces is null) yield break;
        for (int index = 0; index < _surfaces.Count; index++)
            if (index >= _baselineSurfaces.Count ||
                !_surfaces[index].HasSameStagedTransform(_baselineSurfaces[index]))
                yield return index;
    }
    public XModelSurfsDraft Clone() => new(this);
    public bool Matches(XModelSurfsDraft other)
    {
        if (other is null || !ReferenceEquals(_source, other._source) ||
            !_partBits.SequenceEqual(other._partBits)) return false;
        if (_surfaces is null && other._surfaces is null) return true;
        if (_surfaces is null)
            return other._surfaces!.Count == _source.Surfaces.Count &&
                other._surfaces.Zip(other._baselineSurfaces!).All(pair =>
                    pair.First.Matches(pair.Second));
        if (other._surfaces is null)
            return _surfaces.Count == _source.Surfaces.Count &&
                _surfaces.Zip(_baselineSurfaces!).All(pair =>
                    pair.First.Matches(pair.Second));
        return _surfaces.Count == other._surfaces.Count &&
            _surfaces.Zip(other._surfaces).All(pair => pair.First.Matches(pair.Second));
    }

    public void ApplyEdits(XModelSurfsDraft baseline, XModelSurfsDraft working)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(working);
        if (!Matches(baseline))
            throw new InvalidOperationException("XModelSurfs changed after this edit was opened. Reopen the current draft.");
        if (!ReferenceEquals(_source, working._source))
            throw new ArgumentException("Drafts belong to different XModelSurfs collections.", nameof(working));
        _surfaces = working._surfaces?.Select(surface => surface.Clone()).ToList();
        _surfacesView = _surfaces?.AsReadOnly();
        _baselineSurfaces = working._baselineSurfaces;
        _partBits = working._partBits.ToArray();
    }

    public void SetTranslation(int index, Vector3 translation) => GetSurfaces()[index].SetTranslation(translation);
    public void SetScale(int index, Vector3 scale) => GetSurfaces()[index].SetScale(scale);

    internal void ReplaceSurface(int targetIndex, XModelSurfsDraft donor, int donorIndex, bool sharedRig)
    {
        ArgumentNullException.ThrowIfNull(donor);
        if (!ReferenceEquals(_source, donor._source) && !sharedRig)
            throw new InvalidOperationException("Choose a surface from this collection, or use the workspace replacement picker for another LOD of the same model. The source skeleton is not established here.");
        GetSurfaces()[targetIndex].ReplaceFrom(donor.GetSurfaces()[donorIndex]);
    }

    /// <summary>Fills a newly created empty collection from a complete donor collection.</summary>
    public void SeedFrom(XModelSurfsDraft donor)
    {
        ArgumentNullException.ThrowIfNull(donor);
        if (_source.NumSurfs != 0 || _source.Surfaces.Count != 0 || GetSurfaces().Count != 0)
            throw new InvalidOperationException("Only a new empty XModelSurfs collection can be seeded.");
        IReadOnlyList<XModelSurfaceDraft> donorRows = donor.GetSurfaces();
        if (donorRows.Count == 0 || donorRows.Count > ushort.MaxValue ||
            donorRows.Any(surface => !surface.CanEdit))
            throw new InvalidOperationException("The donor collection has no complete editable surfaces.");
        _surfaces = donorRows.Select(surface =>
        {
            XSurface geometry = surface.ToSurface();
            return new XModelSurfaceDraft(XModelSurfaceDraft.DetachForNewCollection(geometry));
        }).ToList();
        _surfacesView = _surfaces.AsReadOnly();
        _partBits = donor._partBits.ToArray();
    }

    public XModelSurfsAsset ToAsset() => _surfaces is null ? _source : new()
    {
        Offset = _source.Offset, RuntimeAddress = _source.RuntimeAddress,
        NamePointer = _source.NamePointer, Name = _source.Name, SurfsPointer = _source.SurfsPointer,
        NumSurfs = checked((ushort)_surfaces.Count), Pad0A = _source.Pad0A,
        PartBits = _partBits.ToArray(),
        Surfaces = _surfaces.Select(surface => surface.ToSurface()).ToArray()
    };

    /// <summary>Preserved XModel bounds remain authoritative for independent surface edits.
    /// A vertex outside those bounds requires an XModel assembly edit.</summary>
    internal void ValidateDependentBounds(FastFileEditingSession session)
    {
        IReadOnlyList<XModelSurfsUsage> usages = XModelSurfsUsage.Find(session, _source);
        if (usages.Count == 0) return;
        int[] changed = ChangedSurfaceIndices().ToArray();
        if (changed.Length == 0) return;
        const float tolerance = 0.00001f;
        foreach (XModelSurfsUsage usage in usages)
        foreach (int surfaceIndex in changed)
        {
            XModelAsset model = usage.Model;
            if (session.CanExpandSurfaceOwner(model))
                continue;
            XModelSurfaceDraft row = Surfaces[surfaceIndex];
            XSurface surface = row.BaseSurface;
            if (!XModelSurfaceSkinningProjector.TryProject(
                    surface, model.NumBones, out IReadOnlyList<IReadOnlyList<XModelExportBoneWeight>> weights,
                    out string blocker))
                throw new InvalidOperationException($"XModel '{model.Name}' LOD {usage.LodIndex} surface {surfaceIndex}: {blocker}");
            for (int vertexIndex = 0; vertexIndex < surface.VertCount; vertexIndex++)
            {
                Vector3 position = row.TransformedPosition(vertexIndex);
                if (!InBounds(position, model.Bounds, tolerance) ||
                    !float.IsFinite(model.Radius) || model.Radius < 0 ||
                    position.Length() > model.Radius + tolerance)
                    throw new InvalidOperationException($"XModel '{model.Name}' LOD {usage.LodIndex} surface {surfaceIndex} vertex {vertexIndex} exceeds preserved model bounds or radius. Edit the XModel assembly to rebuild derived bounds.");
                foreach (XModelExportBoneWeight influence in weights[vertexIndex])
                {
                    int bone = influence.BoneIndex;
                    if (bone < 0 || bone >= model.BaseMat.Count || bone >= model.BoneInfo.Count)
                        throw new InvalidOperationException($"XModel '{model.Name}' has no retained bounds for bone {bone}.");
                    var bind = model.BaseMat[bone];
                    Quaternion rotation = new(bind.Quat.X, bind.Quat.Y, bind.Quat.Z, bind.Quat.W);
                    if (!Finite(new Vector3(rotation.X, rotation.Y, rotation.Z)) ||
                        !float.IsFinite(rotation.W) || rotation.LengthSquared() <= 0f)
                        throw new InvalidOperationException($"XModel '{model.Name}' bone {bone} has an invalid bind rotation.");
                    Vector3 local = Vector3.Transform(position -
                        new Vector3(bind.Trans.X, bind.Trans.Y, bind.Trans.Z),
                        Quaternion.Inverse(Quaternion.Normalize(rotation)));
                    XBoneInfo info = model.BoneInfo[bone];
                    if (!InBounds(local, info.Bounds, tolerance) ||
                        !float.IsFinite(info.RadiusSquared) || info.RadiusSquared < 0 ||
                        local.Length() > MathF.Sqrt(info.RadiusSquared) + tolerance)
                        throw new InvalidOperationException($"XModel '{model.Name}' LOD {usage.LodIndex} surface {surfaceIndex} vertex {vertexIndex} exceeds preserved bone {bone} bounds. Edit the XModel assembly to rebuild derived bounds.");
                }
            }
        }
    }

    private static bool InBounds(Vector3 point, Bounds bounds, float tolerance) =>
        MathF.Abs(point.X - bounds.MidPoint.X) <= bounds.HalfSize.X + tolerance &&
        MathF.Abs(point.Y - bounds.MidPoint.Y) <= bounds.HalfSize.Y + tolerance &&
        MathF.Abs(point.Z - bounds.MidPoint.Z) <= bounds.HalfSize.Z + tolerance;
    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

internal sealed class XModelSurfsAdapter : AssetAuthoringAdapter<XModelSurfsAsset, XModelSurfsDraft>
{
    public override XAssetType AssetType => XAssetType.XModelSurfs;
    public override XModelSurfsDraft CreateDraft(XModelSurfsAsset value) => new(value);
    public override XModelSurfsDraft CloneDraft(XModelSurfsDraft value) => value.Clone();
    public override XModelSurfsAsset CreateDefinition(XModelSurfsDraft value) => value.ToAsset();
    public override bool SemanticallyEquals(XModelSurfsDraft left, XModelSurfsDraft right) => left.Matches(right);
}

public sealed partial class AssetEditorSession
{
    /// <summary>Checks preserved owner bounds before an interactive surface edit is applied.</summary>
    public string? ValidateXModelSurfsBounds(XModelSurfsDraft candidate)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(candidate);
        if (Definition is not XModelSurfsAsset ||
            !ReferenceEquals(candidate.Source, Definition))
            throw new ArgumentException("The candidate belongs to another XModelSurfs editor.", nameof(candidate));
        try
        {
            candidate.ValidateDependentBounds(_session);
            return null;
        }
        catch (InvalidOperationException error)
        {
            return error.Message;
        }
    }

    /// <summary>Stages a replacement from this workspace. Separate collections must
    /// be referenced by the same XModel, which proves their bone-index identity.</summary>
    public XModelSurfsDraft StageXModelSurfaceReplacement(
        XModelSurfsDraft working, int targetIndex, XModelSurfsDraft donor, int donorIndex)
    {
        ThrowIfClosed();
        if (!CanEdit || Definition is not XModelSurfsAsset ||
            _adapter.AssetType != XAssetType.XModelSurfs)
            throw new InvalidOperationException("This XModelSurfs collection is not editable.");
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(donor);
        if (!ReferenceEquals(working.Source, Definition))
            throw new ArgumentException("The working draft belongs to another XModelSurfs editor.", nameof(working));
        bool sameCollection = ReferenceEquals(working.Source, donor.Source);
        bool sharedRig = sameCollection;
        if (!sameCollection)
        {
            IReadOnlyList<XModelSurfsUsage> targetUses =
                XModelSurfsUsage.Find(_session, working.Source);
            IReadOnlyList<XModelSurfsUsage> donorUses =
                XModelSurfsUsage.Find(_session, donor.Source);
            sharedRig = targetUses.Any(target => donorUses.Any(source =>
                ReferenceEquals(target.Model, source.Model) ||
                target.Model.RuntimeAddress?.AssetPoolAddress is { } address &&
                source.Model.RuntimeAddress?.AssetPoolAddress == address));
            if (!sharedRig)
                throw new InvalidOperationException("Choose a surface from another LOD of this model. The selected collection uses a different or unknown skeleton.");
        }
        XModelSurfsDraft staged = working.Clone();
        staged.ReplaceSurface(targetIndex, donor, donorIndex, sharedRig);
        return staged;
    }
}
