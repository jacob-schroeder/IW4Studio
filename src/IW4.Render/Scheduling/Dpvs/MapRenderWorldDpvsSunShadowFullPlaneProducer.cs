using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.Transforms;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Implements the normal PS3 <c>rg.sunShadowFull == 1</c> GfxSunShadowClip
/// producer. Helper boundaries correspond to
/// R_SetSunShadowSinesAndBoundingArcEdge,
/// R_AddSunShadowClipPlanesForBoundingArc,
/// R_AddSunShadowClipPlanesForMapBoundary, far-origin, near-clip, and
/// frustum-side paths.
/// </summary>
public static class MapRenderWorldDpvsSunShadowFullPlaneProducer
{
    private const float ArcClassificationEpsilon = 0.001f;
    private const float BoundaryLengthFraction = 0.02f;
    private const float CullingRectangleGuardFraction = 0.0625f;
    private const float RectangleConnectionEpsilon = 0.01f;
    private const float FarPartitionSampleScale = 4f;

    // Native full-window corner order: (maxX,minY), (minX,minY),
    // (minX,maxY), (maxX,maxY), expressed after the sub-window sign map at
    // the PS3 projection's 0.99951171875 clip scale.
    private static readonly Vector2[] NativeClipCorners =
    [
        new(
            RenderViewerMatrixMath.ClipScale,
            RenderViewerMatrixMath.ClipScale),
        new(
            -RenderViewerMatrixMath.ClipScale,
            RenderViewerMatrixMath.ClipScale),
        new(
            -RenderViewerMatrixMath.ClipScale,
            -RenderViewerMatrixMath.ClipScale),
        new(
            RenderViewerMatrixMath.ClipScale,
            -RenderViewerMatrixMath.ClipScale)
    ];

    public static MapRenderWorldDpvsSunShadowFrameBuildResult Build(
        string producerIdentity,
        long sourceRevision,
        GfxWorldAsset world,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame,
        MapRenderWorldDpvsSunShadowFullSetupState setupState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        if (sourceRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cameraFrame);
        ArgumentNullException.ThrowIfNull(setupState);

        MapRenderWorldDpvsSunShadowFrameFailure? failure =
            ValidateInputs(world, cameraFrame, setupState);
        if (failure is not null)
            return MapRenderWorldDpvsSunShadowFrameBuildResult.Failed(failure);

        if (!TryBuildSunAxes(
                setupState.SunShadowLightDirection,
                out Vector3 axis0,
                out Vector3 axis1,
                out Vector3 axis2))
        {
            return Failed("The PS3 sun-shadow basis cannot be normalized.");
        }

        if (!TryBuildWorldRays(
                cameraFrame,
                out Vector3[] worldRays,
                out float cameraNearDistance))
        {
            return Failed(
                "The exact normal-camera near corners cannot produce four finite PS3 world rays.");
        }

        if (!TryBuildFrustumRays(
                worldRays,
                axis0,
                axis1,
                out MapRenderWorldDpvsSunShadowFullFrustumRays? frustumRays))
        {
            return Failed(
                "The projected PS3 sun-shadow frustum rays are degenerate.");
        }

        if (!TryBuildProjectionState(
                world,
                cameraFrame,
                setupState,
                axis0,
                axis1,
                axis2,
                frustumRays!,
                out MapRenderWorldDpvsSunShadowFullProjectionState?
                    projectionState))
        {
            return Failed(
                "The PS3 sun-shadow projection scale or snapped origin is non-finite or degenerate.");
        }

        MapRenderWorldDpvsSunShadowFullFrustumRays nativeRays =
            frustumRays!;
        MapRenderWorldDpvsSunShadowFullProjectionState nativeProjection =
            projectionState!;
        var clip = new MapRenderWorldDpvsSunShadowFullClipBuilder();
        try
        {
            AddInitialClipPlanes(
                clip,
                nativeProjection,
                nativeRays,
                cameraFrame,
                cameraNearDistance);
            AddMapBoundaryPlanes(
                clip,
                partitionIndex: 0,
                nativeProjection,
                nativeRays,
                setupState.SunShadowSize);

            // The operational profile follows the exact zero-center branch of
            // R_SetFarSunShadowOrg_NormalCascade.
            TryAddFarNearClipPlane(
                clip,
                nativeProjection,
                nativeRays,
                setupState.SunShadowSize);

            AddMapBoundaryPlanes(
                clip,
                partitionIndex: 1,
                nativeProjection,
                nativeRays,
                setupState.SunShadowSize);

            // Snapshot planeCount[0..1] before adding later camera-side rows.
            // Those rows remain retained but stay outside the active
            // frustumPlaneCount prefix consumed by DPVS traversal.
            clip.SnapshotFrustumPlaneCounts();
            AddFrustumSidePlanes(clip, nativeRays, cameraFrame.Origin);

            return MapRenderWorldDpvsSunShadowFrameBuildResult.Succeeded(
                clip.ToFrame(
                    producerIdentity,
                    sourceRevision,
                    nativeProjection));
        }
        catch (InvalidOperationException exception)
        {
            return Failed(exception.Message);
        }
    }

    private static MapRenderWorldDpvsSunShadowFrameFailure? ValidateInputs(
        GfxWorldAsset world,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame,
        MapRenderWorldDpvsSunShadowFullSetupState setupState)
    {
        Vector3 direction = setupState.SunShadowLightDirection;
        float directionLengthSquared = direction.LengthSquared();
        if (!IsFinite(direction) ||
            !float.IsFinite(directionLengthSquared) ||
            MathF.Abs(directionLengthSquared - 1f) >
            ArcClassificationEpsilon ||
            !IsFinite(setupState.SunShadowCenter) ||
            setupState.SunShadowCenter != Vector3.Zero ||
            setupState.ShadowMapResolution <= 0 ||
            setupState.ShadowMapTileCount <= 0 ||
            setupState.SunShadowSize <= 2 ||
            setupState.SunShadowSize > setupState.ShadowMapResolution ||
            !(setupState.SunShadowMapScale > 0f) ||
            !float.IsFinite(setupState.SunShadowMapScale) ||
            !(setupState.SunSampleSizeNear > 0f) ||
            !float.IsFinite(setupState.SunSampleSizeNear) ||
            !Enum.IsDefined(setupState.SwitchPartitionZBranch))
        {
            return new(
                MapRenderWorldDpvsSunShadowFrameFailureKind
                    .InvalidNativeSetupState,
                "Full sun-shadow setup requires a finite unit direction, the exact zero-center branch, and finite positive PS3 map metrics.");
        }

        if (world.Mins.Count != 3 || world.Maxs.Count != 3)
        {
            return new(
                MapRenderWorldDpvsSunShadowFrameFailureKind
                    .InvalidNativeSetupState,
                "GfxWorld bounds midpoint/half-size must each contain exactly three coordinates.");
        }
        for (int coordinateIndex = 0; coordinateIndex < 3; coordinateIndex++)
        {
            float midpoint = world.Mins[coordinateIndex];
            float halfSize = world.Maxs[coordinateIndex];
            if (!float.IsFinite(midpoint) ||
                !float.IsFinite(halfSize) ||
                halfSize < 0f)
            {
                return new(
                    MapRenderWorldDpvsSunShadowFrameFailureKind
                        .InvalidNativeSetupState,
                    $"GfxWorld coordinate {coordinateIndex} has an invalid bounds midpoint or half-size.");
            }
        }

        if (!IsFinite(cameraFrame.Origin) ||
            !IsFinite(cameraFrame.Forward) ||
            !RenderMatrixValidation.IsFinite(cameraFrame.InverseViewProjection))
        {
            return new(
                MapRenderWorldDpvsSunShadowFrameFailureKind
                    .InvalidNativeSetupState,
                "The exact normal-camera origin, forward axis, or inverse view-projection matrix is non-finite.");
        }

        return null;
    }

    private static bool TryBuildSunAxes(
        Vector3 lightDirection,
        out Vector3 axis0,
        out Vector3 axis1,
        out Vector3 axis2)
    {
        Vector3 seed = MathF.Abs(lightDirection.Z) <= 0.9f
            ? Vector3.UnitZ
            : Vector3.UnitX;
        axis0 = Vector3.Cross(seed, lightDirection);
        float length = axis0.Length();
        if (!(length > 0f) || !float.IsFinite(length))
        {
            axis1 = default;
            axis2 = default;
            return false;
        }

        axis0 /= length;
        axis1 = Vector3.Cross(lightDirection, axis0);
        axis2 = lightDirection;
        return IsFinite(axis0) && IsFinite(axis1);
    }

    private static bool TryBuildWorldRays(
        MapRenderWorldDpvsNormalCameraFrame cameraFrame,
        out Vector3[] worldRays,
        out float cameraNearDistance)
    {
        worldRays = new Vector3[NativeClipCorners.Length];
        cameraNearDistance = 0f;
        for (int cornerIndex = 0;
             cornerIndex < NativeClipCorners.Length;
             cornerIndex++)
        {
            Vector2 corner = NativeClipCorners[cornerIndex];
            Vector4 homogeneous = Vector4.Transform(
                new Vector4(corner.X, corner.Y, 0f, 1f),
                cameraFrame.InverseViewProjection);
            if (!(homogeneous.W > 0f) || !IsFinite(homogeneous))
                return false;

            Vector3 nearPoint = new(
                homogeneous.X / homogeneous.W,
                homogeneous.Y / homogeneous.W,
                homogeneous.Z / homogeneous.W);
            Vector3 fromOrigin = nearPoint - cameraFrame.Origin;
            float rayLength = fromOrigin.Length();
            if (!(rayLength > 0f) ||
                !float.IsFinite(rayLength) ||
                !IsFinite(nearPoint))
            {
                return false;
            }

            worldRays[cornerIndex] = fromOrigin / rayLength;
            float nearDistance = Vector3.Dot(
                fromOrigin,
                cameraFrame.Forward);
            if (!(nearDistance > 0f) || !float.IsFinite(nearDistance))
                return false;
            if (cornerIndex == 0)
                cameraNearDistance = nearDistance;
        }

        return cameraNearDistance > 0f;
    }

    private static bool TryBuildFrustumRays(
        IReadOnlyList<Vector3> worldRays,
        Vector3 axis0,
        Vector3 axis1,
        out MapRenderWorldDpvsSunShadowFullFrustumRays? frustumRays)
    {
        var shadowRays = new Vector2[4];
        Vector2 mins = Vector2.Zero;
        Vector2 maxs = Vector2.Zero;
        for (int rayIndex = 0; rayIndex < shadowRays.Length; rayIndex++)
        {
            Vector3 worldRay = worldRays[rayIndex];
            Vector2 shadowRay = new(
                Vector3.Dot(worldRay, axis0),
                Vector3.Dot(worldRay, axis1));
            if (!IsFinite(shadowRay))
            {
                frustumRays = null;
                return false;
            }
            shadowRays[rayIndex] = shadowRay;
            mins = Vector2.Min(mins, shadowRay);
            maxs = Vector2.Max(maxs, shadowRay);
        }

        var sines = new float[4];
        for (int edgeIndex = 0; edgeIndex < sines.Length; edgeIndex++)
        {
            Vector2 current = shadowRays[edgeIndex];
            Vector2 next = shadowRays[(edgeIndex + 1) & 3];
            sines[edgeIndex] = Cross(current, next);
            if (!float.IsFinite(sines[edgeIndex]))
            {
                frustumRays = null;
                return false;
            }
        }

        float sinMin = sines.Min();
        float sinMax = sines.Max();
        int boundingArcRay0 = -1;
        int boundingArcRay1 = -1;
        var interiorArcRays = new List<int>(2);
        if (MathF.Min(-sinMin, sinMax) >= ArcClassificationEpsilon)
        {
            int previousEdge = 3;
            for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
            {
                float sine = sines[edgeIndex];
                float previousSine = sines[previousEdge];
                if (sine > ArcClassificationEpsilon &&
                    previousSine <= ArcClassificationEpsilon)
                {
                    boundingArcRay0 = edgeIndex;
                }
                else if (sine < -ArcClassificationEpsilon &&
                         previousSine >= -ArcClassificationEpsilon)
                {
                    boundingArcRay1 = edgeIndex;
                }
                else
                {
                    interiorArcRays.Add(edgeIndex);
                }
                previousEdge = edgeIndex;
            }
        }

        if (boundingArcRay0 >= 0 && boundingArcRay1 < 0)
        {
            frustumRays = null;
            return false;
        }

        frustumRays = new(
            worldRays,
            shadowRays,
            sines,
            sinMin,
            sinMax,
            mins,
            maxs,
            boundingArcRay0,
            boundingArcRay1,
            interiorArcRays);
        return true;
    }

    private static bool TryBuildProjectionState(
        GfxWorldAsset world,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame,
        MapRenderWorldDpvsSunShadowFullSetupState setupState,
        Vector3 axis0,
        Vector3 axis1,
        Vector3 axis2,
        MapRenderWorldDpvsSunShadowFullFrustumRays frustumRays,
        out MapRenderWorldDpvsSunShadowFullProjectionState? projectionState)
    {
        if (!TryBuildProjectionDimension(
                frustumRays.Mins.X,
                frustumRays.Maxs.X,
                setupState.SunShadowSize,
                out float scaleX,
                out float pixelCenterX) ||
            !TryBuildProjectionDimension(
                frustumRays.Mins.Y,
                frustumRays.Maxs.Y,
                setupState.SunShadowSize,
                out float scaleY,
                out float pixelCenterY))
        {
            projectionState = null;
            return false;
        }

        float partition0SampleSize =
            setupState.SunSampleSizeNear /
            setupState.SunShadowMapScale;
        float partition1SampleSize =
            partition0SampleSize * FarPartitionSampleScale;
        float rayDistanceToNearMapEdge =
            MathF.Min(scaleX, scaleY) * partition0SampleSize;
        float minimumForwardSine = frustumRays.WorldRays.Min(
            ray => Vector3.Dot(cameraFrame.Forward, ray));
        float nearShadowMinDistance =
            minimumForwardSine * rayDistanceToNearMapEdge;
        var shadowOrigin = new Vector2(
            Vector3.Dot(cameraFrame.Origin, axis0),
            Vector3.Dot(cameraFrame.Origin, axis1));
        var shadowOriginPixelCenter = new Vector2(
            pixelCenterX,
            pixelCenterY);
        Vector2 snapped0 = SnapShadowOrigin(
            shadowOrigin,
            partition0SampleSize);
        Vector2 snapped1 = SnapShadowOrigin(
            shadowOrigin,
            partition1SampleSize);

        if (!(rayDistanceToNearMapEdge > 0f) ||
            !(nearShadowMinDistance > 0f) ||
            !(partition0SampleSize > 0f) ||
            !IsFinite(shadowOrigin) ||
            !IsFinite(shadowOriginPixelCenter) ||
            !IsFinite(snapped0) ||
            !IsFinite(snapped1))
        {
            projectionState = null;
            return false;
        }

        MapRenderWorldDpvsSunShadowProjectionCodeConstants codeConstants =
            BuildProjectionCodeConstants(
                setupState,
                shadowOriginPixelCenter,
                snapped0,
                snapped1,
                partition1SampleSize);

        if (!TryBuildProjectionMatrices(
                world,
                cameraFrame,
                setupState,
                axis0,
                axis1,
                axis2,
                nearShadowMinDistance,
                shadowOriginPixelCenter,
                snapped0,
                snapped1,
                partition0SampleSize,
                partition1SampleSize,
                out float worldDepthMinimum,
                out float worldDepthMaximum,
                out Matrix4x4 partition0WorldToClip,
                out Matrix4x4 partition1WorldToClip,
                out Matrix4x4 shadowLookupMatrix))
        {
            projectionState = null;
            return false;
        }

        projectionState = new(
            axis0,
            axis1,
            axis2,
            cameraFrame.Origin,
            cameraFrame.Forward,
            nearShadowMinDistance,
            rayDistanceToNearMapEdge,
            worldDepthMinimum,
            worldDepthMaximum,
            shadowOrigin,
            shadowOriginPixelCenter,
            snapped0,
            snapped1,
            partition0SampleSize,
            partition1SampleSize,
            partition0WorldToClip,
            partition1WorldToClip,
            shadowLookupMatrix,
            codeConstants);
        return true;
    }

    private static bool TryBuildProjectionMatrices(
        GfxWorldAsset world,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame,
        MapRenderWorldDpvsSunShadowFullSetupState setupState,
        Vector3 axis0,
        Vector3 axis1,
        Vector3 axis2,
        float nearShadowMinDistance,
        Vector2 shadowOriginPixelCenter,
        Vector2 partition0SnappedShadowOrigin,
        Vector2 partition1SnappedShadowOrigin,
        float partition0SampleSize,
        float partition1SampleSize,
        out float worldDepthMinimum,
        out float worldDepthMaximum,
        out Matrix4x4 partition0WorldToClip,
        out Matrix4x4 partition1WorldToClip,
        out Matrix4x4 shadowLookupMatrix)
    {
        Vector3 depthAxis = -axis2;
        ProjectWorldBounds(
            world,
            depthAxis,
            out worldDepthMinimum,
            out worldDepthMaximum);
        float worldDepthSpan =
            worldDepthMaximum - worldDepthMinimum + 2f;
        if (!(worldDepthSpan > 0f) ||
            !float.IsFinite(worldDepthSpan) ||
            !(nearShadowMinDistance > 0f) ||
            !float.IsFinite(nearShadowMinDistance))
        {
            partition0WorldToClip = default;
            partition1WorldToClip = default;
            shadowLookupMatrix = default;
            return false;
        }

        partition0WorldToClip = BuildWorldToClipMatrix(
            setupState,
            axis0,
            axis1,
            axis2,
            partition0SnappedShadowOrigin,
            partition0SampleSize,
            shadowOriginPixelCenter,
            worldDepthMinimum,
            worldDepthSpan);
        partition1WorldToClip = BuildWorldToClipMatrix(
            setupState,
            axis0,
            axis1,
            axis2,
            partition1SnappedShadowOrigin,
            partition1SampleSize,
            shadowOriginPixelCenter,
            worldDepthMinimum,
            worldDepthSpan);
        shadowLookupMatrix = BuildShadowLookupMatrix(
            partition0WorldToClip,
            setupState.ShadowMapTileCount,
            cameraFrame.Origin,
            cameraFrame.Forward,
            nearShadowMinDistance);
        return RenderMatrixValidation.IsFinite(partition0WorldToClip) &&
               RenderMatrixValidation.IsFinite(partition1WorldToClip) &&
               RenderMatrixValidation.IsFinite(shadowLookupMatrix);
    }

    private static Matrix4x4 BuildWorldToClipMatrix(
        MapRenderWorldDpvsSunShadowFullSetupState setupState,
        Vector3 axis0,
        Vector3 axis1,
        Vector3 axis2,
        Vector2 snappedShadowOrigin,
        float sampleSize,
        Vector2 shadowOriginPixelCenter,
        float worldDepthMinimum,
        float worldDepthSpan)
    {
        var view = new Matrix4x4(
            axis0.X, axis1.X, -axis2.X, 0f,
            axis0.Y, axis1.Y, -axis2.Y, 0f,
            axis0.Z, axis1.Z, -axis2.Z, 0f,
            -snappedShadowOrigin.X,
            -snappedShadowOrigin.Y,
            0f,
            1f);
        float border =
            (setupState.ShadowMapResolution - setupState.SunShadowSize) /
            2f;
        float xyScale =
            2f / (setupState.ShadowMapResolution * sampleSize);
        var orthographic = new Matrix4x4(
            xyScale, 0f, 0f, 0f,
            0f, xyScale, 0f, 0f,
            0f, 0f, 1f / worldDepthSpan, 0f,
            2f * (border + shadowOriginPixelCenter.X) /
                setupState.ShadowMapResolution - 1f,
            2f * (border + shadowOriginPixelCenter.Y) /
                setupState.ShadowMapResolution - 1f,
            (1f - worldDepthMinimum) / worldDepthSpan,
            1f);
        return view * orthographic;
    }

    private static Matrix4x4 BuildShadowLookupMatrix(
        Matrix4x4 projection,
        int shadowMapTileCount,
        Vector3 cameraOrigin,
        Vector3 cameraForward,
        float nearShadowMinDistance)
    {
        float a = 0.5f * (0f - 1f / shadowMapTileCount);
        float b = 0.5f * (1f / shadowMapTileCount + 0f);
        Vector3 lookupW = cameraForward / nearShadowMinDistance;
        return new Matrix4x4(
            0.5f * (projection.M11 + projection.M14),
            a * projection.M12 + b * projection.M14,
            projection.M13,
            lookupW.X,
            0.5f * (projection.M21 + projection.M24),
            a * projection.M22 + b * projection.M24,
            projection.M23,
            lookupW.Y,
            0.5f * (projection.M31 + projection.M34),
            a * projection.M32 + b * projection.M34,
            projection.M33,
            lookupW.Z,
            0.5f * (projection.M41 + projection.M44),
            a * projection.M42 + b * projection.M44,
            projection.M43,
            -Vector3.Dot(lookupW, cameraOrigin));
    }

    private static void ProjectWorldBounds(
        GfxWorldAsset world,
        Vector3 axis,
        out float minimum,
        out float maximum)
    {
        float center = 0f;
        float radius = 0f;
        for (int coordinateIndex = 0;
             coordinateIndex < 3;
             coordinateIndex++)
        {
            float component = coordinateIndex switch
            {
                0 => axis.X,
                1 => axis.Y,
                _ => axis.Z
            };
            // GfxWorld's historical Mins/Maxs property names retain the
            // native Bounds storage order: midpoint then half-size.
            center += component * world.Mins[coordinateIndex];
            radius += MathF.Abs(component) * world.Maxs[coordinateIndex];
        }
        minimum = center - radius;
        maximum = center + radius;
    }

    private static MapRenderWorldDpvsSunShadowProjectionCodeConstants
        BuildProjectionCodeConstants(
            MapRenderWorldDpvsSunShadowFullSetupState setupState,
            Vector2 shadowOriginPixelCenter,
            Vector2 partition0SnappedShadowOrigin,
            Vector2 partition1SnappedShadowOrigin,
            float partition1SampleSize)
    {
        float border =
            (setupState.ShadowMapResolution - setupState.SunShadowSize) / 2;
        // The receiver remaps the near lookup component-wise
        // (far.xy = near.xy * 0.25 + switch.xy). Keep each pixel-center and
        // snapped-origin delta on that same axis so the remap lands on the
        // partition-one projection rendered into the atlas.
        float switchX =
            ((border + shadowOriginPixelCenter.X) * 0.75f +
             (partition0SnappedShadowOrigin.X -
              partition1SnappedShadowOrigin.X) /
             partition1SampleSize) /
            setupState.ShadowMapResolution;
        float switchY =
            (1.75f -
             ((border + shadowOriginPixelCenter.Y) * 0.75f +
              (partition0SnappedShadowOrigin.Y -
               partition1SnappedShadowOrigin.Y) /
              partition1SampleSize) /
             setupState.ShadowMapResolution) /
            setupState.ShadowMapTileCount;
        float switchZ = setupState.SwitchPartitionZBranch switch
        {
            MapRenderWorldDpvsSunShadowSwitchPartitionZBranch
                .TestedVectorNonZero => -0.001f,
            MapRenderWorldDpvsSunShadowSwitchPartitionZBranch
                .TestedVectorZero => 1.0f,
            _ => throw new ArgumentOutOfRangeException(
                nameof(setupState.SwitchPartitionZBranch))
        };
        return new(
            new Vector4(switchX, switchY, switchZ, 0.25f),
            new Vector4(
                16.0f / setupState.SunShadowMapScale,
                (setupState.ShadowMapTileCount * 16.0f) /
                setupState.SunShadowMapScale,
                -8.0f / setupState.SunShadowMapScale,
                -24.0f / setupState.SunShadowMapScale));
    }

    private static bool TryBuildProjectionDimension(
        float minimum,
        float maximum,
        int usefulSize,
        out float scale,
        out float pixelCenter)
    {
        float span = maximum - minimum;
        if (!(span > 0f) || !float.IsFinite(span))
        {
            scale = default;
            pixelCenter = default;
            return false;
        }

        scale = (usefulSize - 2f) / span;
        float lower = MathF.Ceiling(0.5f - minimum * scale) + 0.5f;
        float upper = MathF.Floor(
            (usefulSize - 0.5f) - maximum * scale) - 0.5f;
        if (lower != upper)
        {
            if (maximum <= -minimum)
            {
                lower -= 1f;
                scale = (1f - lower) / minimum;
                pixelCenter = lower;
            }
            else
            {
                upper += 1f;
                scale = ((usefulSize - 1f) - upper) / maximum;
                pixelCenter = upper;
            }
        }
        else
        {
            pixelCenter = lower;
        }

        return scale > 0f &&
               float.IsFinite(scale) &&
               float.IsFinite(pixelCenter);
    }

    private static Vector2 SnapShadowOrigin(
        Vector2 shadowOrigin,
        float sampleSize) => new(
            sampleSize *
            (MathF.Floor(shadowOrigin.X / sampleSize) + 0.5f),
            sampleSize *
            (MathF.Floor(shadowOrigin.Y / sampleSize) + 0.5f));

    private static void AddInitialClipPlanes(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        MapRenderWorldDpvsSunShadowFullProjectionState projection,
        MapRenderWorldDpvsSunShadowFullFrustumRays rays,
        MapRenderWorldDpvsNormalCameraFrame cameraFrame,
        float cameraNearDistance)
    {
        if (rays.BoundingArcRay0 < 0)
        {
            if (rays.SinMax < -ArcClassificationEpsilon)
            {
                Vector3 normal = cameraFrame.Forward;
                float baseCoefficient =
                    -Vector3.Dot(normal, cameraFrame.Origin);
                AppendChecked(
                    clip,
                    0,
                    normal,
                    baseCoefficient - cameraNearDistance);
                AppendChecked(
                    clip,
                    1,
                    normal,
                    baseCoefficient - projection.NearShadowMinDistance);
            }
            return;
        }

        Vector2 firstArc = Normalize(
            rays.ShadowRays[rays.BoundingArcRay0]);
        Vector2 secondArc = Normalize(
            rays.ShadowRays[rays.BoundingArcRay1]);
        AddBoundingArcPlane(
            clip,
            projection,
            cameraFrame.Origin,
            new Vector2(-firstArc.Y, firstArc.X));
        AddBoundingArcPlane(
            clip,
            projection,
            cameraFrame.Origin,
            new Vector2(secondArc.Y, -secondArc.X));
    }

    private static void AddBoundingArcPlane(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        MapRenderWorldDpvsSunShadowFullProjectionState projection,
        Vector3 cameraOrigin,
        Vector2 shadowNormal)
    {
        Vector3 worldNormal =
            shadowNormal.X * projection.Axis0 +
            shadowNormal.Y * projection.Axis1;
        float baseCoefficient = -Vector3.Dot(worldNormal, cameraOrigin);
        float partition0Padding =
            0.5f * projection.Partition0SampleSize *
            (MathF.Abs(shadowNormal.X) + MathF.Abs(shadowNormal.Y));
        AppendChecked(
            clip,
            0,
            worldNormal,
            baseCoefficient + partition0Padding);
        AppendChecked(
            clip,
            1,
            worldNormal,
            baseCoefficient +
            FarPartitionSampleScale * partition0Padding);
    }

    private static void AddMapBoundaryPlanes(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        int partitionIndex,
        MapRenderWorldDpvsSunShadowFullProjectionState projection,
        MapRenderWorldDpvsSunShadowFullFrustumRays rays,
        int usefulSize)
    {
        float sampleSize = projection.SampleSize(partitionIndex);
        float usefulWorldSize = usefulSize * sampleSize;
        float halfExtent = usefulWorldSize * 0.5f;
        Vector2 snapped = projection.SnappedShadowOrigin(partitionIndex);
        var candidates = new MapRenderWorldDpvsClipPlane[4];
        // The first normal is perpendicular to the longer boundary edge.
        bool selectAxis1First =
            rays.Maxs.Y - rays.Mins.Y < rays.Maxs.X - rays.Mins.X;
        for (int iteration = 0; iteration < 2; iteration++)
        {
            int axisIndex = (selectAxis1First ? 1 : 0) ^ iteration;
            Vector3 normal = axisIndex == 0
                ? projection.Axis0
                : projection.Axis1;
            float pixelCenter = axisIndex == 0
                ? projection.ShadowOriginPixelCenter.X
                : projection.ShadowOriginPixelCenter.Y;
            float snappedCoordinate = axisIndex == 0
                ? snapped.X
                : snapped.Y;
            float boundary =
                pixelCenter * sampleSize - snappedCoordinate;
            int positiveIndex =
                iteration * 2 + (boundary >= halfExtent ? 1 : 0);
            candidates[positiveIndex] = ToPlane(normal, boundary);
            candidates[positiveIndex ^ 1] =
                ToPlane(-normal, usefulWorldSize - boundary);
        }

        IReadOnlyList<MapRenderWorldDpvsClipPlane> currentPlanes =
            clip.Planes(partitionIndex);
        int firstPlanarPlaneIndex = -1;
        for (int planeIndex = 0;
             planeIndex < currentPlanes.Count;
             planeIndex++)
        {
            if (MathF.Abs(Vector3.Dot(
                    projection.Axis2,
                    Normal(currentPlanes[planeIndex]))) <
                ArcClassificationEpsilon)
            {
                firstPlanarPlaneIndex = planeIndex;
                break;
            }
        }

        if (firstPlanarPlaneIndex < 0)
        {
            int firstPair = candidates[0].CoefficientW <
                            candidates[2].CoefficientW
                ? 0
                : 2;
            int secondPair = firstPair ^ 2;
            AppendChecked(clip, partitionIndex, candidates[firstPair]);
            AppendChecked(clip, partitionIndex, candidates[firstPair + 1]);
            AppendChecked(clip, partitionIndex, candidates[secondPair]);
            AppendChecked(clip, partitionIndex, candidates[secondPair + 1]);
            return;
        }

        Vector2 mapCenterCoordinates = new(
            halfExtent -
            sampleSize * projection.ShadowOriginPixelCenter.X +
            snapped.X,
            halfExtent -
            sampleSize * projection.ShadowOriginPixelCenter.Y +
            snapped.Y);
        float minimumLength =
            halfExtent * BoundaryLengthFraction;
        float minimumLengthSquared = minimumLength * minimumLength;
        for (int candidateIndex = 0;
             candidateIndex < candidates.Length;
             candidateIndex++)
        {
            MapRenderWorldDpvsClipPlane candidate =
                candidates[candidateIndex];
            MapRenderWorldDpvsClipPlane perpendicular =
                candidates[candidateIndex ^ 2];
            Vector3 center =
                mapCenterCoordinates.X * projection.Axis0 +
                mapCenterCoordinates.Y * projection.Axis1 -
                halfExtent * Normal(candidate);
            Vector3 extent = halfExtent * Normal(perpendicular);
            Vector3 start = center - extent;
            Vector3 end = center + extent;
            if (!ClipSegment(
                    ref start,
                    ref end,
                    currentPlanes,
                    firstPlanarPlaneIndex))
            {
                continue;
            }

            if (Vector3.DistanceSquared(start, end) >=
                minimumLengthSquared)
            {
                AppendChecked(clip, partitionIndex, candidate);
            }
        }
    }

    private static bool ClipSegment(
        ref Vector3 start,
        ref Vector3 end,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int firstPlaneIndex)
    {
        for (int planeIndex = firstPlaneIndex;
             planeIndex < planes.Count;
             planeIndex++)
        {
            MapRenderWorldDpvsClipPlane plane = planes[planeIndex];
            float startDistance = Evaluate(plane, start);
            float endDistance = Evaluate(plane, end);
            if (startDistance <= 0f)
            {
                if (endDistance <= 0f)
                    return false;
                float denominator = endDistance - startDistance;
                start =
                    (endDistance * start - startDistance * end) /
                    denominator;
            }
            else if (endDistance <= 0f)
            {
                float denominator = startDistance - endDistance;
                end =
                    (startDistance * end - endDistance * start) /
                    denominator;
            }
        }

        return true;
    }

    private static void TryAddFarNearClipPlane(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        MapRenderWorldDpvsSunShadowFullProjectionState projection,
        MapRenderWorldDpvsSunShadowFullFrustumRays rays,
        int usefulSize)
    {
        if (rays.BoundingArcRay0 < 0)
            return;

        IReadOnlyList<Vector4> rectangles = BuildCullingRectangles(
            projection,
            usefulSize);
        Vector2 ray0 = Normalize(
            rays.ShadowRays[rays.BoundingArcRay0]);
        Vector2 ray1 = Normalize(
            rays.ShadowRays[rays.BoundingArcRay1]);
        if (!TryGetRayRangeInsideRectangles(
                projection.ShadowOrigin,
                ray0,
                rectangles,
                out Vector2 range0) ||
            !TryGetRayRangeInsideRectangles(
                projection.ShadowOrigin,
                ray1,
                rectangles,
                out Vector2 range1))
        {
            return;
        }

        Vector2 point0 = projection.ShadowOrigin + ray0 * range0.X;
        Vector2 point1 = projection.ShadowOrigin + ray1 * range1.X;
        if (rectangles.Count != 1 &&
            !IsSegmentContainedInRectangles(
                point0,
                point1,
                rectangles))
        {
            Vector2 alternate0 =
                projection.ShadowOrigin + ray0 * range0.Y;
            Vector2 alternate1 =
                projection.ShadowOrigin + ray1 * range1.Y;
            if (range0.X <= range0.Y)
            {
                if (range1.X <= range1.Y ||
                    !IsSegmentContainedInRectangles(
                        point0,
                        alternate1,
                        rectangles))
                {
                    return;
                }
                point1 = alternate1;
            }
            else if (range1.X <= range1.Y)
            {
                if (!IsSegmentContainedInRectangles(
                        alternate0,
                        point1,
                        rectangles))
                {
                    return;
                }
                point0 = alternate0;
            }
            else
            {
                bool chooseSecondAlternateFirst =
                    range0.Y * range1.X < range1.Y * range0.X;
                bool selected = chooseSecondAlternateFirst
                    ? IsSegmentContainedInRectangles(
                        point0,
                        alternate1,
                        rectangles)
                    : IsSegmentContainedInRectangles(
                        alternate0,
                        point1,
                        rectangles);
                if (selected)
                {
                    if (chooseSecondAlternateFirst)
                        point1 = alternate1;
                    else
                        point0 = alternate0;
                }
                else
                {
                    selected = chooseSecondAlternateFirst
                        ? IsSegmentContainedInRectangles(
                            alternate0,
                            point1,
                            rectangles)
                        : IsSegmentContainedInRectangles(
                            point0,
                            alternate1,
                            rectangles);
                    if (selected)
                    {
                        if (chooseSecondAlternateFirst)
                            point0 = alternate0;
                        else
                            point1 = alternate1;
                    }
                    else if (IsSegmentContainedInRectangles(
                                 alternate0,
                                 alternate1,
                                 rectangles))
                    {
                        point0 = alternate0;
                        point1 = alternate1;
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }

        Vector2 edgeNormal = Normalize(new(
            point1.Y - point0.Y,
            point0.X - point1.X));
        Vector3 worldNormal =
            edgeNormal.X * projection.Axis0 +
            edgeNormal.Y * projection.Axis1;
        float coefficient =
            0.5f * projection.Partition1SampleSize *
            (MathF.Abs(edgeNormal.X) + MathF.Abs(edgeNormal.Y)) -
            Vector2.Dot(edgeNormal, point0);
        AppendChecked(clip, 1, worldNormal, coefficient);
    }

    private static IReadOnlyList<Vector4> BuildCullingRectangles(
        MapRenderWorldDpvsSunShadowFullProjectionState projection,
        int usefulSize)
    {
        Vector4 nearOuter = BuildMapRectangle(
            projection,
            partitionIndex: 0,
            usefulSize);
        Vector4 farOuter = BuildMapRectangle(
            projection,
            partitionIndex: 1,
            usefulSize);
        float nearGuard =
            usefulSize * projection.Partition0SampleSize *
            CullingRectangleGuardFraction;
        float farGuard =
            usefulSize * projection.Partition1SampleSize *
            CullingRectangleGuardFraction;
        Vector4 nearInner = Inset(nearOuter, nearGuard);
        Vector4 farInner = Inset(farOuter, farGuard);
        var rectangles = new List<Vector4>(3) { nearInner };

        if (farInner.X > nearOuter.X)
        {
            rectangles.Add(new(
                nearOuter.X,
                nearOuter.Y,
                farInner.X,
                nearOuter.W));
        }
        else if (farInner.Z < nearOuter.Z)
        {
            rectangles.Add(new(
                farInner.Z,
                nearOuter.Y,
                nearOuter.Z,
                nearOuter.W));
        }

        if (farInner.Y > nearOuter.Y)
        {
            rectangles.Add(new(
                nearOuter.X,
                nearOuter.Y,
                nearOuter.Z,
                farInner.Y));
        }
        else if (farInner.W < nearOuter.W)
        {
            rectangles.Add(new(
                nearOuter.X,
                farInner.W,
                nearOuter.Z,
                nearOuter.W));
        }

        return rectangles;
    }

    private static Vector4 BuildMapRectangle(
        MapRenderWorldDpvsSunShadowFullProjectionState projection,
        int partitionIndex,
        int usefulSize)
    {
        float sampleSize = projection.SampleSize(partitionIndex);
        Vector2 minimum =
            projection.SnappedShadowOrigin(partitionIndex) -
            sampleSize * projection.ShadowOriginPixelCenter;
        Vector2 maximum = minimum +
                          new Vector2(usefulSize * sampleSize);
        return new(minimum.X, minimum.Y, maximum.X, maximum.Y);
    }

    private static Vector4 Inset(Vector4 rectangle, float amount) => new(
        rectangle.X + amount,
        rectangle.Y + amount,
        rectangle.Z - amount,
        rectangle.W - amount);

    private static bool TryGetRayRangeInsideRectangles(
        Vector2 origin,
        Vector2 direction,
        IReadOnlyList<Vector4> rectangles,
        out Vector2 range)
    {
        var intervals = new List<Vector2>(rectangles.Count);
        float firstRectangleExit = float.MaxValue * 0.5f;
        for (int rectangleIndex = 0;
             rectangleIndex < rectangles.Count;
             rectangleIndex++)
        {
            Vector2 interval = GetRayRectangleInterval(
                origin,
                direction,
                rectangles[rectangleIndex]);
            if (interval.X < interval.Y &&
                interval.Y >= RectangleConnectionEpsilon)
            {
                if (rectangleIndex == 0)
                    firstRectangleExit = interval.Y;
                intervals.Add(interval);
            }
        }

        intervals.Sort(static (left, right) => left.X.CompareTo(right.X));
        if (intervals.Count == 0 ||
            intervals[0].X > RectangleConnectionEpsilon)
        {
            range = default;
            return false;
        }

        float connectedEnd = intervals[0].Y;
        for (int intervalIndex = 1;
             intervalIndex < intervals.Count;
             intervalIndex++)
        {
            Vector2 interval = intervals[intervalIndex];
            if (interval.X >=
                connectedEnd + RectangleConnectionEpsilon)
            {
                break;
            }
            connectedEnd = MathF.Max(connectedEnd, interval.Y);
        }

        range = new(
            connectedEnd,
            MathF.Min(firstRectangleExit, connectedEnd));
        return true;
    }

    private static bool IsSegmentContainedInRectangles(
        Vector2 start,
        Vector2 end,
        IReadOnlyList<Vector4> rectangles)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (!(length > 0f) || !float.IsFinite(length))
            return false;
        Vector2 direction = delta / length;
        var intervals = new List<Vector2>(rectangles.Count);
        foreach (Vector4 rectangle in rectangles)
        {
            Vector2 interval = GetRayRectangleInterval(
                start,
                direction,
                rectangle);
            if (interval.X < interval.Y &&
                interval.Y >= RectangleConnectionEpsilon)
            {
                intervals.Add(interval);
            }
        }

        intervals.Sort(static (left, right) => left.X.CompareTo(right.X));
        if (intervals.Count == 0 ||
            intervals[0].X > RectangleConnectionEpsilon)
        {
            return false;
        }

        float connectedEnd = intervals[0].Y;
        for (int intervalIndex = 1;
             intervalIndex < intervals.Count;
             intervalIndex++)
        {
            Vector2 interval = intervals[intervalIndex];
            if (interval.X >=
                connectedEnd + RectangleConnectionEpsilon)
            {
                break;
            }
            connectedEnd = MathF.Max(connectedEnd, interval.Y);
        }

        return length - RectangleConnectionEpsilon <= connectedEnd;
    }

    private static Vector2 GetRayRectangleInterval(
        Vector2 origin,
        Vector2 direction,
        Vector4 rectangle)
    {
        if (MathF.Abs(direction.X) < ArcClassificationEpsilon)
        {
            float entry = direction.Y < 0f
                ? origin.Y - rectangle.W
                : rectangle.Y - origin.Y;
            float exit = direction.Y < 0f
                ? origin.Y - rectangle.Y
                : rectangle.W - origin.Y;
            return new(entry, exit);
        }
        if (MathF.Abs(direction.Y) < ArcClassificationEpsilon)
        {
            float entry = direction.X < 0f
                ? origin.X - rectangle.Z
                : rectangle.X - origin.X;
            float exit = direction.X < 0f
                ? origin.X - rectangle.X
                : rectangle.Z - origin.X;
            return new(entry, exit);
        }

        float nearX = ((direction.X < 0f ? rectangle.Z : rectangle.X) -
                       origin.X) / direction.X;
        float nearY = ((direction.Y < 0f ? rectangle.W : rectangle.Y) -
                       origin.Y) / direction.Y;
        float farX = ((direction.X < 0f ? rectangle.X : rectangle.Z) -
                      origin.X) / direction.X;
        float farY = ((direction.Y < 0f ? rectangle.Y : rectangle.W) -
                      origin.Y) / direction.Y;
        return new(MathF.Max(nearX, nearY), MathF.Min(farX, farY));
    }

    private static void AddFrustumSidePlanes(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        MapRenderWorldDpvsSunShadowFullFrustumRays rays,
        Vector3 cameraOrigin)
    {
        if (rays.SinMin >= -ArcClassificationEpsilon)
            return;

        // Native loop starts with the wrap edge and then advances 0,1,2.
        ReadOnlySpan<int> edgeOrder = [3, 0, 1, 2];
        foreach (int edgeIndex in edgeOrder)
        {
            if (rays.SinInteriorAngles[edgeIndex] >=
                -ArcClassificationEpsilon)
            {
                continue;
            }
            Vector3 normal = Vector3.Cross(
                rays.WorldRays[edgeIndex],
                rays.WorldRays[(edgeIndex + 1) & 3]);
            float length = normal.Length();
            if (!(length > 0f) || !float.IsFinite(length))
            {
                throw new InvalidOperationException(
                    $"PS3 frustum-side plane {edgeIndex} is degenerate.");
            }
            normal /= length;
            MapRenderWorldDpvsClipPlane plane = ToPlane(
                normal,
                -Vector3.Dot(normal, cameraOrigin));
            AppendChecked(clip, 0, plane);
            AppendChecked(clip, 1, plane);
        }
    }

    private static void AppendChecked(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        int partitionIndex,
        Vector3 normal,
        float coefficient) => AppendChecked(
            clip,
            partitionIndex,
            ToPlane(normal, coefficient));

    private static void AppendChecked(
        MapRenderWorldDpvsSunShadowFullClipBuilder clip,
        int partitionIndex,
        MapRenderWorldDpvsClipPlane plane)
    {
        if (!IsFinite(plane))
        {
            throw new InvalidOperationException(
                $"PS3 partition {partitionIndex} produced a non-finite clip plane.");
        }
        clip.Append(partitionIndex, plane);
    }

    private static MapRenderWorldDpvsClipPlane ToPlane(
        Vector3 normal,
        float coefficient) => new(
            normal.X,
            normal.Y,
            normal.Z,
            coefficient);

    private static Vector3 Normal(MapRenderWorldDpvsClipPlane plane) => new(
        plane.NormalX,
        plane.NormalY,
        plane.NormalZ);

    private static float Evaluate(
        MapRenderWorldDpvsClipPlane plane,
        Vector3 point) =>
        plane.NormalX * point.X +
        plane.NormalY * point.Y +
        plane.NormalZ * point.Z +
        plane.CoefficientW;

    private static Vector2 Normalize(Vector2 value)
    {
        float length = value.Length();
        if (!(length > 0f) || !float.IsFinite(length))
        {
            throw new InvalidOperationException(
                "PS3 projected sun-shadow ray is degenerate.");
        }
        return value / length;
    }

    private static float Cross(Vector2 left, Vector2 right) =>
        left.X * right.Y - left.Y * right.X;

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

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

    private static MapRenderWorldDpvsSunShadowFrameBuildResult Failed(
        string detail) =>
        MapRenderWorldDpvsSunShadowFrameBuildResult.Failed(
            new(
                MapRenderWorldDpvsSunShadowFrameFailureKind
                    .PartitionPlaneProductionFailed,
                detail));
}
