using System.Numerics;

namespace IW4.Render.Geometry;

/// <summary>
/// Pure direction transforms for PS3 static-model placement bases. Packed
/// placements use a uniform scale, so normal and tangent directions share the
/// same basis transform and are normalized after translation-free expansion.
/// </summary>
internal static class StaticVertexBasisTransformer
{
    private const float MinimumDirectionLengthSquared = 1e-12f;

    internal static bool TryTransformNormal(
        Vector3 localNormal,
        StaticModelPlacement placement,
        out Vector3 transformedNormal) =>
        TryTransformDirection(localNormal, placement, out transformedNormal);

    internal static bool TryTransformTangent(
        Vector3 localTangent,
        StaticModelPlacement placement,
        out Vector3 transformedTangent) =>
        TryTransformDirection(localTangent, placement, out transformedTangent);

    internal static bool TryNormalizeDirection(Vector3 value, out Vector3 normalized)
    {
        normalized = default;
        if (!IsFinite(value))
            return false;

        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) ||
            lengthSquared <= MinimumDirectionLengthSquared)
        {
            return false;
        }

        Vector3 candidate = value / MathF.Sqrt(lengthSquared);
        if (!IsFinite(candidate))
            return false;

        normalized = candidate;
        return true;
    }

    private static bool TryTransformDirection(
        Vector3 localDirection,
        StaticModelPlacement placement,
        out Vector3 transformedDirection)
    {
        transformedDirection = default;
        if (!TryNormalizeDirection(localDirection, out Vector3 normalizedLocal) ||
            !IsFinite(placement.Axis0) ||
            !IsFinite(placement.Axis1) ||
            !IsFinite(placement.Axis2))
        {
            return false;
        }

        Vector3 candidate =
            placement.Axis0 * normalizedLocal.X +
            placement.Axis1 * normalizedLocal.Y +
            placement.Axis2 * normalizedLocal.Z;
        return TryNormalizeDirection(candidate, out transformedDirection);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
