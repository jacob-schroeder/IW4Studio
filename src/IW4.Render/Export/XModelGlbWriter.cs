using System.Numerics;
using System.Text.Json;
using IW4.Assets.Export.XModel;

namespace IW4.Render.Export;

public sealed class XModelGlbMaterialTexture
{
    private readonly byte[] _pngBytes;

    public XModelGlbMaterialTexture(byte[] pngBytes, bool hasTransparency)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
            throw new ArgumentException("A GLB material texture cannot be empty.", nameof(pngBytes));

        _pngBytes = pngBytes.ToArray();
        HasTransparency = hasTransparency;
    }

    public bool HasTransparency { get; }

    internal byte[] GetPngBytesCopy() => _pngBytes.ToArray();
}

/// <summary>
/// Writes one portable binary glTF 2.0 representation of an XMODEL_EXPORT
/// document. IW4 material identities are retained in glTF extras while the
/// optional decoded color image is represented as a conventional PBR base
/// color texture; authored IW4 technique execution is not serialized.
/// </summary>
public static class XModelGlbWriter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;
    private const int FloatComponentType = 5126;
    private const int UnsignedShortComponentType = 5123;
    private const int ArrayBufferTarget = 34962;
    private static readonly Matrix4x4 GameToGltf = new(
        1f, 0f, 0f, 0f,
        0f, 0f, -1f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    public static void Write(
        Stream output,
        XModelExportDocument document,
        IReadOnlyList<XModelGlbMaterialTexture?> materialTextures)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(materialTextures);
        if (!output.CanWrite)
            throw new ArgumentException("The GLB destination is not writable.", nameof(output));
        if (materialTextures.Count != document.Materials.Count)
        {
            throw new ArgumentException(
                "GLB material texture rows must match the XModel material table exactly.",
                nameof(materialTextures));
        }

        Validate(document);
        var binary = new BinaryPayload();
        var bufferViews = new List<BufferView>();
        var accessors = new List<Accessor>();
        var imageBufferViews = new int?[document.Materials.Count];
        var materialTextureIndices = new int?[document.Materials.Count];
        var textures = new List<int>();

        for (int materialIndex = 0; materialIndex < materialTextures.Count; materialIndex++)
        {
            XModelGlbMaterialTexture? texture = materialTextures[materialIndex];
            if (texture is null)
                continue;

            byte[] png = texture.GetPngBytesCopy();
            int offset = binary.AddBytes(png);
            int bufferViewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView(offset, png.Length, null));
            imageBufferViews[materialIndex] = bufferViewIndex;
            materialTextureIndices[materialIndex] = textures.Count;
            textures.Add(materialIndex);
        }

        var meshes = new List<Mesh>();
        for (int objectIndex = 0; objectIndex < document.Objects.Count; objectIndex++)
        {
            XModelExportTriangle[] objectTriangles = document.Triangles
                .Where(triangle => triangle.ObjectIndex == objectIndex)
                .ToArray();
            var primitives = new List<Primitive>();
            foreach (IGrouping<int, XModelExportTriangle> group in objectTriangles
                         .GroupBy(triangle => triangle.MaterialIndex)
                         .OrderBy(group => group.Key))
            {
                primitives.Add(BuildPrimitive(
                    document,
                    group.Key,
                    group,
                    binary,
                    bufferViews,
                    accessors));
            }

            if (primitives.Count == 0)
                throw new InvalidDataException($"XModel object {objectIndex} has no triangles.");
            meshes.Add(new Mesh(document.Objects[objectIndex].SurfaceIdentity, primitives));
        }

        Matrix4x4[] gameGlobals = document.Bones
            .Select(bone => Matrix4x4.CreateFromQuaternion(bone.GlobalRotation) *
                            Matrix4x4.CreateTranslation(bone.GlobalOffset))
            .ToArray();
        if (!Matrix4x4.Invert(GameToGltf, out Matrix4x4 gltfToGame))
            throw new InvalidOperationException("The recovered IW4-to-glTF basis is not invertible.");
        Matrix4x4[] gltfGlobals = gameGlobals
            .Select(global => gltfToGame * global * GameToGltf)
            .ToArray();
        Matrix4x4[] localBones = new Matrix4x4[document.Bones.Count];
        Matrix4x4[] inverseBindMatrices = new Matrix4x4[document.Bones.Count];
        for (int boneIndex = 0; boneIndex < document.Bones.Count; boneIndex++)
        {
            int parent = document.Bones[boneIndex].ParentIndex;
            if (parent < 0)
                localBones[boneIndex] = gltfGlobals[boneIndex];
            else
            {
                if (!Matrix4x4.Invert(gltfGlobals[parent], out Matrix4x4 inverseParent))
                    throw new InvalidDataException($"XModel bone {parent} has a singular global bind transform.");
                localBones[boneIndex] = gltfGlobals[boneIndex] * inverseParent;
            }
            if (!Matrix4x4.Invert(gltfGlobals[boneIndex], out inverseBindMatrices[boneIndex]))
                throw new InvalidDataException($"XModel bone {boneIndex} has a singular global bind transform.");
        }

        int inverseBindBufferView = AddFloatBufferView(
            inverseBindMatrices.SelectMany(MatrixValues),
            binary,
            bufferViews,
            target: null);
        int inverseBindAccessor = accessors.Count;
        accessors.Add(new Accessor(
            inverseBindBufferView,
            FloatComponentType,
            document.Bones.Count,
            "MAT4",
            null,
            null));

        byte[] json = BuildJson(
            document,
            binary.Length,
            bufferViews,
            accessors,
            meshes,
            localBones,
            inverseBindAccessor,
            imageBufferViews,
            materialTextureIndices,
            materialTextures);
        byte[] binaryBytes = binary.ToArray();
        int paddedJsonLength = Align4(json.Length);
        int paddedBinaryLength = Align4(binaryBytes.Length);
        int totalLength = checked(12 + 8 + paddedJsonLength + 8 + paddedBinaryLength);

        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(GlbMagic);
        writer.Write(2u);
        writer.Write((uint)totalLength);
        writer.Write((uint)paddedJsonLength);
        writer.Write(JsonChunkType);
        writer.Write(json);
        for (int index = json.Length; index < paddedJsonLength; index++)
            writer.Write((byte)0x20);
        writer.Write((uint)paddedBinaryLength);
        writer.Write(BinaryChunkType);
        writer.Write(binaryBytes);
        for (int index = binaryBytes.Length; index < paddedBinaryLength; index++)
            writer.Write((byte)0);
    }

    private static Primitive BuildPrimitive(
        XModelExportDocument document,
        int materialIndex,
        IEnumerable<XModelExportTriangle> triangles,
        BinaryPayload binary,
        List<BufferView> bufferViews,
        List<Accessor> accessors)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var texCoords = new List<float>();
        var colors = new List<float>();
        var joints = new List<ushort>();
        var weights = new List<float>();
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);

        foreach (XModelExportTriangle triangle in triangles)
        {
            foreach (XModelExportCorner corner in new[] { triangle.First, triangle.Second, triangle.Third })
            {
                XModelExportVertex vertex = document.Vertices[corner.VertexIndex];
                Vector3 position = ConvertVector(vertex.Position);
                Vector3 normal = Vector3.Normalize(ConvertVector(corner.Normal));
                positions.AddRange([position.X, position.Y, position.Z]);
                normals.AddRange([normal.X, normal.Y, normal.Z]);
                texCoords.AddRange([corner.Uv0.X, corner.Uv0.Y]);
                colors.AddRange([corner.Color.X, corner.Color.Y, corner.Color.Z, corner.Color.W]);
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);

                for (int influence = 0; influence < 4; influence++)
                {
                    if (influence < vertex.Weights.Count)
                    {
                        joints.Add(checked((ushort)vertex.Weights[influence].BoneIndex));
                        weights.Add(vertex.Weights[influence].Weight);
                    }
                    else
                    {
                        joints.Add(0);
                        weights.Add(0f);
                    }
                }
            }
        }

        int vertexCount = positions.Count / 3;
        var attributes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["POSITION"] = AddFloatAccessor(positions, vertexCount, "VEC3", minimum, maximum, binary, bufferViews, accessors),
            ["NORMAL"] = AddFloatAccessor(normals, vertexCount, "VEC3", null, null, binary, bufferViews, accessors),
            ["TEXCOORD_0"] = AddFloatAccessor(texCoords, vertexCount, "VEC2", null, null, binary, bufferViews, accessors),
            ["COLOR_0"] = AddFloatAccessor(colors, vertexCount, "VEC4", null, null, binary, bufferViews, accessors),
            ["JOINTS_0"] = AddUShortAccessor(joints, vertexCount, "VEC4", binary, bufferViews, accessors),
            ["WEIGHTS_0"] = AddFloatAccessor(weights, vertexCount, "VEC4", null, null, binary, bufferViews, accessors)
        };
        return new Primitive(materialIndex, attributes);
    }

    private static int AddFloatAccessor(
        IEnumerable<float> values,
        int count,
        string type,
        Vector3? minimum,
        Vector3? maximum,
        BinaryPayload binary,
        List<BufferView> bufferViews,
        List<Accessor> accessors)
    {
        int bufferView = AddFloatBufferView(values, binary, bufferViews);
        int accessor = accessors.Count;
        accessors.Add(new Accessor(
            bufferView,
            FloatComponentType,
            count,
            type,
            minimum is Vector3 min ? [min.X, min.Y, min.Z] : null,
            maximum is Vector3 max ? [max.X, max.Y, max.Z] : null));
        return accessor;
    }

    private static int AddUShortAccessor(
        IEnumerable<ushort> values,
        int count,
        string type,
        BinaryPayload binary,
        List<BufferView> bufferViews,
        List<Accessor> accessors)
    {
        int offset = binary.AddUShorts(values);
        int length = checked(count * 4 * sizeof(ushort));
        int bufferView = bufferViews.Count;
        bufferViews.Add(new BufferView(offset, length, ArrayBufferTarget));
        int accessor = accessors.Count;
        accessors.Add(new Accessor(bufferView, UnsignedShortComponentType, count, type, null, null));
        return accessor;
    }

    private static int AddFloatBufferView(
        IEnumerable<float> values,
        BinaryPayload binary,
        List<BufferView> bufferViews,
        int? target = ArrayBufferTarget)
    {
        float[] rows = values.ToArray();
        int offset = binary.AddFloats(rows);
        int bufferView = bufferViews.Count;
        bufferViews.Add(new BufferView(offset, checked(rows.Length * sizeof(float)), target));
        return bufferView;
    }

    private static byte[] BuildJson(
        XModelExportDocument document,
        int binaryLength,
        IReadOnlyList<BufferView> bufferViews,
        IReadOnlyList<Accessor> accessors,
        IReadOnlyList<Mesh> meshes,
        IReadOnlyList<Matrix4x4> localBones,
        int inverseBindAccessor,
        IReadOnlyList<int?> imageBufferViews,
        IReadOnlyList<int?> materialTextureIndices,
        IReadOnlyList<XModelGlbMaterialTexture?> materialTextures)
    {
        using var jsonStream = new MemoryStream();
        using (var json = new Utf8JsonWriter(jsonStream))
        {
            json.WriteStartObject();
            json.WriteNumber("scene", 0);
            json.WritePropertyName("asset");
            json.WriteStartObject();
            json.WriteString("version", "2.0");
            json.WriteString("generator", "IW4 Studio XModel GLB Export");
            json.WriteEndObject();

            json.WritePropertyName("scenes");
            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteString("name", document.Objects.Count == 1 ? document.Objects[0].SurfaceIdentity : "IW4 XModel");
            json.WritePropertyName("nodes");
            json.WriteStartArray();
            for (int boneIndex = 0; boneIndex < document.Bones.Count; boneIndex++)
            {
                if (document.Bones[boneIndex].ParentIndex < 0)
                    json.WriteNumberValue(boneIndex);
            }
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                json.WriteNumberValue(document.Bones.Count + meshIndex);
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();

            json.WritePropertyName("nodes");
            json.WriteStartArray();
            for (int boneIndex = 0; boneIndex < document.Bones.Count; boneIndex++)
            {
                json.WriteStartObject();
                json.WriteString("name", document.Bones[boneIndex].Name);
                WriteMatrix(json, localBones[boneIndex]);
                int[] children = document.Bones
                    .Select((bone, index) => (bone, index))
                    .Where(value => value.bone.ParentIndex == boneIndex)
                    .Select(value => value.index)
                    .ToArray();
                if (children.Length != 0)
                {
                    json.WritePropertyName("children");
                    json.WriteStartArray();
                    foreach (int child in children)
                        json.WriteNumberValue(child);
                    json.WriteEndArray();
                }
                json.WriteEndObject();
            }
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                json.WriteStartObject();
                json.WriteString("name", meshes[meshIndex].Name);
                json.WriteNumber("mesh", meshIndex);
                json.WriteNumber("skin", 0);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WritePropertyName("meshes");
            json.WriteStartArray();
            foreach (Mesh mesh in meshes)
            {
                json.WriteStartObject();
                json.WriteString("name", mesh.Name);
                json.WritePropertyName("primitives");
                json.WriteStartArray();
                foreach (Primitive primitive in mesh.Primitives)
                {
                    json.WriteStartObject();
                    json.WritePropertyName("attributes");
                    json.WriteStartObject();
                    foreach ((string semantic, int accessor) in primitive.Attributes)
                        json.WriteNumber(semantic, accessor);
                    json.WriteEndObject();
                    json.WriteNumber("material", primitive.MaterialIndex);
                    json.WriteNumber("mode", 4);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WritePropertyName("skins");
            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteString("name", "IW4 XModel Skin");
            json.WriteNumber("inverseBindMatrices", inverseBindAccessor);
            int[] rootBones = document.Bones
                .Select((bone, index) => (bone, index))
                .Where(value => value.bone.ParentIndex < 0)
                .Select(value => value.index)
                .ToArray();
            if (rootBones.Length == 1)
                json.WriteNumber("skeleton", rootBones[0]);
            json.WritePropertyName("joints");
            json.WriteStartArray();
            for (int boneIndex = 0; boneIndex < document.Bones.Count; boneIndex++)
                json.WriteNumberValue(boneIndex);
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();

            json.WritePropertyName("materials");
            json.WriteStartArray();
            for (int materialIndex = 0; materialIndex < document.Materials.Count; materialIndex++)
            {
                XModelExportMaterial material = document.Materials[materialIndex];
                json.WriteStartObject();
                json.WriteString("name", material.Name);
                json.WriteBoolean("doubleSided", true);
                if (materialTextures[materialIndex]?.HasTransparency == true)
                    json.WriteString("alphaMode", "BLEND");
                json.WritePropertyName("pbrMetallicRoughness");
                json.WriteStartObject();
                json.WriteNumber("metallicFactor", 0d);
                json.WriteNumber("roughnessFactor", 0.8d);
                if (materialTextureIndices[materialIndex] is int textureIndex)
                {
                    json.WritePropertyName("baseColorTexture");
                    json.WriteStartObject();
                    json.WriteNumber("index", textureIndex);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
                json.WritePropertyName("extras");
                json.WriteStartObject();
                json.WriteString("iw4Material", material.Name);
                json.WriteString("iw4ColorMapPath", material.ColorMapPath);
                json.WriteString("iw4MaterialApproximation", "base-color-only");
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            if (materialTextureIndices.Any(value => value.HasValue))
            {
                json.WritePropertyName("samplers");
                json.WriteStartArray();
                json.WriteStartObject();
                json.WriteNumber("magFilter", 9729);
                json.WriteNumber("minFilter", 9987);
                json.WriteNumber("wrapS", 10497);
                json.WriteNumber("wrapT", 10497);
                json.WriteEndObject();
                json.WriteEndArray();

                json.WritePropertyName("images");
                json.WriteStartArray();
                for (int materialIndex = 0; materialIndex < imageBufferViews.Count; materialIndex++)
                {
                    if (imageBufferViews[materialIndex] is not int imageBufferView)
                        continue;
                    json.WriteStartObject();
                    json.WriteString("name", document.Materials[materialIndex].Name + " base color");
                    json.WriteNumber("bufferView", imageBufferView);
                    json.WriteString("mimeType", "image/png");
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WritePropertyName("textures");
                json.WriteStartArray();
                foreach (int materialIndex in materialTextureIndices
                             .Select((value, index) => (value, index))
                             .Where(value => value.value.HasValue)
                             .Select(value => value.index))
                {
                    int imageIndex = materialTextureIndices.Take(materialIndex + 1).Count(value => value.HasValue) - 1;
                    json.WriteStartObject();
                    json.WriteNumber("sampler", 0);
                    json.WriteNumber("source", imageIndex);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }

            json.WritePropertyName("accessors");
            json.WriteStartArray();
            foreach (Accessor accessor in accessors)
            {
                json.WriteStartObject();
                json.WriteNumber("bufferView", accessor.BufferView);
                json.WriteNumber("componentType", accessor.ComponentType);
                json.WriteNumber("count", accessor.Count);
                json.WriteString("type", accessor.Type);
                if (accessor.Minimum is not null)
                    WriteFloatArray(json, "min", accessor.Minimum);
                if (accessor.Maximum is not null)
                    WriteFloatArray(json, "max", accessor.Maximum);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WritePropertyName("bufferViews");
            json.WriteStartArray();
            foreach (BufferView bufferView in bufferViews)
            {
                json.WriteStartObject();
                json.WriteNumber("buffer", 0);
                json.WriteNumber("byteOffset", bufferView.Offset);
                json.WriteNumber("byteLength", bufferView.Length);
                if (bufferView.Target is int target)
                    json.WriteNumber("target", target);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WritePropertyName("buffers");
            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteNumber("byteLength", binaryLength);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return jsonStream.ToArray();
    }

    private static void Validate(XModelExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Bones);
        ArgumentNullException.ThrowIfNull(document.Vertices);
        ArgumentNullException.ThrowIfNull(document.Triangles);
        ArgumentNullException.ThrowIfNull(document.Objects);
        ArgumentNullException.ThrowIfNull(document.Materials);
        if (document.Bones.Count == 0 || document.Bones.Count > ushort.MaxValue)
            throw new InvalidDataException("GLB export requires between one and 65,535 XModel bones.");
        if (document.Vertices.Count == 0 || document.Triangles.Count == 0 ||
            document.Objects.Count == 0 || document.Materials.Count == 0)
            throw new InvalidDataException("GLB export requires bones, vertices, triangles, objects, and materials.");

        for (int boneIndex = 0; boneIndex < document.Bones.Count; boneIndex++)
        {
            XModelExportBone bone = document.Bones[boneIndex];
            if (string.IsNullOrWhiteSpace(bone.Name) || bone.ParentIndex >= boneIndex || bone.ParentIndex < -1 ||
                !IsFinite(bone.GlobalOffset) || !IsFinite(bone.GlobalRotation) ||
                MathF.Abs(bone.GlobalRotation.LengthSquared() - 1f) > 0.001f)
                throw new InvalidDataException($"XModel bone {boneIndex} is not a finite ordered unit bind transform.");
        }
        for (int vertexIndex = 0; vertexIndex < document.Vertices.Count; vertexIndex++)
        {
            XModelExportVertex vertex = document.Vertices[vertexIndex];
            if (!IsFinite(vertex.Position) || vertex.Weights.Count is < 1 or > 4 ||
                vertex.Weights.Any(weight => weight.BoneIndex < 0 || weight.BoneIndex >= document.Bones.Count ||
                                             !float.IsFinite(weight.Weight) || weight.Weight <= 0f) ||
                vertex.Weights.Select(weight => weight.BoneIndex).Distinct().Count() != vertex.Weights.Count ||
                MathF.Abs(vertex.Weights.Sum(weight => weight.Weight) - 1f) > 0.00001f)
                throw new InvalidDataException($"XModel vertex {vertexIndex} has invalid position or skin weights.");
        }
        foreach (XModelExportTriangle triangle in document.Triangles)
        {
            if (triangle.ObjectIndex < 0 || triangle.ObjectIndex >= document.Objects.Count ||
                triangle.MaterialIndex < 0 || triangle.MaterialIndex >= document.Materials.Count)
                throw new InvalidDataException("An XModel triangle references an invalid object or material.");
            ValidateCorner(triangle.First, document.Vertices.Count);
            ValidateCorner(triangle.Second, document.Vertices.Count);
            ValidateCorner(triangle.Third, document.Vertices.Count);
        }
    }

    private static void ValidateCorner(XModelExportCorner corner, int vertexCount)
    {
        if (corner.VertexIndex < 0 || corner.VertexIndex >= vertexCount ||
            !IsFinite(corner.Normal) || corner.Normal.LengthSquared() < 0.000001f ||
            !IsFinite(corner.Color) || !IsFinite(corner.Uv0))
            throw new InvalidDataException("An XModel triangle corner has invalid vertex, normal, color, or UV0 data.");
    }

    private static Vector3 ConvertVector(Vector3 value) => new(value.X, value.Z, -value.Y);

    private static IEnumerable<float> MatrixValues(Matrix4x4 value)
    {
        yield return value.M11; yield return value.M12; yield return value.M13; yield return value.M14;
        yield return value.M21; yield return value.M22; yield return value.M23; yield return value.M24;
        yield return value.M31; yield return value.M32; yield return value.M33; yield return value.M34;
        yield return value.M41; yield return value.M42; yield return value.M43; yield return value.M44;
    }

    private static void WriteMatrix(Utf8JsonWriter json, Matrix4x4 value)
    {
        json.WritePropertyName("matrix");
        json.WriteStartArray();
        foreach (float component in MatrixValues(value))
            json.WriteNumberValue(component);
        json.WriteEndArray();
    }

    private static void WriteFloatArray(Utf8JsonWriter json, string name, IReadOnlyList<float> values)
    {
        json.WritePropertyName(name);
        json.WriteStartArray();
        foreach (float value in values)
            json.WriteNumberValue(value);
        json.WriteEndArray();
    }

    private static int Align4(int value) => checked((value + 3) & ~3);

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool IsFinite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static bool IsFinite(Quaternion value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private sealed record BufferView(int Offset, int Length, int? Target);
    private sealed record Accessor(int BufferView, int ComponentType, int Count, string Type, float[]? Minimum, float[]? Maximum);
    private sealed record Primitive(int MaterialIndex, IReadOnlyDictionary<string, int> Attributes);
    private sealed record Mesh(string Name, IReadOnlyList<Primitive> Primitives);

    private sealed class BinaryPayload
    {
        private readonly MemoryStream _stream = new();

        internal int Length => checked((int)_stream.Length);

        internal int AddBytes(byte[] values)
        {
            Align();
            int offset = Length;
            _stream.Write(values);
            return offset;
        }

        internal int AddFloats(IEnumerable<float> values)
        {
            Align();
            int offset = Length;
            using var writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
            foreach (float value in values)
                writer.Write(value);
            return offset;
        }

        internal int AddUShorts(IEnumerable<ushort> values)
        {
            Align();
            int offset = Length;
            using var writer = new BinaryWriter(_stream, System.Text.Encoding.UTF8, leaveOpen: true);
            foreach (ushort value in values)
                writer.Write(value);
            return offset;
        }

        internal byte[] ToArray() => _stream.ToArray();

        private void Align()
        {
            while ((_stream.Length & 3) != 0)
                _stream.WriteByte(0);
        }
    }
}
