using System.Numerics;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;

namespace IW4.Game.Codecs.MapEnts;

public static class TriggerGeometry
{
    public static IReadOnlyList<TriggerSlab> CreateSlabs(
        IReadOnlyList<CBrushSide> sides,
        Bounds bounds,
        int brushIndex)
    {
        ArgumentNullException.ThrowIfNull(sides);
        ValidateTriggerBounds(bounds, $"Collision brush {brushIndex}");

        var slabs = new List<TriggerSlab>(sides.Count);
        var usedSides = new bool[sides.Count];
        for (int sideIndex = 0; sideIndex < sides.Count; sideIndex++)
        {
            if (usedSides[sideIndex])
                continue;

            CPlane upperPlane = GetValidatedTriggerPlane(sides, brushIndex, sideIndex);
            int oppositeSideIndex = -1;
            for (int candidateIndex = 0; candidateIndex < sides.Count; candidateIndex++)
            {
                if (candidateIndex == sideIndex || usedSides[candidateIndex])
                    continue;
                CPlane candidate = GetValidatedTriggerPlane(sides, brushIndex, candidateIndex);
                if (!NearlySameTriggerFloat(upperPlane.Normal.X, -candidate.Normal.X) ||
                    !NearlySameTriggerFloat(upperPlane.Normal.Y, -candidate.Normal.Y) ||
                    !NearlySameTriggerFloat(upperPlane.Normal.Z, -candidate.Normal.Z))
                {
                    continue;
                }
                if (oppositeSideIndex >= 0)
                {
                    throw new InvalidDataException(
                        $"Collision brush {brushIndex} side {sideIndex} has more than one " +
                        "opposite non-axial plane.");
                }
                oppositeSideIndex = candidateIndex;
            }
            if (oppositeSideIndex < 0)
            {
                slabs.Add(DecodeSingleSidedTriggerSlab(
                    upperPlane,
                    bounds,
                    brushIndex,
                    sideIndex));
                usedSides[sideIndex] = true;
                continue;
            }

            CPlane lowerPlane = GetValidatedTriggerPlane(
                sides,
                brushIndex,
                oppositeSideIndex);
            float upper = upperPlane.Dist;
            float lower = -lowerPlane.Dist;
            float midpoint = (lower + upper) * 0.5f;
            float halfSize = (upper - lower) * 0.5f;
            if (!float.IsFinite(midpoint) || !float.IsFinite(halfSize) ||
                (halfSize < 0.0f && !NearlySameTriggerFloat(lower, upper)))
            {
                throw new InvalidDataException(
                    $"Collision brush {brushIndex} sides {sideIndex} and {oppositeSideIndex} " +
                    "produce an invalid trigger slab interval.");
            }
            if (halfSize < 0.0f)
                halfSize = 0.0f;

            usedSides[sideIndex] = true;
            usedSides[oppositeSideIndex] = true;
            slabs.Add(new TriggerSlab
            {
                Dir = upperPlane.Normal,
                MidPoint = midpoint,
                HalfSize = halfSize
            });
        }

        return slabs.AsReadOnly();
    }

    private static TriggerSlab DecodeSingleSidedTriggerSlab(
        CPlane upperPlane,
        Bounds bounds,
        int brushIndex,
        int sideIndex)
    {
        Vec3 normal = upperPlane.Normal;
        float lower =
            normal.X * bounds.MidPoint.X +
            normal.Y * bounds.MidPoint.Y +
            normal.Z * bounds.MidPoint.Z -
            MathF.Abs(normal.X) * bounds.HalfSize.X -
            MathF.Abs(normal.Y) * bounds.HalfSize.Y -
            MathF.Abs(normal.Z) * bounds.HalfSize.Z;
        float upper = upperPlane.Dist;
        float midpoint = (lower + upper) * 0.5f;
        float halfSize = (upper - lower) * 0.5f;
        if (!float.IsFinite(lower) || !float.IsFinite(midpoint) ||
            !float.IsFinite(halfSize) ||
            (halfSize < 0.0f && !NearlySameTriggerFloat(lower, upper)))
        {
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} and its axial bounds " +
                "produce an invalid trigger slab interval.");
        }
        if (halfSize < 0.0f)
            halfSize = 0.0f;

        return new TriggerSlab
        {
            Dir = normal,
            MidPoint = midpoint,
            HalfSize = halfSize
        };
    }

    private static CPlane GetValidatedTriggerPlane(
        IReadOnlyList<CBrushSide> sides,
        int brushIndex,
        int sideIndex)
    {
        CBrushSide side = sides[sideIndex] ??
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} is null.");
        CPlane plane = side.Plane ??
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} has no plane.");
        float normalX = RequireFinite(
            plane.Normal.X,
            $"collision brush {brushIndex} side {sideIndex} plane normal X");
        float normalY = RequireFinite(
            plane.Normal.Y,
            $"collision brush {brushIndex} side {sideIndex} plane normal Y");
        float normalZ = RequireFinite(
            plane.Normal.Z,
            $"collision brush {brushIndex} side {sideIndex} plane normal Z");
        RequireFinite(
            plane.Dist,
            $"collision brush {brushIndex} side {sideIndex} plane distance");
        float normalLengthSquared =
            normalX * normalX + normalY * normalY + normalZ * normalZ;
        if (!float.IsFinite(normalLengthSquared) || normalLengthSquared < 0.999f ||
            normalLengthSquared > 1.001f)
        {
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} plane normal is not unit length.");
        }
        return plane;
    }

    public static bool ContainsPoint(Bounds bounds, IReadOnlyList<TriggerSlab> slabs,
        Vector3 localPoint)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(slabs);
        if (!(MathF.Abs(localPoint.X - bounds.MidPoint.X) < bounds.HalfSize.X) ||
            !(MathF.Abs(localPoint.Y - bounds.MidPoint.Y) < bounds.HalfSize.Y) ||
            !(MathF.Abs(localPoint.Z - bounds.MidPoint.Z) < bounds.HalfSize.Z))
            return false;
        foreach (TriggerSlab slab in slabs)
        {
            float projected = slab.Dir.X * localPoint.X + slab.Dir.Y * localPoint.Y +
                slab.Dir.Z * localPoint.Z;
            if (!(MathF.Abs(projected - slab.MidPoint) < slab.HalfSize))
                return false;
        }
        return true;
    }

    private static void ValidateTriggerBounds(Bounds bounds, string description)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        RequireFinite(bounds.MidPoint.X, $"{description} midpoint X");
        RequireFinite(bounds.MidPoint.Y, $"{description} midpoint Y");
        RequireFinite(bounds.MidPoint.Z, $"{description} midpoint Z");
        float halfX = RequireFinite(bounds.HalfSize.X, $"{description} half-size X");
        float halfY = RequireFinite(bounds.HalfSize.Y, $"{description} half-size Y");
        float halfZ = RequireFinite(bounds.HalfSize.Z, $"{description} half-size Z");
        if (halfX < 0.0f || halfY < 0.0f || halfZ < 0.0f)
            throw new InvalidDataException($"{description} has negative bounds half-size.");
    }

    private static bool NearlySameTriggerFloat(float left, float right)
    {
        if (!float.IsFinite(left) || !float.IsFinite(right))
            return false;
        float scale = MathF.Max(1.0f, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
        return MathF.Abs(left - right) <= scale * 0.000001f;
    }

    private static float RequireFinite(float value, string description)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"The {description} is not finite.");
        return value;
    }
}
