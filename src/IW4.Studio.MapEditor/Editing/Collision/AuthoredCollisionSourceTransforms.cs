using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Editing.Collision;

/// <summary>
/// Pure canonical authored-collision transformations. Operations allocate a
/// replacement source and never mutate imported or authored source objects.
/// </summary>
public static class AuthoredCollisionSourceTransforms
{
    /// <summary>
    /// Returns the canonical source-space bounds used by editor tools and
    /// detached compiler candidates. Authored collision always owns finite,
    /// non-empty bounds at construction time.
    /// </summary>
    public static MapBounds GetBounds(AuthoredCollisionSource source) =>
        source switch
        {
            AuthoredConvexBrushCollisionSource brush => brush.Bounds,
            AuthoredIndexedTriangleMeshCollisionSource mesh => mesh.Bounds,
            AuthoredPairedStaticModelCollisionSource staticModel =>
                staticModel.Bounds,
            null => throw new ArgumentNullException(nameof(source)),
            _ => throw new NotSupportedException(
                $"Authored collision source type {source.GetType().Name} " +
                "does not expose canonical bounds.")
        };

    /// <summary>
    /// Stable translation handle for viewport manipulation. Geometry is
    /// translated from its committed source on every update, so pointer
    /// sampling never accumulates floating-point drift.
    /// </summary>
    public static MapVector3 GetTranslationAnchor(
        AuthoredCollisionSource source) =>
        GetBounds(source).MidPoint;

    public static AuthoredCollisionSource Translate(
        AuthoredCollisionSource source,
        MapVector3 offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireFinite(offset);
        if (offset == default)
            return source;

        return source switch
        {
            AuthoredConvexBrushCollisionSource brush =>
                Translate(brush, offset),
            AuthoredIndexedTriangleMeshCollisionSource mesh =>
                Translate(mesh, offset),
            AuthoredPairedStaticModelCollisionSource staticModel =>
                Translate(staticModel, offset),
            _ => throw new NotSupportedException(
                $"Authored collision source type {source.GetType().Name} " +
                "does not have translation semantics.")
        };
    }

    public static AuthoredConvexBrushCollisionSource Translate(
        AuthoredConvexBrushCollisionSource source,
        MapVector3 offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireFinite(offset);
        if (offset == default)
            return source;

        AuthoredConvexBrushFace[] translatedFaces = source.Faces
            .Select(face => new AuthoredConvexBrushFace(
                Translate(face.Plane, offset),
                face.Winding.Select(point =>
                    TranslatePoint(point, offset)),
                face.Material))
            .ToArray();
        return new AuthoredConvexBrushCollisionSource(
            source.ObjectId,
            source.Ownership,
            translatedFaces,
            source.Contents);
    }

    public static AuthoredIndexedTriangleMeshCollisionSource Translate(
        AuthoredIndexedTriangleMeshCollisionSource source,
        MapVector3 offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireFinite(offset);
        if (offset == default)
            return source;

        return new AuthoredIndexedTriangleMeshCollisionSource(
            source.ObjectId,
            (StandaloneWorldCollisionSourceOwnership)source.Ownership,
            source.Vertices.Select(point =>
                TranslatePoint(point, offset)),
            source.Triangles);
    }

    public static AuthoredPairedStaticModelCollisionSource Translate(
        AuthoredPairedStaticModelCollisionSource source,
        MapVector3 offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireFinite(offset);
        if (offset == default)
            return source;

        AuthoredStaticModelCollisionPlacement placement = source.Placement;
        return new AuthoredPairedStaticModelCollisionSource(
            source.ObjectId,
            (PairedStaticModelCollisionSourceOwnership)source.Ownership,
            source.ExactSerializedModelName,
            new AuthoredStaticModelCollisionPlacement(
                TranslatePoint(placement.Origin, offset),
                placement.InverseScaledAxis,
                TranslateBounds(placement.Bounds, offset)));
    }

    private static AuthoredCollisionPlane Translate(
        AuthoredCollisionPlane plane,
        MapVector3 offset)
    {
        double translatedDistance =
            plane.Distance +
            (double)plane.Normal.X * offset.X +
            (double)plane.Normal.Y * offset.Y +
            (double)plane.Normal.Z * offset.Z;
        float distance = (float)translatedDistance;
        if (!float.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Collision translation produces a non-finite brush plane.");
        }

        return new AuthoredCollisionPlane(
            plane.Normal,
            distance);
    }

    private static MapVector3 TranslatePoint(
        MapVector3 point,
        MapVector3 offset)
    {
        MapVector3 translated = point + offset;
        if (!translated.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Collision translation produces non-finite geometry.");
        }

        return translated;
    }

    private static MapBounds TranslateBounds(
        MapBounds bounds,
        MapVector3 offset)
    {
        MapBounds translated = bounds.Translate(offset);
        if (!translated.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Collision translation produces non-finite bounds.");
        }

        return translated;
    }

    private static void RequireFinite(MapVector3 offset)
    {
        if (!offset.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Collision translation must contain finite components.");
        }
    }
}
