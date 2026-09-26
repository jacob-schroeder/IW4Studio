using System.Numerics;
using Evergine.Bindings.MeshOptimizer;

namespace IW4.Formats.XModel;

/// <summary>Builds the authored visual LODs for a skinned character from its full-detail mesh.</summary>
public static class XModelCharacterLodGenerator
{
    // These are authoring aspirations, not guaranteed output counts. Meshoptimizer
    // may stop early to preserve topology or stay within the error limit. Error is
    // relative to each source object/material partition's spatial extent.
    private static readonly float[] TriangleRatios = [1f, 0.67f, 0.31f, 0.08f];
    private static readonly float[] ErrorLimits = [0f, 0.01f, 0.02f, 0.04f];
    private static readonly float[] AttributeWeights = [1f, 1f, 1f, 1f, 1f, 0.25f, 0.25f, 0.25f, 0.25f];
    private const int AttributeCount = 9; // normal3, UV2, color4; bone IDs are never numeric attributes.
    private const float FullDetailErrorLimit = 0.005f;
    private const float SharpCreaseCosine = 0.7071068f; // 45 degrees.
    private const int MaxUvAttempts = 4;

    /// <summary>
    /// Makes one LOD0 candidate from the original document. The 0.5% geometric
    /// error limit is fixed across budget attempts; callers can tighten only the
    /// triangle aspiration. Returned corner and skin facts come from the source.
    /// </summary>
    public static XModelExportDocument ReduceFullDetail(XModelExportDocument fullDetail, float targetTriangleRatio)
    {
        ArgumentNullException.ThrowIfNull(fullDetail);
        if (!float.IsFinite(targetTriangleRatio) || targetTriangleRatio is <= 0f or >= 1f)
            throw new ArgumentOutOfRangeException(nameof(targetTriangleRatio));
        ValidateNativeInput(fullDetail);
        return SimplifyDocument(fullDetail, targetTriangleRatio, FullDetailErrorLimit, 0);
    }

    public static IReadOnlyList<XModelExportDocument> Generate(XModelExportDocument fullDetail, int lodCount)
    {
        ArgumentNullException.ThrowIfNull(fullDetail);
        if (lodCount is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(lodCount), "A character model has one to four LOD slots.");
        if (lodCount == 1)
            return Array.AsReadOnly(new[] { fullDetail });

        ValidateNativeInput(fullDetail);
        var result = new XModelExportDocument[lodCount];
        result[0] = fullDetail;
        int previousTriangleCount = fullDetail.Triangles.Count;
        for (int lod = 1; lod < lodCount; lod++)
        {
            XModelExportDocument reduced = SimplifyDocument(
                fullDetail, TriangleRatios[lod], ErrorLimits[lod], lod);
            if (reduced.Triangles.Count >= previousTriangleCount)
                throw new InvalidDataException($"Character LOD {lod} has {reduced.Triangles.Count} triangles; LOD {lod - 1} has {previousTriangleCount}. The {ErrorLimits[lod]:P0} geometry error limit and protected borders/skin seams prevent further reduction. Simplify the source topology in Blender, then re-import.");
            result[lod] = reduced;
            previousTriangleCount = reduced.Triangles.Count;
        }
        return Array.AsReadOnly(result);
    }

    private static XModelExportDocument SimplifyDocument(
        XModelExportDocument source, float triangleRatio, float errorLimit, int lod)
    {
        string[] skinSignatures = SkinSignatures(source);
        Dictionary<Vector3, byte> positionFlags = PositionFlags(source, skinSignatures);
        var triangles = new List<XModelExportTriangle>(source.Triangles.Count);
        var vertices = new List<XModelExportVertex>();
        var compactIndexBySource = new Dictionary<int, int>();
        foreach (XModelExportTriangle[] partition in source.Triangles
                     .GroupBy(triangle => (triangle.ObjectIndex, triangle.MaterialIndex))
                     .Select(group => group.ToArray()))
        {
            XModelExportTriangle[] reduced = SimplifyPartition(
                source, partition, skinSignatures, positionFlags, triangleRatio, errorLimit, lod);
            foreach (XModelExportTriangle triangle in reduced)
            {
                triangles.Add(triangle with
                {
                    First = Compact(triangle.First),
                    Second = Compact(triangle.Second),
                    Third = Compact(triangle.Third)
                });
            }
        }
        return new XModelExportDocument(
            source.Bones,
            Array.AsReadOnly(vertices.ToArray()),
            Array.AsReadOnly(triangles.ToArray()),
            source.Objects,
            source.Materials);

        XModelExportCorner Compact(XModelExportCorner corner)
        {
            if (!compactIndexBySource.TryGetValue(corner.VertexIndex, out int index))
            {
                index = vertices.Count;
                compactIndexBySource.Add(corner.VertexIndex, index);
                vertices.Add(source.Vertices[corner.VertexIndex]);
            }
            return corner with { VertexIndex = index };
        }
    }

    private static unsafe XModelExportTriangle[] SimplifyPartition(
        XModelExportDocument document,
        XModelExportTriangle[] triangles,
        string[] skinSignatures,
        Dictionary<Vector3, byte> positionFlags,
        float triangleRatio,
        float errorLimit,
        int lod)
    {
        int targetTriangles = Math.Max(1, (int)MathF.Round(triangles.Length * triangleRatio, MidpointRounding.AwayFromZero));
        if (targetTriangles >= triangles.Length)
            return triangles;

        var unique = new List<XModelExportCorner>();
        var indexByCorner = new Dictionary<CornerKey, uint>();
        uint[] indices = new uint[checked(triangles.Length * 3)];
        int cursor = 0;
        foreach (XModelExportTriangle triangle in triangles)
        {
            Add(triangle.First);
            Add(triangle.Second);
            Add(triangle.Third);
        }

        float[] positions = new float[checked(unique.Count * 3)];
        float[] attributes = new float[checked(unique.Count * AttributeCount)];
        byte[] locks = new byte[unique.Count];
        for (int i = 0; i < unique.Count; i++)
        {
            XModelExportCorner corner = unique[i];
            Vector3 position = document.Vertices[corner.VertexIndex].Position;
            positions[i * 3] = position.X;
            positions[i * 3 + 1] = position.Y;
            positions[i * 3 + 2] = position.Z;
            attributes[i * AttributeCount] = corner.Normal.X;
            attributes[i * AttributeCount + 1] = corner.Normal.Y;
            attributes[i * AttributeCount + 2] = corner.Normal.Z;
            attributes[i * AttributeCount + 3] = corner.Uv0.X;
            attributes[i * AttributeCount + 4] = corner.Uv0.Y;
            attributes[i * AttributeCount + 5] = corner.Color.X;
            attributes[i * AttributeCount + 6] = corner.Color.Y;
            attributes[i * AttributeCount + 7] = corner.Color.Z;
            attributes[i * AttributeCount + 8] = corner.Color.W;
            if (positionFlags.TryGetValue(position, out byte flags))
                locks[i] = flags;
        }

        uint[] output = new uint[indices.Length]; // Native worst-case destination size is the full index count.
        nuint outputCount = 0;
        for (int attempt = 0; attempt < MaxUvAttempts; attempt++)
        {
            try
            {
                fixed (uint* destination = output, source = indices)
                fixed (float* vertexPositions = positions, vertexAttributes = attributes, weights = AttributeWeights)
                fixed (byte* vertexLocks = locks)
                {
                    outputCount = MeshOptimizer.SimplifyWithAttributes(
                        destination, source, (nuint)indices.Length,
                        vertexPositions, (nuint)unique.Count, (nuint)(3 * sizeof(float)),
                        vertexAttributes, (nuint)(AttributeCount * sizeof(float)),
                        weights, (nuint)AttributeCount, vertexLocks,
                        (nuint)(targetTriangles * 3), errorLimit,
                        (uint)(SimplifyOptions.LockBorder | SimplifyOptions.Regularize | SimplifyOptions.Permissive), null);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw new NotSupportedException(
                    "Automatic LOD generation could not load its bundled meshoptimizer library. " +
                    "Reinstall D3dbspLinker with its native libraries. Supported platforms are Apple Silicon, " +
                    "Windows x64/arm64, and Linux x64/arm64.", exception);
            }
            if (outputCount < (nuint)3 || outputCount > (nuint)output.Length || outputCount % (nuint)3 != 0)
                throw new InvalidDataException($"Character LOD {lod} received an invalid index count from meshoptimizer.");

            var invalidPositions = new HashSet<Vector3>();
            int invalidTriangles = 0;
            for (int triangleIndex = 0; triangleIndex < (int)outputCount / 3; triangleIndex++)
            {
                XModelExportCorner first = Resolve(output[triangleIndex * 3]);
                XModelExportCorner second = Resolve(output[triangleIndex * 3 + 1]);
                XModelExportCorner third = Resolve(output[triangleIndex * 3 + 2]);
                if (!XModelExportLodCompiler.HasUvDegenerateMapping(first.Uv0, second.Uv0, third.Uv0))
                    continue;
                invalidTriangles++;
                invalidPositions.Add(document.Vertices[first.VertexIndex].Position);
                invalidPositions.Add(document.Vertices[second.VertexIndex].Position);
                invalidPositions.Add(document.Vertices[third.VertexIndex].Position);
            }
            if (invalidTriangles == 0)
                break;

            // Keep the original one-ring around each bad corner intact. Every
            // coincident source vertex is locked through its position, including
            // UV and skin seam duplicates in the same geometric neighborhood.
            var lockedNeighborhood = new HashSet<Vector3>(invalidPositions);
            foreach (XModelExportTriangle sourceTriangle in triangles)
            {
                Vector3 first = document.Vertices[sourceTriangle.First.VertexIndex].Position;
                Vector3 second = document.Vertices[sourceTriangle.Second.VertexIndex].Position;
                Vector3 third = document.Vertices[sourceTriangle.Third.VertexIndex].Position;
                if (invalidPositions.Contains(first) || invalidPositions.Contains(second) ||
                    invalidPositions.Contains(third))
                {
                    lockedNeighborhood.Add(first);
                    lockedNeighborhood.Add(second);
                    lockedNeighborhood.Add(third);
                }
            }
            bool addedLock = false;
            for (int vertexIndex = 0; vertexIndex < unique.Count; vertexIndex++)
            {
                Vector3 position = document.Vertices[unique[vertexIndex].VertexIndex].Position;
                if (lockedNeighborhood.Contains(position) && locks[vertexIndex] != (byte)SimplifyVertexOptions.Lock)
                {
                    locks[vertexIndex] = (byte)SimplifyVertexOptions.Lock;
                    addedLock = true;
                }
            }
            if (!addedLock || attempt == MaxUvAttempts - 1)
                throw new InvalidDataException(
                    $"Character LOD {lod} material '{document.Materials[triangles[0].MaterialIndex].Name}' still has {invalidTriangles} UV-degenerate triangles after protected simplification. " +
                    "This reduction cannot produce valid native tangents with the authored UVs. Correct the UV layout or simplify the source mesh in Blender, then re-import.");
        }

        var reduced = new XModelExportTriangle[(int)outputCount / 3];
        for (int triangleIndex = 0; triangleIndex < reduced.Length; triangleIndex++)
        {
            XModelExportCorner first = Resolve(output[triangleIndex * 3]);
            XModelExportCorner second = Resolve(output[triangleIndex * 3 + 1]);
            XModelExportCorner third = Resolve(output[triangleIndex * 3 + 2]);
            reduced[triangleIndex] = new XModelExportTriangle(
                triangles[0].ObjectIndex, triangles[0].MaterialIndex, first, second, third);
        }
        return reduced;

        void Add(XModelExportCorner corner)
        {
            var key = new CornerKey(
                document.Vertices[corner.VertexIndex].Position,
                corner.Normal, corner.Color, corner.Uv0, skinSignatures[corner.VertexIndex]);
            if (!indexByCorner.TryGetValue(key, out uint index))
            {
                index = checked((uint)unique.Count);
                indexByCorner.Add(key, index);
                unique.Add(corner);
            }
            indices[cursor++] = index;
        }

        XModelExportCorner Resolve(uint index)
        {
            if (index >= (uint)unique.Count)
                throw new InvalidDataException($"Character LOD {lod} received an invalid vertex index from meshoptimizer.");
            return unique[(int)index];
        }
    }

    private static string[] SkinSignatures(XModelExportDocument document)
    {
        var signatures = new string[document.Vertices.Count];
        for (int index = 0; index < signatures.Length; index++)
        {
            signatures[index] = string.Join(",", document.Vertices[index].Weights
                .OrderBy(weight => weight.BoneIndex)
                .Select(weight => $"{weight.BoneIndex}:{BitConverter.SingleToInt32Bits(weight.Weight)}"));
        }
        return signatures;
    }

    private static Dictionary<Vector3, byte> PositionFlags(XModelExportDocument document, string[] skinSignatures)
    {
        var factsByPosition = new Dictionary<Vector3, PositionFacts>();
        foreach (XModelExportTriangle triangle in document.Triangles)
        {
            Visit(triangle.First);
            Visit(triangle.Second);
            Visit(triangle.Third);
        }
        var result = new Dictionary<Vector3, byte>();
        foreach ((Vector3 position, PositionFacts facts) in factsByPosition)
        {
            if (facts.Skins.Count > 1)
                result.Add(position, (byte)SimplifyVertexOptions.Lock);
            else if (facts.Uvs.Count > 1 || HasSharpCrease(facts.Normals))
                result.Add(position, (byte)SimplifyVertexOptions.Protect);
        }
        return result;

        void Visit(XModelExportCorner corner)
        {
            Vector3 position = document.Vertices[corner.VertexIndex].Position;
            if (!factsByPosition.TryGetValue(position, out PositionFacts? facts))
            {
                facts = new PositionFacts();
                factsByPosition.Add(position, facts);
            }
            facts.Skins.Add(skinSignatures[corner.VertexIndex]);
            facts.Uvs.Add(corner.Uv0);
            facts.Normals.Add(corner.Normal);
        }
    }

    private static bool HasSharpCrease(HashSet<Vector3> normals)
    {
        // Pathological coincident groups are conservatively protected without
        // quadratic pair comparisons.
        if (normals.Count > 16)
            return true;
        Vector3[] values = normals.ToArray();
        for (int first = 0; first < values.Length; first++)
            for (int second = first + 1; second < values.Length; second++)
                if (Vector3.Dot(values[first], values[second]) < SharpCreaseCosine)
                    return true;
        return false;
    }

    private static void ValidateNativeInput(XModelExportDocument document)
    {
        if (document.Bones is null || document.Vertices is null || document.Triangles is null ||
            document.Objects is null || document.Materials is null ||
            document.Triangles.Count == 0 || document.Objects.Count == 0 || document.Materials.Count == 0)
            throw new InvalidDataException("The full-detail character mesh has missing or empty geometry rows.");
        foreach ((XModelExportVertex? vertex, int index) in document.Vertices.Select((value, index) => (value, index)))
        {
            if (vertex is null || !Finite(vertex.Position) || vertex.Weights is null || vertex.Weights.Count is < 1 or > 4 ||
                vertex.Weights.Any(weight => weight is null || (uint)weight.BoneIndex >= (uint)document.Bones.Count ||
                    !float.IsFinite(weight.Weight) || weight.Weight <= 0f) ||
                vertex.Weights.Select(weight => weight.BoneIndex).Distinct().Count() != vertex.Weights.Count ||
                MathF.Abs(vertex.Weights.Sum(weight => weight.Weight) - 1f) > 0.002f)
                throw new InvalidDataException($"Character vertex {index} has invalid position or skin weights.");
        }
        foreach ((XModelExportTriangle? triangle, int index) in document.Triangles.Select((value, index) => (value, index)))
        {
            if (triangle is null || (uint)triangle.ObjectIndex >= (uint)document.Objects.Count ||
                (uint)triangle.MaterialIndex >= (uint)document.Materials.Count)
                throw new InvalidDataException($"Character triangle {index} has an invalid object or material index.");
            XModelExportCorner[] corners = [triangle.First, triangle.Second, triangle.Third];
            foreach (XModelExportCorner? corner in corners)
                if (corner is null || (uint)corner.VertexIndex >= (uint)document.Vertices.Count ||
                    !Finite(corner.Normal) || !Finite(corner.Uv0) || !Finite(corner.Color))
                    throw new InvalidDataException($"Character triangle {index} has an invalid corner.");
            if (XModelExportLodCompiler.HasUvDegenerateMapping(
                    triangle.First.Uv0, triangle.Second.Uv0, triangle.Third.Uv0))
                throw new InvalidDataException($"Character triangle {index} has UV-degenerate mapping before simplification.");
            Vector3 a = document.Vertices[triangle.First.VertexIndex].Position;
            Vector3 b = document.Vertices[triangle.Second.VertexIndex].Position;
            Vector3 c = document.Vertices[triangle.Third.VertexIndex].Position;
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float areaSquared = cross.LengthSquared();
            if (!Finite(cross) || !float.IsFinite(areaSquared) || areaSquared <= 0.0000000001f)
                throw new InvalidDataException($"Character triangle {index} has degenerate positions.");
        }
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private readonly record struct CornerKey(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 Uv, string SkinSignature);

    private sealed class PositionFacts
    {
        public HashSet<string> Skins { get; } = new(StringComparer.Ordinal);
        public HashSet<Vector2> Uvs { get; } = [];
        public HashSet<Vector3> Normals { get; } = [];
    }
}
