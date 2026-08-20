using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Material;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec3 = IW4.Assets.Math.Vec3;

using IW4.Render.Assets;
using IW4.Render.Geometry;
using IW4.Render.Picking;
using IW4.Render.Transforms;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    private static readonly uint[] CollisionBoundsLineIndices =
    [
        0, 1,
        1, 2,
        2, 3,
        3, 0,
        4, 5,
        5, 6,
        6, 7,
        7, 4,
        0, 4,
        1, 5,
        2, 6,
        3, 7
    ];

    private static readonly uint[] CollisionBoundsTriangleIndices =
    [
        0, 1, 2, 0, 2, 3,
        4, 6, 5, 4, 7, 6,
        0, 5, 1, 0, 4, 5,
        3, 2, 6, 3, 6, 7,
        0, 3, 7, 0, 7, 4,
        1, 5, 6, 1, 6, 2
    ];

    internal static bool MaterializesCollisionDiagnosticGeometry(
        MapRenderSceneBuildProfile profile) =>
        profile switch
        {
            MapRenderSceneBuildProfile.Neutral => true,
            MapRenderSceneBuildProfile.InteractiveNative => true,
            _ => throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile,
                "Unknown map-render scene build profile.")
        };

    internal static int AddCollisionDiagnosticGeometry(
        ClipMapAsset clipMap,
        List<float> vertices,
        List<uint> indices,
        List<MapRenderPickTriangle> pickTriangles,
        bool materializeDiagnosticGeometry,
        ref RenderBounds bounds,
        ref RenderBounds collisionBounds)
    {
        ArgumentNullException.ThrowIfNull(clipMap);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(pickTriangles);

        string collisionName = clipMap.Name ?? "collision";
        string trianglePickName = $"{collisionName}:triangle";
        string brushBoundsPickName =
            $"{collisionName}:brush bounds proxy";
        string staticModelBoundsPickName =
            $"{collisionName}:static-model bounds proxy";
        Vector3 triangleColor = new(0.1f, 0.85f, 0.95f);
        int triangles = 0;
        int triangleIndexCount = Math.Min(
            clipMap.TriIndices.Count,
            checked(clipMap.TriCount * 3));
        for (int i = 0; i + 2 < triangleIndexCount; i += 3)
        {
            int i0 = clipMap.TriIndices[i];
            int i1 = clipMap.TriIndices[i + 1];
            int i2 = clipMap.TriIndices[i + 2];
            if (i0 >= clipMap.Verts.Count || i1 >= clipMap.Verts.Count || i2 >= clipMap.Verts.Count)
                continue;

            Vector3 p0 = ToVector3(clipMap.Verts[i0]);
            Vector3 p1 = ToVector3(clipMap.Verts[i1]);
            Vector3 p2 = ToVector3(clipMap.Verts[i2]);
            if (!IsReasonable(p0) ||
                !IsReasonable(p1) ||
                !IsReasonable(p2))
            {
                continue;
            }
            if (materializeDiagnosticGeometry)
            {
                int triangleIndex = i / 3;
                pickTriangles.Add(new MapRenderPickTriangle(
                    MapRenderPickKind.CollisionTriangle,
                    triangleIndex,
                    0,
                    0,
                    trianglePickName,
                    p0,
                    p1,
                    p2));
                AddLine(vertices, indices, p0, p1, triangleColor);
                AddLine(vertices, indices, p1, p2, triangleColor);
                AddLine(vertices, indices, p2, p0, triangleColor);
            }
            bounds = bounds.Include(p0).Include(p1).Include(p2);
            collisionBounds = collisionBounds.Include(p0).Include(p1).Include(p2);
            triangles++;
        }

        if (!materializeDiagnosticGeometry)
            return triangles;

        Vector3 brushBoundsColor = new(1f, 0.38f, 0.08f);
        int brushProxyCount = Math.Min(
            clipMap.Brushes.Count,
            clipMap.BrushBounds.Count);
        Span<Vector3> boundsCorners = stackalloc Vector3[8];
        for (int brushIndex = 0;
             brushIndex < brushProxyCount;
             brushIndex++)
        {
            ModelBounds brushBounds = clipMap.BrushBounds[brushIndex];
            Vector3 midPoint = ToVector3(brushBounds.MidPoint);
            Vector3 halfSize = ToRenderHalfSize(brushBounds.HalfSize);
            if (!TryWriteBoundsCorners(
                    midPoint,
                    halfSize,
                    boundsCorners))
            {
                continue;
            }

            AddCollisionBoundsProxy(
                vertices,
                indices,
                pickTriangles,
                MapRenderPickKind.CollisionBrushBounds,
                brushIndex,
                brushBoundsPickName,
                boundsCorners,
                brushBoundsColor);
            IncludeCollisionBounds(
                boundsCorners,
                ref bounds,
                ref collisionBounds);
        }

        Vector3 staticModelBoundsColor = new(0.72f, 0.3f, 1f);
        for (int modelIndex = 0;
             modelIndex < clipMap.StaticModelList.Count;
             modelIndex++)
        {
            ClipStaticModel model = clipMap.StaticModelList[modelIndex];
            // IW4 serializes these historically named fields with Bounds
            // semantics: AbsMin is the midpoint and AbsMax is the half size.
            Vector3 midPoint = ToVector3(model.AbsMin);
            Vector3 halfSize = ToRenderHalfSize(model.AbsMax);
            if (!TryWriteBoundsCorners(
                    midPoint,
                    halfSize,
                    boundsCorners))
            {
                continue;
            }

            AddCollisionBoundsProxy(
                vertices,
                indices,
                pickTriangles,
                MapRenderPickKind.CollisionStaticModelBounds,
                modelIndex,
                staticModelBoundsPickName,
                boundsCorners,
                staticModelBoundsColor);
            IncludeCollisionBounds(
                boundsCorners,
                ref bounds,
                ref collisionBounds);
        }

        return triangles;
    }

    private static Vector3 ToRenderHalfSize(ModelVec3 value) =>
        new(
            MathF.Abs(value.X),
            MathF.Abs(value.Z),
            MathF.Abs(value.Y));

    private static bool TryWriteBoundsCorners(
        Vector3 midPoint,
        Vector3 halfSize,
        Span<Vector3> corners)
    {
        if (corners.Length < 8)
        {
            throw new ArgumentException(
                "Collision bounds require eight corner slots.",
                nameof(corners));
        }
        if (!IsFinite(midPoint) ||
            !IsFinite(halfSize) ||
            halfSize.X < 0f ||
            halfSize.Y < 0f ||
            halfSize.Z < 0f)
        {
            return false;
        }

        Vector3 minimum = midPoint - halfSize;
        Vector3 maximum = midPoint + halfSize;
        if (!IsReasonable(minimum) ||
            !IsReasonable(maximum))
        {
            return false;
        }

        corners[0] = new(minimum.X, minimum.Y, minimum.Z);
        corners[1] = new(maximum.X, minimum.Y, minimum.Z);
        corners[2] = new(maximum.X, maximum.Y, minimum.Z);
        corners[3] = new(minimum.X, maximum.Y, minimum.Z);
        corners[4] = new(minimum.X, minimum.Y, maximum.Z);
        corners[5] = new(maximum.X, minimum.Y, maximum.Z);
        corners[6] = new(maximum.X, maximum.Y, maximum.Z);
        corners[7] = new(minimum.X, maximum.Y, maximum.Z);
        return true;
    }

    private static void AddCollisionBoundsProxy(
        List<float> vertices,
        List<uint> indices,
        List<MapRenderPickTriangle> pickTriangles,
        MapRenderPickKind kind,
        int objectIndex,
        string name,
        ReadOnlySpan<Vector3> corners,
        Vector3 color)
    {
        uint baseIndex =
            checked((uint)(vertices.Count / MapRenderScene.VertexFloatCount));
        foreach (Vector3 corner in corners)
            AddVertex(vertices, corner, color);
        foreach (uint index in CollisionBoundsLineIndices)
            indices.Add(checked(baseIndex + index));

        for (int index = 0;
             index < CollisionBoundsTriangleIndices.Length;
             index += 3)
        {
            int faceIndex = index / 6;
            int triangleInFace = index / 3 % 2;
            pickTriangles.Add(new MapRenderPickTriangle(
                kind,
                objectIndex,
                faceIndex,
                triangleInFace,
                name,
                corners[checked((int)CollisionBoundsTriangleIndices[index])],
                corners[checked((int)CollisionBoundsTriangleIndices[index + 1])],
                corners[checked((int)CollisionBoundsTriangleIndices[index + 2])]));
        }
    }

    private static void IncludeCollisionBounds(
        ReadOnlySpan<Vector3> corners,
        ref RenderBounds bounds,
        ref RenderBounds collisionBounds)
    {
        foreach (Vector3 corner in corners)
        {
            bounds = bounds.Include(corner);
            collisionBounds = collisionBounds.Include(corner);
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static void AddPickRange(
        List<MapRenderPickRange> ranges,
        MapRenderPickKind kind,
        int objectIndex,
        int surfaceIndex,
        int firstIndex,
        int currentIndexCount,
        string name,
        string? authoredMaterialName = null)
    {
        int indexCount = currentIndexCount - firstIndex;
        if (indexCount <= 0)
            return;

        ranges.Add(new MapRenderPickRange(
            kind,
            objectIndex,
            surfaceIndex,
            firstIndex,
            indexCount,
            name,
            authoredMaterialName ?? string.Empty));
    }

    private static bool IsSkyMaterial(MaterialAsset? material, RenderAssetLookup lookup)
    {
        string? techniqueSetName = material?.TechniqueSet?.Name ??
                                   (material is null ? null : lookup.ResolveTechniqueSet(material.TechniqueSetPointer)?.Name);
        return string.Equals(techniqueSetName, "wc_sky", StringComparison.Ordinal);
    }

    private static void AddTriangle(
        List<float> vertices,
        List<uint> indices,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector3 color)
    {
        uint baseIndex = checked((uint)(vertices.Count / MapRenderScene.VertexFloatCount));
        AddVertex(vertices, p0, color);
        AddVertex(vertices, p1, color);
        AddVertex(vertices, p2, color);
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
    }

    private static void AddTexturedTriangle(
        List<float> vertices,
        List<uint> indices,
        Vector3 p0,
        Vector3 p1,
        Vector3 p2,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        IReadOnlyList<Vector2>? layerUvs0 = null,
        IReadOnlyList<Vector2>? layerUvs1 = null,
        IReadOnlyList<Vector2>? layerUvs2 = null,
        Vector4 blendWeights0 = default,
        Vector4 blendWeights1 = default,
        Vector4 blendWeights2 = default,
        Vector3 normal0 = default,
        Vector3 normal1 = default,
        Vector3 normal2 = default)
    {
        uint baseIndex = checked((uint)(vertices.Count / MapRenderScene.TexturedVertexFloatCount));
        AddTexturedVertex(vertices, p0, uv0, layerUvs0, blendWeights0, normal0);
        AddTexturedVertex(vertices, p1, uv1, layerUvs1, blendWeights1, normal1);
        AddTexturedVertex(vertices, p2, uv2, layerUvs2, blendWeights2, normal2);
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
    }

    private static void AddLine(List<float> vertices, List<uint> indices, Vector3 p0, Vector3 p1, Vector3 color)
    {
        uint baseIndex = checked((uint)(vertices.Count / MapRenderScene.VertexFloatCount));
        AddVertex(vertices, p0, color);
        AddVertex(vertices, p1, color);
        indices.Add(baseIndex);
        indices.Add(baseIndex + 1);
    }

    private static void AddVertex(List<float> vertices, Vector3 position, Vector3 color)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
        vertices.Add(color.X);
        vertices.Add(color.Y);
        vertices.Add(color.Z);
    }

    private static void AddTexturedVertex(
        List<float> vertices,
        Vector3 position,
        Vector2 texCoord,
        IReadOnlyList<Vector2>? layerUvs,
        Vector4 blendWeights,
        Vector3 normal)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
        for (int layerIndex = 0; layerIndex < MapRenderScene.MaxColorLayerCount; layerIndex++)
        {
            Vector2 layerUv = layerUvs is not null && layerIndex < layerUvs.Count
                ? layerUvs[layerIndex]
                : texCoord;
            vertices.Add(layerUv.X);
            vertices.Add(layerUv.Y);
        }

        vertices.Add(blendWeights.X);
        vertices.Add(blendWeights.Y);
        vertices.Add(blendWeights.Z);
        vertices.Add(blendWeights.W);

        // Static models do not index the world lightmap arrays. Keep the
        // dedicated channel neutral instead of aliasing the color UV.
        vertices.Add(0f);
        vertices.Add(0f);
        vertices.Add(normal.X);
        vertices.Add(normal.Y);
        vertices.Add(normal.Z);
    }

    private static Vector3 ColorFor(string key)
    {
        uint hash = 2166136261u;
        foreach (char value in key)
        {
            hash ^= value;
            hash *= 16777619u;
        }

        return new Vector3(
            0.35f + ((hash & 0xff) / 255f) * 0.45f,
            0.35f + (((hash >> 8) & 0xff) / 255f) * 0.45f,
            0.35f + (((hash >> 16) & 0xff) / 255f) * 0.45f);
    }

    private static bool IsReasonable(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z) &&
               MathF.Abs(value.X) < MaxReasonableCoordinate &&
               MathF.Abs(value.Y) < MaxReasonableCoordinate &&
               MathF.Abs(value.Z) < MaxReasonableCoordinate;
    }

    private static bool TryPrepareTexCoord(
        Vector2 raw,
        bool allowSanitization,
        out Vector2 texCoord,
        out bool sanitized)
    {
        sanitized = false;
        if (IsReasonableTexCoord(raw))
        {
            texCoord = raw;
            return true;
        }

        if (!allowSanitization)
        {
            texCoord = default;
            return false;
        }

        texCoord = new Vector2(SanitizeTexCoordComponent(raw.X), SanitizeTexCoordComponent(raw.Y));
        sanitized = true;
        return true;
    }

    private static bool IsReasonableTexCoord(Vector2 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               MathF.Abs(value.X) <= MaxReasonableTexCoord &&
               MathF.Abs(value.Y) <= MaxReasonableTexCoord;
    }

    private static float SanitizeTexCoordComponent(float value)
    {
        if (!float.IsFinite(value))
            return 0f;

        return Math.Clamp(value, -MaxReasonableTexCoord, MaxReasonableTexCoord);
    }

    private static Vector3 ToVector3(ModelVec3 value)
    {
        return ToRenderCoordinates(new Vector3(value.X, value.Y, value.Z));
    }

    private static Vector3 ToRenderCoordinates(Vector3 value) =>
        RenderCoordinateConverter.GameToRenderPosition(value);

    private static RenderBounds IncludeBounds(RenderBounds bounds, RenderBounds other)
    {
        return other.IsValid
            ? bounds.Include(other.Min).Include(other.Max)
            : bounds;
    }

}
