using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Resolves an exact serialized XModel name to detached, canonical collision
/// geometry expressed in that model's local coordinate space.
/// </summary>
public interface ICollisionStaticModelLocalGeometryProvider
{
    bool TryResolve(
        string exactSerializedModelName,
        out CollisionStaticModelLocalGeometry? geometry);
}

/// <summary>
/// Canonical local-space collision supplied by one XModel dependency.
/// Collection order is semantic and must be stable for repeatable trace
/// diagnostics.
/// </summary>
public sealed class CollisionStaticModelLocalGeometry
{
    public CollisionStaticModelLocalGeometry(
        IEnumerable<CollisionStaticModelLocalConvexBrush>? brushes = null,
        IEnumerable<CollisionStaticModelLocalIndexedTriangleMesh>? meshes =
            null)
    {
        Brushes = ReadOnly(brushes ?? []);
        Meshes = ReadOnly(meshes ?? []);
        if (Brushes.Count == 0 && Meshes.Count == 0)
        {
            throw new ArgumentException(
                "Static-model collision geometry must contain at least one " +
                "convex brush or indexed triangle mesh.");
        }
    }

    public IReadOnlyList<CollisionStaticModelLocalConvexBrush> Brushes
    {
        get;
    }

    public IReadOnlyList<CollisionStaticModelLocalIndexedTriangleMesh> Meshes
    {
        get;
    }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> source)
        where T : class
    {
        T[] copy = source.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Static-model local geometry cannot contain null members.",
                nameof(source));
        }

        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>
/// One canonical local-space convex brush supplied by an XModel dependency.
/// The existing authored-brush constructor remains the single authority for
/// closed winding and half-space validation.
/// </summary>
public sealed class CollisionStaticModelLocalConvexBrush
{
    private static readonly MapObjectId ValidationObjectId =
        new(new Guid("6e81d90b-5d40-4fda-b535-a5a69dd03af1"));

    private readonly IReadOnlyList<AuthoredConvexBrushFace> _faces;

    public CollisionStaticModelLocalConvexBrush(
        IEnumerable<AuthoredConvexBrushFace> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);
        AuthoredConvexBrushFace[] copy = faces.ToArray();

        // This adapter deliberately creates no persistent semantic source.
        // It invokes the canonical validator so dependency geometry cannot
        // weaken the invariants used by authored world brushes.
        _ = new AuthoredConvexBrushCollisionSource(
            ValidationObjectId,
            new StandaloneWorldCollisionSourceOwnership(),
            copy,
            contents: 0);
        _faces = new ReadOnlyCollection<AuthoredConvexBrushFace>(copy);
    }

    public IReadOnlyList<AuthoredConvexBrushFace> Faces => _faces;
}

/// <summary>
/// One canonical local-space indexed triangle mesh supplied by an XModel
/// dependency.
/// </summary>
public sealed class CollisionStaticModelLocalIndexedTriangleMesh
{
    private static readonly MapObjectId ValidationObjectId =
        new(new Guid("3695c524-d7b1-4967-89f4-13bc78b54e2b"));

    private readonly IReadOnlyList<MapVector3> _vertices;
    private readonly IReadOnlyList<AuthoredIndexedCollisionTriangle>
        _triangles;

    public CollisionStaticModelLocalIndexedTriangleMesh(
        IEnumerable<MapVector3> vertices,
        IEnumerable<AuthoredIndexedCollisionTriangle> triangles)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        MapVector3[] vertexCopy = vertices.ToArray();
        AuthoredIndexedCollisionTriangle[] triangleCopy =
            triangles.ToArray();

        _ = new AuthoredIndexedTriangleMeshCollisionSource(
            ValidationObjectId,
            new StandaloneWorldCollisionSourceOwnership(),
            vertexCopy,
            triangleCopy);
        _vertices = new ReadOnlyCollection<MapVector3>(vertexCopy);
        _triangles =
            new ReadOnlyCollection<AuthoredIndexedCollisionTriangle>(
                triangleCopy);
    }

    public IReadOnlyList<MapVector3> Vertices => _vertices;

    public IReadOnlyList<AuthoredIndexedCollisionTriangle> Triangles =>
        _triangles;
}

public enum CollisionOfflineTracePrimitiveKind
{
    ConvexBrush,
    Triangle
}

public sealed record CollisionOfflineTraceHit(
    MapObjectId SourceObjectId,
    CollisionGeometryKind GeometryKind,
    CollisionOfflineTracePrimitiveKind PrimitiveKind,
    double Fraction,
    MapVector3 Point,
    AuthoredCollisionMaterialInput? Material,
    bool StartsInside);

public enum CollisionOfflineTraceIssueKind
{
    MissingStaticModelGeometryProvider,
    UnresolvedStaticModelGeometryDependency,
    InvalidStaticModelGeometryResolution
}

public sealed record CollisionOfflineTraceIssue(
    CollisionOfflineTraceIssueKind Kind,
    MapObjectId SourceObjectId,
    string ExactSerializedModelName,
    string Message);

/// <summary>
/// Offline trace outcome. A miss is authoritative only when
/// <see cref="IsComplete"/> is true; unresolved XModel geometry is reported
/// explicitly and is never approximated with placement bounds.
/// </summary>
public sealed class CollisionOfflineTraceResult
{
    internal CollisionOfflineTraceResult(
        CollisionOfflineTraceHit? nearestHit,
        IEnumerable<CollisionOfflineTraceIssue> issues)
    {
        NearestHit = nearestHit;
        Issues = new ReadOnlyCollection<CollisionOfflineTraceIssue>(
            issues.ToArray());
    }

    public CollisionOfflineTraceHit? NearestHit { get; }

    public IReadOnlyList<CollisionOfflineTraceIssue> Issues { get; }

    public bool IsComplete => Issues.Count == 0;

    public void RequireComplete()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException(
                "Offline collision trace is incomplete: " +
                string.Join(
                    " | ",
                    Issues.Select(value => value.Message)));
        }
    }
}

/// <summary>
/// Deterministic, renderer-neutral collision tracer for canonical authored
/// sources. It performs exact convex half-space clipping and two-sided
/// indexed-triangle intersection. This utility does not consume compiled
/// pointers, mutate a document, or authorize persistence.
/// </summary>
public static class CollisionOfflineTrace
{
    private const double ParallelTolerance = 1e-10;
    private const double BoundaryTolerance = 1e-7;

    public static CollisionOfflineTraceResult TraceSegment(
        IEnumerable<AuthoredCollisionSource> sources,
        MapVector3 start,
        MapVector3 end,
        ICollisionStaticModelLocalGeometryProvider? staticModelGeometry =
            null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!start.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (!end.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(end));

        MapVector3 direction = end - start;
        if (CollisionGeometryValidation.LengthSquared(direction) <=
            CollisionGeometryValidation.DegenerateAreaTolerance)
        {
            throw new ArgumentException(
                "An offline collision segment must have non-zero length.",
                nameof(end));
        }

        AuthoredCollisionSource[] sourceCopy = sources.ToArray();
        if (sourceCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Offline collision sources cannot contain null rows.",
                nameof(sources));
        }

        AuthoredCollisionSource[] ordered = sourceCopy
            .OrderBy(
                value => value.ObjectId.Value.ToString("N"),
                StringComparer.Ordinal)
            .ToArray();

        MapObjectId? duplicate = ordered
            .GroupBy(value => value.ObjectId)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Offline collision source {duplicate} is duplicated.",
                nameof(sources));
        }

        var issues = new List<CollisionOfflineTraceIssue>();
        TraceCandidate? nearest = null;
        for (int sourceOrdinal = 0;
             sourceOrdinal < ordered.Length;
             sourceOrdinal++)
        {
            AuthoredCollisionSource source = ordered[sourceOrdinal];
            switch (source)
            {
                case AuthoredConvexBrushCollisionSource brush:
                    Consider(
                        ref nearest,
                        TraceBrush(
                            source,
                            brush.Faces,
                            start,
                            end,
                            sourceOrdinal,
                            primitiveOrdinal: 0));
                    break;

                case AuthoredIndexedTriangleMeshCollisionSource mesh:
                    TraceMesh(
                        source,
                        mesh.Vertices,
                        mesh.Triangles,
                        start,
                        end,
                        sourceOrdinal,
                        primitiveOrdinalBase: 0,
                        ref nearest);
                    break;

                case AuthoredPairedStaticModelCollisionSource model:
                    TraceStaticModel(
                        model,
                        start,
                        end,
                        staticModelGeometry,
                        sourceOrdinal,
                        issues,
                        ref nearest);
                    break;

                default:
                    throw new InvalidDataException(
                        $"Unsupported authored collision source " +
                        $"{source.GetType().Name}.");
            }
        }

        CollisionOfflineTraceHit? hit = nearest is null
            ? null
            : new CollisionOfflineTraceHit(
                nearest.Source.ObjectId,
                nearest.Source.GeometryKind,
                nearest.PrimitiveKind,
                nearest.Fraction,
                Interpolate(start, end, nearest.Fraction),
                nearest.Material,
                nearest.StartsInside);
        return new CollisionOfflineTraceResult(hit, issues);
    }

    public static CollisionOfflineTraceResult TraceRay(
        IEnumerable<AuthoredCollisionSource> sources,
        MapVector3 origin,
        MapVector3 direction,
        float maximumDistance,
        ICollisionStaticModelLocalGeometryProvider? staticModelGeometry =
            null)
    {
        if (!origin.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!direction.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!float.IsFinite(maximumDistance) || maximumDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        }

        double lengthSquared =
            CollisionGeometryValidation.LengthSquared(direction);
        if (lengthSquared <=
            CollisionGeometryValidation.DegenerateAreaTolerance)
        {
            throw new ArgumentException(
                "An offline collision ray direction must be non-zero.",
                nameof(direction));
        }

        float inverseLength = checked(
            (float)(1d / Math.Sqrt(lengthSquared)));
        var end = new MapVector3(
            origin.X + direction.X * inverseLength * maximumDistance,
            origin.Y + direction.Y * inverseLength * maximumDistance,
            origin.Z + direction.Z * inverseLength * maximumDistance);
        if (!end.IsFinite)
        {
            throw new OverflowException(
                "Offline collision ray endpoint is outside the finite " +
                "coordinate range.");
        }

        return TraceSegment(
            sources,
            origin,
            end,
            staticModelGeometry);
    }

    private static void TraceStaticModel(
        AuthoredPairedStaticModelCollisionSource source,
        MapVector3 worldStart,
        MapVector3 worldEnd,
        ICollisionStaticModelLocalGeometryProvider? provider,
        int sourceOrdinal,
        ICollection<CollisionOfflineTraceIssue> issues,
        ref TraceCandidate? nearest)
    {
        if (provider is null)
        {
            issues.Add(new CollisionOfflineTraceIssue(
                CollisionOfflineTraceIssueKind
                    .MissingStaticModelGeometryProvider,
                source.ObjectId,
                source.ExactSerializedModelName,
                $"Static-model collision source {source.ObjectId} requires " +
                $"exact local geometry for XModel " +
                $"'{source.ExactSerializedModelName}', but no provider was " +
                "supplied."));
            return;
        }

        if (!provider.TryResolve(
                source.ExactSerializedModelName,
                out CollisionStaticModelLocalGeometry? geometry))
        {
            issues.Add(new CollisionOfflineTraceIssue(
                CollisionOfflineTraceIssueKind
                    .UnresolvedStaticModelGeometryDependency,
                source.ObjectId,
                source.ExactSerializedModelName,
                $"Static-model collision source {source.ObjectId} could not " +
                $"resolve exact local geometry for XModel " +
                $"'{source.ExactSerializedModelName}'."));
            return;
        }
        if (geometry is null)
        {
            issues.Add(new CollisionOfflineTraceIssue(
                CollisionOfflineTraceIssueKind
                    .InvalidStaticModelGeometryResolution,
                source.ObjectId,
                source.ExactSerializedModelName,
                $"The geometry provider reported XModel " +
                $"'{source.ExactSerializedModelName}' as resolved but " +
                "returned no geometry."));
            return;
        }

        MapVector3 localStart = TransformPointToModelLocal(
            worldStart,
            source.Placement);
        MapVector3 localEnd = TransformPointToModelLocal(
            worldEnd,
            source.Placement);
        int primitiveOrdinal = 0;
        foreach (CollisionStaticModelLocalConvexBrush brush in
                 geometry.Brushes)
        {
            Consider(
                ref nearest,
                TraceBrush(
                    source,
                    brush.Faces,
                    localStart,
                    localEnd,
                    sourceOrdinal,
                    primitiveOrdinal++));
        }

        foreach (CollisionStaticModelLocalIndexedTriangleMesh mesh in
                 geometry.Meshes)
        {
            TraceMesh(
                source,
                mesh.Vertices,
                mesh.Triangles,
                localStart,
                localEnd,
                sourceOrdinal,
                primitiveOrdinal,
                ref nearest);
            primitiveOrdinal = checked(
                primitiveOrdinal + mesh.Triangles.Count);
        }
    }

    private static MapVector3 TransformPointToModelLocal(
        MapVector3 worldPoint,
        AuthoredStaticModelCollisionPlacement placement)
    {
        MapVector3 relative = worldPoint - placement.Origin;
        IReadOnlyList<MapVector3> rows = placement.InverseScaledAxis;
        var local = new MapVector3(
            checked((float)CollisionGeometryValidation.Dot(
                rows[0],
                relative)),
            checked((float)CollisionGeometryValidation.Dot(
                rows[1],
                relative)),
            checked((float)CollisionGeometryValidation.Dot(
                rows[2],
                relative)));
        if (!local.IsFinite)
        {
            throw new OverflowException(
                "Static-model inverse placement produced a non-finite " +
                "local collision coordinate.");
        }

        return local;
    }

    private static void TraceMesh(
        AuthoredCollisionSource source,
        IReadOnlyList<MapVector3> vertices,
        IReadOnlyList<AuthoredIndexedCollisionTriangle> triangles,
        MapVector3 start,
        MapVector3 end,
        int sourceOrdinal,
        int primitiveOrdinalBase,
        ref TraceCandidate? nearest)
    {
        MapVector3 direction = end - start;
        for (int triangleOrdinal = 0;
             triangleOrdinal < triangles.Count;
             triangleOrdinal++)
        {
            AuthoredIndexedCollisionTriangle triangle =
                triangles[triangleOrdinal];
            double? fraction = IntersectTriangle(
                start,
                direction,
                vertices[triangle.Vertex0],
                vertices[triangle.Vertex1],
                vertices[triangle.Vertex2]);
            if (fraction is null)
                continue;

            Consider(
                ref nearest,
                new TraceCandidate(
                    source,
                    CollisionOfflineTracePrimitiveKind.Triangle,
                    fraction.Value,
                    triangle.Material,
                    StartsInside: false,
                    sourceOrdinal,
                    checked(primitiveOrdinalBase + triangleOrdinal)));
        }
    }

    private static TraceCandidate? TraceBrush(
        AuthoredCollisionSource source,
        IReadOnlyList<AuthoredConvexBrushFace> faces,
        MapVector3 start,
        MapVector3 end,
        int sourceOrdinal,
        int primitiveOrdinal)
    {
        double enter = 0d;
        double leave = 1d;
        AuthoredCollisionMaterialInput? enterMaterial = null;
        bool startsInside = true;

        foreach (AuthoredConvexBrushFace face in faces)
        {
            double startDistance =
                CollisionGeometryValidation.Dot(face.Plane.Normal, start) -
                face.Plane.Distance;
            double endDistance =
                CollisionGeometryValidation.Dot(face.Plane.Normal, end) -
                face.Plane.Distance;
            if (startDistance > BoundaryTolerance)
                startsInside = false;
            if (startDistance > BoundaryTolerance &&
                endDistance > BoundaryTolerance)
            {
                return null;
            }

            double delta = endDistance - startDistance;
            if (Math.Abs(delta) <= ParallelTolerance)
                continue;

            double crossing = -startDistance / delta;
            if (delta < 0d)
            {
                if (crossing > enter)
                {
                    enter = crossing;
                    enterMaterial = face.Material;
                }
            }
            else if (crossing < leave)
            {
                leave = crossing;
            }

            if (enter - leave > BoundaryTolerance)
                return null;
        }

        if (leave < -BoundaryTolerance ||
            enter > 1d + BoundaryTolerance)
        {
            return null;
        }

        return new TraceCandidate(
            source,
            CollisionOfflineTracePrimitiveKind.ConvexBrush,
            Math.Clamp(enter, 0d, 1d),
            startsInside ? null : enterMaterial,
            startsInside,
            sourceOrdinal,
            primitiveOrdinal);
    }

    private static double? IntersectTriangle(
        MapVector3 start,
        MapVector3 direction,
        MapVector3 vertex0,
        MapVector3 vertex1,
        MapVector3 vertex2)
    {
        MapVector3 edge01 = vertex1 - vertex0;
        MapVector3 edge02 = vertex2 - vertex0;
        MapVector3 p = CollisionGeometryValidation.Cross(
            direction,
            edge02);
        double determinant =
            CollisionGeometryValidation.Dot(edge01, p);
        if (Math.Abs(determinant) <= ParallelTolerance)
            return null;

        double inverse = 1d / determinant;
        MapVector3 fromVertex = start - vertex0;
        double u =
            CollisionGeometryValidation.Dot(fromVertex, p) * inverse;
        if (u < -BoundaryTolerance || u > 1d + BoundaryTolerance)
            return null;

        MapVector3 q = CollisionGeometryValidation.Cross(
            fromVertex,
            edge01);
        double v =
            CollisionGeometryValidation.Dot(direction, q) * inverse;
        if (v < -BoundaryTolerance ||
            u + v > 1d + BoundaryTolerance)
        {
            return null;
        }

        double fraction =
            CollisionGeometryValidation.Dot(edge02, q) * inverse;
        return fraction < -BoundaryTolerance ||
               fraction > 1d + BoundaryTolerance
            ? null
            : Math.Clamp(fraction, 0d, 1d);
    }

    private static void Consider(
        ref TraceCandidate? nearest,
        TraceCandidate? candidate)
    {
        if (candidate is null)
            return;
        if (nearest is null ||
            Compare(candidate, nearest) < 0)
        {
            nearest = candidate;
        }
    }

    private static int Compare(
        TraceCandidate left,
        TraceCandidate right)
    {
        int fraction = left.Fraction.CompareTo(right.Fraction);
        if (fraction != 0)
            return fraction;

        int source = left.SourceOrdinal.CompareTo(right.SourceOrdinal);
        if (source != 0)
            return source;

        int primitive = left.PrimitiveOrdinal.CompareTo(
            right.PrimitiveOrdinal);
        if (primitive != 0)
            return primitive;

        return left.PrimitiveKind.CompareTo(right.PrimitiveKind);
    }

    private static MapVector3 Interpolate(
        MapVector3 start,
        MapVector3 end,
        double fraction) =>
        new(
            checked((float)(start.X +
                ((double)end.X - start.X) * fraction)),
            checked((float)(start.Y +
                ((double)end.Y - start.Y) * fraction)),
            checked((float)(start.Z +
                ((double)end.Z - start.Z) * fraction)));

    private sealed record TraceCandidate(
        AuthoredCollisionSource Source,
        CollisionOfflineTracePrimitiveKind PrimitiveKind,
        double Fraction,
        AuthoredCollisionMaterialInput? Material,
        bool StartsInside,
        int SourceOrdinal,
        int PrimitiveOrdinal);
}
