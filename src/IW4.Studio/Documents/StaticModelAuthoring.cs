using IW4.Assets.Math;

namespace IW4.Studio.Documents;

/// <summary>
/// One absolute translation request for an existing compiled static-model
/// row. Cross-asset identity and save authority remain the responsibility of
/// the map-compilation layer.
/// </summary>
public readonly record struct StaticModelTranslationEdit
{
    public StaticModelTranslationEdit(
        int sourceOrdinal,
        float x,
        float y,
        float z)
    {
        if (sourceOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceOrdinal));
        if (!float.IsFinite(x) ||
            !float.IsFinite(y) ||
            !float.IsFinite(z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                "Static-model translation coordinates must be finite.");
        }

        SourceOrdinal = sourceOrdinal;
        X = x;
        Y = y;
        Z = z;
    }

    public int SourceOrdinal { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    internal Vec3 ToVec3() =>
        new()
        {
            X = X,
            Y = Y,
            Z = Z
        };
}

/// <summary>
/// Outward-rounded midpoint/half-size envelope operations shared by the
/// detached Gfx and collision authoring paths.
/// </summary>
internal static class StaticModelSpatialEnvelope
{
    // Official IW4 compiler output can place a child endpoint one or two
    // binary32 ULPs outside its serialized parent after midpoint/half-size
    // reconstruction. In the official terminal audit the largest deficit is
    // 0.001953125 world units. This bound admits that source rounding while
    // remaining far below an authoring translation; larger discrepancies
    // continue to fail closed.
    public const float ImportedBoundaryTolerance = 1f / 256f;

    public static Bounds Include(Bounds current, Bounds required)
    {
        Validate(current, nameof(current));
        Validate(required, nameof(required));
        if (Contains(current, required))
            return Copy(current);

        double minimumX = Math.Min(
            (double)current.MidPoint.X - current.HalfSize.X,
            (double)required.MidPoint.X - required.HalfSize.X);
        double minimumY = Math.Min(
            (double)current.MidPoint.Y - current.HalfSize.Y,
            (double)required.MidPoint.Y - required.HalfSize.Y);
        double minimumZ = Math.Min(
            (double)current.MidPoint.Z - current.HalfSize.Z,
            (double)required.MidPoint.Z - required.HalfSize.Z);
        double maximumX = Math.Max(
            (double)current.MidPoint.X + current.HalfSize.X,
            (double)required.MidPoint.X + required.HalfSize.X);
        double maximumY = Math.Max(
            (double)current.MidPoint.Y + current.HalfSize.Y,
            (double)required.MidPoint.Y + required.HalfSize.Y);
        double maximumZ = Math.Max(
            (double)current.MidPoint.Z + current.HalfSize.Z,
            (double)required.MidPoint.Z + required.HalfSize.Z);

        return new Bounds
        {
            MidPoint = new Vec3
            {
                X = Midpoint(minimumX, maximumX),
                Y = Midpoint(minimumY, maximumY),
                Z = Midpoint(minimumZ, maximumZ)
            },
            HalfSize = new Vec3
            {
                X = OutwardHalfSize(minimumX, maximumX),
                Y = OutwardHalfSize(minimumY, maximumY),
                Z = OutwardHalfSize(minimumZ, maximumZ)
            }
        };
    }

    public static bool Contains(Bounds outer, Bounds inner) =>
        ContainsAxis(
            outer.MidPoint.X,
            outer.HalfSize.X,
            inner.MidPoint.X,
            inner.HalfSize.X,
            tolerance: 0f) &&
        ContainsAxis(
            outer.MidPoint.Y,
            outer.HalfSize.Y,
            inner.MidPoint.Y,
            inner.HalfSize.Y,
            tolerance: 0f) &&
        ContainsAxis(
            outer.MidPoint.Z,
            outer.HalfSize.Z,
            inner.MidPoint.Z,
            inner.HalfSize.Z,
            tolerance: 0f);

    public static bool ContainsImported(Bounds outer, Bounds inner) =>
        ContainsAxis(
            outer.MidPoint.X,
            outer.HalfSize.X,
            inner.MidPoint.X,
            inner.HalfSize.X,
            ImportedBoundaryTolerance) &&
        ContainsAxis(
            outer.MidPoint.Y,
            outer.HalfSize.Y,
            inner.MidPoint.Y,
            inner.HalfSize.Y,
            ImportedBoundaryTolerance) &&
        ContainsAxis(
            outer.MidPoint.Z,
            outer.HalfSize.Z,
            inner.MidPoint.Z,
            inner.HalfSize.Z,
            ImportedBoundaryTolerance);

    public static void Validate(Bounds value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!float.IsFinite(value.MidPoint.X) ||
            !float.IsFinite(value.MidPoint.Y) ||
            !float.IsFinite(value.MidPoint.Z) ||
            !float.IsFinite(value.HalfSize.X) ||
            !float.IsFinite(value.HalfSize.Y) ||
            !float.IsFinite(value.HalfSize.Z) ||
            value.HalfSize.X < 0 ||
            value.HalfSize.Y < 0 ||
            value.HalfSize.Z < 0)
        {
            throw new InvalidDataException(
                $"{parameterName} must be a finite midpoint/nonnegative-half-size AABB.");
        }
    }

    public static Bounds Copy(Bounds value) =>
        new()
        {
            MidPoint = Copy(value.MidPoint),
            HalfSize = Copy(value.HalfSize)
        };

    public static Vec3 Copy(Vec3 value) =>
        new()
        {
            X = value.X,
            Y = value.Y,
            Z = value.Z
        };

    public static Vec3 Translate(Vec3 value, Vec3 delta) =>
        new()
        {
            X = CheckedFinite(value.X + delta.X),
            Y = CheckedFinite(value.Y + delta.Y),
            Z = CheckedFinite(value.Z + delta.Z)
        };

    private static bool ContainsAxis(
        float outerMidpoint,
        float outerHalfSize,
        float innerMidpoint,
        float innerHalfSize,
        float tolerance) =>
        (double)outerMidpoint - outerHalfSize - tolerance <=
            (double)innerMidpoint - innerHalfSize &&
        (double)outerMidpoint + outerHalfSize + tolerance >=
            (double)innerMidpoint + innerHalfSize;

    private static float Midpoint(double minimum, double maximum)
    {
        float result = checked((float)(minimum + (maximum - minimum) / 2d));
        if (!float.IsFinite(result))
        {
            throw new InvalidDataException(
                "The rebuilt static-model spatial midpoint exceeds the IW4 float domain.");
        }
        return result;
    }

    private static float OutwardHalfSize(
        double minimum,
        double maximum)
    {
        float midpoint = Midpoint(minimum, maximum);
        float halfSize =
            checked((float)((maximum - minimum) / 2d));
        if (!float.IsFinite(halfSize) || halfSize < 0)
        {
            throw new InvalidDataException(
                "The rebuilt static-model spatial half-size exceeds the IW4 float domain.");
        }
        while ((double)midpoint - halfSize > minimum ||
               (double)midpoint + halfSize < maximum)
        {
            halfSize = MathF.BitIncrement(halfSize);
            if (!float.IsFinite(halfSize))
            {
                throw new InvalidDataException(
                    "The rebuilt static-model spatial envelope cannot be represented without inward rounding.");
            }
        }
        return halfSize;
    }

    private static float CheckedFinite(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                "The translated static-model coordinate exceeds the IW4 float domain.");
        }
        return value;
    }
}
