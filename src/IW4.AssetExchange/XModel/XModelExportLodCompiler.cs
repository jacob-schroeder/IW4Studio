using System.Numerics;
using IW4.Assets.Assets.XModel;

namespace IW4.AssetExchange.XModel;

/// <summary>Strict conversion of the visual portion of an XMODEL_EXPORT LOD to native XSurfaces.</summary>
public static class XModelExportLodCompiler
{
    private const float UnitTolerance = 0.002f;
    private const int DObjSkelMatSize = 0x40;

    public static XModelExportLodCompileResult Compile(
        XModelExportDocument document, int boneCount,
        bool compileCollisionTrees)
    {
        ArgumentNullException.ThrowIfNull(document);
        var blockers = new List<string>();
        if (boneCount is < 1 or > 192) blockers.Add("The XModel bone count cannot be represented by six PartBits words.");
        var result = new List<XSurface>();
        var materialIndices = new List<int>();
        foreach ((XModelExportObject _, int objectIndex) in document.Objects.Select((value, index) => (value, index)))
        {
            XModelExportTriangle[] objectTriangles = document.Triangles.Where(triangle => triangle.ObjectIndex == objectIndex).ToArray();
            string prefix = $"object {objectIndex}";
            if (objectTriangles.Length == 0) { blockers.Add($"{prefix}: has no triangles."); continue; }
            foreach (IGrouping<int, XModelExportTriangle> partition in objectTriangles.GroupBy(triangle => triangle.MaterialIndex).OrderBy(group => group.Key))
            {
                int materialIndex = partition.Key;
                XModelExportTriangle[] triangles = partition.ToArray();
                string partitionPrefix = $"{prefix} material {materialIndex}";
                if (materialIndex < 0 || materialIndex >= document.Materials.Count) { blockers.Add($"{partitionPrefix}: is outside imported material rows."); continue; }
                CompileSurfacePartition(
                    document,
                    triangles,
                    boneCount,
                    compileCollisionTrees,
                    materialIndex,
                    partitionPrefix,
                    triangleOffset: 0,
                    isWholePartition: true,
                    result,
                    materialIndices,
                    blockers);
            }
        }
        if (document.Objects.Count == 0) blockers.Add("The imported LOD has no objects.");
        if (result.Count > byte.MaxValue) blockers.Add("The imported LOD surface count exceeds the XModel byte limit.");
        uint[] partBits = new uint[6];
        foreach (XSurface surface in result)
            for (int i = 0; i < partBits.Length; i++) partBits[i] |= surface.PartBits[i];
        return new XModelExportLodCompileResult(Array.AsReadOnly(result.ToArray()), Array.AsReadOnly(materialIndices.ToArray()), Array.AsReadOnly(partBits), Array.AsReadOnly(blockers.ToArray()));
    }

    private static void CompileSurfacePartition(
        XModelExportDocument document,
        XModelExportTriangle[] triangles,
        int boneCount,
        bool compileCollisionTrees,
        int materialIndex,
        string partitionPrefix,
        int triangleOffset,
        bool isWholePartition,
        List<XSurface> destination,
        List<int> materialIndices,
        List<string> blockers)
    {
        string surfacePrefix = isWholePartition
            ? partitionPrefix
            : $"{partitionPrefix} triangles {triangleOffset}-{triangleOffset + triangles.Length - 1}";
        bool exceedsTriangleLimit = triangles.Length > ushort.MaxValue;
        bool compiled = false;
        bool exceedsVertexLimit = false;
        XSurface? surface = null;
        IReadOnlyList<string> errors = [];
        if (!exceedsTriangleLimit)
        {
            compiled = TryCompileSurface(
                document,
                triangles,
                boneCount,
                surfacePrefix,
                out surface,
                out errors,
                out exceedsVertexLimit);
        }

        if (!compiled && triangles.Length > 1 &&
            (exceedsTriangleLimit || exceedsVertexLimit))
        {
            int firstCount = triangles.Length / 2;
            CompileSurfacePartition(
                document,
                triangles[..firstCount],
                boneCount,
                compileCollisionTrees,
                materialIndex,
                partitionPrefix,
                triangleOffset,
                isWholePartition: false,
                destination,
                materialIndices,
                blockers);
            CompileSurfacePartition(
                document,
                triangles[firstCount..],
                boneCount,
                compileCollisionTrees,
                materialIndex,
                partitionPrefix,
                triangleOffset + firstCount,
                isWholePartition: false,
                destination,
                materialIndices,
                blockers);
            return;
        }

        if (!compiled || surface is null)
        {
            if (exceedsTriangleLimit)
                blockers.Add($"{surfacePrefix}: triangle count exceeds UInt16.");
            else
                blockers.AddRange(errors);
            return;
        }
        if (compileCollisionTrees && !XModelCollisionTreeCompiler.TryAttach(
                surface,
                surfacePrefix,
                out surface,
                out string? collisionBlocker))
        {
            blockers.Add(collisionBlocker!);
            return;
        }
        destination.Add(surface);
        materialIndices.Add(materialIndex);
    }

    private static bool TryCompileSurface(XModelExportDocument document, IReadOnlyList<XModelExportTriangle> triangles, int boneCount, string prefix, out XSurface? surface, out IReadOnlyList<string> blockers, out bool exceedsVertexLimit)
    {
        surface = null; exceedsVertexLimit = false;
        var errors = new List<string>();
        var corners = new List<CompiledCorner>(checked(triangles.Count * 3));
        foreach ((XModelExportTriangle triangle, int triangleIndex) in triangles.Select((value, index) => (value, index)))
        {
            if (!TryCorner(document, triangle.First, boneCount, $"{prefix} triangle {triangleIndex} corner 0", out CompiledCorner first, out string? error) ||
                !TryCorner(document, triangle.Second, boneCount, $"{prefix} triangle {triangleIndex} corner 1", out CompiledCorner second, out error) ||
                !TryCorner(document, triangle.Third, boneCount, $"{prefix} triangle {triangleIndex} corner 2", out CompiledCorner third, out error)) { errors.Add(error!); continue; }
            Vector3 cross = Vector3.Cross(second.Position - first.Position, third.Position - first.Position);
            if (!Finite(cross) || cross.LengthSquared() <= 0.0000000001f) { errors.Add($"{prefix} triangle {triangleIndex}: has non-finite or degenerate positions."); continue; }
            Vector2 duv1 = second.Uv - first.Uv, duv2 = third.Uv - first.Uv;
            float determinant = duv1.X * duv2.Y - duv1.Y * duv2.X;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.0000001f) { errors.Add($"{prefix} triangle {triangleIndex}: has UV-degenerate mapping."); continue; }
            Vector3 tangent = ((second.Position - first.Position) * duv2.Y - (third.Position - first.Position) * duv1.Y) / determinant;
            if (!Append(first, tangent, corners) || !Append(second, tangent, corners) || !Append(third, tangent, corners))
            {
                errors.Add($"{prefix} triangle {triangleIndex}: tangent cannot be orthogonalized and normalized against an authored normal.");
                continue;
            }
        }
        if (errors.Count != 0) { blockers = errors; return false; }
        // A native XSurface is indexed.  Collapse only byte-identical emitted
        // vertex facts (including the derived tangent and native weights), in
        // first-seen order, before applying the UInt16 vertex limit.
        var unique = new List<CompiledCorner>();
        var cornerToUnique = new int[corners.Count];
        var uniqueBySignature = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < corners.Count; index++)
        {
            string signature = Signature(corners[index]);
            if (!uniqueBySignature.TryGetValue(signature, out int vertex))
            {
                vertex = unique.Count;
                uniqueBySignature.Add(signature, vertex);
                unique.Add(corners[index]);
            }
            cornerToUnique[index] = vertex;
        }
        if (unique.Count == 0 || unique.Count > ushort.MaxValue) { exceedsVertexLimit = unique.Count > ushort.MaxValue; blockers = [$"{prefix}: emitted vertex count exceeds UInt16 or is empty."]; return false; }
        // The native blend stream is grouped by influence cardinality.  Reorder all corner-expanded vertices and remap triangle indices together.
        (CompiledCorner Corner, int Original)[] sorted = unique.Select((corner, index) => (corner, index)).OrderBy(value => value.corner.Weights.Count).ThenBy(value => value.index).ToArray();
        CompiledCorner[] ordered = sorted.Select(value => value.Corner).ToArray();
        int[] uniqueToOrdered = new int[unique.Count];
        for (int index = 0; index < sorted.Length; index++) uniqueToOrdered[sorted[index].Original] = index;
        bool rigid = ordered.All(corner => corner.Weights.Count == 1) &&
            ordered.Select(corner => corner.Weights[0].BoneIndex).Distinct().Count() == 1;
        byte[] verts0 = new byte[ordered.Length * XSurfaceVertexCodec.StreamStride];
        byte[] verts1 = new byte[ordered.Length * XSurfaceVertexCodec.StreamStride];
        var blend = new List<ushort>(); ushort[] counts = new ushort[4]; uint[] bits = new uint[6];
        for (int index = 0; index < ordered.Length; index++)
        {
            CompiledCorner corner = ordered[index];
            try { XSurfaceVertexCodec.WriteVertex(verts0, verts1, index, corner.Position, corner.Uv, corner.Color, corner.Normal, corner.Tangent); }
            catch (ArgumentOutOfRangeException) { blockers = [$"{prefix}: emitted vertex {index} has a UV, colour, or direction not representable by the native stream."]; surface = null; return false; }
            if (!rigid) counts[corner.Weights.Count - 1]++;
            foreach (Weight weight in corner.Weights) SetPartBit(bits, weight.BoneIndex);
            if (!rigid)
            {
                blend.Add(checked((ushort)(corner.Weights[0].BoneIndex * DObjSkelMatSize)));
                foreach (Weight weight in corner.Weights.Skip(1)) { blend.Add(checked((ushort)(weight.BoneIndex * DObjSkelMatSize))); blend.Add(weight.QuantizedWeight); }
            }
        }
        ushort[] indices = cornerToUnique.Select(value => checked((ushort)uniqueToOrdered[value])).ToArray();
        surface = new XSurface { DeformedRaw = rigid ? (byte)0 : (byte)1, StreamFlags = XSurfaceStreamFlags.None, VertCount = checked((ushort)ordered.Length), TriCount = checked((ushort)triangles.Count), TriIndices = Array.AsReadOnly(indices), VertexInfo = new XSurfaceVertexInfo { Blend0 = counts[0], Blend1 = counts[1], Blend2 = counts[2], Blend3 = counts[3], VertsBlend = Array.AsReadOnly(blend.ToArray()) }, Verts0 = Array.AsReadOnly(verts0), Verts1 = Array.AsReadOnly(verts1), VertListCount = rigid ? 1 : 0, VertList = rigid ? [new XRigidVertList { BoneOffset = checked((ushort)(ordered[0].Weights[0].BoneIndex * DObjSkelMatSize)), VertCount = checked((ushort)ordered.Length), TriOffset = 0, TriCount = checked((ushort)triangles.Count) }] : [], PartBits = Array.AsReadOnly(bits) };
        blockers = [];
        return true;
    }

    private static bool Append(CompiledCorner corner, Vector3 rawTangent, List<CompiledCorner> destination)
    {
        Vector3 tangent = rawTangent - corner.Normal * Vector3.Dot(rawTangent, corner.Normal);
        if (!Normalize(tangent, out tangent)) return false;
        destination.Add(corner with { Tangent = tangent, Serial = destination.Count });
        return true;
    }
    private static bool TryCorner(XModelExportDocument document, XModelExportCorner corner, int boneCount, string prefix, out CompiledCorner result, out string? error)
    {
        result = null!; error = null;
        if (corner.VertexIndex < 0 || corner.VertexIndex >= document.Vertices.Count) { error = $"{prefix}: vertex index is out of range."; return false; }
        XModelExportVertex vertex = document.Vertices[corner.VertexIndex];
        if (!Finite(vertex.Position) || !Finite(corner.Uv0) || !Finite(corner.Color) || corner.Color.X is < 0 or > 1 || corner.Color.Y is < 0 or > 1 || corner.Color.Z is < 0 or > 1 || corner.Color.W is < 0 or > 1) { error = $"{prefix}: position, UV, or colour is non-finite/outside [0,1]."; return false; }
        if (!Finite(corner.Normal) || MathF.Abs(corner.Normal.Length() - 1f) > UnitTolerance || !Normalize(corner.Normal, out Vector3 normal)) { error = $"{prefix}: normal must be finite and unit length."; return false; }
        if (vertex.Weights.Count is < 1 or > 4 || vertex.Weights.Select(w => w.BoneIndex).Distinct().Count() != vertex.Weights.Count) { error = $"{prefix}: vertex has duplicate or unsupported bone influences."; return false; }
        if (vertex.Weights.Any(w => w.BoneIndex < 0 || w.BoneIndex >= boneCount || !float.IsFinite(w.Weight) || w.Weight <= 0f)) { error = $"{prefix}: vertex has invalid bone index or weight."; return false; }
        if (MathF.Abs(vertex.Weights.Sum(w => w.Weight) - 1f) > UnitTolerance) { error = $"{prefix}: vertex weights must sum to one."; return false; }
        Weight[] weights = Quantize(vertex.Weights, prefix, out error); if (error is not null) return false;
        result = new CompiledCorner(vertex.Position, corner.Uv0, corner.Color, normal, default, weights, -1); return true;
    }
    private static Weight[] Quantize(IReadOnlyList<XModelExportBoneWeight> values, string prefix, out string? error)
    {
        error = null;
        XModelExportBoneWeight[] sorted = values.OrderByDescending(v => v.Weight).ThenBy(v => v.BoneIndex).ToArray();
        var result = new Weight[sorted.Length]; int secondaryTotal = 0;
        for (int i = 1; i < sorted.Length; i++) { int quantized = (int)MathF.Round(sorted[i].Weight * ushort.MaxValue, MidpointRounding.AwayFromZero); if (quantized <= 0 || quantized > ushort.MaxValue) { error = $"{prefix}: secondary weight cannot be represented as UInt16."; return []; } secondaryTotal += quantized; result[i] = new Weight(sorted[i].BoneIndex, (ushort)quantized); }
        if (secondaryTotal >= ushort.MaxValue) { error = $"{prefix}: primary weight has no representable nonnegative remainder."; return []; }
        result[0] = new Weight(sorted[0].BoneIndex, checked((ushort)(ushort.MaxValue - secondaryTotal)));
        return result;
    }
    private static void SetPartBit(uint[] bits, int bone) { if (bone >= bits.Length * 32) throw new InvalidDataException("Bone index exceeds six-word PartBits."); bits[bone / 32] |= 0x80000000u >> (bone % 32); }
    private static bool Normalize(Vector3 value, out Vector3 result) { result = default; if (!Finite(value) || value.LengthSquared() <= 0f) return false; result = Vector3.Normalize(value); return Finite(result); }
    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
    private static string Signature(CompiledCorner value) => string.Join("|", new[]
    {
        value.Position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Position.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        value.Uv.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Uv.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        value.Color.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Color.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Color.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Color.W.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        value.Normal.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Normal.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Normal.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        value.Tangent.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Tangent.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), value.Tangent.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        string.Join(",", value.Weights.Select(weight => $"{weight.BoneIndex}:{weight.QuantizedWeight}"))
    });
    private sealed record CompiledCorner(Vector3 Position, Vector2 Uv, Vector4 Color, Vector3 Normal, Vector3 Tangent, IReadOnlyList<Weight> Weights, int Serial);
    private readonly record struct Weight(int BoneIndex, ushort QuantizedWeight);
}

public sealed record XModelExportLodCompileResult(IReadOnlyList<XSurface> Surfaces, IReadOnlyList<int> ImportedMaterialIndices, IReadOnlyList<uint> PartBits, IReadOnlyList<string> Blockers)
{
    public bool IsSuccess => Blockers.Count == 0;
}
