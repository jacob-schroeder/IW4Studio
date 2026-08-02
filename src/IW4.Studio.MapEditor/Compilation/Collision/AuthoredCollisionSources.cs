using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Explicit semantic ownership for one authored collision source. Ownership
/// is a required input; it is never inferred from geometry, names, bounds, or
/// proximity.
/// </summary>
public abstract class CollisionSourceOwnership
{
    protected CollisionSourceOwnership(
        CollisionOwnershipCategory category,
        CollisionCounterpartIdentity? counterpart)
    {
        Category = category;
        Counterpart = counterpart;
    }

    public CollisionOwnershipCategory Category { get; }
    public CollisionCounterpartIdentity? Counterpart { get; }
}

public sealed class StandaloneWorldCollisionSourceOwnership
    : CollisionSourceOwnership
{
    public StandaloneWorldCollisionSourceOwnership()
        : base(
            CollisionOwnershipCategory.StandaloneWorld,
            counterpart: null)
    {
    }
}

public sealed class PairedStaticModelCollisionSourceOwnership
    : CollisionSourceOwnership
{
    public PairedStaticModelCollisionSourceOwnership(
        MapObjectId renderStaticModelObjectId)
        : base(
            CollisionOwnershipCategory.PairedStaticModel,
            new CollisionCounterpartIdentity(
                renderStaticModelObjectId,
                CollisionCounterpartKind.RenderStaticModel))
    {
    }

    public MapObjectId RenderStaticModelObjectId =>
        Counterpart!.Value.ObjectId;
}

public sealed class BrushModelEntityCollisionSourceOwnership
    : CollisionSourceOwnership
{
    public BrushModelEntityCollisionSourceOwnership(
        MapObjectId mapEntityObjectId)
        : base(
            CollisionOwnershipCategory.BrushModelEntity,
            new CollisionCounterpartIdentity(
                mapEntityObjectId,
                CollisionCounterpartKind.MapEntity))
    {
    }

    public MapObjectId MapEntityObjectId =>
        Counterpart!.Value.ObjectId;
}

/// <summary>
/// Canonical authored ClipMaterial input. The exact name spelling and both
/// bit fields are semantic compiler inputs; no material-name inference or
/// default contents are applied.
/// </summary>
public sealed record AuthoredCollisionMaterialInput
{
    public AuthoredCollisionMaterialInput(
        string exactName,
        int surfaceFlags,
        int contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactName);
        if (exactName.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "Collision material names cannot contain NUL bytes.",
                nameof(exactName));
        }

        ExactName = exactName;
        SurfaceFlags = surfaceFlags;
        Contents = contents;
    }

    public string ExactName { get; }
    public int SurfaceFlags { get; }
    public int Contents { get; }
}

/// <summary>
/// Canonical plane equation dot(Normal, point) = Distance. Authored normals
/// must already be unit length; silently normalizing would change stable
/// semantic input.
/// </summary>
public readonly record struct AuthoredCollisionPlane
{
    public const double UnitLengthTolerance = 1e-4;

    public AuthoredCollisionPlane(
        MapVector3 normal,
        float distance)
    {
        if (!normal.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normal),
                "Collision plane normal components must be finite.");
        }
        if (!float.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance),
                "Collision plane distance must be finite.");
        }

        double lengthSquared = CollisionGeometryValidation.LengthSquared(
            normal);
        if (Math.Abs(lengthSquared - 1d) > UnitLengthTolerance)
        {
            throw new ArgumentException(
                "Authored collision plane normals must be unit length.",
                nameof(normal));
        }

        Normal = normal;
        Distance = distance;
    }

    public MapVector3 Normal { get; }
    public float Distance { get; }
}

/// <summary>
/// One outward-facing convex brush face. Winding vertices are ordered
/// counter-clockwise when viewed from outside the brush, so every positive
/// turn points along <see cref="Plane"/>.Normal.
/// </summary>
public sealed class AuthoredConvexBrushFace
{
    private readonly IReadOnlyList<MapVector3> _winding;

    public AuthoredConvexBrushFace(
        AuthoredCollisionPlane plane,
        IEnumerable<MapVector3> winding,
        AuthoredCollisionMaterialInput material)
    {
        ArgumentNullException.ThrowIfNull(winding);
        ArgumentNullException.ThrowIfNull(material);

        MapVector3[] copy = winding.ToArray();
        if (copy.Length < 3)
        {
            throw new ArgumentException(
                "A convex brush face requires at least three winding " +
                "vertices.",
                nameof(winding));
        }
        if (copy.Any(value => !value.IsFinite))
        {
            throw new ArgumentException(
                "Convex brush winding vertices must be finite.",
                nameof(winding));
        }
        if (copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException(
                "A canonical brush winding cannot repeat a vertex.",
                nameof(winding));
        }

        for (int index = 0; index < copy.Length; index++)
        {
            MapVector3 point = copy[index];
            double planeResidual = Math.Abs(
                CollisionGeometryValidation.Dot(plane.Normal, point) -
                plane.Distance);
            if (planeResidual >
                CollisionGeometryValidation.PlanarityTolerance)
            {
                throw new ArgumentException(
                    $"Brush winding vertex {index} is not on its declared " +
                    "plane.",
                    nameof(winding));
            }

            MapVector3 current = copy[index];
            MapVector3 next = copy[(index + 1) % copy.Length];
            MapVector3 after = copy[(index + 2) % copy.Length];
            MapVector3 firstEdge = next - current;
            MapVector3 secondEdge = after - next;
            double signedTurn = CollisionGeometryValidation.Dot(
                CollisionGeometryValidation.Cross(
                    firstEdge,
                    secondEdge),
                plane.Normal);
            if (!double.IsFinite(signedTurn) ||
                signedTurn <=
                CollisionGeometryValidation.DegenerateAreaTolerance)
            {
                throw new ArgumentException(
                    "Brush winding vertices must form a non-degenerate " +
                    "convex polygon with canonical outward orientation.",
                    nameof(winding));
            }
        }
        for (int edgeIndex = 0;
             edgeIndex < copy.Length;
             edgeIndex++)
        {
            MapVector3 edgeStart = copy[edgeIndex];
            MapVector3 edgeEnd =
                copy[(edgeIndex + 1) % copy.Length];
            MapVector3 edge = edgeEnd - edgeStart;
            for (int vertexIndex = 0;
                 vertexIndex < copy.Length;
                 vertexIndex++)
            {
                if (vertexIndex == edgeIndex ||
                    vertexIndex == (edgeIndex + 1) % copy.Length)
                {
                    continue;
                }

                double inside = CollisionGeometryValidation.Dot(
                    CollisionGeometryValidation.Cross(
                        edge,
                        copy[vertexIndex] - edgeStart),
                    plane.Normal);
                if (!double.IsFinite(inside) ||
                    inside <=
                    CollisionGeometryValidation.DegenerateAreaTolerance)
                {
                    throw new ArgumentException(
                        "Every non-edge winding vertex must lie strictly " +
                        "inside every directed face edge.",
                        nameof(winding));
                }
            }
        }

        Plane = plane;
        Material = material;
        _winding = new ReadOnlyCollection<MapVector3>(copy);
    }

    public AuthoredCollisionPlane Plane { get; }
    public IReadOnlyList<MapVector3> Winding => _winding;
    public AuthoredCollisionMaterialInput Material { get; }
}

public abstract class AuthoredCollisionSource
{
    protected AuthoredCollisionSource(
        MapObjectId objectId,
        CollisionGeometryKind geometryKind,
        CollisionSourceOwnership ownership)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        ArgumentNullException.ThrowIfNull(ownership);
        if (!Enum.IsDefined(geometryKind))
            throw new ArgumentOutOfRangeException(nameof(geometryKind));

        // Reuse the compilation identity contract as the single authority for
        // ownership/geometry compatibility.
        _ = new CollisionCompilationSource(
            objectId,
            geometryKind,
            ownership.Category,
            CollisionSourceProvenance.Authored,
            importedSourceOrdinal: null,
            ownership.Counterpart);

        ObjectId = objectId;
        GeometryKind = geometryKind;
        Ownership = ownership;
    }

    public MapObjectId ObjectId { get; }
    public CollisionGeometryKind GeometryKind { get; }
    public CollisionSourceOwnership Ownership { get; }
    public CollisionSourceProvenance Provenance =>
        CollisionSourceProvenance.Authored;

    internal CollisionCompilationSource CreateCompilationIdentity() =>
        new(
            ObjectId,
            GeometryKind,
            Ownership.Category,
            Provenance,
            importedSourceOrdinal: null,
            Ownership.Counterpart);
}

/// <summary>
/// Closed canonical convex brush. Plane normals point outward, all winding
/// vertices lie behind every brush plane, and each undirected winding edge is
/// owned by exactly two oppositely directed faces.
/// </summary>
public sealed class AuthoredConvexBrushCollisionSource
    : AuthoredCollisionSource
{
    private readonly IReadOnlyList<AuthoredConvexBrushFace> _faces;
    private readonly IReadOnlyList<AuthoredCollisionMaterialInput> _materials;

    public AuthoredConvexBrushCollisionSource(
        MapObjectId objectId,
        CollisionSourceOwnership ownership,
        IEnumerable<AuthoredConvexBrushFace> faces,
        uint contents)
        : base(
            objectId,
            CollisionGeometryKind.ConvexBrush,
            ownership)
    {
        ArgumentNullException.ThrowIfNull(faces);
        if (ownership.Category == CollisionOwnershipCategory.PairedStaticModel)
        {
            throw new ArgumentException(
                "A canonical convex brush cannot use paired static-model " +
                "ownership.",
                nameof(ownership));
        }

        AuthoredConvexBrushFace[] copy = faces.ToArray();
        if (copy.Length < 4 || copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "A closed convex brush requires at least four non-null faces.",
                nameof(faces));
        }
        ValidateUniquePlanes(copy);
        ValidateInteriorHalfSpaces(copy);
        ValidateClosedEdgeTopology(copy);

        MapVector3[] vertices = copy
            .SelectMany(value => value.Winding)
            .Distinct()
            .ToArray();
        Bounds = CollisionGeometryValidation.Bounds(vertices);
        Contents = contents;
        _faces = new ReadOnlyCollection<AuthoredConvexBrushFace>(copy);
        _materials =
            new ReadOnlyCollection<AuthoredCollisionMaterialInput>(
                copy.Select(value => value.Material)
                    .Distinct()
                    .ToArray());
    }

    public IReadOnlyList<AuthoredConvexBrushFace> Faces => _faces;
    public IReadOnlyList<AuthoredCollisionMaterialInput> Materials =>
        _materials;
    public uint Contents { get; }
    public MapBounds Bounds { get; }

    private static void ValidateUniquePlanes(
        IReadOnlyList<AuthoredConvexBrushFace> faces)
    {
        for (int left = 0; left < faces.Count; left++)
        {
            for (int right = left + 1; right < faces.Count; right++)
            {
                AuthoredCollisionPlane a = faces[left].Plane;
                AuthoredCollisionPlane b = faces[right].Plane;
                if (CollisionGeometryValidation.NearlyEqual(
                        a.Normal,
                        b.Normal) &&
                    Math.Abs(a.Distance - b.Distance) <=
                    CollisionGeometryValidation.PlanarityTolerance)
                {
                    throw new ArgumentException(
                        "A canonical convex brush cannot contain duplicate " +
                        "or near-duplicate outward planes.",
                        nameof(faces));
                }
            }
        }
    }

    private static void ValidateInteriorHalfSpaces(
        IReadOnlyList<AuthoredConvexBrushFace> faces)
    {
        for (int planeIndex = 0;
             planeIndex < faces.Count;
             planeIndex++)
        {
            AuthoredCollisionPlane plane = faces[planeIndex].Plane;
            foreach (MapVector3 point in faces.SelectMany(value =>
                         value.Winding))
            {
                double signedDistance =
                    CollisionGeometryValidation.Dot(plane.Normal, point) -
                    plane.Distance;
                if (signedDistance >
                    CollisionGeometryValidation.PlanarityTolerance)
                {
                    throw new ArgumentException(
                        "Brush windings do not bound one closed convex " +
                        "half-space intersection.",
                        nameof(faces));
                }
            }
        }
    }

    private static void ValidateClosedEdgeTopology(
        IReadOnlyList<AuthoredConvexBrushFace> faces)
    {
        var edges = new Dictionary<
            CollisionGeometryValidation.UndirectedEdge,
            (int Count, int DirectionBalance)>();
        foreach (AuthoredConvexBrushFace face in faces)
        {
            for (int index = 0; index < face.Winding.Count; index++)
            {
                MapVector3 start = face.Winding[index];
                MapVector3 end =
                    face.Winding[(index + 1) % face.Winding.Count];
                var edge =
                    CollisionGeometryValidation.UndirectedEdge.Create(
                        start,
                        end,
                        out int direction);
                (int count, int balance) =
                    edges.GetValueOrDefault(edge);
                edges[edge] = (
                    checked(count + 1),
                    checked(balance + direction));
            }
        }

        if (edges.Any(value =>
                value.Value.Count != 2 ||
                value.Value.DirectionBalance != 0))
        {
            throw new ArgumentException(
                "Every canonical brush winding edge must be shared by " +
                "exactly two oppositely directed faces.",
                nameof(faces));
        }
    }
}

public readonly record struct AuthoredTriangleEdgeWalkability(
    bool Edge01,
    bool Edge12,
    bool Edge20);

public sealed class AuthoredIndexedCollisionTriangle
{
    public AuthoredIndexedCollisionTriangle(
        int vertex0,
        int vertex1,
        int vertex2,
        AuthoredTriangleEdgeWalkability walkability,
        AuthoredCollisionMaterialInput material)
    {
        if (vertex0 < 0)
            throw new ArgumentOutOfRangeException(nameof(vertex0));
        if (vertex1 < 0)
            throw new ArgumentOutOfRangeException(nameof(vertex1));
        if (vertex2 < 0)
            throw new ArgumentOutOfRangeException(nameof(vertex2));
        if (vertex0 == vertex1 ||
            vertex1 == vertex2 ||
            vertex2 == vertex0)
        {
            throw new ArgumentException(
                "A collision triangle requires three distinct vertex " +
                "indices.");
        }
        ArgumentNullException.ThrowIfNull(material);

        Vertex0 = vertex0;
        Vertex1 = vertex1;
        Vertex2 = vertex2;
        Walkability = walkability;
        Material = material;
    }

    public int Vertex0 { get; }
    public int Vertex1 { get; }
    public int Vertex2 { get; }
    public AuthoredTriangleEdgeWalkability Walkability { get; }
    public AuthoredCollisionMaterialInput Material { get; }

    public IEnumerable<int> VertexIndices
    {
        get
        {
            yield return Vertex0;
            yield return Vertex1;
            yield return Vertex2;
        }
    }
}

/// <summary>
/// Canonical indexed collision mesh. Vertices are one source-owned shared
/// table and triangles reference it explicitly. Walkability is retained per
/// directed triangle edge. The root vertex table is not globally limited to
/// 65,536 rows: IW4 triangle indices are unsigned-16 values relative to a
/// partition-selected 1,024-vertex segment.
/// </summary>
public sealed class AuthoredIndexedTriangleMeshCollisionSource
    : AuthoredCollisionSource
{
    private readonly IReadOnlyList<MapVector3> _vertices;
    private readonly IReadOnlyList<AuthoredIndexedCollisionTriangle>
        _triangles;
    private readonly IReadOnlyList<AuthoredCollisionMaterialInput> _materials;

    public AuthoredIndexedTriangleMeshCollisionSource(
        MapObjectId objectId,
        StandaloneWorldCollisionSourceOwnership ownership,
        IEnumerable<MapVector3> vertices,
        IEnumerable<AuthoredIndexedCollisionTriangle> triangles)
        : base(
            objectId,
            CollisionGeometryKind.TriangleMesh,
            ownership)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);

        MapVector3[] vertexCopy = vertices.ToArray();
        AuthoredIndexedCollisionTriangle[] triangleCopy =
            triangles.ToArray();
        if (vertexCopy.Length < 3)
        {
            throw new ArgumentException(
                "An indexed collision mesh requires at least three shared " +
                "vertices.",
                nameof(vertices));
        }
        if (vertexCopy.Any(value => !value.IsFinite))
        {
            throw new ArgumentException(
                "Indexed collision vertices must be finite.",
                nameof(vertices));
        }
        if (triangleCopy.Length == 0 ||
            triangleCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "An indexed collision mesh requires at least one non-null " +
                "triangle.",
                nameof(triangles));
        }

        var referencedVertices = new HashSet<int>();
        var triangleKeys = new HashSet<TriangleKey>();
        for (int index = 0; index < triangleCopy.Length; index++)
        {
            AuthoredIndexedCollisionTriangle triangle =
                triangleCopy[index];
            int[] indices = triangle.VertexIndices.ToArray();
            if (indices.Any(value => value >= vertexCopy.Length))
            {
                throw new ArgumentException(
                    $"Collision triangle {index} references a vertex outside " +
                    "the shared vertex table.",
                    nameof(triangles));
            }

            MapVector3 edge01 =
                vertexCopy[triangle.Vertex1] -
                vertexCopy[triangle.Vertex0];
            MapVector3 edge02 =
                vertexCopy[triangle.Vertex2] -
                vertexCopy[triangle.Vertex0];
            double areaSquared = CollisionGeometryValidation.LengthSquared(
                CollisionGeometryValidation.Cross(edge01, edge02));
            if (!double.IsFinite(areaSquared) ||
                areaSquared <=
                CollisionGeometryValidation.DegenerateAreaTolerance)
            {
                throw new ArgumentException(
                    $"Collision triangle {index} is geometrically " +
                    "degenerate.",
                    nameof(triangles));
            }

            if (!triangleKeys.Add(TriangleKey.Create(triangle)))
            {
                throw new ArgumentException(
                    "A canonical indexed mesh cannot contain duplicate " +
                    "triangles.",
                    nameof(triangles));
            }
            referencedVertices.UnionWith(indices);
        }

        if (referencedVertices.Count != vertexCopy.Length)
        {
            throw new ArgumentException(
                "Every canonical shared collision vertex must be referenced " +
                "by at least one triangle.",
                nameof(vertices));
        }

        Bounds = CollisionGeometryValidation.Bounds(vertexCopy);
        _vertices = new ReadOnlyCollection<MapVector3>(vertexCopy);
        _triangles =
            new ReadOnlyCollection<AuthoredIndexedCollisionTriangle>(
                triangleCopy);
        _materials =
            new ReadOnlyCollection<AuthoredCollisionMaterialInput>(
                triangleCopy.Select(value => value.Material)
                    .Distinct()
                    .ToArray());
    }

    public IReadOnlyList<MapVector3> Vertices => _vertices;
    public IReadOnlyList<AuthoredIndexedCollisionTriangle> Triangles =>
        _triangles;
    public IReadOnlyList<AuthoredCollisionMaterialInput> Materials =>
        _materials;
    public MapBounds Bounds { get; }

    private readonly record struct TriangleKey(int A, int B, int C)
    {
        public static TriangleKey Create(
            AuthoredIndexedCollisionTriangle triangle)
        {
            int[] indices = triangle.VertexIndices.Order().ToArray();
            return new TriangleKey(
                indices[0],
                indices[1],
                indices[2]);
        }
    }
}

/// <summary>
/// Complete collision-side placement retained by one authored paired static
/// model. The three rows match ClipStaticModel inverse-scaled-axis semantics.
/// </summary>
public sealed class AuthoredStaticModelCollisionPlacement
{
    private readonly IReadOnlyList<MapVector3> _inverseScaledAxis;

    public AuthoredStaticModelCollisionPlacement(
        MapVector3 origin,
        IEnumerable<MapVector3> inverseScaledAxis,
        MapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(inverseScaledAxis);
        MapVector3[] axisCopy = inverseScaledAxis.ToArray();
        if (!origin.IsFinite ||
            axisCopy.Length != 3 ||
            axisCopy.Any(value => !value.IsFinite))
        {
            throw new ArgumentException(
                "Static-model collision placement requires a finite origin " +
                "and exactly three finite inverse-scaled-axis rows.",
                nameof(inverseScaledAxis));
        }

        double determinant =
            axisCopy[0].X *
            ((double)axisCopy[1].Y * axisCopy[2].Z -
             (double)axisCopy[1].Z * axisCopy[2].Y) -
            axisCopy[0].Y *
            ((double)axisCopy[1].X * axisCopy[2].Z -
             (double)axisCopy[1].Z * axisCopy[2].X) +
            axisCopy[0].Z *
            ((double)axisCopy[1].X * axisCopy[2].Y -
             (double)axisCopy[1].Y * axisCopy[2].X);
        if (!double.IsFinite(determinant) ||
            Math.Abs(determinant) <=
            CollisionGeometryValidation.DegenerateAreaTolerance)
        {
            throw new ArgumentException(
                "Static-model inverse-scaled-axis rows must form an " +
                "invertible transform.",
                nameof(inverseScaledAxis));
        }
        if (!bounds.IsFinite ||
            bounds.HalfSize.X < 0 ||
            bounds.HalfSize.Y < 0 ||
            bounds.HalfSize.Z < 0 ||
            bounds.HalfSize == default)
        {
            throw new ArgumentException(
                "Paired static-model collision requires a finite, " +
                "non-empty bounds projection.",
                nameof(bounds));
        }

        Origin = origin;
        Bounds = bounds;
        _inverseScaledAxis =
            new ReadOnlyCollection<MapVector3>(axisCopy);
    }

    public MapVector3 Origin { get; }
    public IReadOnlyList<MapVector3> InverseScaledAxis =>
        _inverseScaledAxis;
    public MapBounds Bounds { get; }
}

/// <summary>
/// Canonical authored reference to collision supplied by an XModel and
/// paired with one explicit render static-model object. The XModel hull is a
/// dependency, not geometry synthesized by this source.
/// </summary>
public sealed class AuthoredPairedStaticModelCollisionSource
    : AuthoredCollisionSource
{
    public AuthoredPairedStaticModelCollisionSource(
        MapObjectId objectId,
        PairedStaticModelCollisionSourceOwnership ownership,
        string exactSerializedModelName,
        AuthoredStaticModelCollisionPlacement placement)
        : base(
            objectId,
            CollisionGeometryKind.StaticModelHull,
            ownership)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            exactSerializedModelName);
        if (exactSerializedModelName.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "Static-model collision names cannot contain NUL bytes.",
                nameof(exactSerializedModelName));
        }

        ExactSerializedModelName = exactSerializedModelName;
        Placement = placement;
    }

    public string ExactSerializedModelName { get; }
    public AuthoredStaticModelCollisionPlacement Placement { get; }
    public MapVector3 Origin => Placement.Origin;
    public IReadOnlyList<MapVector3> InverseScaledAxis =>
        Placement.InverseScaledAxis;
    public MapBounds Bounds => Placement.Bounds;
}

internal static class CollisionGeometryValidation
{
    public const double PlanarityTolerance = 1e-3;
    public const double DegenerateAreaTolerance = 1e-10;

    public static double Dot(MapVector3 left, MapVector3 right) =>
        (double)left.X * right.X +
        (double)left.Y * right.Y +
        (double)left.Z * right.Z;

    public static MapVector3 Cross(MapVector3 left, MapVector3 right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    public static double LengthSquared(MapVector3 value) =>
        Dot(value, value);

    public static bool NearlyEqual(
        MapVector3 left,
        MapVector3 right) =>
        Math.Abs(left.X - right.X) <= PlanarityTolerance &&
        Math.Abs(left.Y - right.Y) <= PlanarityTolerance &&
        Math.Abs(left.Z - right.Z) <= PlanarityTolerance;

    public static MapBounds Bounds(IReadOnlyList<MapVector3> vertices)
    {
        if (vertices.Count == 0)
            throw new ArgumentException("Geometry requires vertices.");
        return CollisionOutwardBounds.FromVertices(vertices);
    }

    internal readonly record struct UndirectedEdge(
        MapVector3 First,
        MapVector3 Second)
    {
        public static UndirectedEdge Create(
            MapVector3 start,
            MapVector3 end,
            out int direction)
        {
            if (Compare(start, end) < 0)
            {
                direction = 1;
                return new UndirectedEdge(start, end);
            }

            direction = -1;
            return new UndirectedEdge(end, start);
        }

        private static int Compare(
            MapVector3 left,
            MapVector3 right)
        {
            int comparison = left.X.CompareTo(right.X);
            if (comparison != 0)
                return comparison;
            comparison = left.Y.CompareTo(right.Y);
            if (comparison != 0)
                return comparison;
            comparison = left.Z.CompareTo(right.Z);
            if (comparison != 0)
                return comparison;

            comparison = BitConverter.SingleToInt32Bits(left.X).CompareTo(
                BitConverter.SingleToInt32Bits(right.X));
            if (comparison != 0)
                return comparison;
            comparison = BitConverter.SingleToInt32Bits(left.Y).CompareTo(
                BitConverter.SingleToInt32Bits(right.Y));
            if (comparison != 0)
                return comparison;
            return BitConverter.SingleToInt32Bits(left.Z).CompareTo(
                BitConverter.SingleToInt32Bits(right.Z));
        }
    }
}
