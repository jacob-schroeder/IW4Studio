using System.Numerics;

using IW4.Assets.Assets.XModel;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Scheduling.Dpvs;
using IW4.Render.Visibility;

namespace IW4.Render.Scheduling.StaticModels;

/// <summary>
/// Scalar PS3 static-model LOD selection. The caller supplies the two distance
/// values after the view-list builder's distance/placement-scale and
/// view-scalar calculation.
/// </summary>
public static class MapRenderStaticModelLodSelector
{
    public const int CulledLodIndex = -1;

    public static bool TrySelect(
        XModelAsset? model,
        float nearScaledDistance,
        float farScaledDistance,
        out int lodIndex)
    {
        lodIndex = CulledLodIndex;
        if (model is null ||
            !IsValidDistance(nearScaledDistance) ||
            !IsValidDistance(farScaledDistance))
        {
            return false;
        }

        int lodCount = unchecked((sbyte)model.NumLods);
        int firstLoadedLod = unchecked((sbyte)model.MaxLoadedLod);
        if (lodCount <= 0 ||
            lodCount > model.Lods.Count ||
            firstLoadedLod < 0 ||
            firstLoadedLod >= lodCount)
        {
            return false;
        }

        for (int index = firstLoadedLod; index < lodCount; index++)
        {
            if (!float.IsFinite(model.Lods[index].Dist))
                return false;
        }

        int lastLod = lodCount - 1;
        int selected = firstLoadedLod;
        while (selected < lastLod)
        {
            if (model.Lods[selected].Dist > nearScaledDistance)
            {
                lodIndex = selected;
                return true;
            }

            selected++;
        }

        lodIndex = model.Lods[lastLod].Dist <= farScaledDistance
            ? CulledLodIndex
            : lastLod;
        return true;
    }

    /// <summary>
    /// Applies the caller-side distance calculation. The two view scalars
    /// remain explicit because their runtime/dvar producers are not part of
    /// XModel and must not be inferred by this selector.
    /// </summary>
    public static bool TrySelectForCameraDistance(
        XModelAsset? model,
        float cameraDistance,
        float placementScale,
        float nearViewScale,
        float farViewScale,
        out int lodIndex)
    {
        lodIndex = CulledLodIndex;
        if (!IsValidDistance(cameraDistance) ||
            !(placementScale > 0f) ||
            !float.IsFinite(placementScale) ||
            !IsValidDistance(nearViewScale) ||
            !IsValidDistance(farViewScale))
        {
            return false;
        }

        float localDistance = cameraDistance / placementScale;
        return TrySelect(
            model,
            localDistance * nearViewScale,
            localDistance * farViewScale,
            out lodIndex);
    }

    /// <summary>
    /// Applies the nonzero CullDist comparison. The view distance scale is
    /// explicit for the same reason as the LOD scalars.
    /// </summary>
    public static bool IsBeyondCullDistance(
        ushort cullDistance,
        float cameraDistance,
        float viewDistanceScale)
    {
        if (cullDistance == 0)
            return false;
        if (!IsValidDistance(cameraDistance) ||
            !IsValidDistance(viewDistanceScale))
        {
            throw new ArgumentOutOfRangeException(
                !IsValidDistance(cameraDistance)
                    ? nameof(cameraDistance)
                    : nameof(viewDistanceScale));
        }

        return cameraDistance * viewDistanceScale >= cullDistance;
    }

    /// <summary>
    /// Keeps native terminal culling, but falls back to the already-rendered
    /// prepared LOD when an alternate LOD is incomplete in the host preview.
    /// </summary>
    public static int ResolveRenderReadyLod(
        int selectedLodIndex,
        int preparedLodIndex,
        uint renderableLodMask)
    {
        if (selectedLodIndex == CulledLodIndex)
            return CulledLodIndex;
        if ((uint)selectedLodIndex < 32u &&
            (renderableLodMask & (1u << selectedLodIndex)) != 0)
        {
            return selectedLodIndex;
        }

        return preparedLodIndex;
    }

    /// <summary>
    /// Allocation-free backend-neutral frame selection extracted from the
    /// characterized EditorPreview path. Unscheduled object slots remain
    /// conservatively visible and retain the caller's prepared fallback LOD;
    /// scheduled rows apply host-frustum, camera DPVS, CullDist, native LOD
    /// threshold selection, and render-ready fallback in that order.
    /// </summary>
    /// <remarks>
    /// <paramref name="selectedLodByObject"/> must be initialized with the
    /// prepared fallback LOD for every materialized object before this call.
    /// The method overwrites every scheduled object and performs no managed
    /// allocation.
    /// </remarks>
    public static int SelectFrame(
        IReadOnlyList<MapRenderStaticModelSchedulingInfo> scheduling,
        RenderCamera camera,
        MapRenderCameraFrustum frustum,
        MapRenderWorldDpvsViewVisibility? cameraVisibility,
        Span<bool> visibleByObject,
        Span<int> selectedLodByObject,
        float viewDistanceScale = 1f,
        float nearViewScale = 1f,
        float farViewScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(scheduling);
        ArgumentNullException.ThrowIfNull(frustum);
        return SelectFrameCore(
            scheduling,
            camera,
            frustum.NormalizedPlanes,
            cameraVisibility,
            visibleByObject,
            selectedLodByObject,
            visibleScheduledObjectIndices: default,
            captureVisibleScheduledObjects: false,
            viewDistanceScale,
            nearViewScale,
            farViewScale);
    }

    /// <summary>
    /// Allocation-free frame selection which also emits the visible
    /// scheduled object identities into caller-owned storage. The populated
    /// prefix is exactly the returned count.
    /// </summary>
    public static int SelectFrame(
        IReadOnlyList<MapRenderStaticModelSchedulingInfo> scheduling,
        RenderCamera camera,
        MapRenderCameraFrustum frustum,
        MapRenderWorldDpvsViewVisibility? cameraVisibility,
        Span<bool> visibleByObject,
        Span<int> selectedLodByObject,
        Span<int> visibleScheduledObjectIndices,
        float viewDistanceScale = 1f,
        float nearViewScale = 1f,
        float farViewScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(scheduling);
        ArgumentNullException.ThrowIfNull(frustum);
        return SelectFrameCore(
            scheduling,
            camera,
            frustum.NormalizedPlanes,
            cameraVisibility,
            visibleByObject,
            selectedLodByObject,
            visibleScheduledObjectIndices,
            captureVisibleScheduledObjects: true,
            viewDistanceScale,
            nearViewScale,
            farViewScale);
    }

    /// <summary>
    /// Allocation-free moving-camera overload using caller-owned normalized
    /// planes produced by <see cref="MapRenderCameraFrustum.BuildPlanes"/>.
    /// </summary>
    public static int SelectFrame(
        IReadOnlyList<MapRenderStaticModelSchedulingInfo> scheduling,
        RenderCamera camera,
        ReadOnlySpan<Vector4> normalizedFrustumPlanes,
        MapRenderWorldDpvsViewVisibility? cameraVisibility,
        Span<bool> visibleByObject,
        Span<int> selectedLodByObject,
        float viewDistanceScale = 1f,
        float nearViewScale = 1f,
        float farViewScale = 1f)
    {
        return SelectFrameCore(
            scheduling,
            camera,
            normalizedFrustumPlanes,
            cameraVisibility,
            visibleByObject,
            selectedLodByObject,
            visibleScheduledObjectIndices: default,
            captureVisibleScheduledObjects: false,
            viewDistanceScale,
            nearViewScale,
            farViewScale);
    }

    /// <summary>
    /// Allocation-free moving-camera overload which emits the compact visible
    /// scheduled-object list into caller-owned storage.
    /// </summary>
    public static int SelectFrame(
        IReadOnlyList<MapRenderStaticModelSchedulingInfo> scheduling,
        RenderCamera camera,
        ReadOnlySpan<Vector4> normalizedFrustumPlanes,
        MapRenderWorldDpvsViewVisibility? cameraVisibility,
        Span<bool> visibleByObject,
        Span<int> selectedLodByObject,
        Span<int> visibleScheduledObjectIndices,
        float viewDistanceScale = 1f,
        float nearViewScale = 1f,
        float farViewScale = 1f)
    {
        return SelectFrameCore(
            scheduling,
            camera,
            normalizedFrustumPlanes,
            cameraVisibility,
            visibleByObject,
            selectedLodByObject,
            visibleScheduledObjectIndices,
            captureVisibleScheduledObjects: true,
            viewDistanceScale,
            nearViewScale,
            farViewScale);
    }

    private static int SelectFrameCore(
        IReadOnlyList<MapRenderStaticModelSchedulingInfo> scheduling,
        RenderCamera camera,
        ReadOnlySpan<Vector4> normalizedFrustumPlanes,
        MapRenderWorldDpvsViewVisibility? cameraVisibility,
        Span<bool> visibleByObject,
        Span<int> selectedLodByObject,
        Span<int> visibleScheduledObjectIndices,
        bool captureVisibleScheduledObjects,
        float viewDistanceScale,
        float nearViewScale,
        float farViewScale)
    {
        ArgumentNullException.ThrowIfNull(scheduling);
        if (normalizedFrustumPlanes.Length <
            MapRenderCameraFrustum.PlaneCount)
        {
            throw new ArgumentException(
                $"Static selection requires {MapRenderCameraFrustum.PlaneCount} normalized frustum planes.",
                nameof(normalizedFrustumPlanes));
        }
        if (visibleByObject.Length != selectedLodByObject.Length)
        {
            throw new ArgumentException(
                "Static visibility and selected-LOD storage must have the same object capacity.");
        }
        if (captureVisibleScheduledObjects &&
            visibleScheduledObjectIndices.Length < scheduling.Count)
        {
            throw new ArgumentException(
                "Visible scheduled-object storage must cover every scheduling row.",
                nameof(visibleScheduledObjectIndices));
        }
        if (!IsValidDistance(viewDistanceScale) ||
            !IsValidDistance(nearViewScale) ||
            !IsValidDistance(farViewScale))
        {
            throw new ArgumentOutOfRangeException(
                !IsValidDistance(viewDistanceScale)
                    ? nameof(viewDistanceScale)
                    : !IsValidDistance(nearViewScale)
                        ? nameof(nearViewScale)
                        : nameof(farViewScale));
        }

        // This matches the established OpenGL fallback: an object that has
        // prepared geometry but no scheduling row must not disappear merely
        // because host reconstruction lacks its bounds/LOD metadata.
        visibleByObject.Fill(true);
        ReadOnlySpan<uint> cameraStaticModelBits =
            cameraVisibility is null
                ? default
                : cameraVisibility.StaticModelBitSpan;
        int visibleScheduledObjectCount = 0;
        for (int schedulingIndex = 0;
             schedulingIndex < scheduling.Count;
             schedulingIndex++)
        {
            MapRenderStaticModelSchedulingInfo row =
                scheduling[schedulingIndex] ??
                throw new ArgumentException(
                    "Static scheduling cannot contain null rows.",
                    nameof(scheduling));
            if ((uint)row.ObjectIndex >= (uint)visibleByObject.Length)
            {
                throw new ArgumentException(
                    "A static scheduling object is outside the supplied frame storage.",
                    nameof(scheduling));
            }

            bool visible =
                MapRenderCameraFrustum.Intersects(
                    row.Bounds,
                    normalizedFrustumPlanes) &&
                IsDpvsBitVisible(
                    cameraStaticModelBits,
                    cameraVisibility is not null,
                    row.ObjectIndex);
            float cameraDistance = Vector3.Distance(
                camera.Position,
                row.Origin);
            if (visible && row.CullDistance != 0)
            {
                visible = !IsBeyondCullDistance(
                    row.CullDistance,
                    cameraDistance,
                    viewDistanceScale);
            }

            int selectedLod = row.PreparedLodIndex;
            if (visible && TrySelectForCameraDistance(
                    row.Model,
                    cameraDistance,
                    row.PlacementScale,
                    nearViewScale,
                    farViewScale,
                    out int requestedLod))
            {
                selectedLod = ResolveRenderReadyLod(
                    requestedLod,
                    row.PreparedLodIndex,
                    row.RenderableLodMask);
            }
            if (selectedLod == CulledLodIndex)
                visible = false;

            visibleByObject[row.ObjectIndex] = visible;
            selectedLodByObject[row.ObjectIndex] = selectedLod;
            if (visible)
            {
                if (captureVisibleScheduledObjects)
                {
                    visibleScheduledObjectIndices[
                        visibleScheduledObjectCount] =
                            row.ObjectIndex;
                }
                visibleScheduledObjectCount++;
            }
        }

        return visibleScheduledObjectCount;
    }

    private static bool IsDpvsBitVisible(
        ReadOnlySpan<uint> words,
        bool hasDpvsVisibility,
        int index)
    {
        if (!hasDpvsVisibility || index < 0)
            return true;
        int wordIndex = index >> 5;
        if ((uint)wordIndex >= (uint)words.Length)
            return true;
        uint mask = 0x80000000u >> (index & 31);
        return (words[wordIndex] & mask) != 0;
    }

    private static bool IsValidDistance(float value) =>
        value >= 0f && float.IsFinite(value);
}
