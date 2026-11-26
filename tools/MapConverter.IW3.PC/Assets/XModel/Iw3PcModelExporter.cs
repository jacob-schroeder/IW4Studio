using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets.XModel;

/// <summary>Exports IW3 PC models as OAT version-6 mesh files.</summary>
internal static class Iw3PcModelExporter
{
    internal static Dictionary<string, byte[]> ReadFiles(Iw3PcZone zone, Iw3PcAsset asset)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Type != "xmodel" || asset.Name.StartsWith(",", StringComparison.Ordinal))
            throw new ArgumentException("An owned IW3 PC XModel is required.", nameof(asset));
        int root = asset.Offset;
        ReadOnlySpan<byte> model = zone.Bytes(root, 220);
        int bones = model[4], roots = model[5], surfaceCount = model[6];
        int lodCount = U16(model, 196);
        if (bones == 0 || roots == 0 || roots > bones || lodCount > 4)
            throw new NotSupportedException($"IW3 PC XModel '{asset.Name}' has unsupported bone or LOD counts.");
        int boneNames = Required(zone, root + 8, "bone names");
        int parentList = roots == bones ? 0 : Required(zone, root + 12, "bone parents");
        int baseMat = Required(zone, root + 28, "base matrix");
        zone.Bytes(boneNames, checked(bones * 2));
        zone.Bytes(baseMat, checked(bones * 32));
        var skeleton = new Bone[bones];
        for (int index = 0; index < bones; index++)
        {
            int scriptString = U16(zone.Bytes(boneNames + index * 2, 2), 0);
            if (scriptString >= zone.ScriptStrings.Count)
                throw new InvalidDataException($"IW3 PC XModel '{asset.Name}' has an invalid bone name.");
            string boneName = zone.ScriptStrings[scriptString];
            if (!ValidQuoted(boneName))
                throw new NotSupportedException($"IW3 PC XModel '{asset.Name}' bone name cannot be exported.");
            int parent = index < roots ? -1 : index - zone.Bytes(parentList + index - roots, 1)[0];
            if (index >= roots && (parent < 0 || parent >= index))
                throw new InvalidDataException($"IW3 PC XModel '{asset.Name}' has an invalid bone parent.");
            int matrix = baseMat + index * 32;
            Vector4 quat = new(Float(zone, matrix), Float(zone, matrix + 4),
                Float(zone, matrix + 8), Float(zone, matrix + 12));
            Vector3 offset = new(Float(zone, matrix + 16), Float(zone, matrix + 20), Float(zone, matrix + 24));
            if (!Finite(quat) || !Finite(offset))
                throw new InvalidDataException($"IW3 PC XModel '{asset.Name}' has an invalid bone transform.");
            skeleton[index] = new Bone(boneName, parent, quat, offset);
        }
        int surfaces = surfaceCount == 0 ? zone.Pointer(root + 32) : Required(zone, root + 32, "surfaces");
        int materialFields = surfaceCount == 0 ? zone.Pointer(root + 36) : Required(zone, root + 36, "materials");
        if (surfaceCount != 0)
        {
            zone.Bytes(surfaces, checked(surfaceCount * 56));
            zone.Bytes(materialFields, checked(surfaceCount * 4));
        }
        var materials = new List<(string Name, string ColorMap)>();
        var materialByPointer = new Dictionary<int, int>();
        var materialIndices = new int[surfaceCount];
        for (int index = 0; index < surfaceCount; index++)
        {
            int material = Required(zone, materialFields + index * 4, "surface material");
            string name = StripReference(zone.String(material));
            if (!ValidQuoted(name) || name.Length == 0)
                throw new InvalidDataException($"IW3 PC XModel '{asset.Name}' has an invalid material name.");
            if (!materialByPointer.TryGetValue(material, out int existing))
            {
                existing = materials.Count;
                string colorMap = ReadColorMap(zone, material);
                if (!ValidQuoted(colorMap))
                    throw new NotSupportedException($"IW3 PC XModel '{asset.Name}' has an unexportable color map name.");
                materials.Add((name, colorMap));
                materialByPointer.Add(material, existing);
            }
            materialIndices[index] = existing;
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var lods = new List<object>(lodCount);
        bool animated = false;
        for (int index = 0; index < surfaceCount; index++)
            animated |= zone.Pointer(surfaces + index * 56 + 24) != 0;
        string modelType = !animated ? "rigid" :
            IsViewhands(zone, root, bones, roots) ? "viewhands" : "animated";
        for (int lod = 0; lod < lodCount; lod++)
        {
            int info = root + 40 + lod * 28;
            float distance = Float(zone, info);
            int firstSurface = U16(zone.Bytes(info + 6, 2), 0);
            int activeSurfaceCount = U16(zone.Bytes(info + 4, 2), 0);
            if (!float.IsFinite(distance) ||
                (activeSurfaceCount != 0 && firstSurface > surfaceCount - activeSurfaceCount))
                throw new NotSupportedException($"IW3 PC XModel '{asset.Name}' has an unsupported LOD {lod}.");
            string path = $"model_export/{asset.Name}_lod{lod}.xmodel_export";
            files.Add(path, Encoding.UTF8.GetBytes(WriteLod(zone, asset.Name,
                surfaces, firstSurface, activeSurfaceCount, materialIndices, materials,
                skeleton, animated, out byte[]? nativeBasis)));
            if (nativeBasis is not null)
                files.Add(Path.ChangeExtension(path, ".basis.json"), nativeBasis);
            lods.Add(new { distance, file = path });
        }
        int collLod = BinaryPrimitives.ReadInt16LittleEndian(model[198..]);
        var metadata = new Dictionary<string, object>
        {
            ["$schema"] = "http://openassettools.dev/schema/xmodel.v1.json",
            ["_type"] = "xmodel",
            ["_version"] = 2,
            ["_game"] = "iw3",
            ["type"] = modelType,
            ["flags"] = model[208],
            ["lods"] = lods
        };
        if (collLod >= 0) metadata.Add("collLod", collLod);
        files.Add($"xmodel/{asset.Name}.json", JsonSerializer.SerializeToUtf8Bytes(metadata,
            new JsonSerializerOptions { WriteIndented = true }));
        return files;
    }

    private static string WriteLod(Iw3PcZone zone, string name, int surfaceBase,
        int firstSurface, int surfaceCount, IReadOnlyList<int> materialIndices,
        IReadOnlyList<(string Name, string ColorMap)> materials, IReadOnlyList<Bone> skeleton,
        bool animated, out byte[]? nativeBasis)
    {
        var vertices = new List<Vertex>();
        var positions = new List<Vertex>();
        var positionByCorner = new List<int>();
        var rigidPositions = new Dictionary<(Vector3 Position, int Bone), int>();
        List<float[]>? basisRows = animated ? [] : null;
        var faces = new List<(int Object, int Material, int A, int B, int C)>();
        for (int objectIndex = 0; objectIndex < surfaceCount; objectIndex++)
        {
            int surfaceIndex = firstSurface + objectIndex;
            int surface = surfaceBase + surfaceIndex * 56;
            int vertCount = U16(zone.Bytes(surface + 2, 2), 0);
            int triCount = U16(zone.Bytes(surface + 4, 2), 0);
            int rigidCount = I32(zone.Bytes(surface + 32, 4), 0);
            if (rigidCount < 0)
                throw new NotSupportedException($"IW3 PC XModel '{name}' has an invalid rigid-list count.");
            List<BoneWeight[]> weights = ReadWeights(zone, name, surface, vertCount,
                triCount, rigidCount, skeleton.Count);
            int vertexBase = vertCount == 0 ? zone.Pointer(surface + 28) :
                Required(zone, surface + 28, "packed vertices");
            int triangleBase = triCount == 0 ? zone.Pointer(surface + 12) :
                Required(zone, surface + 12, "triangle indices");
            if (vertCount != 0) zone.Bytes(vertexBase, checked(vertCount * 32));
            if (triCount != 0) zone.Bytes(triangleBase, checked(triCount * 6));
            int firstVertex = vertices.Count;
            for (int index = 0; index < vertCount; index++)
            {
                ReadOnlySpan<byte> source = zone.Bytes(vertexBase + index * 32, 32);
                Vertex vertex = ReadVertex(source, weights[index]);
                vertices.Add(vertex);
                int positionIndex = positions.Count;
                if (!animated)
                {
                    // XMODEL_EXPORT face corners share a position and bone across UV and normal seams.
                    var key = (vertex.Position, vertex.Weights.Length == 0 ? -1 : vertex.Weights[0].Bone);
                    if (!rigidPositions.TryAdd(key, positionIndex))
                        positionIndex = rigidPositions[key];
                }
                if (positionIndex == positions.Count)
                    positions.Add(vertex);
                positionByCorner.Add(positionIndex);
                if (basisRows is not null)
                {
                    Vector3 tangent = DecodeDirection(U32(source, 28));
                    basisRows.Add([vertex.Position.X, vertex.Position.Y, vertex.Position.Z,
                        tangent.X, tangent.Y, tangent.Z, Float(source, 12)]);
                }
            }
            for (int index = 0; index < triCount; index++)
            {
                ReadOnlySpan<byte> triangle = zone.Bytes(triangleBase + index * 6, 6);
                int a = U16(triangle, 0), b = U16(triangle, 2), c = U16(triangle, 4);
                if (a >= vertCount || b >= vertCount || c >= vertCount)
                    throw new InvalidDataException($"IW3 PC XModel '{name}' has a triangle outside its surface.");
                faces.Add((objectIndex, materialIndices[surfaceIndex],
                    firstVertex + a, firstVertex + b, firstVertex + c));
            }
        }
        nativeBasis = basisRows is null ? null : JsonSerializer.SerializeToUtf8Bytes(basisRows);
        var text = new StringBuilder();
        void Line(string value = "") => text.Append(value).Append('\n');
        Line("MODEL"); Line("VERSION 6"); Line();
        Line($"NUMBONES {skeleton.Count}");
        for (int index = 0; index < skeleton.Count; index++)
            Line($"BONE {index} {skeleton[index].Parent} \"{skeleton[index].Name}\"");
        Line();
        for (int index = 0; index < skeleton.Count; index++)
        {
            Bone bone = skeleton[index];
            Line($"BONE {index}"); Line($"OFFSET {Vec(bone.Offset)}");
            Line("SCALE 1.000000, 1.000000, 1.000000");
            WriteRotation(Line, bone.Quaternion); Line();
        }
        Line($"NUMVERTS {positions.Count}");
        for (int index = 0; index < positions.Count; index++)
        {
            Vertex position = positions[index];
            Line($"VERT {index}"); Line($"OFFSET {Vec(position.Position)}");
            Line($"BONES {position.Weights.Length}");
            foreach (BoneWeight weight in position.Weights)
                Line($"BONE {weight.Bone} {F(weight.Weight)}");
            Line();
        }
        Line($"NUMFACES {faces.Count}");
        foreach ((int obj, int material, int a, int b, int c) in faces)
        {
            Line($"TRI {obj} {material} 0 0");
            WriteCorner(Line, positionByCorner[a], vertices[a]);
            WriteCorner(Line, positionByCorner[b], vertices[b]);
            WriteCorner(Line, positionByCorner[c], vertices[c]); Line();
        }
        Line($"NUMOBJECTS {surfaceCount}");
        for (int index = 0; index < surfaceCount; index++) Line($"OBJECT {index} \"surf{index}\"");
        Line(); Line($"NUMMATERIALS {materials.Count}");
        for (int index = 0; index < materials.Count; index++)
        {
            (string materialName, string colorMap) = materials[index];
            Line($"MATERIAL {index} \"{materialName}\" \"Phong\" \"../images/{colorMap}.dds\"");
            Line("COLOR 0.000000 0.000000 0.000000 1.000000");
            Line("TRANSPARENCY 0.000000 0.000000 0.000000 1.000000");
            Line("AMBIENTCOLOR 0.000000 0.000000 0.000000 1.000000");
            Line("INCANDESCENCE 0.000000 0.000000 0.000000 1.000000");
            Line("COEFFS 0.800000 0.000000"); Line("GLOW 0.000000 0");
            Line("REFRACTIVE 6 1.000000");
            Line("SPECULARCOLOR -1.000000 -1.000000 -1.000000 1.000000");
            Line("REFLECTIVECOLOR -1.000000 -1.000000 -1.000000 1.000000");
            Line("REFLECTIVE -1 -1.000000"); Line("BLINN -1.000000 -1.000000");
            Line("PHONG -1.000000"); Line();
        }
        return text.ToString();
    }

    private static Vertex ReadVertex(ReadOnlySpan<byte> source, BoneWeight[] weights)
    {
        var position = new Vector3(Float(source, 0), Float(source, 4), Float(source, 8));
        uint color = U32(source, 16), packedUv = U32(source, 20);
        Vector3 normal = DecodeDirection(U32(source, 24));
        var rgba = new Vector4((color & 0xff) / 255f, ((color >> 8) & 0xff) / 255f,
            ((color >> 16) & 0xff) / 255f, (color >> 24) / 255f);
        var uv = new Vector2(DecodeHalf((ushort)(packedUv >> 16)),
            DecodeHalf((ushort)packedUv));
        if (!Finite(position) || !Finite(normal) || !Finite(uv))
            throw new InvalidDataException("IW3 PC XModel has a nonfinite packed vertex.");
        return new Vertex(position, normal, rgba, uv, weights);
    }

    private static Vector3 DecodeDirection(uint packed)
    {
        float scale = ((packed >> 24) + 192f) / 32385f;
        return new Vector3(
            ((packed & 0xff) - 127f) * scale,
            (((packed >> 8) & 0xff) - 127f) * scale,
            (((packed >> 16) & 0xff) - 127f) * scale);
    }

    private static float DecodeHalf(ushort value)
    {
        if (value == 0) return 0f;
        // IW3 Vec2UnpackTexCoords uses this packed UV conversion, including
        // its non-IEEE treatment of subnormal values.
        uint shifted = (uint)value << 14;
        uint magnitude = unchecked((shifted & 0x0FFFC000u) - (~shifted & 0x10000000u));
        uint bits = ((uint)value << 16 & 0x80000000u) |
            ((magnitude ^ 0x80000000u) >> 1);
        return BitConverter.Int32BitsToSingle(unchecked((int)bits));
    }

    private static List<BoneWeight[]> ReadWeights(Iw3PcZone zone, string name, int surface,
        int vertexCount, int triangleCount, int rigidCount, int boneCount)
    {
        int blend = zone.Pointer(surface + 24);
        bool deformed = zone.Bytes(surface + 1, 1)[0] != 0;
        if (deformed != (blend != 0))
            throw new NotSupportedException($"IW3 PC XModel '{name}' has inconsistent deformation data.");
        var result = new List<BoneWeight[]>(vertexCount);
        int rigid = rigidCount == 0 ? 0 : Required(zone, surface + 36, "rigid vertex lists");
        if (rigidCount != 0) zone.Bytes(rigid, checked(rigidCount * 12));
        int rigidTriangles = 0;
        for (int index = 0; index < rigidCount; index++)
        {
            int row = rigid + index * 12;
            int boneOffset = U16(zone.Bytes(row, 2), 0);
            int vertices = U16(zone.Bytes(row + 2, 2), 0);
            int firstTriangle = U16(zone.Bytes(row + 4, 2), 0);
            int triangles = U16(zone.Bytes(row + 6, 2), 0);
            if (boneOffset % 64 != 0 || boneOffset / 64 >= boneCount ||
                firstTriangle + triangles > triangleCount || result.Count + vertices > vertexCount)
                throw new InvalidDataException($"IW3 PC XModel '{name}' has an invalid rigid vertex list.");
            rigidTriangles += triangles;
            for (int vertex = 0; vertex < vertices; vertex++)
                result.Add([new BoneWeight(boneOffset / 64, 1f)]);
        }
        if (rigidTriangles > triangleCount)
            throw new InvalidDataException($"IW3 PC XModel '{name}' has too many rigid triangles.");

        int[] blendCounts = new int[4];
        for (int index = 0; index < blendCounts.Length; index++)
        {
            int count = BinaryPrimitives.ReadInt16LittleEndian(zone.Bytes(surface + 16 + index * 2, 2));
            if (count < 0) throw new InvalidDataException($"IW3 PC XModel '{name}' has a negative blend count.");
            blendCounts[index] = count;
        }
        int blendedVertices = blendCounts.Sum();
        if (result.Count + blendedVertices > vertexCount || (blend == 0 && blendedVertices != 0))
            throw new InvalidDataException($"IW3 PC XModel '{name}' has invalid vertex-weight counts.");
        int blendWords = checked(blendCounts[0] + blendCounts[1] * 3 +
            blendCounts[2] * 5 + blendCounts[3] * 7);
        if (blendWords != 0) zone.Bytes(blend, checked(blendWords * 2));
        int cursor = 0;
        for (int group = 0; group < blendCounts.Length; group++)
            for (int vertex = 0; vertex < blendCounts[group]; vertex++)
            {
                int influences = group + 1;
                var weights = new BoneWeight[influences];
                float remaining = 1f;
                for (int influence = 0; influence < influences; influence++)
                {
                    int boneOffset = U16(zone.Bytes(blend + cursor * 2, 2), 0);
                    cursor++;
                    if (boneOffset % 64 != 0 || boneOffset / 64 >= boneCount)
                        throw new InvalidDataException($"IW3 PC XModel '{name}' has an invalid blended bone.");
                    float weight = influence == 0 ? 0f :
                        U16(zone.Bytes(blend + cursor++ * 2, 2), 0) / 65535f;
                    weights[influence] = new BoneWeight(boneOffset / 64, weight);
                    if (influence > 0) remaining -= weight;
                }
                weights[0] = weights[0] with { Weight = remaining };
                result.Add(weights);
            }
        while (result.Count < vertexCount) result.Add([]);
        return result;
    }

    private static void WriteCorner(Action<string> line, int index, Vertex vertex)
    {
        line($"VERT {index}");
        line($"NORMAL {F(vertex.Normal.X)} {F(vertex.Normal.Y)} {F(vertex.Normal.Z)}");
        line($"COLOR {F(vertex.Color.X)} {F(vertex.Color.Y)} {F(vertex.Color.Z)} {F(vertex.Color.W)}");
        line($"UV 1 {F(vertex.Uv.X)} {F(vertex.Uv.Y)}");
    }

    private static void WriteRotation(Action<string> line, Vector4 q)
    {
        float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
        float xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z;
        float wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;
        line($"X {Vec(1f - 2f * (yy + zz), 2f * (xy + wz), 2f * (xz - wy))}");
        line($"Y {Vec(2f * (xy - wz), 1f - 2f * (xx + zz), 2f * (yz + wx))}");
        line($"Z {Vec(2f * (xz + wy), 2f * (yz - wx), 1f - 2f * (xx + yy))}");
    }

    private static string ReadColorMap(Iw3PcZone zone, int material)
    {
        int textures = zone.Pointer(material + 68);
        int count = zone.Bytes(material + 58, 1)[0];
        if (textures == 0) return string.Empty;
        var candidates = new List<int>();
        for (int index = 0; index < count; index++)
        {
            int texture = textures + index * 12;
            if (zone.Bytes(texture + 7, 1)[0] == 2) candidates.Add(texture);
        }
        if (candidates.Count == 0) return string.Empty;
        int selected = candidates[0];
        if (candidates.Count > 1)
        {
            foreach ((char start, char end) in new[] { ('c', 'p'), ('r', 'k'), ('d', 'p') })
            {
                int match = candidates.FindIndex(texture =>
                    char.ToLowerInvariant((char)zone.Bytes(texture + 4, 1)[0]) == start &&
                    char.ToLowerInvariant((char)zone.Bytes(texture + 5, 1)[0]) == end);
                if (match < 0) continue;
                selected = candidates[match];
                break;
            }
        }
        int image = zone.Pointer(selected + 8);
        return image == 0 ? string.Empty : StripReference(zone.String(image + 32));
    }

    private static bool IsViewhands(Iw3PcZone zone, int root, int bones, int roots)
    {
        int translations = zone.Pointer(root + 20);
        bool zeroTranslations = translations == 0;
        if (!zeroTranslations)
        {
            zeroTranslations = true;
            for (int index = 0; index < (bones - roots) * 3; index++)
                zeroTranslations &= Float(zone, translations + index * 4) == 0f;
        }
        if (!zeroTranslations) return false;
        int boneInfo = zone.Pointer(root + 164);
        if (boneInfo == 0) return false;
        for (int index = 0; index < bones; index++)
            for (int axis = 0; axis < 3; axis++)
                if (Float(zone, boneInfo + index * 40 + 24 + axis * 4) != 0f)
                    return true;
        return false;
    }

    private static string StripReference(string name) =>
        name.StartsWith(",", StringComparison.Ordinal) ? name[1..] : name;
    private static bool ValidQuoted(string value) =>
        !value.Contains('"') && !value.Contains('\\') &&
        !value.Any(char.IsControl);
    private static int Required(Iw3PcZone zone, int field, string label) =>
        zone.Pointer(field) is var offset && offset != 0 ? offset :
            throw new InvalidDataException($"IW3 PC XModel has no {label}.");
    private static ushort U16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
    private static uint U32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);
    private static int I32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
    private static float Float(Iw3PcZone zone, int offset) => Float(zone.Bytes(offset, 4), 0);
    private static float Float(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
    private static string F(float value) => value.ToString("F6", CultureInfo.InvariantCulture);
    private static string Vec(Vector3 value) => Vec(value.X, value.Y, value.Z);
    private static string Vec(float x, float y, float z) => $"{F(x)}, {F(y)}, {F(z)}";
    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);
    private sealed record Bone(string Name, int Parent, Vector4 Quaternion, Vector3 Offset);
    private sealed record BoneWeight(int Bone, float Weight);
    private sealed record Vertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 Uv,
        BoneWeight[] Weights);
}
