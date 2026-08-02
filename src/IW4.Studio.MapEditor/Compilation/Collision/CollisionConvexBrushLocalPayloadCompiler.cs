using System.Collections.ObjectModel;
using IW4.Assets.Assets.Physics;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Consumer face ordinals used by one compiled brush. The order is part of
/// the local CBrush contract: axial faces occupy ordinals 0 through 5 and
/// non-axial CBrushSide rows follow at ordinal 6.
/// </summary>
public enum CollisionBrushAxialFace
{
    NegativeX = 0,
    PositiveX = 1,
    NegativeY = 2,
    PositiveY = 3,
    NegativeZ = 4,
    PositiveZ = 5
}

/// <summary>
/// Semantic binding for one compiled brush face. Axial faces bind to the
/// fixed CBrush arrays; non-axial faces bind to one local plane and
/// CBrushSide row.
/// </summary>
public sealed record CollisionCompiledConvexBrushFaceBinding(
    int UnifiedFaceOrdinal,
    CollisionBrushAxialFace? AxialFace,
    int? PlaneOrdinal,
    ushort MaterialOrdinal,
    byte FirstAdjacentSideOffset,
    byte EdgeCount);

/// <summary>
/// Detached, source-local payload for one canonical convex brush. It contains
/// no ClipMapAsset, packed pointer, spatial structure, checksum, or
/// persistence authority. A later map-wide assembler assigns root ranges,
/// owner-specific world/submodel reachability, and aliases these local rows
/// into them.
/// </summary>
public sealed class CollisionCompiledConvexBrushLocalPayload
{
    private readonly IReadOnlyList<CPlane> _planes;
    private readonly IReadOnlyList<CollisionAuthoredMaterialOrdinal>
        _materialBindings;
    private readonly IReadOnlyList<CBrushSide> _brushSides;
    private readonly IReadOnlyList<byte> _brushEdges;
    private readonly IReadOnlyList<CollisionCompiledConvexBrushFaceBinding>
        _faceBindings;

    internal CollisionCompiledConvexBrushLocalPayload(
        MapObjectId sourceObjectId,
        IEnumerable<CPlane> planes,
        IEnumerable<CollisionAuthoredMaterialOrdinal> materialBindings,
        IEnumerable<CBrushSide> brushSides,
        IEnumerable<byte> brushEdges,
        CBrush brush,
        MapBounds bounds,
        uint contents,
        IEnumerable<CollisionCompiledConvexBrushFaceBinding> faceBindings,
        CollisionBrushReferenceContract referenceContract)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        ArgumentNullException.ThrowIfNull(planes);
        ArgumentNullException.ThrowIfNull(materialBindings);
        ArgumentNullException.ThrowIfNull(brushSides);
        ArgumentNullException.ThrowIfNull(brushEdges);
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(faceBindings);

        SourceObjectId = sourceObjectId;
        _planes = new ReadOnlyCollection<CPlane>(planes.ToArray());
        _materialBindings =
            new ReadOnlyCollection<CollisionAuthoredMaterialOrdinal>(
                materialBindings.ToArray());
        _brushSides =
            new ReadOnlyCollection<CBrushSide>(brushSides.ToArray());
        _brushEdges =
            new ReadOnlyCollection<byte>(brushEdges.ToArray());
        _faceBindings =
            new ReadOnlyCollection<CollisionCompiledConvexBrushFaceBinding>(
                faceBindings.ToArray());
        Brush = brush;
        Bounds = bounds;
        Contents = contents;
        ReferenceContract = referenceContract;
    }

    public MapObjectId SourceObjectId { get; }
    public IReadOnlyList<CPlane> Planes => _planes;
    public IReadOnlyList<CollisionAuthoredMaterialOrdinal> MaterialBindings =>
        _materialBindings;
    public IReadOnlyList<CBrushSide> BrushSides => _brushSides;
    public IReadOnlyList<byte> BrushEdges => _brushEdges;
    public CBrush Brush { get; }
    public MapBounds Bounds { get; }
    public uint Contents { get; }
    public IReadOnlyList<CollisionCompiledConvexBrushFaceBinding>
        FaceBindings => _faceBindings;
    public CollisionBrushReferenceContract ReferenceContract { get; }
}

/// <summary>
/// First bounded M3 brush compiler. It accepts canonical standalone brushes
/// that retain an explicit face at every axial bound. This makes all six
/// axial material bindings source-authored rather than inferred. General
/// brushes that need synthesized axial bevel faces remain fail-closed.
/// </summary>
public static class CollisionConvexBrushLocalPayloadCompiler
{
    private const int AxialFaceCount = 6;
    private const byte NonAxialPlaneType = 3;
    private const ushort NoGlassPieceIndex = 0;

    private static readonly IReadOnlyList<byte> CanonicalPlanePadding =
        Array.AsReadOnly(new byte[] { 0, 0 });

    public static CollisionCompiledConvexBrushLocalPayload CompileStandalone(
        AuthoredConvexBrushCollisionSource source,
        CollisionAuthoredMaterialOrdinalPlan materialOrdinals)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(materialOrdinals);
        if (source.Ownership.Category !=
            CollisionOwnershipCategory.StandaloneWorld)
        {
            throw new ArgumentException(
                "The standalone brush compiler accepts only explicit " +
                "standalone-world collision ownership.",
                nameof(source));
        }

        return CompileLocal(source, materialOrdinals);
    }

    /// <summary>
    /// Builds only the owner-local brush payload used by the input projector
    /// and later aggregate compiler. Brush-model ownership still requires
    /// cmodel/leaf construction before it can enter a structural candidate.
    /// </summary>
    internal static CollisionCompiledConvexBrushLocalPayload CompileLocal(
        AuthoredConvexBrushCollisionSource source,
        CollisionAuthoredMaterialOrdinalPlan materialOrdinals)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(materialOrdinals);

        ValidateUniformContents(source);

        AuthoredConvexBrushFace?[] axialFaces =
            new AuthoredConvexBrushFace?[AxialFaceCount];
        var nonAxialFaces = new List<AuthoredConvexBrushFace>();
        foreach (AuthoredConvexBrushFace face in source.Faces)
        {
            CollisionBrushAxialFace? axialFace =
                ClassifyAxialFace(face.Plane.Normal);
            if (axialFace is null)
            {
                nonAxialFaces.Add(face);
                continue;
            }

            int ordinal = (int)axialFace.Value;
            if (axialFaces[ordinal] is not null)
            {
                throw new InvalidDataException(
                    $"Collision brush {source.ObjectId} has more than one " +
                    $"{axialFace} face.");
            }
            axialFaces[ordinal] = face;
        }

        CollisionBrushAxialFace[] missingAxialFaces =
            Enum.GetValues<CollisionBrushAxialFace>()
                .Where(value => axialFaces[(int)value] is null)
                .ToArray();
        if (missingAxialFaces.Length != 0)
        {
            throw new NotSupportedException(
                $"Collision brush {source.ObjectId} requires synthesized " +
                $"axial bevel faces ({string.Join(", ", missingAxialFaces)}). " +
                "The bounded M3 compiler will not infer their materials.");
        }

        ValidateAxialBounds(source, axialFaces);
        nonAxialFaces.Sort(CompareNonAxialFaces);

        AuthoredConvexBrushFace[] compiledFaceOrder =
        [
            .. axialFaces.Select(value => value!),
            .. nonAxialFaces
        ];
        if (compiledFaceOrder.Length > byte.MaxValue + 1)
        {
            throw new OverflowException(
                $"Collision brush {source.ObjectId} has " +
                $"{compiledFaceOrder.Length} faces, but adjacency bytes can " +
                "address at most 256 unified faces.");
        }
        if (nonAxialFaces.Count > ushort.MaxValue)
        {
            throw new OverflowException(
                $"Collision brush {source.ObjectId} has too many non-axial " +
                "CBrushSide rows.");
        }

        var compiledOrdinals =
            new Dictionary<AuthoredConvexBrushFace, int>(
                ReferenceEqualityComparer.Instance);
        for (int index = 0; index < compiledFaceOrder.Length; index++)
            compiledOrdinals.Add(compiledFaceOrder[index], index);

        IReadOnlyDictionary<
            CollisionGeometryValidation.UndirectedEdge,
            IReadOnlyList<AuthoredConvexBrushFace>>? edgeOwners =
            nonAxialFaces.Count == 0
                ? null
                : BuildEdgeOwners(compiledFaceOrder);
        var adjacency = new byte[compiledFaceOrder.Length][];
        var adjacencyStarts = new byte[compiledFaceOrder.Length];
        int adjacencyByteCount = 0;
        for (int faceOrdinal = 0;
             faceOrdinal < compiledFaceOrder.Length;
             faceOrdinal++)
        {
            // Retail pure-axis, non-glass brushes use BrushBounds for their
            // six planes and retain only AxialMaterialNum in CBrush. Their
            // adjacency pointer and all six offset/count pairs are empty.
            if (edgeOwners is null)
            {
                adjacency[faceOrdinal] = [];
                continue;
            }

            AuthoredConvexBrushFace face =
                compiledFaceOrder[faceOrdinal];
            if (adjacencyByteCount > byte.MaxValue)
            {
                throw new OverflowException(
                    $"Collision brush {source.ObjectId} adjacency offset " +
                    $"{adjacencyByteCount} cannot be represented by CBrush.");
            }
            if (face.Winding.Count > byte.MaxValue)
            {
                throw new OverflowException(
                    $"Collision brush {source.ObjectId} face {faceOrdinal} " +
                    "has more than 255 edges.");
            }

            adjacencyStarts[faceOrdinal] =
                checked((byte)adjacencyByteCount);
            var adjacentFaces = new byte[face.Winding.Count];
            for (int edgeIndex = 0;
                 edgeIndex < face.Winding.Count;
                 edgeIndex++)
            {
                var edge =
                    CollisionGeometryValidation.UndirectedEdge.Create(
                        face.Winding[edgeIndex],
                        face.Winding[
                            (edgeIndex + 1) % face.Winding.Count],
                        out _);
                IReadOnlyList<AuthoredConvexBrushFace> owners =
                    edgeOwners[edge];
                AuthoredConvexBrushFace adjacent =
                    owners.Single(value => !ReferenceEquals(value, face));
                adjacentFaces[edgeIndex] = checked(
                    (byte)compiledOrdinals[adjacent]);
            }

            adjacency[faceOrdinal] = adjacentFaces;
            adjacencyByteCount = checked(
                adjacencyByteCount + adjacentFaces.Length);
        }

        var brushEdges = new byte[adjacencyByteCount];
        int edgeDestination = 0;
        foreach (byte[] faceAdjacency in adjacency)
        {
            faceAdjacency.CopyTo(brushEdges, edgeDestination);
            edgeDestination = checked(
                edgeDestination + faceAdjacency.Length);
        }

        var planes = new CPlane[nonAxialFaces.Count];
        var brushSides = new CBrushSide[nonAxialFaces.Count];
        var faceBindings =
            new CollisionCompiledConvexBrushFaceBinding[
                compiledFaceOrder.Length];
        for (int faceOrdinal = 0;
             faceOrdinal < compiledFaceOrder.Length;
             faceOrdinal++)
        {
            AuthoredConvexBrushFace face =
                compiledFaceOrder[faceOrdinal];
            ushort materialOrdinal =
                materialOrdinals.GetRequiredOrdinal(
                    source.ObjectId,
                    face.Material);
            int? planeOrdinal = null;
            if (faceOrdinal >= AxialFaceCount)
            {
                int localOrdinal = faceOrdinal - AxialFaceCount;
                CPlane plane = CompileNonAxialPlane(face.Plane);
                planes[localOrdinal] = plane;
                brushSides[localOrdinal] = new CBrushSide
                {
                    Plane = plane,
                    MaterialNum = materialOrdinal,
                    FirstAdjacentSideOffset =
                        adjacencyStarts[faceOrdinal],
                    EdgeCount = checked((byte)adjacency[faceOrdinal].Length)
                };
                planeOrdinal = localOrdinal;
            }

            faceBindings[faceOrdinal] =
                new CollisionCompiledConvexBrushFaceBinding(
                    faceOrdinal,
                    faceOrdinal < AxialFaceCount
                        ? (CollisionBrushAxialFace)faceOrdinal
                        : null,
                    planeOrdinal,
                    materialOrdinal,
                    adjacencyStarts[faceOrdinal],
                    checked((byte)adjacency[faceOrdinal].Length));
        }

        IReadOnlyList<short> axialMaterialNumbers =
            CompileAxialArray(
                faceBindings,
                value => unchecked((short)value.MaterialOrdinal));
        IReadOnlyList<byte> firstAdjacentSideOffsets =
            CompileAxialArray(
                faceBindings,
                value => value.FirstAdjacentSideOffset);
        IReadOnlyList<byte> axialEdgeCounts =
            CompileAxialArray(
                faceBindings,
                value => value.EdgeCount);
        IReadOnlyList<CBrushSide> detachedSides =
            Array.AsReadOnly(brushSides);
        IReadOnlyList<byte> detachedEdges =
            Array.AsReadOnly(brushEdges);
        var brush = new CBrush
        {
            NumSides = checked((ushort)brushSides.Length),
            GlassPieceIndex = NoGlassPieceIndex,
            Sides = detachedSides,
            BaseAdjacentSide = detachedEdges,
            AxialMaterialNum = axialMaterialNumbers,
            FirstAdjacentSideOffsets = firstAdjacentSideOffsets,
            EdgeCount = axialEdgeCounts
        };
        var referenceContract = new CollisionBrushReferenceContract(
            brush,
            materialOrdinals.Entries.Count,
            sideSliceStart: 0,
            brushSideCount: brushSides.Length,
            edgeSliceStart: 0,
            brushEdgeCount: brushEdges.Length);
        CollisionAuthoredMaterialOrdinal[] sourceMaterialBindings =
            materialOrdinals.Entries
                .Where(value => value.SourceObjectId == source.ObjectId)
                .OrderBy(value => value.Ordinal)
                .ToArray();

        return new CollisionCompiledConvexBrushLocalPayload(
            source.ObjectId,
            planes,
            sourceMaterialBindings,
            brushSides,
            brushEdges,
            brush,
            source.Bounds,
            source.Contents,
            faceBindings,
            referenceContract);
    }

    private static IReadOnlyDictionary<
        CollisionGeometryValidation.UndirectedEdge,
        IReadOnlyList<AuthoredConvexBrushFace>> BuildEdgeOwners(
            IReadOnlyList<AuthoredConvexBrushFace> faces)
    {
        var pending = new Dictionary<
            CollisionGeometryValidation.UndirectedEdge,
            List<AuthoredConvexBrushFace>>();
        foreach (AuthoredConvexBrushFace face in faces)
        {
            for (int index = 0; index < face.Winding.Count; index++)
            {
                var edge =
                    CollisionGeometryValidation.UndirectedEdge.Create(
                        face.Winding[index],
                        face.Winding[(index + 1) % face.Winding.Count],
                        out _);
                if (!pending.TryGetValue(edge, out var owners))
                {
                    owners = [];
                    pending.Add(edge, owners);
                }
                owners.Add(face);
            }
        }

        if (pending.Values.Any(value =>
                value.Count != 2 ||
                ReferenceEquals(value[0], value[1])))
        {
            throw new InvalidDataException(
                "Canonical brush edge ownership changed after source " +
                "validation.");
        }

        return new ReadOnlyDictionary<
            CollisionGeometryValidation.UndirectedEdge,
            IReadOnlyList<AuthoredConvexBrushFace>>(
                pending.ToDictionary(
                    value => value.Key,
                    value => (IReadOnlyList<AuthoredConvexBrushFace>)
                        new ReadOnlyCollection<AuthoredConvexBrushFace>(
                            value.Value.ToArray())));
    }

    private static CPlane CompileNonAxialPlane(
        AuthoredCollisionPlane source)
    {
        MapVector3 normal = CanonicalizeZero(source.Normal);
        return new CPlane
        {
            Normal = new AssetVector3
            {
                X = normal.X,
                Y = normal.Y,
                Z = normal.Z
            },
            Dist = CanonicalizeZero(source.Distance),
            Type = NonAxialPlaneType,
            SignBits = SignBits(normal),
            Pad12 = CanonicalPlanePadding
        };
    }

    private static IReadOnlyList<T> CompileAxialArray<T>(
        IReadOnlyList<CollisionCompiledConvexBrushFaceBinding> faces,
        Func<CollisionCompiledConvexBrushFaceBinding, T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var values = new T[AxialFaceCount];
        int destination = 0;

        // Serialized CBrush fixed arrays are [direction][axis], while the
        // unified face namespace is [axis][direction].
        for (int direction = 0; direction < 2; direction++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int unifiedFaceOrdinal = axis * 2 + direction;
                values[destination++] =
                    selector(faces[unifiedFaceOrdinal]);
            }
        }

        return Array.AsReadOnly(values);
    }

    private static void ValidateAxialBounds(
        AuthoredConvexBrushCollisionSource source,
        IReadOnlyList<AuthoredConvexBrushFace?> axialFaces)
    {
        MapVector3 minimum =
            source.Bounds.MidPoint - source.Bounds.HalfSize;
        MapVector3 maximum =
            source.Bounds.MidPoint + source.Bounds.HalfSize;
        float[] expectedDistances =
        [
            -minimum.X,
            maximum.X,
            -minimum.Y,
            maximum.Y,
            -minimum.Z,
            maximum.Z
        ];
        for (int index = 0; index < AxialFaceCount; index++)
        {
            float actual = axialFaces[index]!.Plane.Distance;
            if (Math.Abs(actual - expectedDistances[index]) >
                CollisionGeometryValidation.PlanarityTolerance)
            {
                throw new InvalidDataException(
                    $"Collision brush {source.ObjectId} " +
                    $"{(CollisionBrushAxialFace)index} face is not its " +
                    "corresponding authored bounds plane.");
            }
        }
    }

    private static void ValidateUniformContents(
        AuthoredConvexBrushCollisionSource source)
    {
        AuthoredConvexBrushFace? mismatchedFace = source.Faces
            .FirstOrDefault(face =>
                unchecked((uint)face.Material.Contents) != source.Contents);
        if (mismatchedFace is null)
            return;

        throw new NotSupportedException(
            $"Collision brush {source.ObjectId} has brush-wide contents " +
            $"0x{source.Contents:X8}, but face material " +
            $"'{mismatchedFace.Material.ExactName}' has contents " +
            $"0x{unchecked((uint)mismatchedFace.Material.Contents):X8}. " +
            "The bounded M3 compiler requires every face material contents " +
            "value to match the brush-wide contents exactly.");
    }

    private static CollisionBrushAxialFace? ClassifyAxialFace(
        MapVector3 normal)
    {
        if (normal == new MapVector3(-1, 0, 0))
            return CollisionBrushAxialFace.NegativeX;
        if (normal == new MapVector3(1, 0, 0))
            return CollisionBrushAxialFace.PositiveX;
        if (normal == new MapVector3(0, -1, 0))
            return CollisionBrushAxialFace.NegativeY;
        if (normal == new MapVector3(0, 1, 0))
            return CollisionBrushAxialFace.PositiveY;
        if (normal == new MapVector3(0, 0, -1))
            return CollisionBrushAxialFace.NegativeZ;
        if (normal == new MapVector3(0, 0, 1))
            return CollisionBrushAxialFace.PositiveZ;
        return null;
    }

    private static int CompareNonAxialFaces(
        AuthoredConvexBrushFace left,
        AuthoredConvexBrushFace right)
    {
        int comparison = CanonicalizeZero(left.Plane.Normal.X).CompareTo(
            CanonicalizeZero(right.Plane.Normal.X));
        if (comparison != 0)
            return comparison;
        comparison = CanonicalizeZero(left.Plane.Normal.Y).CompareTo(
            CanonicalizeZero(right.Plane.Normal.Y));
        if (comparison != 0)
            return comparison;
        comparison = CanonicalizeZero(left.Plane.Normal.Z).CompareTo(
            CanonicalizeZero(right.Plane.Normal.Z));
        if (comparison != 0)
            return comparison;
        comparison = CanonicalizeZero(left.Plane.Distance).CompareTo(
            CanonicalizeZero(right.Plane.Distance));
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.Ordinal.Compare(
            left.Material.ExactName,
            right.Material.ExactName);
        if (comparison != 0)
            return comparison;
        comparison = left.Material.SurfaceFlags.CompareTo(
            right.Material.SurfaceFlags);
        return comparison != 0
            ? comparison
            : left.Material.Contents.CompareTo(
                right.Material.Contents);
    }

    private static byte SignBits(MapVector3 normal)
    {
        int bits = 0;
        if (normal.X < 0)
            bits |= 1;
        if (normal.Y < 0)
            bits |= 2;
        if (normal.Z < 0)
            bits |= 4;
        return checked((byte)bits);
    }

    private static MapVector3 CanonicalizeZero(MapVector3 value) =>
        new(
            CanonicalizeZero(value.X),
            CanonicalizeZero(value.Y),
            CanonicalizeZero(value.Z));

    private static float CanonicalizeZero(float value) =>
        value == 0 ? 0 : value;
}
