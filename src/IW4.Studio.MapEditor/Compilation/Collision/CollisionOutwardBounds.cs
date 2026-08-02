using IW4.Studio.MapEditor.Editing.Objects;
using AssetBounds = IW4.Assets.Math.Bounds;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Builds float bounds that never contract their source extents through
/// midpoint/half-size rounding. Collision spatial envelopes must round
/// outward because an inward ULP can create a false-negative traversal.
/// </summary>
internal static class CollisionOutwardBounds
{
    public static MapBounds FromVertices(
        IEnumerable<MapVector3> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        MapVector3[] copy = vertices.ToArray();
        if (copy.Length == 0 ||
            copy.Any(value => !value.IsFinite))
        {
            throw new ArgumentException(
                "Collision bounds require finite vertices.",
                nameof(vertices));
        }

        return FromExtents(
            copy.Min(value => (double)value.X),
            copy.Min(value => (double)value.Y),
            copy.Min(value => (double)value.Z),
            copy.Max(value => (double)value.X),
            copy.Max(value => (double)value.Y),
            copy.Max(value => (double)value.Z));
    }

    public static MapBounds Include(IEnumerable<MapBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        MapBounds[] copy = bounds.ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException(
                "Collision bounds collection cannot be empty.",
                nameof(bounds));
        }
        Validate(copy);

        return FromExtents(
            copy.Min(value =>
                (double)value.MidPoint.X - value.HalfSize.X),
            copy.Min(value =>
                (double)value.MidPoint.Y - value.HalfSize.Y),
            copy.Min(value =>
                (double)value.MidPoint.Z - value.HalfSize.Z),
            copy.Max(value =>
                (double)value.MidPoint.X + value.HalfSize.X),
            copy.Max(value =>
                (double)value.MidPoint.Y + value.HalfSize.Y),
            copy.Max(value =>
                (double)value.MidPoint.Z + value.HalfSize.Z));
    }

    public static MapBounds Expand(MapBounds bounds, float amount)
    {
        Validate([bounds]);
        if (!float.IsFinite(amount) || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        return FromExtents(
            (double)bounds.MidPoint.X - bounds.HalfSize.X - amount,
            (double)bounds.MidPoint.Y - bounds.HalfSize.Y - amount,
            (double)bounds.MidPoint.Z - bounds.HalfSize.Z - amount,
            (double)bounds.MidPoint.X + bounds.HalfSize.X + amount,
            (double)bounds.MidPoint.Y + bounds.HalfSize.Y + amount,
            (double)bounds.MidPoint.Z + bounds.HalfSize.Z + amount);
    }

    public static MapVector3 Minimum(MapBounds bounds)
    {
        Validate([bounds]);
        return new MapVector3(
            bounds.MidPoint.X - bounds.HalfSize.X,
            bounds.MidPoint.Y - bounds.HalfSize.Y,
            bounds.MidPoint.Z - bounds.HalfSize.Z);
    }

    public static MapVector3 Maximum(MapBounds bounds)
    {
        Validate([bounds]);
        return new MapVector3(
            bounds.MidPoint.X + bounds.HalfSize.X,
            bounds.MidPoint.Y + bounds.HalfSize.Y,
            bounds.MidPoint.Z + bounds.HalfSize.Z);
    }

    public static AssetBounds ToAsset(MapBounds bounds) =>
        new()
        {
            MidPoint = ToAsset(bounds.MidPoint),
            HalfSize = ToAsset(bounds.HalfSize)
        };

    private static MapBounds FromExtents(
        double minimumX,
        double minimumY,
        double minimumZ,
        double maximumX,
        double maximumY,
        double maximumZ)
    {
        (float midpointX, float halfSizeX) =
            CompileAxis(minimumX, maximumX);
        (float midpointY, float halfSizeY) =
            CompileAxis(minimumY, maximumY);
        (float midpointZ, float halfSizeZ) =
            CompileAxis(minimumZ, maximumZ);
        return new MapBounds(
            new MapVector3(midpointX, midpointY, midpointZ),
            new MapVector3(halfSizeX, halfSizeY, halfSizeZ));
    }

    private static (float Midpoint, float HalfSize) CompileAxis(
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                "Collision bounds extents must be finite and ordered.");
        }

        float midpoint = CanonicalizeZero(
            checked((float)(minimum + (maximum - minimum) * 0.5d)));
        double requiredHalfSize = Math.Max(
            (double)midpoint - minimum,
            maximum - midpoint);
        float halfSize = CanonicalizeZero(
            checked((float)requiredHalfSize));
        if (!float.IsFinite(midpoint) || !float.IsFinite(halfSize))
        {
            throw new OverflowException(
                "Collision bounds cannot represent finite midpoint and " +
                "half-size values.");
        }

        // IW4 consumers reconstruct bounds with float arithmetic. Test the
        // same rounded endpoints rather than only their exact double sums.
        while (midpoint - halfSize > minimum ||
               midpoint + halfSize < maximum)
        {
            halfSize = MathF.BitIncrement(halfSize);
            if (!float.IsFinite(halfSize))
            {
                throw new OverflowException(
                    "Collision bounds cannot represent an outward-rounded " +
                    "finite half-size.");
            }
        }

        return (midpoint, halfSize);
    }

    private static void Validate(IReadOnlyList<MapBounds> values)
    {
        if (values.Any(value =>
                !value.IsFinite ||
                value.HalfSize.X < 0 ||
                value.HalfSize.Y < 0 ||
                value.HalfSize.Z < 0))
        {
            throw new ArgumentException(
                "Collision bounds must be finite and non-negative.",
                nameof(values));
        }
    }

    private static float CanonicalizeZero(float value) =>
        value == 0f ? 0f : value;

    private static AssetVector3 ToAsset(MapVector3 value) =>
        new() { X = value.X, Y = value.Y, Z = value.Z };
}
