using System.Buffers.Binary;
using System.Numerics;

using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;

namespace IW4.Render.Picking;

public static class MapRenderPicker
{
    private const int DefaultCandidateCount = 12;
    private const int DefaultCandidateSearchCount = 96;
    private const float UvAreaEpsilon = 0.000001f;

    public static bool TryPick(
        MapRenderScene scene,
        MapRenderCamera camera,
        Vector2 screenPosition,
        Vector2 viewportSize,
        bool includeUntexturedGeometry,
        bool includeCollision,
        out MapRenderPickHit hit)
    {
        hit = default;
        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
            return false;

        if (!TryMakeRay(camera, screenPosition, viewportSize, out Vector3 origin, out Vector3 direction))
            return false;

        float bestDistance = float.PositiveInfinity;
        foreach (MapRenderTexturedBatch batch in scene.TexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderPickRange range in batch.PickRanges)
                PickRange(batch.Vertices, batch.Indices, MapRenderScene.TexturedVertexFloatCount, range, materialInfo, origin, direction, ref bestDistance, ref hit);
        }
        foreach (MapRenderInstancedTexturedBatch batch in scene.InstancedTexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderStaticModelInstance instance in batch.Instances)
                PickInstanced(batch.Vertices, batch.Indices, MapRenderScene.TexturedVertexFloatCount, instance, materialInfo, origin, direction, ref bestDistance, ref hit);
        }

        // ponytail: linear scan keeps source identity simple; add a grid/BVH only if click latency is measurable.
        if (includeUntexturedGeometry)
        {
            foreach (MapRenderPickRange range in scene.FallbackSolidPickRanges)
                PickRange(scene.FallbackSolidVertices, scene.FallbackSolidIndices, MapRenderScene.VertexFloatCount, range, null, origin, direction, ref bestDistance, ref hit);

            foreach (MapRenderPickRange range in scene.SolidPickRanges)
                PickRange(scene.SolidVertices, scene.SolidIndices, MapRenderScene.VertexFloatCount, range, null, origin, direction, ref bestDistance, ref hit);

            foreach (MapRenderInstancedSolidBatch batch in scene.InstancedSolidBatches)
            {
                foreach (MapRenderStaticModelInstance instance in batch.Instances)
                    PickInstanced(batch.Vertices, batch.Indices, MapRenderScene.VertexFloatCount, instance, null, origin, direction, ref bestDistance, ref hit);
            }
        }

        if (includeCollision)
        {
            foreach (MapRenderPickTriangle triangle in scene.CollisionPickTriangles)
            {
                if (TryHitTriangle(origin, direction, triangle.P0, triangle.P1, triangle.P2, out float distance) &&
                    distance < bestDistance)
                {
                    bestDistance = distance;
                    hit = ToHit(triangle, distance, origin + direction * distance);
                }
            }
        }

        return float.IsFinite(bestDistance);
    }

    /// <summary>
    /// Picks only the typed collision diagnostic channel. This keeps a
    /// collision-authoring workspace from selecting a collocated render
    /// surface merely because the camera-color geometry was traversed first.
    /// Exact collision triangles and explicitly identified bounds proxies
    /// share this path; their <see cref="MapRenderPickKind"/> retains the
    /// semantic source domain used by the editor resolver.
    /// </summary>
    public static bool TryPickCollision(
        MapRenderScene scene,
        MapRenderCamera camera,
        Vector2 screenPosition,
        Vector2 viewportSize,
        out MapRenderPickHit hit)
    {
        ArgumentNullException.ThrowIfNull(scene);
        hit = default;
        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
            return false;
        if (!TryMakeRay(
                camera,
                screenPosition,
                viewportSize,
                out Vector3 origin,
                out Vector3 direction))
        {
            return false;
        }

        float bestDistance = float.PositiveInfinity;
        foreach (MapRenderPickTriangle triangle in
                 scene.CollisionPickTriangles)
        {
            if (!IsCollisionKind(triangle.Kind) ||
                !TryHitTriangle(
                    origin,
                    direction,
                    triangle.P0,
                    triangle.P1,
                    triangle.P2,
                    out float distance) ||
                distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            hit = ToHit(
                triangle,
                distance,
                origin + direction * distance);
        }

        return float.IsFinite(bestDistance);
    }

    private static bool IsCollisionKind(MapRenderPickKind kind) =>
        kind is
            MapRenderPickKind.CollisionTriangle or
            MapRenderPickKind.CollisionBrushBounds or
            MapRenderPickKind.CollisionStaticModelBounds;

    /// <summary>
    /// Builds the same screen ray used by the scene-level picking APIs. This
    /// overload lets a backend apply its own exact visibility and LOD view
    /// without duplicating camera projection semantics.
    /// </summary>
    public static bool TryCreateScreenRay(
        MapRenderCamera camera,
        Vector2 screenPosition,
        Vector2 viewportSize,
        out Vector3 origin,
        out Vector3 direction) =>
        TryMakeRay(
            camera,
            screenPosition,
            viewportSize,
            out origin,
            out direction);

    /// <summary>
    /// Creates the exact diagnostic material payload used by the legacy
    /// scene picker. Backends may cache this scene-lifetime object and keep
    /// frame-local picking allocation-free.
    /// </summary>
    public static MapRenderPickMaterialInfo CreatePickMaterialInfo(
        MapRenderTexturedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return ToMaterialInfo(batch);
    }

    /// <summary>
    /// Creates the exact diagnostic material payload used by the legacy
    /// scene picker for one static-model batch.
    /// </summary>
    public static MapRenderPickMaterialInfo CreatePickMaterialInfo(
        MapRenderInstancedTexturedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return ToMaterialInfo(batch);
    }

    /// <summary>
    /// Intersects one immutable normal-camera pass using the same front-face
    /// agnostic ray/triangle rule and hit identity as the scene picker. World
    /// passes require an empty instance selection; static passes consume only
    /// the exact selected source-instance indices supplied by the caller.
    /// </summary>
    public static bool TryPickPreparedPass(
        RenderNormalCameraPreparedPassSnapshot source,
        MapRenderPickMaterialInfo materialInfo,
        ReadOnlySpan<int> selectedStaticInstanceIndices,
        Vector3 origin,
        Vector3 direction,
        out MapRenderPickHit hit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(materialInfo);
        hit = default;
        if (!Finite(origin) ||
            !Finite(direction) ||
            direction == Vector3.Zero ||
            source.Geometry.ByteOrder !=
                RenderPayloadByteOrder.LittleEndian ||
            source.Geometry.Topology !=
                RenderPrimitiveTopology.TriangleList ||
            !TryResolvePreparedLayout(
                source,
                out int positionOffsetBytes,
                out int textureCoordinateOffsetBytes))
        {
            return false;
        }

        float bestDistance = float.PositiveInfinity;
        if (source.SourceKind == RenderNormalCameraDrawSourceKind.World)
        {
            if (!selectedStaticInstanceIndices.IsEmpty)
                return false;
            foreach (RenderMaterialPickRangeSnapshot range in
                     source.PickRanges)
            {
                PickPreparedRange(
                    source,
                    range,
                    materialInfo,
                    positionOffsetBytes,
                    textureCoordinateOffsetBytes,
                    origin,
                    direction,
                    ref bestDistance,
                    ref hit);
            }
            return float.IsFinite(bestDistance);
        }

        foreach (int instanceIndex in selectedStaticInstanceIndices)
        {
            if ((uint)instanceIndex >=
                (uint)source.StaticInstances.Length)
            {
                hit = default;
                return false;
            }
        }
        foreach (int instanceIndex in selectedStaticInstanceIndices)
        {
            PickPreparedInstance(
                source,
                source.StaticInstances[instanceIndex],
                materialInfo,
                positionOffsetBytes,
                textureCoordinateOffsetBytes,
                origin,
                direction,
                ref bestDistance,
                ref hit);
        }
        return float.IsFinite(bestDistance);
    }

    public static IReadOnlyList<MapRenderPickCandidate> PickCandidates(
        MapRenderScene scene,
        MapRenderCamera camera,
        Vector2 screenPosition,
        Vector2 viewportSize,
        bool includeUntexturedGeometry = true,
        bool includeCollision = false,
        int maxCount = DefaultCandidateCount,
        int nearestSearchCount = DefaultCandidateSearchCount)
    {
        if (viewportSize.X <= 0 || viewportSize.Y <= 0 || maxCount <= 0)
            return [];

        if (!TryMakeRay(camera, screenPosition, viewportSize, out Vector3 origin, out Vector3 direction))
            return [];

        var candidates = new List<MapRenderPickCandidate>();
        foreach (MapRenderTexturedBatch batch in scene.TexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderPickRange range in batch.PickRanges)
            {
                if (TryPickRange(
                        batch.Vertices,
                        batch.Indices,
                        MapRenderScene.TexturedVertexFloatCount,
                        range,
                        materialInfo,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.Textured));
                }
            }
        }
        foreach (MapRenderInstancedTexturedBatch batch in scene.InstancedTexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderStaticModelInstance instance in batch.Instances)
            {
                if (TryPickInstanced(
                        batch.Vertices,
                        batch.Indices,
                        MapRenderScene.TexturedVertexFloatCount,
                        instance,
                        materialInfo,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.Textured));
                }
            }
        }

        if (includeUntexturedGeometry)
        {
            foreach (MapRenderPickRange range in scene.FallbackSolidPickRanges)
            {
                if (TryPickRange(
                        scene.FallbackSolidVertices,
                        scene.FallbackSolidIndices,
                        MapRenderScene.VertexFloatCount,
                        range,
                        null,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.FallbackSolid));
                }
            }

            foreach (MapRenderPickRange range in scene.SolidPickRanges)
            {
                if (TryPickRange(
                        scene.SolidVertices,
                        scene.SolidIndices,
                        MapRenderScene.VertexFloatCount,
                        range,
                        null,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.UntexturedSolid));
                }
            }

            foreach (MapRenderInstancedSolidBatch batch in scene.InstancedSolidBatches)
            {
                foreach (MapRenderStaticModelInstance instance in batch.Instances)
                {
                    if (TryPickInstanced(
                            batch.Vertices,
                            batch.Indices,
                            MapRenderScene.VertexFloatCount,
                            instance,
                            null,
                            origin,
                            direction,
                            out MapRenderPickHit hit))
                    {
                        candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.UntexturedSolid));
                    }
                }
            }
        }

        if (includeCollision)
        {
            foreach (MapRenderPickTriangle triangle in scene.CollisionPickTriangles)
            {
                if (TryHitTriangle(origin, direction, triangle.P0, triangle.P1, triangle.P2, out float distance))
                    candidates.Add(ToCandidate(ToHit(triangle, distance, origin + direction * distance), MapRenderPickCandidateLayer.Collision));
            }
        }

        int searchCount = Math.Max(maxCount, nearestSearchCount);
        return candidates
            .OrderBy(candidate => candidate.Hit.Distance)
            .ThenBy(candidate => candidate.Hit.SurfaceIndex)
            .Take(searchCount)
            .Select((candidate, index) => candidate with { DistanceRank = index + 1 })
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.DistanceRank)
            .ThenBy(candidate => candidate.Hit.SurfaceIndex)
            .Take(maxCount)
            .ToArray();
    }

    /// <summary>
    /// Returns the unique authored objects intersected by a screen ray in
    /// exact front-to-back order. Unlike <see cref="PickCandidates"/>, this
    /// diagnostic does not reorder hits by material-fallback priority, so an
    /// invisible/no-write surface cannot hide the geometry behind it.
    /// </summary>
    public static IReadOnlyList<MapRenderPickCandidate> PickRayStack(
        MapRenderScene scene,
        MapRenderCamera camera,
        Vector2 screenPosition,
        Vector2 viewportSize,
        bool includeUntexturedGeometry = true,
        bool includeCollision = false,
        int maxCount = 64)
    {
        if (viewportSize.X <= 0 || viewportSize.Y <= 0 || maxCount <= 0)
            return [];

        if (!TryMakeRay(camera, screenPosition, viewportSize, out Vector3 origin, out Vector3 direction))
            return [];

        var candidates = new List<MapRenderPickCandidate>();
        foreach (MapRenderTexturedBatch batch in scene.TexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderPickRange range in batch.PickRanges)
            {
                if (TryPickRange(
                        batch.Vertices,
                        batch.Indices,
                        MapRenderScene.TexturedVertexFloatCount,
                        range,
                        materialInfo,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.Textured));
                }
            }
        }
        foreach (MapRenderInstancedTexturedBatch batch in scene.InstancedTexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderStaticModelInstance instance in batch.Instances)
            {
                if (TryPickInstanced(
                        batch.Vertices,
                        batch.Indices,
                        MapRenderScene.TexturedVertexFloatCount,
                        instance,
                        materialInfo,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.Textured));
                }
            }
        }

        if (includeUntexturedGeometry)
        {
            foreach (MapRenderPickRange range in scene.FallbackSolidPickRanges)
            {
                if (TryPickRange(
                        scene.FallbackSolidVertices,
                        scene.FallbackSolidIndices,
                        MapRenderScene.VertexFloatCount,
                        range,
                        null,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.FallbackSolid));
                }
            }

            foreach (MapRenderPickRange range in scene.SolidPickRanges)
            {
                if (TryPickRange(
                        scene.SolidVertices,
                        scene.SolidIndices,
                        MapRenderScene.VertexFloatCount,
                        range,
                        null,
                        origin,
                        direction,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.UntexturedSolid));
                }
            }

            foreach (MapRenderInstancedSolidBatch batch in scene.InstancedSolidBatches)
            {
                foreach (MapRenderStaticModelInstance instance in batch.Instances)
                {
                    if (TryPickInstanced(
                            batch.Vertices,
                            batch.Indices,
                            MapRenderScene.VertexFloatCount,
                            instance,
                            null,
                            origin,
                            direction,
                            out MapRenderPickHit hit))
                    {
                        candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.UntexturedSolid));
                    }
                }
            }
        }

        if (includeCollision)
        {
            foreach (MapRenderPickTriangle triangle in scene.CollisionPickTriangles)
            {
                if (TryHitTriangle(origin, direction, triangle.P0, triangle.P1, triangle.P2, out float distance))
                {
                    candidates.Add(ToCandidate(
                        ToHit(triangle, distance, origin + direction * distance),
                        MapRenderPickCandidateLayer.Collision));
                }
            }
        }

        return candidates
            .GroupBy(candidate => (
                candidate.Hit.Kind,
                candidate.Hit.ObjectIndex,
                candidate.Hit.SurfaceIndex))
            .Select(group => group
                .OrderBy(candidate => candidate.Hit.Distance)
                .ThenBy(candidate => candidate.Layer == MapRenderPickCandidateLayer.Textured ? 0 : 1)
                .First())
            .OrderBy(candidate => candidate.Hit.Distance)
            .ThenBy(candidate => candidate.Hit.Kind)
            .ThenBy(candidate => candidate.Hit.ObjectIndex)
            .ThenBy(candidate => candidate.Hit.SurfaceIndex)
            .Take(maxCount)
            .Select((candidate, index) => candidate with { DistanceRank = index + 1 })
            .ToArray();
    }

    public static IReadOnlyList<MapRenderPickCandidate> FindNearbyCandidates(
        MapRenderScene scene,
        Vector3 position,
        bool includeUntexturedGeometry = true,
        bool includeCollision = false,
        int maxCount = DefaultCandidateCount,
        int nearestSearchCount = 256)
    {
        if (maxCount <= 0)
            return [];

        var candidates = new List<MapRenderPickCandidate>();
        foreach (MapRenderTexturedBatch batch in scene.TexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderPickRange range in batch.PickRanges)
            {
                if (TryFindClosestRange(
                        batch.Vertices,
                        batch.Indices,
                        MapRenderScene.TexturedVertexFloatCount,
                        range,
                        materialInfo,
                        position,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.Textured));
                }
            }
        }
        foreach (MapRenderInstancedTexturedBatch batch in scene.InstancedTexturedBatches)
        {
            MapRenderPickMaterialInfo materialInfo = ToMaterialInfo(batch);
            foreach (MapRenderStaticModelInstance instance in batch.Instances)
            {
                if (TryFindClosestInstanced(
                        batch.Vertices,
                        batch.Indices,
                        MapRenderScene.TexturedVertexFloatCount,
                        instance,
                        materialInfo,
                        position,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.Textured));
                }
            }
        }

        if (includeUntexturedGeometry)
        {
            foreach (MapRenderPickRange range in scene.FallbackSolidPickRanges)
            {
                if (TryFindClosestRange(
                        scene.FallbackSolidVertices,
                        scene.FallbackSolidIndices,
                        MapRenderScene.VertexFloatCount,
                        range,
                        null,
                        position,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.FallbackSolid));
                }
            }

            foreach (MapRenderPickRange range in scene.SolidPickRanges)
            {
                if (TryFindClosestRange(
                        scene.SolidVertices,
                        scene.SolidIndices,
                        MapRenderScene.VertexFloatCount,
                        range,
                        null,
                        position,
                        out MapRenderPickHit hit))
                {
                    candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.UntexturedSolid));
                }
            }

            foreach (MapRenderInstancedSolidBatch batch in scene.InstancedSolidBatches)
            {
                foreach (MapRenderStaticModelInstance instance in batch.Instances)
                {
                    if (TryFindClosestInstanced(
                            batch.Vertices,
                            batch.Indices,
                            MapRenderScene.VertexFloatCount,
                            instance,
                            null,
                            position,
                            out MapRenderPickHit hit))
                    {
                        candidates.Add(ToCandidate(hit, MapRenderPickCandidateLayer.UntexturedSolid));
                    }
                }
            }
        }

        if (includeCollision)
        {
            foreach (MapRenderPickTriangle triangle in scene.CollisionPickTriangles)
            {
                Vector3 closest = ClosestPointOnTriangle(position, triangle.P0, triangle.P1, triangle.P2);
                float distance = Vector3.Distance(position, closest);
                candidates.Add(ToCandidate(ToHit(triangle, distance, closest), MapRenderPickCandidateLayer.Collision));
            }
        }

        int searchCount = Math.Max(maxCount, nearestSearchCount);
        return candidates
            .OrderBy(candidate => candidate.Hit.Distance)
            .ThenBy(candidate => candidate.Hit.SurfaceIndex)
            .Take(searchCount)
            .Select((candidate, index) => candidate with { DistanceRank = index + 1 })
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.DistanceRank)
            .ThenBy(candidate => candidate.Hit.SurfaceIndex)
            .Take(maxCount)
            .ToArray();
    }

    private static bool TryMakeRay(
        MapRenderCamera camera,
        Vector2 screenPosition,
        Vector2 viewportSize,
        out Vector3 origin,
        out Vector3 direction)
    {
        float aspect = viewportSize.X / viewportSize.Y;
        float x = screenPosition.X / viewportSize.X * 2f - 1f;
        float y = 1f - screenPosition.Y / viewportSize.Y * 2f;
        float halfHeight = MathF.Tan(camera.FieldOfViewRadians * 0.5f);
        origin = camera.Position;
        Vector3 ray = camera.Forward + camera.Right * (x * halfHeight * aspect) + camera.Up * (y * halfHeight);
        if (ray == Vector3.Zero)
        {
            direction = default;
            return false;
        }

        direction = Vector3.Normalize(ray);
        return true;
    }

    private static bool TryResolvePreparedLayout(
        RenderNormalCameraPreparedPassSnapshot source,
        out int positionOffsetBytes,
        out int textureCoordinateOffsetBytes)
    {
        positionOffsetBytes = -1;
        textureCoordinateOffsetBytes = -1;
        if (source.Geometry.VertexLayout != source.VertexLayout.Identity ||
            source.Geometry.VertexStrideBytes !=
                source.VertexLayout.StrideBytes)
        {
            return false;
        }

        foreach (RenderVertexElementDescriptor element in
                 source.VertexLayout.Elements)
        {
            if (element.Semantic == RenderVertexSemantic.Position &&
                element.SemanticIndex == 0 &&
                element.Format == RenderVertexElementFormat.Float32x3)
            {
                positionOffsetBytes = element.OffsetBytes;
            }
            else if (
                element.Semantic ==
                    RenderVertexSemantic.TextureCoordinate &&
                element.SemanticIndex == 0 &&
                element.Format == RenderVertexElementFormat.Float32x2)
            {
                textureCoordinateOffsetBytes = element.OffsetBytes;
            }
        }
        return positionOffsetBytes >= 0;
    }

    private static void PickPreparedRange(
        RenderNormalCameraPreparedPassSnapshot source,
        RenderMaterialPickRangeSnapshot range,
        MapRenderPickMaterialInfo materialInfo,
        int positionOffsetBytes,
        int textureCoordinateOffsetBytes,
        Vector3 origin,
        Vector3 direction,
        ref float bestDistance,
        ref MapRenderPickHit hit)
    {
        if (range.FirstIndex < 0 || range.IndexCount < 0)
            return;

        int endIndex = Math.Min(
            source.Geometry.IndexCount,
            checked(range.FirstIndex + range.IndexCount));
        for (int index = range.FirstIndex;
             index + 2 < endIndex;
             index += 3)
        {
            if (!TryReadPreparedTriangle(
                    source,
                    positionOffsetBytes,
                    index,
                    out Vector3 p0,
                    out Vector3 p1,
                    out Vector3 p2) ||
                !TryHitTriangle(
                    origin,
                    direction,
                    p0,
                    p1,
                    p2,
                    out float distance) ||
                distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            MapRenderPickTriangleTexCoords? texCoords =
                TryReadPreparedTriangleTexCoords(
                    source,
                    textureCoordinateOffsetBytes,
                    index,
                    out MapRenderPickTriangleTexCoords foundTexCoords)
                    ? foundTexCoords
                    : null;
            hit = new MapRenderPickHit(
                range.Kind,
                range.ObjectIndex,
                range.SurfaceIndex,
                (index - range.FirstIndex) / 3,
                range.Name,
                distance,
                origin + direction * distance,
                materialInfo,
                texCoords,
                range.AuthoredMaterialName);
        }
    }

    private static void PickPreparedInstance(
        RenderNormalCameraPreparedPassSnapshot source,
        MapRenderStaticModelInstance instance,
        MapRenderPickMaterialInfo materialInfo,
        int positionOffsetBytes,
        int textureCoordinateOffsetBytes,
        Vector3 origin,
        Vector3 direction,
        ref float bestDistance,
        ref MapRenderPickHit hit)
    {
        int bestIndex = -1;
        float instanceBestDistance = bestDistance;
        for (int index = 0;
             index + 2 < source.Geometry.IndexCount;
             index += 3)
        {
            if (!TryReadPreparedTriangle(
                    source,
                    positionOffsetBytes,
                    index,
                    out Vector3 local0,
                    out Vector3 local1,
                    out Vector3 local2))
            {
                continue;
            }

            Vector3 p0 = TransformStaticPosition(instance, local0);
            Vector3 p1 = TransformStaticPosition(instance, local1);
            Vector3 p2 = TransformStaticPosition(instance, local2);
            if (!TryHitTriangle(
                    origin,
                    direction,
                    p0,
                    p1,
                    p2,
                    out float distance) ||
                distance >= instanceBestDistance)
            {
                continue;
            }

            instanceBestDistance = distance;
            bestIndex = index;
        }

        if (bestIndex < 0)
            return;

        bestDistance = instanceBestDistance;
        MapRenderPickTriangleTexCoords? texCoords =
            TryReadPreparedTriangleTexCoords(
                source,
                textureCoordinateOffsetBytes,
                bestIndex,
                out MapRenderPickTriangleTexCoords foundTexCoords)
                ? foundTexCoords
                : null;
        hit = new MapRenderPickHit(
            MapRenderPickKind.StaticModel,
            instance.ObjectIndex,
            instance.SurfaceIndex,
            bestIndex / 3,
            instance.Name,
            bestDistance,
            origin + direction * bestDistance,
            materialInfo,
            texCoords,
            instance.AuthoredMaterialName);
    }

    private static bool TryReadPreparedTriangle(
        RenderNormalCameraPreparedPassSnapshot source,
        int positionOffsetBytes,
        int indexOffset,
        out Vector3 p0,
        out Vector3 p1,
        out Vector3 p2)
    {
        p0 = default;
        p1 = default;
        p2 = default;
        if (!TryReadPreparedIndex(source, indexOffset, out uint i0) ||
            !TryReadPreparedIndex(source, indexOffset + 1, out uint i1) ||
            !TryReadPreparedIndex(source, indexOffset + 2, out uint i2))
        {
            return false;
        }
        return TryReadPreparedPosition(
                   source,
                   positionOffsetBytes,
                   i0,
                   out p0) &&
               TryReadPreparedPosition(
                   source,
                   positionOffsetBytes,
                   i1,
                   out p1) &&
               TryReadPreparedPosition(
                   source,
                   positionOffsetBytes,
                   i2,
                   out p2);
    }

    private static bool TryReadPreparedIndex(
        RenderNormalCameraPreparedPassSnapshot source,
        int indexOffset,
        out uint index)
    {
        index = 0;
        if ((uint)indexOffset >= (uint)source.Geometry.IndexCount)
            return false;

        ReadOnlySpan<byte> payload = source.Geometry.IndexPayload.AsSpan();
        int byteOffset;
        if (source.Geometry.IndexFormat == RenderIndexFormat.Unsigned16)
        {
            byteOffset = checked(indexOffset * sizeof(ushort));
            if ((uint)byteOffset > (uint)(payload.Length - sizeof(ushort)))
                return false;
            index = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(byteOffset, sizeof(ushort)));
            return true;
        }
        if (source.Geometry.IndexFormat != RenderIndexFormat.Unsigned32)
            return false;

        byteOffset = checked(indexOffset * sizeof(uint));
        if ((uint)byteOffset > (uint)(payload.Length - sizeof(uint)))
            return false;
        index = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.Slice(byteOffset, sizeof(uint)));
        return true;
    }

    private static bool TryReadPreparedPosition(
        RenderNormalCameraPreparedPassSnapshot source,
        int positionOffsetBytes,
        uint vertexIndex,
        out Vector3 position)
    {
        position = default;
        ulong byteOffset =
            (ulong)vertexIndex *
                (ulong)source.Geometry.VertexStrideBytes +
            (uint)positionOffsetBytes;
        ReadOnlySpan<byte> payload = source.Geometry.VertexPayload.AsSpan();
        if (byteOffset + 3 * sizeof(float) > (ulong)payload.Length)
            return false;

        int offset = (int)byteOffset;
        position = new Vector3(
            ReadSingleLittleEndian(payload, offset),
            ReadSingleLittleEndian(payload, offset + sizeof(float)),
            ReadSingleLittleEndian(payload, offset + 2 * sizeof(float)));
        return Finite(position);
    }

    private static bool TryReadPreparedTriangleTexCoords(
        RenderNormalCameraPreparedPassSnapshot source,
        int textureCoordinateOffsetBytes,
        int indexOffset,
        out MapRenderPickTriangleTexCoords texCoords)
    {
        texCoords = default;
        return textureCoordinateOffsetBytes >= 0 &&
            TryReadPreparedIndex(source, indexOffset, out uint i0) &&
            TryReadPreparedIndex(source, indexOffset + 1, out uint i1) &&
            TryReadPreparedIndex(source, indexOffset + 2, out uint i2) &&
            TryReadPreparedTexCoords(
                source,
                textureCoordinateOffsetBytes,
                i0,
                out Vector2 uv0,
                out Vector2 lightmapUv0) &&
            TryReadPreparedTexCoords(
                source,
                textureCoordinateOffsetBytes,
                i1,
                out Vector2 uv1,
                out Vector2 lightmapUv1) &&
            TryReadPreparedTexCoords(
                source,
                textureCoordinateOffsetBytes,
                i2,
                out Vector2 uv2,
                out Vector2 lightmapUv2) &&
            SetTexCoords(
                uv0,
                uv1,
                uv2,
                lightmapUv0,
                lightmapUv1,
                lightmapUv2,
                out texCoords);
    }

    private static bool TryReadPreparedTexCoords(
        RenderNormalCameraPreparedPassSnapshot source,
        int textureCoordinateOffsetBytes,
        uint vertexIndex,
        out Vector2 uv,
        out Vector2 lightmapUv)
    {
        uv = default;
        lightmapUv = default;
        ulong vertexOffset =
            (ulong)vertexIndex *
            (ulong)source.Geometry.VertexStrideBytes;
        ulong uvOffset = vertexOffset + (uint)textureCoordinateOffsetBytes;
        ulong lightmapOffset = vertexOffset +
            (uint)(MapRenderScene.TexturedLightmapUvOffset * sizeof(float));
        ReadOnlySpan<byte> payload = source.Geometry.VertexPayload.AsSpan();
        if (uvOffset + 2 * sizeof(float) > (ulong)payload.Length ||
            lightmapOffset + 2 * sizeof(float) > (ulong)payload.Length)
        {
            return false;
        }

        uv = new Vector2(
            ReadSingleLittleEndian(payload, (int)uvOffset),
            ReadSingleLittleEndian(
                payload,
                checked((int)uvOffset + sizeof(float))));
        lightmapUv = new Vector2(
            ReadSingleLittleEndian(payload, (int)lightmapOffset),
            ReadSingleLittleEndian(
                payload,
                checked((int)lightmapOffset + sizeof(float))));
        return Finite(uv) && Finite(lightmapUv);
    }

    private static float ReadSingleLittleEndian(
        ReadOnlySpan<byte> payload,
        int offset) =>
        BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(
                payload.Slice(offset, sizeof(float))));

    private static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static void PickRange(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        MapRenderPickRange range,
        MapRenderPickMaterialInfo? materialInfo,
        Vector3 origin,
        Vector3 direction,
        ref float bestDistance,
        ref MapRenderPickHit hit)
    {
        int endIndex = Math.Min(indices.Length, range.FirstIndex + range.IndexCount);
        for (int index = range.FirstIndex; index + 2 < endIndex; index += 3)
        {
            if (!TryReadTriangle(vertices, indices, vertexFloatCount, index, out Vector3 p0, out Vector3 p1, out Vector3 p2) ||
                !TryHitTriangle(origin, direction, p0, p1, p2, out float distance) ||
                distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            int triangleIndex = (index - range.FirstIndex) / 3;
            MapRenderPickTriangleTexCoords? texCoords = vertexFloatCount == MapRenderScene.TexturedVertexFloatCount &&
                                                         TryReadTriangleTexCoords(vertices, indices, index, out MapRenderPickTriangleTexCoords foundTexCoords)
                ? foundTexCoords
                : null;
            hit = new MapRenderPickHit(
                range.Kind,
                range.ObjectIndex,
                range.SurfaceIndex,
                triangleIndex,
                range.Name,
                distance,
                origin + direction * distance,
                materialInfo,
                texCoords,
                range.AuthoredMaterialName);
        }
    }

    private static void PickInstanced(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        MapRenderStaticModelInstance instance,
        MapRenderPickMaterialInfo? materialInfo,
        Vector3 origin,
        Vector3 direction,
        ref float bestDistance,
        ref MapRenderPickHit hit)
    {
        if (TryPickInstanced(
                vertices,
                indices,
                vertexFloatCount,
                instance,
                materialInfo,
                origin,
                direction,
                out MapRenderPickHit candidate) &&
            candidate.Distance < bestDistance)
        {
            bestDistance = candidate.Distance;
            hit = candidate;
        }
    }

    private static bool TryPickInstanced(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        MapRenderStaticModelInstance instance,
        MapRenderPickMaterialInfo? materialInfo,
        Vector3 origin,
        Vector3 direction,
        out MapRenderPickHit hit)
    {
        hit = default;
        float bestDistance = float.PositiveInfinity;
        int bestIndex = -1;
        for (int index = 0; index + 2 < indices.Length; index += 3)
        {
            if (!TryReadTriangle(vertices, indices, vertexFloatCount, index, out Vector3 local0, out Vector3 local1, out Vector3 local2))
                continue;

            Vector3 p0 = TransformStaticPosition(instance, local0);
            Vector3 p1 = TransformStaticPosition(instance, local1);
            Vector3 p2 = TransformStaticPosition(instance, local2);
            if (!TryHitTriangle(origin, direction, p0, p1, p2, out float distance) || distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = index;
        }

        if (bestIndex < 0)
            return false;

        MapRenderPickTriangleTexCoords? texCoords = vertexFloatCount == MapRenderScene.TexturedVertexFloatCount &&
                                                     TryReadTriangleTexCoords(vertices, indices, bestIndex, out MapRenderPickTriangleTexCoords foundTexCoords)
            ? foundTexCoords
            : null;
        hit = new MapRenderPickHit(
            MapRenderPickKind.StaticModel,
            instance.ObjectIndex,
            instance.SurfaceIndex,
            bestIndex / 3,
            instance.Name,
            bestDistance,
            origin + direction * bestDistance,
            materialInfo,
            texCoords,
            instance.AuthoredMaterialName);
        return true;
    }

    private static bool TryFindClosestInstanced(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        MapRenderStaticModelInstance instance,
        MapRenderPickMaterialInfo? materialInfo,
        Vector3 position,
        out MapRenderPickHit hit)
    {
        hit = default;
        float bestDistanceSquared = float.PositiveInfinity;
        int bestIndex = -1;
        Vector3 bestPosition = default;
        for (int index = 0; index + 2 < indices.Length; index += 3)
        {
            if (!TryReadTriangle(vertices, indices, vertexFloatCount, index, out Vector3 local0, out Vector3 local1, out Vector3 local2))
                continue;

            Vector3 p0 = TransformStaticPosition(instance, local0);
            Vector3 p1 = TransformStaticPosition(instance, local1);
            Vector3 p2 = TransformStaticPosition(instance, local2);
            Vector3 closest = ClosestPointOnTriangle(position, p0, p1, p2);
            float distanceSquared = Vector3.DistanceSquared(position, closest);
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestIndex = index;
            bestPosition = closest;
        }

        if (bestIndex < 0 || !float.IsFinite(bestDistanceSquared))
            return false;

        MapRenderPickTriangleTexCoords? texCoords = vertexFloatCount == MapRenderScene.TexturedVertexFloatCount &&
                                                     TryReadTriangleTexCoords(vertices, indices, bestIndex, out MapRenderPickTriangleTexCoords foundTexCoords)
            ? foundTexCoords
            : null;
        hit = new MapRenderPickHit(
            MapRenderPickKind.StaticModel,
            instance.ObjectIndex,
            instance.SurfaceIndex,
            bestIndex / 3,
            instance.Name,
            MathF.Sqrt(bestDistanceSquared),
            bestPosition,
            materialInfo,
            texCoords,
            instance.AuthoredMaterialName);
        return true;
    }

    private static Vector3 TransformStaticPosition(MapRenderStaticModelInstance instance, Vector3 local)
    {
        var position = new Vector4(local, 1f);
        return new Vector3(
            Vector4.Dot(instance.TransformRow0, position),
            Vector4.Dot(instance.TransformRow1, position),
            Vector4.Dot(instance.TransformRow2, position));
    }

    private static bool TryPickRange(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        MapRenderPickRange range,
        MapRenderPickMaterialInfo? materialInfo,
        Vector3 origin,
        Vector3 direction,
        out MapRenderPickHit hit)
    {
        hit = default;
        float bestDistance = float.PositiveInfinity;
        int endIndex = Math.Min(indices.Length, range.FirstIndex + range.IndexCount);
        for (int index = range.FirstIndex; index + 2 < endIndex; index += 3)
        {
            if (!TryReadTriangle(vertices, indices, vertexFloatCount, index, out Vector3 p0, out Vector3 p1, out Vector3 p2) ||
                !TryHitTriangle(origin, direction, p0, p1, p2, out float distance) ||
                distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            int triangleIndex = (index - range.FirstIndex) / 3;
            MapRenderPickTriangleTexCoords? texCoords = vertexFloatCount == MapRenderScene.TexturedVertexFloatCount &&
                                                         TryReadTriangleTexCoords(vertices, indices, index, out MapRenderPickTriangleTexCoords foundTexCoords)
                ? foundTexCoords
                : null;
            hit = new MapRenderPickHit(
                range.Kind,
                range.ObjectIndex,
                range.SurfaceIndex,
                triangleIndex,
                range.Name,
                distance,
                origin + direction * distance,
                materialInfo,
                texCoords,
                range.AuthoredMaterialName);
        }

        return float.IsFinite(bestDistance);
    }

    private static bool TryFindClosestRange(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        MapRenderPickRange range,
        MapRenderPickMaterialInfo? materialInfo,
        Vector3 position,
        out MapRenderPickHit hit)
    {
        hit = default;
        float bestDistanceSquared = float.PositiveInfinity;
        int bestIndex = -1;
        Vector3 bestPosition = default;
        int endIndex = Math.Min(indices.Length, range.FirstIndex + range.IndexCount);
        for (int index = range.FirstIndex; index + 2 < endIndex; index += 3)
        {
            if (!TryReadTriangle(vertices, indices, vertexFloatCount, index, out Vector3 p0, out Vector3 p1, out Vector3 p2))
                continue;

            Vector3 closest = ClosestPointOnTriangle(position, p0, p1, p2);
            float distanceSquared = Vector3.DistanceSquared(position, closest);
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            bestIndex = index;
            bestPosition = closest;
        }

        if (bestIndex < 0 || !float.IsFinite(bestDistanceSquared))
            return false;

        MapRenderPickTriangleTexCoords? texCoords = vertexFloatCount == MapRenderScene.TexturedVertexFloatCount &&
                                                     TryReadTriangleTexCoords(vertices, indices, bestIndex, out MapRenderPickTriangleTexCoords foundTexCoords)
            ? foundTexCoords
            : null;
        hit = new MapRenderPickHit(
            range.Kind,
            range.ObjectIndex,
            range.SurfaceIndex,
            (bestIndex - range.FirstIndex) / 3,
            range.Name,
            MathF.Sqrt(bestDistanceSquared),
            bestPosition,
            materialInfo,
            texCoords,
            range.AuthoredMaterialName);
        return true;
    }

    private static bool TryReadTriangle(
        float[] vertices,
        uint[] indices,
        int vertexFloatCount,
        int indexOffset,
        out Vector3 p0,
        out Vector3 p1,
        out Vector3 p2)
    {
        p0 = default;
        p1 = default;
        p2 = default;
        return TryReadPosition(vertices, vertexFloatCount, indices[indexOffset], out p0) &&
               TryReadPosition(vertices, vertexFloatCount, indices[indexOffset + 1], out p1) &&
               TryReadPosition(vertices, vertexFloatCount, indices[indexOffset + 2], out p2);
    }

    private static bool TryReadPosition(float[] vertices, int vertexFloatCount, uint vertexIndex, out Vector3 position)
    {
        position = default;
        ulong offset = (ulong)vertexIndex * (ulong)vertexFloatCount;
        if (offset + 2 >= (ulong)vertices.Length)
            return false;

        int i = (int)offset;
        position = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
        return true;
    }

    private static bool TryReadTriangleTexCoords(
        float[] vertices,
        uint[] indices,
        int indexOffset,
        out MapRenderPickTriangleTexCoords texCoords)
    {
        texCoords = default;
        return TryReadTexCoords(vertices, indices[indexOffset], out Vector2 uv0, out Vector2 lightmapUv0) &&
               TryReadTexCoords(vertices, indices[indexOffset + 1], out Vector2 uv1, out Vector2 lightmapUv1) &&
               TryReadTexCoords(vertices, indices[indexOffset + 2], out Vector2 uv2, out Vector2 lightmapUv2) &&
               SetTexCoords(uv0, uv1, uv2, lightmapUv0, lightmapUv1, lightmapUv2, out texCoords);
    }

    private static bool TryReadTexCoords(float[] vertices, uint vertexIndex, out Vector2 uv, out Vector2 lightmapUv)
    {
        uv = default;
        lightmapUv = default;
        const int stride = MapRenderScene.TexturedVertexFloatCount;
        ulong offset = (ulong)vertexIndex * stride;
        if (offset + MapRenderScene.TexturedVertexFloatCount - 1 >= (ulong)vertices.Length)
            return false;

        int i = (int)offset;
        uv = new Vector2(vertices[i + 3], vertices[i + 4]);
        lightmapUv = new Vector2(
            vertices[i + MapRenderScene.TexturedLightmapUvOffset],
            vertices[i + MapRenderScene.TexturedLightmapUvOffset + 1]);
        return true;
    }

    private static bool SetTexCoords(
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Vector2 lightmapUv0,
        Vector2 lightmapUv1,
        Vector2 lightmapUv2,
        out MapRenderPickTriangleTexCoords texCoords)
    {
        texCoords = new MapRenderPickTriangleTexCoords(uv0, uv1, uv2, lightmapUv0, lightmapUv1, lightmapUv2);
        return true;
    }

    private static bool TryHitTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        out float distance)
    {
        const float epsilon = 0.000001f;
        distance = 0f;
        Vector3 edge1 = p1 - p0;
        Vector3 edge2 = p2 - p0;
        Vector3 h = Vector3.Cross(direction, edge2);
        float a = Vector3.Dot(edge1, h);
        if (MathF.Abs(a) < epsilon)
            return false;

        float f = 1f / a;
        Vector3 s = origin - p0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0f || u > 1f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(direction, q);
        if (v < 0f || u + v > 1f)
            return false;

        float t = f * Vector3.Dot(edge2, q);
        if (t <= epsilon)
            return false;

        distance = t;
        return true;
    }

    private static MapRenderPickHit ToHit(MapRenderPickTriangle triangle, float distance, Vector3 position)
    {
        return new MapRenderPickHit(
            triangle.Kind,
            triangle.ObjectIndex,
            triangle.SurfaceIndex,
            triangle.TriangleIndex,
            triangle.Name,
            distance,
            position,
            null);
    }

    private static MapRenderPickCandidate ToCandidate(MapRenderPickHit hit, MapRenderPickCandidateLayer layer)
    {
        MapRenderPickMaterialInfo? material = hit.Material;
        bool isTextured = material is not null;
        bool isCameraColorCandidate = material is not null && MapRenderPassClassifier.CanSubmitToCameraColor(material.PassClass);
        bool isFallbackMaterialCandidate = material?.PassClass is
            "GenericMaterialFallback" or "MaterialColor" or "AuthoredMaterialCandidate";
        bool hasColorSemantic = material?.TextureSemantic == 0x02;
        float uvArea = hit.TexCoords is { } texCoords ? UvArea(texCoords) : 0f;
        bool hasNonDegenerateUv = uvArea > UvAreaEpsilon;

        int priority = layer switch
        {
            MapRenderPickCandidateLayer.Textured => 100,
            MapRenderPickCandidateLayer.FallbackSolid => 25,
            MapRenderPickCandidateLayer.UntexturedSolid => 10,
            _ => 0
        };
        if (isCameraColorCandidate)
            priority += hasColorSemantic ? 360 : 80;
        if (isFallbackMaterialCandidate)
            priority += 260;
        if (hasColorSemantic)
            priority += 360;
        if (hasNonDegenerateUv)
            priority += 360;
        else if (isTextured)
            priority -= 260;
        if (material?.UnresolvedCodeSamplerCount == 0)
            priority += 40;

        string reason = string.Join(
            ",",
            new[]
            {
                isCameraColorCandidate ? "camera-color" : null,
                isFallbackMaterialCandidate ? "material-fallback" : null,
                hasColorSemantic ? "color-semantic" : null,
                hasNonDegenerateUv ? "nondegenerate-uv" : "degenerate-or-solid",
                layer.ToString()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new MapRenderPickCandidate(
            hit,
            layer,
            DistanceRank: 0,
            priority,
            isTextured,
            isCameraColorCandidate,
            isFallbackMaterialCandidate,
            hasColorSemantic,
            hasNonDegenerateUv,
            uvArea,
            reason);
    }

    private static float UvArea(MapRenderPickTriangleTexCoords texCoords)
    {
        Vector2 edge0 = texCoords.Uv1 - texCoords.Uv0;
        Vector2 edge1 = texCoords.Uv2 - texCoords.Uv0;
        return MathF.Abs(edge0.X * edge1.Y - edge0.Y * edge1.X) * 0.5f;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
            return a;

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
            return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + ab * v;
        }

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
            return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + ac * w;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            float w = (d4 - d3) / (d4 - d3 + d5 - d6);
            return b + (c - b) * w;
        }

        float denominator = 1f / (va + vb + vc);
        float vFace = vb * denominator;
        float wFace = vc * denominator;
        return a + ab * vFace + ac * wFace;
    }

    private static MapRenderPickMaterialInfo ToMaterialInfo(MapRenderTexturedBatch batch)
    {
        return ToMaterialInfo(
            batch.Pass,
            batch.Texture,
            batch.UnresolvedCodeSamplerCount,
            batch.ColorLayers,
            batch.MaterialSamplers,
            batch.ShaderExecution,
            batch.ShaderExecutionStatus,
            batch.UvRoute,
            batch.State);
    }

    private static MapRenderPickMaterialInfo ToMaterialInfo(MapRenderInstancedTexturedBatch batch)
    {
        return ToMaterialInfo(
            batch.Pass,
            batch.Texture,
            batch.UnresolvedCodeSamplerCount,
            batch.ColorLayers,
            batch.MaterialSamplers,
            batch.ShaderExecution,
            batch.ShaderExecution.ProgramExecutionStatus,
            batch.UvRoute,
            batch.State);
    }

    private static MapRenderPickMaterialInfo ToMaterialInfo(
        MapRenderMaterialPass pass,
        MapRenderTexture texture,
        int unresolvedCodeSamplerCount,
        IReadOnlyList<MapRenderColorLayer> colorLayers,
        IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
        MapRenderShaderExecutionContract shaderExecution,
        string shaderExecutionStatus,
        MapRenderUvRoute uvRoute,
        MapRenderState state)
    {
        MapRenderSamplerState sampler = texture.DecodedSamplerState;
        return new MapRenderPickMaterialInfo(
            pass.MaterialName,
            pass.TechniqueSetName,
            pass.TechniqueSlot,
            pass.TechniqueName,
            pass.PassClass,
            pass.PassIndex,
            pass.SamplerArgIndex,
            pass.SamplerDest,
            pass.SamplerHash,
            pass.TextureSemantic,
            pass.TexCoordSource,
            texture.Name,
            texture.Width,
            texture.Height,
            texture.Format,
            texture.SamplerState,
            sampler.RsxClampMax,
            sampler.RsxDescriptorPad0F,
            sampler.RsxDescriptorPad1B,
            sampler.RsxSamplerCachePayload,
            sampler.RsxTexEnablePayload,
            sampler.RsxTexFilterPayload,
            sampler.RsxTexWrapPayload,
            sampler.FilterClass,
            sampler.MipClass,
            sampler.MinFilter,
            sampler.MagFilter,
            sampler.MipFilter,
            sampler.MaxAnisotropy,
            sampler.AddressU,
            sampler.AddressV,
            sampler.AddressW,
            texture.RsxTextureCommandState,
            texture.HasTransparency,
            texture.MipLevels.Count,
            unresolvedCodeSamplerCount,
            colorLayers.Select(layer => new MapRenderPickColorLayerInfo(
                layer.LayerIndex,
                layer.SamplerArgIndex,
                layer.SamplerDest,
                layer.SamplerHash,
                layer.TextureSemantic,
                layer.Texture.Name,
                layer.BlendWeightComponent,
                layer.UvRoute)).ToArray(),
            materialSamplers.Select(binding => new MapRenderPickMaterialSamplerInfo(
                binding.SamplerArgIndex,
                binding.SamplerDest,
                binding.SamplerHash,
                binding.TextureSemantic,
                binding.TextureName,
                binding.UvRoute)).ToArray(),
            shaderExecution,
            shaderExecutionStatus,
            uvRoute,
            state);
    }
}
