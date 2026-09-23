using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>PS3 default_mp.elf 0x0034D128 / 0x0034D8F8.</summary>
internal static class MapRenderWorldDpvsPortalPlaneBuilder
{
    internal const int MaximumPlaneCount = 16;
    private const float PlaneInset = 0.001f;
    private const float ProjectionNearW = 0.125f;

    public static bool TryBuild(
        MapRenderWorldDpvsNormalCameraFrame camera,
        MapRenderWorldDpvsPortalTraversalSettings settings,
        ReadOnlySpan<Vector3> winding,
        out MapRenderWorldDpvsClipPlane[] planes,
        out bool clipChildren,
        out string? failure)
    {
        Span<MapRenderWorldDpvsClipPlane> result =
            stackalloc MapRenderWorldDpvsClipPlane[MaximumPlaneCount];
        bool succeeded = TryBuildInto(
            camera,
            settings,
            winding,
            result,
            out int resultCount,
            out clipChildren,
            out failure);
        planes = succeeded
            ? result[..resultCount].ToArray()
            : [];
        return succeeded;
    }

    /// <summary>
    /// Builds a child aperture directly into traversal-owned retained
    /// storage. The active prefix is published only after this method
    /// succeeds, so failed portal geometry cannot leak partial plane rows.
    /// </summary>
    internal static bool TryBuildInto(
        MapRenderWorldDpvsNormalCameraFrame camera,
        MapRenderWorldDpvsPortalTraversalSettings settings,
        ReadOnlySpan<Vector3> winding,
        Span<MapRenderWorldDpvsClipPlane> result,
        out int resultCount,
        out bool clipChildren,
        out string? failure)
    {
        if (result.Length < MaximumPlaneCount)
        {
            throw new ArgumentException(
                "Portal child-plane storage must retain the native sixteen-plane capacity.",
                nameof(result));
        }
        if (winding.Length is < 3 or > 64)
        {
            resultCount = 0;
            clipChildren = false;
            failure = "Queued portal convex hull must contain three through sixty-four vertices.";
            return false;
        }

        Span<Vector3> normals =
            stackalloc Vector3[64];
        GetSidePlaneNormals(
            camera.Origin,
            winding,
            normals);
        ReadOnlySpan<Vector3> activeNormals =
            normals[..winding.Length];
        bool useNormalPlanes = winding.Length <= 10;
        bool forceBevels =
            winding.Length > 10 ||
            settings.PortalBevelsOnly;
        bool useBevelPlanes = forceBevels || settings.PortalBevels > 0f;
        resultCount = 0;

        if (useBevelPlanes || settings.PortalMinClipArea > 0f)
        {
            if (!TryProjectPortal(
                    camera,
                    settings.PortalMinClipArea,
                    winding,
                    out Vector2 minimum,
                    out Vector2 maximum,
                    out clipChildren,
                    out failure))
            {
                return false;
            }
            if (useBevelPlanes && !TryAddBevelPlanes(
                    camera,
                    settings.PortalBevels,
                    forceBevels,
                    activeNormals,
                    minimum,
                    maximum,
                    result,
                    ref resultCount,
                    out failure))
            {
                return false;
            }
        }
        else
        {
            clipChildren = true;
        }

        if (useNormalPlanes)
        {
            for (int index = 0;
                 index < activeNormals.Length;
                 index++)
            {
                Vector3 normal = activeNormals[index];
                if (normal.LengthSquared() == 0f)
                    continue;
                if (!TryAddPlane(
                        result,
                        ref resultCount,
                        new(
                            normal.X,
                            normal.Y,
                            normal.Z,
                            PlaneInset - Vector3.Dot(normal, winding[index])),
                        out failure))
                {
                    return false;
                }
            }
        }

        MapRenderWorldDpvsClipPlane nearPlane = camera.NearPlane;
        float nearestDistance = NearestPointOnWinding(winding, nearPlane);
        if (nearestDistance > 0f)
        {
            nearPlane = nearPlane with
            {
                CoefficientW = nearPlane.CoefficientW - nearestDistance
            };
        }
        if (!TryAddPlane(
                result,
                ref resultCount,
                nearPlane,
                out failure))
        {
            return false;
        }
        if (camera.FarPlane is { } farPlane &&
            !TryAddPlane(
                result,
                ref resultCount,
                farPlane,
                out failure))
        {
            return false;
        }

        failure = null;
        return true;
    }

    private static void GetSidePlaneNormals(
        Vector3 origin,
        ReadOnlySpan<Vector3> winding,
        Span<Vector3> normals)
    {
        if (normals.Length < winding.Length)
            throw new ArgumentException(
                "The side-plane destination is too small.",
                nameof(normals));

        Span<Vector3> deltas =
            stackalloc Vector3[65];
        for (int index = 0; index < winding.Length; index++)
            deltas[index] = winding[index] - origin;
        deltas[winding.Length] = deltas[0];

        for (int index = 0; index < winding.Length; index++)
        {
            Vector3 normal = Vector3.Cross(deltas[index + 1], deltas[index]);
            float length = normal.Length();
            normals[index] = length > 0f ? normal / length : Vector3.Zero;
        }
    }

    private static bool TryProjectPortal(
        MapRenderWorldDpvsNormalCameraFrame camera,
        float minimumClipArea,
        ReadOnlySpan<Vector3> winding,
        out Vector2 minimum,
        out Vector2 maximum,
        out bool clipChildren,
        out string? failure)
    {
        minimum = Vector2.One;
        maximum = -Vector2.One;
        Span<Vector2> projected =
            stackalloc Vector2[64];
        for (int index = 0; index < winding.Length; index++)
        {
            Vector4 clip = Vector4.Transform(
                new Vector4(winding[index], 1f),
                camera.ViewProjection);
            if (!IsFinite(clip))
            {
                clipChildren = false;
                failure = "Portal projection produced a non-finite clip coordinate.";
                return false;
            }
            if (clip.W < ProjectionNearW)
            {
                minimum = -Vector2.One;
                maximum = Vector2.One;
                clipChildren = true;
                failure = null;
                return true;
            }

            float inverseW = 1f / clip.W;
            projected[index] = new(clip.X * inverseW, clip.Y * inverseW);
            minimum = Vector2.Min(minimum, projected[index]);
            maximum = Vector2.Max(maximum, projected[index]);
        }

        float boundsArea =
            (maximum.X - minimum.X) *
            (maximum.Y - minimum.Y) * 0.25f;
        if (!float.IsFinite(boundsArea) || boundsArea < 0f)
        {
            clipChildren = false;
            failure = "Portal projection produced an invalid screen-space bounds area.";
            return false;
        }
        if (boundsArea < minimumClipArea)
        {
            clipChildren = false;
            failure = null;
            return true;
        }

        float windingArea = 0f;
        for (int index = 0; index < winding.Length; index++)
        {
            int previous =
                (index + winding.Length - 1) %
                winding.Length;
            int next = (index + 1) % winding.Length;
            windingArea +=
                (projected[next].Y - projected[previous].Y) *
                projected[index].X;
        }
        windingArea *= 0.125f;
        if (!float.IsFinite(windingArea))
        {
            clipChildren = false;
            failure = "Portal projection produced a non-finite signed winding area.";
            return false;
        }

        // default_mp 0x0034E1A0..0x0034E204 in function 0x0034D8F8
        // keeps this area signed. The native path compares
        // r_portalMinClipArea directly with the signed result and stores false
        // when the winding is negative; it does not reject the camera
        // traversal. A false result selects the existing all-further-cells
        // path, preserving conservative visibility without replacing the
        // current frame with generic preview submissions.
        clipChildren = minimumClipArea <= windingArea;
        failure = null;
        return true;
    }

    private static bool TryAddBevelPlanes(
        MapRenderWorldDpvsNormalCameraFrame camera,
        float bevelThreshold,
        bool forceBevels,
        ReadOnlySpan<Vector3> windingNormals,
        Vector2 minimum,
        Vector2 maximum,
        Span<MapRenderWorldDpvsClipPlane> planes,
        ref int planeCount,
        out string? failure)
    {
        Span<Vector2> projectedCorners =
        stackalloc Vector2[4]
        {
            new(minimum.X, maximum.Y),
            new(minimum.X, minimum.Y),
            new(maximum.X, minimum.Y),
            new(maximum.X, maximum.Y)
        };
        Span<Vector3> corners =
            stackalloc Vector3[4];
        for (int index = 0; index < corners.Length; index++)
        {
            Vector4 unprojected = Vector4.Transform(
                new Vector4(projectedCorners[index], 0f, 1f),
                camera.InverseViewProjection);
            if (!IsFinite(unprojected) || unprojected.W == 0f)
            {
                failure = "Portal bevel unprojection produced an invalid homogeneous coordinate.";
                return false;
            }
            corners[index] = new Vector3(unprojected.X, unprojected.Y, unprojected.Z) /
                unprojected.W;
        }

        Span<Vector3> bevelNormals =
            stackalloc Vector3[4];
        for (int index = 0; index < corners.Length; index++)
        {
            Vector3 current = corners[index] - camera.Origin;
            Vector3 next =
                corners[(index + 1) % corners.Length] -
                camera.Origin;
            Vector3 normal = Vector3.Cross(next, current);
            float length = normal.Length();
            bevelNormals[index] =
                length > 0f
                    ? normal / length
                    : Vector3.Zero;
        }
        for (int index = 0; index < bevelNormals.Length; index++)
        {
            Vector3 normal = bevelNormals[index];
            bool matchesWinding = false;
            if (!forceBevels)
            {
                for (int windingIndex = 0;
                     windingIndex < windingNormals.Length;
                     windingIndex++)
                {
                    if (Vector3.Dot(
                            windingNormals[windingIndex],
                            normal) > bevelThreshold)
                    {
                        matchesWinding = true;
                        break;
                    }
                }
            }
            if (matchesWinding)
            {
                continue;
            }
            if (!TryAddPlane(
                    planes,
                    ref planeCount,
                    new(
                        normal.X,
                        normal.Y,
                        normal.Z,
                        PlaneInset - Vector3.Dot(normal, corners[index])),
                    out failure))
            {
                return false;
            }
        }

        failure = null;
        return true;
    }

    private static float NearestPointOnWinding(
        ReadOnlySpan<Vector3> winding,
        MapRenderWorldDpvsClipPlane plane)
    {
        float first = SignedDistance(plane, winding[0]);
        float last = SignedDistance(plane, winding[^1]);
        if (last <= first)
        {
            float minimum = last;
            for (int index = winding.Length - 2; index > 0; index--)
            {
                float distance = SignedDistance(plane, winding[index]);
                if (distance > minimum)
                    break;
                minimum = distance;
            }
            return minimum;
        }

        float forwardMinimum = first;
        for (int index = 1; index < winding.Length - 1; index++)
        {
            float distance = SignedDistance(plane, winding[index]);
            if (distance > forwardMinimum)
                break;
            forwardMinimum = distance;
        }
        return forwardMinimum;
    }

    private static bool TryAddPlane(
        Span<MapRenderWorldDpvsClipPlane> planes,
        ref int planeCount,
        MapRenderWorldDpvsClipPlane plane,
        out string? failure)
    {
        if (planeCount == planes.Length)
        {
            failure = "Portal clip-plane generation exceeded the PS3 sixteen-plane command capacity.";
            return false;
        }
        if (!IsFinite(plane))
        {
            failure = "Portal clip-plane generation produced a non-finite coefficient.";
            return false;
        }

        planes[planeCount++] = plane;
        failure = null;
        return true;
    }

    private static float SignedDistance(
        MapRenderWorldDpvsClipPlane plane,
        Vector3 point) =>
        point.X * plane.NormalX +
        point.Y * plane.NormalY +
        point.Z * plane.NormalZ +
        plane.CoefficientW;

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static bool IsFinite(MapRenderWorldDpvsClipPlane value) =>
        float.IsFinite(value.NormalX) &&
        float.IsFinite(value.NormalY) &&
        float.IsFinite(value.NormalZ) &&
        float.IsFinite(value.CoefficientW);
}
