using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Canonical construction primitives for editor-authored collision. These
/// helpers create semantic source geometry only; they do not allocate compiled
/// ordinals, spatial topology, dependencies, or persistence authority.
/// </summary>
public static class AuthoredCollisionPrimitiveFactory
{
    private static readonly MapVector3[] AxisAlignedOutwardNormals =
    [
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0),
        new(0, 0, 1),
        new(0, 0, -1)
    ];

    /// <summary>
    /// Creates one closed, axis-aligned, standalone collision brush. A single
    /// material owns all six faces and therefore supplies the brush-wide
    /// contents value required by the bounded M3 brush compiler.
    /// </summary>
    public static AuthoredConvexBrushCollisionSource
        CreateStandaloneAxisAlignedBox(
            MapObjectId objectId,
            MapBounds bounds,
            AuthoredCollisionMaterialInput material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ValidateBox(bounds);

        MapVector3 minimum = new(
            bounds.MidPoint.X - bounds.HalfSize.X,
            bounds.MidPoint.Y - bounds.HalfSize.Y,
            bounds.MidPoint.Z - bounds.HalfSize.Z);
        MapVector3 maximum = new(
            bounds.MidPoint.X + bounds.HalfSize.X,
            bounds.MidPoint.Y + bounds.HalfSize.Y,
            bounds.MidPoint.Z + bounds.HalfSize.Z);
        if (!minimum.IsFinite || !maximum.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "Axis-aligned collision-box corners must remain finite.");
        }

        return new AuthoredConvexBrushCollisionSource(
            objectId,
            new StandaloneWorldCollisionSourceOwnership(),
            [
                Face(
                    new MapVector3(1, 0, 0),
                    maximum.X,
                    material,
                    new(maximum.X, minimum.Y, minimum.Z),
                    new(maximum.X, maximum.Y, minimum.Z),
                    new(maximum.X, maximum.Y, maximum.Z),
                    new(maximum.X, minimum.Y, maximum.Z)),
                Face(
                    new MapVector3(-1, 0, 0),
                    -minimum.X,
                    material,
                    new(minimum.X, minimum.Y, minimum.Z),
                    new(minimum.X, minimum.Y, maximum.Z),
                    new(minimum.X, maximum.Y, maximum.Z),
                    new(minimum.X, maximum.Y, minimum.Z)),
                Face(
                    new MapVector3(0, 1, 0),
                    maximum.Y,
                    material,
                    new(minimum.X, maximum.Y, minimum.Z),
                    new(minimum.X, maximum.Y, maximum.Z),
                    new(maximum.X, maximum.Y, maximum.Z),
                    new(maximum.X, maximum.Y, minimum.Z)),
                Face(
                    new MapVector3(0, -1, 0),
                    -minimum.Y,
                    material,
                    new(minimum.X, minimum.Y, minimum.Z),
                    new(maximum.X, minimum.Y, minimum.Z),
                    new(maximum.X, minimum.Y, maximum.Z),
                    new(minimum.X, minimum.Y, maximum.Z)),
                Face(
                    new MapVector3(0, 0, 1),
                    maximum.Z,
                    material,
                    new(minimum.X, minimum.Y, maximum.Z),
                    new(maximum.X, minimum.Y, maximum.Z),
                    new(maximum.X, maximum.Y, maximum.Z),
                    new(minimum.X, maximum.Y, maximum.Z)),
                Face(
                    new MapVector3(0, 0, -1),
                    -minimum.Z,
                    material,
                    new(minimum.X, minimum.Y, minimum.Z),
                    new(minimum.X, maximum.Y, minimum.Z),
                    new(maximum.X, maximum.Y, minimum.Z),
                    new(maximum.X, minimum.Y, minimum.Z))
            ],
            unchecked((uint)material.Contents));
    }

    /// <summary>
    /// Reshapes a box created by this factory while preserving semantic
    /// identity, explicit standalone ownership, and exact material fields.
    /// Arbitrary convex brushes fail closed instead of being approximated as
    /// boxes.
    /// </summary>
    public static AuthoredConvexBrushCollisionSource
        ReshapeStandaloneAxisAlignedBox(
            AuthoredConvexBrushCollisionSource source,
            MapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!TryGetStandaloneAxisAlignedBoxMaterial(
                source,
                out AuthoredCollisionMaterialInput material))
        {
            throw new ArgumentException(
                "Only a canonical single-material standalone axis-aligned " +
                "box can use the bounded box reshape operation.",
                nameof(source));
        }

        return CreateStandaloneAxisAlignedBox(
            source.ObjectId,
            bounds,
            material);
    }

    public static bool TryGetStandaloneAxisAlignedBoxMaterial(
        AuthoredConvexBrushCollisionSource source,
        out AuthoredCollisionMaterialInput material)
    {
        ArgumentNullException.ThrowIfNull(source);
        material = null!;
        if (source.Ownership is not
                StandaloneWorldCollisionSourceOwnership ||
            source.Faces.Count != AxisAlignedOutwardNormals.Length)
        {
            return false;
        }

        AuthoredCollisionMaterialInput[] materials = source.Faces
            .Select(value => value.Material)
            .Distinct()
            .ToArray();
        if (materials.Length != 1 ||
            unchecked((uint)materials[0].Contents) != source.Contents)
        {
            return false;
        }

        bool hasExactAxes = AxisAlignedOutwardNormals.All(normal =>
            source.Faces.Count(face => face.Plane.Normal == normal) == 1);
        if (!hasExactAxes)
            return false;

        material = materials[0];
        return true;
    }

    private static AuthoredConvexBrushFace Face(
        MapVector3 normal,
        float distance,
        AuthoredCollisionMaterialInput material,
        params MapVector3[] winding) =>
        new(
            new AuthoredCollisionPlane(normal, distance),
            winding,
            material);

    private static void ValidateBox(MapBounds bounds)
    {
        if (!bounds.IsFinite ||
            !(bounds.HalfSize.X > 0f) ||
            !(bounds.HalfSize.Y > 0f) ||
            !(bounds.HalfSize.Z > 0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "A standalone collision box requires finite, positive half " +
                "sizes on every axis.");
        }
    }
}
