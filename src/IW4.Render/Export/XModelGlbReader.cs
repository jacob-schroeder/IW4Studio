using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using IW4.Assets.XModel.Export;

namespace IW4.Render.Export;

/// <summary>
/// Reads one binary glTF 2.0 model into the engine-neutral XMODEL_EXPORT
/// document consumed by XModel LOD authoring. The supported contract is the
/// skinned triangle subset emitted by IW4 Studio and Blender's glTF exporter.
/// Unsupported glTF features fail closed instead of being baked implicitly.
/// </summary>
public static class XModelGlbReader
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;
    private const int MaximumGlbLength = 1024 * 1024 * 1024;
    private const string MaterialsSpecularExtension = "KHR_materials_specular";
    private static readonly Matrix4x4 GameToGltf = new(
        1f, 0f, 0f, 0f,
        0f, 0f, -1f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    public static bool TryRead(
        Stream input,
        Func<string, ReadOnlyMemory<byte>, XModelImportImage>? imageDecoder,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers) =>
        TryRead(input, rigidModelReplacement: false, imageDecoder, out document, out blockers);

    /// <summary>
    /// Reads ordinary static Blender geometry for complete-model replacement.
    /// Unskinned mesh nodes are transformed into model space and rigid-bound to
    /// one synthetic root; skinned IW4 handoff files retain the strict path.
    /// </summary>
    public static bool TryReadRigidModel(
        Stream input,
        Func<string, ReadOnlyMemory<byte>, XModelImportImage>? imageDecoder,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers) =>
        TryRead(input, rigidModelReplacement: true, imageDecoder, out document, out blockers);

    private static bool TryRead(
        Stream input,
        bool rigidModelReplacement,
        Func<string, ReadOnlyMemory<byte>, XModelImportImage>? imageDecoder,
        out XModelExportDocument? document,
        out IReadOnlyList<string> blockers)
    {
        ArgumentNullException.ThrowIfNull(input);
        document = null;
        try
        {
            if (!input.CanRead)
                throw new InvalidDataException("The GLB source is not readable.");

            byte[] bytes = ReadSource(input);
            (JsonDocument json, ReadOnlyMemory<byte> binary) = ReadContainer(bytes);
            using (json)
                document = new Reader(json.RootElement, binary, imageDecoder).Read(rigidModelReplacement);
            blockers = [];
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException or IOException or OverflowException)
        {
            blockers = [exception.Message];
            return false;
        }
    }

    private static byte[] ReadSource(Stream input)
    {
        if (input.CanSeek && (input.Length < 20 || input.Length > MaximumGlbLength))
            throw new InvalidDataException($"GLB length must be between 20 bytes and {MaximumGlbLength:N0} bytes.");

        using var copy = new MemoryStream();
        input.CopyTo(copy);
        if (copy.Length < 20 || copy.Length > MaximumGlbLength)
            throw new InvalidDataException($"GLB length must be between 20 bytes and {MaximumGlbLength:N0} bytes.");
        return copy.ToArray();
    }

    private static (JsonDocument Json, ReadOnlyMemory<byte> Binary) ReadContainer(byte[] bytes)
    {
        ReadOnlySpan<byte> source = bytes;
        if (BinaryPrimitives.ReadUInt32LittleEndian(source) != GlbMagic)
            throw new InvalidDataException("The selected file is not a binary glTF (GLB) file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != 2)
            throw new InvalidDataException("Only binary glTF version 2 is supported.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != source.Length)
            throw new InvalidDataException("The GLB header length does not match the file length.");

        int offset = 12;
        ReadOnlyMemory<byte>? jsonChunk = null;
        ReadOnlyMemory<byte>? binaryChunk = null;
        while (offset < source.Length)
        {
            if (source.Length - offset < 8)
                throw new InvalidDataException("The GLB has a truncated chunk header.");
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source[(offset + 0)..]));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(source[(offset + 4)..]);
            offset = checked(offset + 8);
            if (length < 0 || length > source.Length - offset)
                throw new InvalidDataException("The GLB has a truncated chunk payload.");

            ReadOnlyMemory<byte> chunk = bytes.AsMemory(offset, length);
            if (type == JsonChunkType)
            {
                if (jsonChunk is not null || binaryChunk is not null)
                    throw new InvalidDataException("The GLB JSON chunk must be first and unique.");
                jsonChunk = chunk;
            }
            else if (type == BinaryChunkType)
            {
                if (jsonChunk is null || binaryChunk is not null)
                    throw new InvalidDataException("The GLB binary chunk must follow the JSON chunk and be unique.");
                binaryChunk = chunk;
            }
            else
                throw new InvalidDataException($"The GLB contains unsupported chunk type 0x{type:X8}.");
            offset = checked(offset + length);
        }

        if (jsonChunk is null || binaryChunk is null)
            throw new InvalidDataException("The GLB must contain one JSON chunk and one binary chunk.");
        return (JsonDocument.Parse(jsonChunk.Value), binaryChunk.Value);
    }

    private sealed class Reader
    {
        private readonly JsonElement _root;
        private readonly AccessorReader _accessors;
        private readonly JsonElement[] _nodes;
        private readonly int[] _parents;
        private readonly HashSet<int> _reachableNodes;
        private readonly Func<string, ReadOnlyMemory<byte>, XModelImportImage>? _imageDecoder;
        private readonly bool _isBlenderExport;

        internal Reader(
            JsonElement root,
            ReadOnlyMemory<byte> binary,
            Func<string, ReadOnlyMemory<byte>, XModelImportImage>? imageDecoder)
        {
            _root = root;
            RequireObject(root, "GLB root");
            RejectExtensionsAndAnimation(root);
            ValidateAsset(root);
            _accessors = new AccessorReader(root, binary);
            _nodes = Elements(root, "nodes", required: true);
            if (_nodes.Length == 0)
                throw new InvalidDataException("The GLB contains no nodes.");
            _parents = BuildParents(_nodes);
            _reachableNodes = ResolveSceneNodes(root, _nodes);
            _imageDecoder = imageDecoder;
            JsonElement asset = root.GetProperty("asset");
            _isBlenderExport = asset.TryGetProperty("generator", out JsonElement generator) &&
                generator.ValueKind == JsonValueKind.String &&
                (generator.GetString()?.Contains("Blender", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        internal XModelExportDocument Read(bool rigidModelReplacement)
        {
            if (rigidModelReplacement && Elements(_root, "skins", required: false).Length == 0)
                return ReadUnskinnedRigidModel();

            (int skinIndex, int[] jointNodes) = ResolveSkin();
            BoneProjection bones = ProjectBones(skinIndex, jointNodes);
            (IReadOnlyList<XModelExportVertex> vertices,
                IReadOnlyList<XModelExportTriangle> triangles,
                IReadOnlyList<XModelExportObject> objects,
                IReadOnlyList<XModelExportMaterial> materials) = ProjectMeshes(skinIndex, bones);

            return new XModelExportDocument(
                System.Array.AsReadOnly(bones.Bones),
                vertices,
                triangles,
                objects,
                materials);
        }

        private XModelExportDocument ReadUnskinnedRigidModel()
        {
            var bones = new BoneProjection(
                [new XModelExportBone("tag_origin", -1, Vector3.Zero, Quaternion.Identity)],
                [0]);
            (IReadOnlyList<XModelExportVertex> vertices,
                IReadOnlyList<XModelExportTriangle> triangles,
                IReadOnlyList<XModelExportObject> objects,
                IReadOnlyList<XModelExportMaterial> materials) = ProjectMeshes(
                    skinIndex: null,
                    bones,
                    bakeNodeTransforms: true);

            return new XModelExportDocument(
                System.Array.AsReadOnly(bones.Bones),
                vertices,
                triangles,
                objects,
                materials);
        }

        private (int SkinIndex, int[] JointNodes) ResolveSkin()
        {
            JsonElement[] skins = Elements(_root, "skins", required: true);
            if (skins.Length == 0)
                throw new InvalidDataException("GLB XModel import requires one skin.");

            int? selectedSkin = null;
            bool foundMesh = false;
            foreach (int nodeIndex in _reachableNodes.Order())
            {
                JsonElement node = _nodes[nodeIndex];
                if (!node.TryGetProperty("mesh", out _))
                    continue;
                foundMesh = true;
                int skin = Integer(node, "skin", required: true);
                if (skin < 0 || skin >= skins.Length)
                    throw new InvalidDataException($"Mesh node {nodeIndex} references invalid skin {skin}.");
                if (selectedSkin is int prior && prior != skin)
                    throw new InvalidDataException("All imported XModel mesh nodes must use one skin.");
                selectedSkin = skin;
            }
            if (!foundMesh || selectedSkin is null)
                throw new InvalidDataException("The selected GLB scene contains no skinned mesh nodes.");

            JsonElement skinRow = skins[selectedSkin.Value];
            int[] joints = IntegerArray(skinRow, "joints", required: true);
            if (joints.Length == 0 || joints.Length > ushort.MaxValue)
                throw new InvalidDataException("A GLB XModel skin must contain between one and 65,535 joints.");
            if (joints.Distinct().Count() != joints.Length || joints.Any(index => index < 0 || index >= _nodes.Length))
                throw new InvalidDataException("The GLB skin contains a duplicate or invalid joint node.");
            if (joints.Any(index => !_reachableNodes.Contains(index)))
                throw new InvalidDataException("Every GLB skin joint must be reachable from the selected scene.");
            return (selectedSkin.Value, joints);
        }

        private BoneProjection ProjectBones(int skinIndex, int[] jointNodes)
        {
            JsonElement skin = Elements(_root, "skins", required: true)[skinIndex];
            int inverseBindAccessor = Integer(skin, "inverseBindMatrices", required: true);
            AccessorView inverseBinds = _accessors.Get(inverseBindAccessor, "MAT4");
            if (inverseBinds.ComponentType != 5126 || inverseBinds.Count != jointNodes.Length)
                throw new InvalidDataException("The GLB inverse bind matrix accessor must contain one float MAT4 per joint.");

            var jointOrdinalByNode = jointNodes
                .Select((node, ordinal) => (node, ordinal))
                .ToDictionary(value => value.node, value => value.ordinal);
            var parentJointByNode = new Dictionary<int, int>();
            foreach (int jointNode in jointNodes)
            {
                int ancestor = _parents[jointNode];
                while (ancestor >= 0 && !jointOrdinalByNode.ContainsKey(ancestor))
                    ancestor = _parents[ancestor];
                parentJointByNode[jointNode] = ancestor;
            }

            var orderedJointNodes = new List<int>(jointNodes.Length);
            var visited = new HashSet<int>();
            void Visit(int node)
            {
                if (!visited.Add(node))
                    throw new InvalidDataException("The GLB skin joint hierarchy contains a cycle.");
                orderedJointNodes.Add(node);
                foreach (int child in jointNodes.Where(candidate => parentJointByNode[candidate] == node))
                    Visit(child);
            }
            foreach (int rootJoint in jointNodes.Where(node => parentJointByNode[node] < 0))
                Visit(rootJoint);
            if (orderedJointNodes.Count != jointNodes.Length)
                throw new InvalidDataException("The GLB skin joint hierarchy is incomplete.");

            var boneIndexByNode = orderedJointNodes
                .Select((node, index) => (node, index))
                .ToDictionary(value => value.node, value => value.index);
            var boneIndexByJointOrdinal = new int[jointNodes.Length];
            var globalsByNode = new Dictionary<int, Matrix4x4>();
            for (int ordinal = 0; ordinal < jointNodes.Length; ordinal++)
            {
                Matrix4x4 inverseBind = inverseBinds.ReadMatrix(ordinal);
                if (!IsFinite(inverseBind) || !Matrix4x4.Invert(inverseBind, out Matrix4x4 global))
                    throw new InvalidDataException($"GLB joint {ordinal} has a non-finite or singular inverse bind matrix.");
                globalsByNode[jointNodes[ordinal]] = global;
                boneIndexByJointOrdinal[ordinal] = boneIndexByNode[jointNodes[ordinal]];
            }

            if (!Matrix4x4.Invert(GameToGltf, out Matrix4x4 gltfToGame))
                throw new InvalidOperationException("The recovered IW4-to-glTF basis is not invertible.");
            var bones = new XModelExportBone[orderedJointNodes.Count];
            for (int boneIndex = 0; boneIndex < orderedJointNodes.Count; boneIndex++)
            {
                int node = orderedJointNodes[boneIndex];
                string name = String(_nodes[node], "name", required: true);
                if (!IsValidName(name))
                    throw new InvalidDataException($"GLB joint node {node} has an invalid bone name.");
                Matrix4x4 gameGlobal = GameToGltf * globalsByNode[node] * gltfToGame;
                if (!Matrix4x4.Decompose(gameGlobal, out Vector3 scale, out Quaternion rotation, out Vector3 offset) ||
                    !IsFinite(scale) || Vector3.DistanceSquared(scale, Vector3.One) > 0.000001f ||
                    !IsFinite(rotation) || MathF.Abs(rotation.LengthSquared() - 1f) > 0.001f || !IsFinite(offset))
                    throw new InvalidDataException($"GLB bone '{name}' has shear, scale, or a non-finite bind transform that IW4 cannot represent.");
                rotation = Quaternion.Normalize(rotation);
                int parentNode = parentJointByNode[node];
                bones[boneIndex] = new XModelExportBone(
                    name,
                    parentNode < 0 ? -1 : boneIndexByNode[parentNode],
                    offset,
                    rotation);
            }
            if (bones.Select(bone => bone.Name).Distinct(StringComparer.Ordinal).Count() != bones.Length)
                throw new InvalidDataException("GLB bone names must be unique.");

            return new BoneProjection(bones, boneIndexByJointOrdinal);
        }

        private (IReadOnlyList<XModelExportVertex> Vertices,
            IReadOnlyList<XModelExportTriangle> Triangles,
            IReadOnlyList<XModelExportObject> Objects,
            IReadOnlyList<XModelExportMaterial> Materials) ProjectMeshes(
                int? skinIndex,
                BoneProjection bones,
                bool bakeNodeTransforms = false)
        {
            JsonElement[] meshes = Elements(_root, "meshes", required: true);
            JsonElement[] sourceMaterials = Elements(_root, "materials", required: !bakeNodeTransforms);
            if (meshes.Length == 0 || !bakeNodeTransforms && sourceMaterials.Length == 0)
                throw new InvalidDataException("GLB XModel import requires meshes and materials.");

            var vertices = new List<XModelExportVertex>();
            var triangles = new List<XModelExportTriangle>();
            var objects = new List<XModelExportObject>();
            var materials = new List<XModelExportMaterial>();
            var materialMap = new Dictionary<int, int>();
            var objectNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (int nodeIndex in _reachableNodes.Order())
            {
                JsonElement node = _nodes[nodeIndex];
                if (!node.TryGetProperty("mesh", out JsonElement meshProperty))
                    continue;
                int meshIndex = StrictInt(meshProperty, $"node {nodeIndex} mesh");
                if (meshIndex < 0 || meshIndex >= meshes.Length)
                    throw new InvalidDataException($"GLB node {nodeIndex} references invalid mesh {meshIndex}.");
                if (!bakeNodeTransforms && Integer(node, "skin", required: true) != skinIndex)
                    throw new InvalidDataException("All imported GLB mesh nodes must use the selected skin.");
                Matrix4x4 nodeGlobal = ResolveNodeGlobal(nodeIndex, new Dictionary<int, Matrix4x4>());
                if (!bakeNodeTransforms && !ApproximatelyIdentity(nodeGlobal))
                    throw new InvalidDataException($"GLB mesh node {nodeIndex} has an unapplied transform. Apply mesh transforms in Blender before export.");
                if (!IsFinite(nodeGlobal) || bakeNodeTransforms && !Matrix4x4.Invert(nodeGlobal, out _))
                    throw new InvalidDataException($"GLB mesh node {nodeIndex} has a non-finite or singular transform.");

                string objectName = node.TryGetProperty("name", out JsonElement nodeName)
                    ? StrictString(nodeName, $"node {nodeIndex} name")
                    : meshes[meshIndex].TryGetProperty("name", out JsonElement meshName)
                        ? StrictString(meshName, $"mesh {meshIndex} name")
                        : $"surf{objects.Count}";
                if (!IsValidName(objectName) || !objectNames.Add(objectName))
                    throw new InvalidDataException("Every imported GLB mesh node must have a unique non-empty object name.");
                int objectIndex = objects.Count;
                objects.Add(new XModelExportObject(objectName));

                JsonElement mesh = meshes[meshIndex];
                if (mesh.TryGetProperty("weights", out _) || mesh.TryGetProperty("extensions", out _))
                    throw new InvalidDataException($"GLB mesh '{objectName}' uses unsupported morph weights or extensions.");
                JsonElement[] primitives = Elements(mesh, "primitives", required: true);
                if (primitives.Length == 0)
                    throw new InvalidDataException($"GLB mesh '{objectName}' has no primitives.");
                foreach (JsonElement primitive in primitives)
                    ProjectPrimitive(
                        primitive,
                        objectIndex,
                        sourceMaterials,
                        materialMap,
                        materials,
                        vertices,
                        triangles,
                        bones,
                        bakeNodeTransforms,
                        nodeGlobal);
            }

            if (triangles.Count == 0)
                throw new InvalidDataException("The selected GLB scene has no triangle geometry.");
            return (
                System.Array.AsReadOnly(vertices.ToArray()),
                System.Array.AsReadOnly(triangles.ToArray()),
                System.Array.AsReadOnly(objects.ToArray()),
                System.Array.AsReadOnly(materials.ToArray()));
        }

        private void ProjectPrimitive(
            JsonElement primitive,
            int objectIndex,
            JsonElement[] sourceMaterials,
            Dictionary<int, int> materialMap,
            List<XModelExportMaterial> materials,
            List<XModelExportVertex> vertices,
            List<XModelExportTriangle> triangles,
            BoneProjection bones,
            bool rigidModelReplacement,
            Matrix4x4 nodeTransform)
        {
            RequireObject(primitive, "mesh primitive");
            if (primitive.TryGetProperty("extensions", out _) || primitive.TryGetProperty("targets", out _))
                throw new InvalidDataException("GLB mesh compression, extensions, and morph targets are not supported for XModel import.");
            int mode = primitive.TryGetProperty("mode", out JsonElement modeProperty)
                ? StrictInt(modeProperty, "primitive mode")
                : 4;
            if (mode != 4)
                throw new InvalidDataException("Only GLB triangle-list primitives can be imported as XModel geometry.");

            int sourceMaterial = primitive.TryGetProperty("material", out JsonElement materialProperty)
                ? StrictInt(materialProperty, "primitive material")
                : -1;
            if (sourceMaterial < -1 || sourceMaterial >= sourceMaterials.Length ||
                !rigidModelReplacement && sourceMaterial < 0)
                throw new InvalidDataException("A GLB primitive has no valid material.");
            if (!materialMap.TryGetValue(sourceMaterial, out int materialIndex))
            {
                materialIndex = materials.Count;
                materialMap.Add(sourceMaterial, materialIndex);
                materials.Add(sourceMaterial < 0
                    ? new XModelExportMaterial("material_default", string.Empty)
                    {
                        ImportMaterial = new XModelImportMaterial(
                            Vector4.One,
                            BaseColorImage: null,
                            NormalImage: null,
                            NormalScale: 1f,
                            DoubleSided: false,
                            XModelImportAlphaMode.Opaque,
                            AlphaCutoff: 0.5f,
                            Warnings: [])
                    }
                    : ProjectMaterial(sourceMaterials[sourceMaterial], sourceMaterial));
            }
            Vector4 materialColor = Vector4.One;

            if (!primitive.TryGetProperty("attributes", out JsonElement attributes) || attributes.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("A GLB primitive has no attribute table.");
            AccessorView positions = Attribute(attributes, "POSITION", "VEC3");
            AccessorView normals = Attribute(attributes, "NORMAL", "VEC3");
            AccessorView uvs = Attribute(attributes, "TEXCOORD_0", "VEC2");
            AccessorView? joints = rigidModelReplacement ? null : Attribute(attributes, "JOINTS_0", "VEC4");
            AccessorView? weights = rigidModelReplacement ? null : Attribute(attributes, "WEIGHTS_0", "VEC4");
            AccessorView? colors = attributes.TryGetProperty("COLOR_0", out JsonElement colorAccessor)
                ? _accessors.Get(StrictInt(colorAccessor, "COLOR_0 accessor"), null)
                : null;
            if (colors is not null && colors.Type is not ("VEC3" or "VEC4"))
                throw new InvalidDataException("GLB COLOR_0 must be VEC3 or VEC4.");
            if (positions.ComponentType != 5126 || normals.ComponentType != 5126)
                throw new InvalidDataException("GLB POSITION and NORMAL must use float components.");
            if (!uvs.HasFloatOrNormalizedUnsignedComponents ||
                weights is not null && !weights.HasFloatOrNormalizedUnsignedComponents ||
                joints is not null && (joints.ComponentType is not (5121 or 5123) || joints.Normalized) ||
                colors is not null && !colors.HasFloatOrNormalizedUnsignedComponents)
                throw new InvalidDataException("GLB UV, color, joint, or weight accessor encoding is not supported for XModel import.");
            int count = positions.Count;
            if (count == 0 || normals.Count != count || uvs.Count != count ||
                joints is not null && joints.Count != count || weights is not null && weights.Count != count ||
                colors is not null && colors.Count != count)
                throw new InvalidDataException("GLB primitive attribute counts do not match.");

            int[] indices;
            if (primitive.TryGetProperty("indices", out JsonElement indicesProperty))
            {
                AccessorView indexAccessor = _accessors.Get(StrictInt(indicesProperty, "indices accessor"), "SCALAR");
                indices = Enumerable.Range(0, indexAccessor.Count)
                    .Select(index => indexAccessor.ReadUnsigned(index))
                    .ToArray();
            }
            else
                indices = Enumerable.Range(0, count).ToArray();
            if (indices.Length == 0 || indices.Length % 3 != 0 || indices.Any(index => index < 0 || index >= count))
                throw new InvalidDataException("GLB triangle indices are empty, incomplete, or out of range.");

            Matrix4x4 normalTransform = Matrix4x4.Identity;
            if (rigidModelReplacement)
            {
                if (!Matrix4x4.Invert(nodeTransform, out Matrix4x4 inverseNode))
                    throw new InvalidDataException("A GLB mesh node has a singular normal transform.");
                normalTransform = Matrix4x4.Transpose(inverseNode);
            }

            var mappedVertices = new Dictionary<int, int>();
            int MapVertex(int sourceIndex)
            {
                if (mappedVertices.TryGetValue(sourceIndex, out int existing))
                    return existing;
                Vector3 sourcePosition = positions.ReadVector3(sourceIndex);
                Vector3 position = ConvertVector(rigidModelReplacement
                    ? Vector3.Transform(sourcePosition, nodeTransform)
                    : sourcePosition);
                if (!IsFinite(position))
                    throw new InvalidDataException("A GLB primitive contains a non-finite transformed position.");
                if (rigidModelReplacement)
                {
                    int rigidMapped = vertices.Count;
                    vertices.Add(new XModelExportVertex(
                        position,
                        System.Array.AsReadOnly([new XModelExportBoneWeight(0, 1f)])));
                    mappedVertices.Add(sourceIndex, rigidMapped);
                    return rigidMapped;
                }

                var projectedWeights = new Dictionary<int, float>();
                for (int lane = 0; lane < 4; lane++)
                {
                    int jointOrdinal = joints!.ReadUnsigned(sourceIndex, lane);
                    float weight = weights!.ReadFloat(sourceIndex, lane);
                    if (!float.IsFinite(weight) || weight < 0f)
                        throw new InvalidDataException("GLB skin weights must be finite and non-negative.");
                    if (weight <= 0f)
                        continue;
                    if (jointOrdinal < 0 || jointOrdinal >= bones.BoneIndexByJointOrdinal.Length)
                        throw new InvalidDataException("A GLB vertex references a joint outside the selected skin.");
                    int boneIndex = bones.BoneIndexByJointOrdinal[jointOrdinal];
                    if (!projectedWeights.TryAdd(boneIndex, weight))
                        throw new InvalidDataException("A GLB vertex contains duplicate positive bone influences.");
                }
                float total = projectedWeights.Values.Sum();
                if (projectedWeights.Count is < 1 or > 4 || !float.IsFinite(total) || MathF.Abs(total - 1f) > 0.001f)
                    throw new InvalidDataException("A GLB vertex must have one to four positive skin weights summing to one.");
                XModelExportBoneWeight[] normalizedWeights = projectedWeights
                    .OrderByDescending(entry => entry.Value)
                    .ThenBy(entry => entry.Key)
                    .Select(entry => new XModelExportBoneWeight(entry.Key, entry.Value / total))
                    .ToArray();
                int mapped = vertices.Count;
                vertices.Add(new XModelExportVertex(position, System.Array.AsReadOnly(normalizedWeights)));
                mappedVertices.Add(sourceIndex, mapped);
                return mapped;
            }

            XModelExportCorner Corner(int sourceIndex)
            {
                Vector3 sourceNormal = normals.ReadVector3(sourceIndex);
                if (rigidModelReplacement)
                    sourceNormal = Vector3.TransformNormal(sourceNormal, normalTransform);
                Vector3 normal = ConvertVector(sourceNormal);
                if (!IsFinite(normal) || normal.LengthSquared() < 0.000001f)
                    throw new InvalidDataException("A GLB primitive contains a zero-length or non-finite normal.");
                normal = Vector3.Normalize(normal);
                Vector2 uv = uvs.ReadVector2(sourceIndex);
                Vector4 vertexColor = colors is null
                    ? Vector4.One
                    : colors.Type == "VEC3"
                        ? new Vector4(colors.ReadVector3(sourceIndex), 1f)
                        : colors.ReadVector4(sourceIndex);
                Vector4 color = vertexColor * materialColor;
                if (!IsFinite(position: uv) || !IsFinite(color) ||
                    color.X < 0f || color.X > 1f || color.Y < 0f || color.Y > 1f ||
                    color.Z < 0f || color.Z > 1f || color.W < 0f || color.W > 1f)
                    throw new InvalidDataException("A GLB primitive contains a non-finite UV or out-of-range vertex color.");
                return new XModelExportCorner(MapVertex(sourceIndex), normal, color, uv);
            }

            bool reverseWinding = rigidModelReplacement && nodeTransform.GetDeterminant() < 0f;
            for (int index = 0; index < indices.Length; index += 3)
            {
                XModelExportCorner first = Corner(indices[index]);
                XModelExportCorner second = Corner(indices[index + (reverseWinding ? 2 : 1)]);
                XModelExportCorner third = Corner(indices[index + (reverseWinding ? 1 : 2)]);
                Vector3 a = vertices[first.VertexIndex].Position;
                Vector3 b = vertices[second.VertexIndex].Position;
                Vector3 c = vertices[third.VertexIndex].Position;
                if (first.VertexIndex == second.VertexIndex || first.VertexIndex == third.VertexIndex ||
                    second.VertexIndex == third.VertexIndex || Vector3.Cross(b - a, c - a).LengthSquared() <= 0.000000000001f)
                {
                    if (rigidModelReplacement)
                        continue;
                    throw new InvalidDataException("A GLB primitive contains a degenerate triangle.");
                }
                if (rigidModelReplacement && IsUvDegenerate(first.Uv0, second.Uv0, third.Uv0))
                {
                    Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                    Vector3 magnitude = Vector3.Abs(faceNormal);
                    Vector2 Project(Vector3 position) =>
                        magnitude.X >= magnitude.Y && magnitude.X >= magnitude.Z
                            ? new Vector2(position.Y, position.Z)
                            : magnitude.Y >= magnitude.Z
                                ? new Vector2(position.X, position.Z)
                                : new Vector2(position.X, position.Y);
                    first = first with { Uv0 = Project(a) };
                    second = second with { Uv0 = Project(b) };
                    third = third with { Uv0 = Project(c) };
                }
                triangles.Add(new XModelExportTriangle(objectIndex, materialIndex, first, second, third));
            }
        }

        private static bool IsUvDegenerate(Vector2 first, Vector2 second, Vector2 third)
        {
            Vector2 a = second - first;
            Vector2 b = third - first;
            float determinant = a.X * b.Y - a.Y * b.X;
            return !float.IsFinite(determinant) || MathF.Abs(determinant) < 0.0000001f;
        }

        private XModelExportMaterial ProjectMaterial(JsonElement material, int index)
        {
            RequireObject(material, $"material {index}");
            string? extrasName = null;
            string colorPath = string.Empty;
            if (material.TryGetProperty("extras", out JsonElement extras) && extras.ValueKind == JsonValueKind.Object)
            {
                if (extras.TryGetProperty("iw4Material", out JsonElement iw4Material) && iw4Material.ValueKind == JsonValueKind.String)
                    extrasName = iw4Material.GetString();
                if (extras.TryGetProperty("iw4ColorMapPath", out JsonElement iw4ColorPath) && iw4ColorPath.ValueKind == JsonValueKind.String)
                    colorPath = iw4ColorPath.GetString() ?? string.Empty;
            }
            string name = IsValidName(extrasName)
                ? extrasName!
                : String(material, "name", required: true);
            if (!IsValidName(name) || colorPath.Any(char.IsControl))
                throw new InvalidDataException($"GLB material {index} has an invalid IW4 material identity.");
            return new XModelExportMaterial(name, colorPath)
            {
                ImportMaterial = ReadImportMaterial(material, index)
            };
        }

        private XModelImportMaterial ReadImportMaterial(JsonElement material, int index)
        {
            Vector4 baseColorFactor = ReadBaseColorFactor(material, index);
            var warnings = new List<string>();
            if (_isBlenderExport)
            {
                warnings.Add(
                    "Procedural Blender nodes and arbitrary Principled BSDF graphs are not represented by this import; only GLB base color, tangent normal, and compatible alpha behavior are authored.");
            }
            if (material.TryGetProperty("extensions", out JsonElement extensions))
            {
                RequireObject(extensions, $"material {index} extensions");
                foreach (JsonProperty extension in extensions.EnumerateObject())
                {
                    if (extension.NameEquals(MaterialsSpecularExtension))
                    {
                        warnings.Add(
                            "KHR_materials_specular properties are not imported; the selected IW4 template retains its specular behavior.");
                        continue;
                    }

                    throw new InvalidDataException(
                        $"GLB material {index} uses unsupported extension '{extension.Name}'.");
                }
            }
            XModelImportImage? baseColorImage = null;
            if (material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
            {
                RequireObject(pbr, $"material {index} pbrMetallicRoughness");
                if (pbr.TryGetProperty("baseColorTexture", out JsonElement textureInfo))
                    baseColorImage = ReadTextureImage(
                        textureInfo,
                        index,
                        "base-color",
                        warnings);
                if (pbr.TryGetProperty("metallicFactor", out _))
                    warnings.Add("Metallic factor is not imported; the selected IW4 template retains its shader behavior.");
                if (pbr.TryGetProperty("roughnessFactor", out _))
                    warnings.Add("Roughness factor is not imported; the selected IW4 template retains its shader behavior.");
                if (pbr.TryGetProperty("metallicRoughnessTexture", out _))
                    warnings.Add("Metallic/roughness textures are not imported.");
            }
            XModelImportImage? normalImage = null;
            float normalScale = 1f;
            if (material.TryGetProperty("normalTexture", out JsonElement normalTexture))
            {
                RequireObject(normalTexture, $"material {index} normalTexture");
                normalImage = ReadTextureImage(
                    normalTexture,
                    index,
                    "normal",
                    warnings);
                if (normalTexture.TryGetProperty("scale", out JsonElement scaleProperty) &&
                    (scaleProperty.ValueKind != JsonValueKind.Number ||
                     !scaleProperty.TryGetSingle(out normalScale) ||
                     !float.IsFinite(normalScale)))
                {
                    throw new InvalidDataException(
                        $"GLB material {index} normal scale must be finite.");
                }
            }
            if (material.TryGetProperty("occlusionTexture", out _))
                warnings.Add("Occlusion maps are not imported.");
            if (material.TryGetProperty("emissiveTexture", out _) ||
                material.TryGetProperty("emissiveFactor", out _))
                warnings.Add("Emissive properties are not imported.");
            bool doubleSided = Boolean(material, "doubleSided", defaultValue: false);

            string alphaName = material.TryGetProperty("alphaMode", out JsonElement alphaProperty)
                ? StrictString(alphaProperty, $"material {index} alphaMode")
                : "OPAQUE";
            XModelImportAlphaMode alphaMode = alphaName switch
            {
                "OPAQUE" => XModelImportAlphaMode.Opaque,
                "MASK" => XModelImportAlphaMode.Mask,
                "BLEND" => XModelImportAlphaMode.Blend,
                _ => throw new InvalidDataException(
                    $"GLB material {index} has unsupported alphaMode '{alphaName}'.")
            };
            float alphaCutoff = 0.5f;
            if (material.TryGetProperty("alphaCutoff", out JsonElement cutoffProperty))
            {
                if (cutoffProperty.ValueKind != JsonValueKind.Number ||
                    !cutoffProperty.TryGetSingle(out alphaCutoff) ||
                    !float.IsFinite(alphaCutoff) || alphaCutoff < 0f || alphaCutoff > 1f)
                {
                    throw new InvalidDataException(
                        $"GLB material {index} alphaCutoff must be within [0, 1].");
                }
                if (alphaMode != XModelImportAlphaMode.Mask)
                    warnings.Add("alphaCutoff is ignored unless alphaMode is MASK.");
            }

            return new XModelImportMaterial(
                baseColorFactor,
                baseColorImage,
                normalImage,
                normalScale,
                doubleSided,
                alphaMode,
                alphaCutoff,
                Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray()));
        }

        private XModelImportImage ReadTextureImage(
            JsonElement textureInfo,
            int materialIndex,
            string usage,
            List<string> warnings)
        {
            RequireObject(textureInfo, $"material {materialIndex} {usage} texture");
            if (textureInfo.TryGetProperty("extensions", out _))
                throw new InvalidDataException("GLB texture transforms and texture extensions are not supported.");
            if (OptionalInteger(textureInfo, "texCoord", 0) != 0)
                throw new InvalidDataException($"GLB {usage} textures must use TEXCOORD_0.");
            int textureIndex = Integer(textureInfo, "index", required: true);
            JsonElement[] textures = Elements(_root, "textures", required: true);
            if (textureIndex < 0 || textureIndex >= textures.Length)
                throw new InvalidDataException($"GLB material {materialIndex} references an invalid texture.");
            JsonElement texture = textures[textureIndex];
            RequireObject(texture, $"texture {textureIndex}");
            if (texture.TryGetProperty("extensions", out _))
                throw new InvalidDataException("GLB texture extensions are not supported.");
            if (texture.TryGetProperty("sampler", out _))
                warnings.Add("GLB sampler state is not imported; the selected IW4 template sampler is retained.");
            int imageIndex = Integer(texture, "source", required: true);
            JsonElement[] images = Elements(_root, "images", required: true);
            if (imageIndex < 0 || imageIndex >= images.Length)
                throw new InvalidDataException($"GLB texture {textureIndex} references an invalid image.");
            JsonElement image = images[imageIndex];
            RequireObject(image, $"image {imageIndex}");
            if (image.TryGetProperty("uri", out _))
                throw new InvalidDataException($"GLB {usage} images must be embedded in a bufferView.");
            string mimeType = String(image, "mimeType", required: true);
            if (mimeType is not ("image/png" or "image/jpeg"))
                throw new InvalidDataException(
                    $"GLB image {imageIndex} must be embedded PNG or JPEG, not '{mimeType}'.");
            int viewIndex = Integer(image, "bufferView", required: true);
            ReadOnlyMemory<byte> encoded = _accessors.GetBufferView(viewIndex);
            if (_imageDecoder is null)
                throw new InvalidDataException("Embedded GLB images require the desktop PNG/JPEG decoder.");
            XModelImportImage decoded = _imageDecoder(mimeType, encoded);
            int expected = checked(decoded.Width * decoded.Height * 4);
            if (decoded.Width <= 0 || decoded.Height <= 0 || decoded.RgbaBytes.Count != expected)
                throw new InvalidDataException($"The decoded GLB {usage} image has an invalid RGBA8 layout.");
            return decoded;
        }

        private static Vector4 ReadBaseColorFactor(JsonElement material, int index)
        {
            if (!material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
                return Vector4.One;
            RequireObject(pbr, $"material {index} pbrMetallicRoughness");
            if (!pbr.TryGetProperty("baseColorFactor", out JsonElement factor))
                return Vector4.One;
            float[] values = FloatArray(factor, 4, $"material {index} baseColorFactor");
            var color = new Vector4(values[0], values[1], values[2], values[3]);
            if (color.X < 0f || color.X > 1f || color.Y < 0f || color.Y > 1f ||
                color.Z < 0f || color.Z > 1f || color.W < 0f || color.W > 1f)
                throw new InvalidDataException($"GLB material {index} baseColorFactor must be within [0, 1].");
            return color;
        }

        private AccessorView Attribute(JsonElement attributes, string name, string type)
        {
            if (!attributes.TryGetProperty(name, out JsonElement property))
                throw new InvalidDataException($"GLB XModel primitive is missing required {name} data.");
            return _accessors.Get(StrictInt(property, $"{name} accessor"), type);
        }

        private Matrix4x4 ResolveNodeGlobal(int nodeIndex, Dictionary<int, Matrix4x4> cache)
        {
            if (cache.TryGetValue(nodeIndex, out Matrix4x4 existing))
                return existing;
            Matrix4x4 local = ReadNodeLocal(_nodes[nodeIndex], nodeIndex);
            Matrix4x4 global = _parents[nodeIndex] < 0
                ? local
                : local * ResolveNodeGlobal(_parents[nodeIndex], cache);
            cache.Add(nodeIndex, global);
            return global;
        }

        private static Matrix4x4 ReadNodeLocal(JsonElement node, int nodeIndex)
        {
            bool hasMatrix = node.TryGetProperty("matrix", out JsonElement matrixProperty);
            bool hasTrs = node.TryGetProperty("translation", out _) || node.TryGetProperty("rotation", out _) || node.TryGetProperty("scale", out _);
            if (hasMatrix && hasTrs)
                throw new InvalidDataException($"GLB node {nodeIndex} cannot define both matrix and TRS transforms.");
            if (hasMatrix)
                return Matrix(matrixProperty, $"node {nodeIndex} matrix");

            Vector3 translation = node.TryGetProperty("translation", out JsonElement translationProperty)
                ? ReadVector3(translationProperty, $"node {nodeIndex} translation")
                : Vector3.Zero;
            Vector3 scale = node.TryGetProperty("scale", out JsonElement scaleProperty)
                ? ReadVector3(scaleProperty, $"node {nodeIndex} scale")
                : Vector3.One;
            Quaternion rotation = node.TryGetProperty("rotation", out JsonElement rotationProperty)
                ? ReadQuaternion(rotationProperty, $"node {nodeIndex} rotation")
                : Quaternion.Identity;
            if (!IsFinite(translation) || !IsFinite(scale) || !IsFinite(rotation) ||
                MathF.Abs(rotation.LengthSquared() - 1f) > 0.001f)
                throw new InvalidDataException($"GLB node {nodeIndex} has a non-finite or non-unit transform.");
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation)) * Matrix4x4.CreateTranslation(translation);
        }

        private static int[] BuildParents(JsonElement[] nodes)
        {
            int[] parents = Enumerable.Repeat(-1, nodes.Length).ToArray();
            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                RequireObject(nodes[nodeIndex], $"node {nodeIndex}");
                int[] children = IntegerArray(nodes[nodeIndex], "children", required: false);
                if (children.Distinct().Count() != children.Length)
                    throw new InvalidDataException($"GLB node {nodeIndex} contains duplicate children.");
                foreach (int child in children)
                {
                    if (child < 0 || child >= nodes.Length || child == nodeIndex || parents[child] >= 0)
                        throw new InvalidDataException("The GLB node graph has an invalid or multiply-parented child.");
                    parents[child] = nodeIndex;
                }
            }
            return parents;
        }

        private static HashSet<int> ResolveSceneNodes(JsonElement root, JsonElement[] nodes)
        {
            JsonElement[] scenes = Elements(root, "scenes", required: true);
            if (scenes.Length == 0)
                throw new InvalidDataException("The GLB contains no scenes.");
            int sceneIndex = root.TryGetProperty("scene", out JsonElement sceneProperty)
                ? StrictInt(sceneProperty, "default scene")
                : 0;
            if (sceneIndex < 0 || sceneIndex >= scenes.Length)
                throw new InvalidDataException("The GLB default scene index is invalid.");
            int[] roots = IntegerArray(scenes[sceneIndex], "nodes", required: true);
            if (roots.Length == 0 || roots.Distinct().Count() != roots.Length)
                throw new InvalidDataException("The selected GLB scene has no unique root nodes.");

            var reached = new HashSet<int>();
            var active = new HashSet<int>();
            void Visit(int node)
            {
                if (node < 0 || node >= nodes.Length)
                    throw new InvalidDataException("The selected GLB scene references an invalid node.");
                if (!active.Add(node))
                    throw new InvalidDataException("The selected GLB scene contains a node cycle.");
                if (reached.Add(node))
                    foreach (int child in IntegerArray(nodes[node], "children", required: false)) Visit(child);
                active.Remove(node);
            }
            foreach (int rootNode in roots) Visit(rootNode);
            return reached;
        }

        private static void ValidateAsset(JsonElement root)
        {
            if (!root.TryGetProperty("asset", out JsonElement asset) || asset.ValueKind != JsonValueKind.Object ||
                String(asset, "version", required: true) != "2.0")
                throw new InvalidDataException("Only glTF asset version 2.0 is supported.");
        }

        private static void RejectExtensionsAndAnimation(JsonElement root)
        {
            string[] unsupportedRequired = Elements(root, "extensionsRequired", required: false)
                .Select((value, index) => StrictString(value, $"extensionsRequired[{index}]"))
                .Where(name => !string.Equals(name, MaterialsSpecularExtension, StringComparison.Ordinal))
                .ToArray();
            if (unsupportedRequired.Length != 0)
            {
                throw new InvalidDataException(
                    $"Required glTF extensions are not supported for XModel import: {string.Join(", ", unsupportedRequired)}.");
            }

            string[] unsupportedUsed = Elements(root, "extensionsUsed", required: false)
                .Select((value, index) => StrictString(value, $"extensionsUsed[{index}]"))
                .Where(name => !string.Equals(name, MaterialsSpecularExtension, StringComparison.Ordinal))
                .ToArray();
            if (unsupportedUsed.Length != 0)
            {
                throw new InvalidDataException(
                    $"glTF extensions are not supported for XModel import: {string.Join(", ", unsupportedUsed)}.");
            }
            if (Elements(root, "animations", required: false).Length != 0)
                throw new InvalidDataException("GLB animations cannot be imported as static XModel LOD geometry.");
        }

        private static bool ApproximatelyIdentity(Matrix4x4 value)
        {
            Matrix4x4 identity = Matrix4x4.Identity;
            return MatrixDistanceSquared(value, identity) <= 0.000001f;
        }

        private static float MatrixDistanceSquared(Matrix4x4 left, Matrix4x4 right)
        {
            float result = 0f;
            foreach ((float a, float b) in MatrixComponents(left).Zip(MatrixComponents(right)))
            {
                float delta = a - b;
                result += delta * delta;
            }
            return result;
        }

        internal static IEnumerable<float> MatrixComponents(Matrix4x4 value)
        {
            yield return value.M11; yield return value.M12; yield return value.M13; yield return value.M14;
            yield return value.M21; yield return value.M22; yield return value.M23; yield return value.M24;
            yield return value.M31; yield return value.M32; yield return value.M33; yield return value.M34;
            yield return value.M41; yield return value.M42; yield return value.M43; yield return value.M44;
        }

        private sealed record BoneProjection(XModelExportBone[] Bones, int[] BoneIndexByJointOrdinal);
    }

    private sealed class AccessorReader
    {
        private readonly JsonElement[] _accessors;
        private readonly JsonElement[] _bufferViews;
        private readonly ReadOnlyMemory<byte> _binary;

        internal AccessorReader(JsonElement root, ReadOnlyMemory<byte> binary)
        {
            JsonElement[] buffers = Elements(root, "buffers", required: true);
            if (buffers.Length != 1 || buffers[0].TryGetProperty("uri", out _))
                throw new InvalidDataException("Binary XModel import requires one embedded GLB buffer.");
            int declaredLength = Integer(buffers[0], "byteLength", required: true);
            if (declaredLength < 0 || declaredLength > binary.Length || binary.Length - declaredLength > 3)
                throw new InvalidDataException("The GLB binary buffer length is invalid.");
            _binary = binary[..declaredLength];
            _accessors = Elements(root, "accessors", required: true);
            _bufferViews = Elements(root, "bufferViews", required: true);
        }

        internal AccessorView Get(int accessorIndex, string? requiredType)
        {
            if (accessorIndex < 0 || accessorIndex >= _accessors.Length)
                throw new InvalidDataException($"GLB accessor {accessorIndex} is out of range.");
            JsonElement accessor = _accessors[accessorIndex];
            if (accessor.TryGetProperty("sparse", out _))
                throw new InvalidDataException("Sparse GLB accessors are not supported for XModel import.");
            int viewIndex = Integer(accessor, "bufferView", required: true);
            if (viewIndex < 0 || viewIndex >= _bufferViews.Length)
                throw new InvalidDataException($"GLB accessor {accessorIndex} references an invalid buffer view.");
            JsonElement view = _bufferViews[viewIndex];
            if (Integer(view, "buffer", required: true) != 0)
                throw new InvalidDataException("GLB XModel accessors must use the embedded buffer.");

            string type = String(accessor, "type", required: true);
            if (requiredType is not null && type != requiredType)
                throw new InvalidDataException($"GLB accessor {accessorIndex} must be {requiredType}, not {type}.");
            int components = type switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                "MAT4" => 16,
                _ => throw new InvalidDataException($"GLB accessor type {type} is not supported for XModel import.")
            };
            int componentType = Integer(accessor, "componentType", required: true);
            int componentSize = componentType switch
            {
                5120 or 5121 => 1,
                5122 or 5123 => 2,
                5125 or 5126 => 4,
                _ => throw new InvalidDataException($"GLB accessor component type {componentType} is not supported.")
            };
            int count = Integer(accessor, "count", required: true);
            if (count < 0)
                throw new InvalidDataException("GLB accessor count cannot be negative.");
            bool normalized = Boolean(accessor, "normalized", defaultValue: false);
            int elementSize = checked(componentSize * components);
            int stride = view.TryGetProperty("byteStride", out JsonElement strideProperty)
                ? StrictInt(strideProperty, "bufferView byteStride")
                : elementSize;
            if (stride < elementSize || stride % componentSize != 0)
                throw new InvalidDataException("GLB bufferView byteStride is invalid for its accessor.");
            int viewOffset = OptionalInteger(view, "byteOffset", 0);
            int viewLength = Integer(view, "byteLength", required: true);
            int accessorOffset = OptionalInteger(accessor, "byteOffset", 0);
            int start = checked(viewOffset + accessorOffset);
            int requiredLength = count == 0 ? 0 : checked((count - 1) * stride + elementSize);
            if (viewOffset < 0 || viewLength < 0 || accessorOffset < 0 ||
                accessorOffset > viewLength || requiredLength > viewLength - accessorOffset ||
                start < 0 || requiredLength > _binary.Length - start)
                throw new InvalidDataException($"GLB accessor {accessorIndex} exceeds its buffer view.");

            return new AccessorView(_binary, start, count, type, components, componentType, componentSize, stride, normalized);
        }

        internal ReadOnlyMemory<byte> GetBufferView(int viewIndex)
        {
            if (viewIndex < 0 || viewIndex >= _bufferViews.Length)
                throw new InvalidDataException($"GLB bufferView {viewIndex} is out of range.");
            JsonElement view = _bufferViews[viewIndex];
            if (Integer(view, "buffer", required: true) != 0 ||
                view.TryGetProperty("byteStride", out _))
            {
                throw new InvalidDataException("Embedded GLB images require an unstrided view in buffer 0.");
            }
            int offset = OptionalInteger(view, "byteOffset", 0);
            int length = Integer(view, "byteLength", required: true);
            if (offset < 0 || length <= 0 || offset > _binary.Length - length)
                throw new InvalidDataException($"GLB bufferView {viewIndex} exceeds the embedded buffer.");
            return _binary.Slice(offset, length);
        }
    }

    private sealed class AccessorView(
        ReadOnlyMemory<byte> binary,
        int start,
        int count,
        string type,
        int components,
        int componentType,
        int componentSize,
        int stride,
        bool normalized)
    {
        internal int Count => count;
        internal string Type => type;
        internal int ComponentType => componentType;
        internal bool Normalized => normalized;
        internal bool HasFloatOrNormalizedUnsignedComponents =>
            componentType == 5126 || normalized && (componentType is 5121 or 5123);

        internal float ReadFloat(int element, int component = 0)
        {
            ReadOnlySpan<byte> value = Component(element, component);
            return componentType switch
            {
                5120 => normalized ? MathF.Max(unchecked((sbyte)value[0]) / 127f, -1f) : unchecked((sbyte)value[0]),
                5121 => normalized ? value[0] / 255f : value[0],
                5122 => normalized ? MathF.Max(BinaryPrimitives.ReadInt16LittleEndian(value) / 32767f, -1f) : BinaryPrimitives.ReadInt16LittleEndian(value),
                5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian(value) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian(value),
                5125 => normalized ? BinaryPrimitives.ReadUInt32LittleEndian(value) / 4294967295f : BinaryPrimitives.ReadUInt32LittleEndian(value),
                5126 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value)),
                _ => throw new InvalidDataException("Unsupported GLB numeric component type.")
            };
        }

        internal int ReadUnsigned(int element, int component = 0)
        {
            if (normalized)
                throw new InvalidDataException("GLB joint and index accessors cannot be normalized.");
            ReadOnlySpan<byte> value = Component(element, component);
            uint result = componentType switch
            {
                5121 => value[0],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(value),
                5125 => BinaryPrimitives.ReadUInt32LittleEndian(value),
                _ => throw new InvalidDataException("GLB joint and index accessors must use unsigned integer components.")
            };
            return checked((int)result);
        }

        internal Vector2 ReadVector2(int element) => new(ReadFloat(element), ReadFloat(element, 1));
        internal Vector3 ReadVector3(int element) => new(ReadFloat(element), ReadFloat(element, 1), ReadFloat(element, 2));
        internal Vector4 ReadVector4(int element) => new(ReadFloat(element), ReadFloat(element, 1), ReadFloat(element, 2), ReadFloat(element, 3));

        internal Matrix4x4 ReadMatrix(int element)
        {
            if (type != "MAT4" || componentType != 5126)
                throw new InvalidDataException("GLB bind matrices must use float MAT4 accessors.");
            return new Matrix4x4(
                ReadFloat(element, 0), ReadFloat(element, 1), ReadFloat(element, 2), ReadFloat(element, 3),
                ReadFloat(element, 4), ReadFloat(element, 5), ReadFloat(element, 6), ReadFloat(element, 7),
                ReadFloat(element, 8), ReadFloat(element, 9), ReadFloat(element, 10), ReadFloat(element, 11),
                ReadFloat(element, 12), ReadFloat(element, 13), ReadFloat(element, 14), ReadFloat(element, 15));
        }

        private ReadOnlySpan<byte> Component(int element, int component)
        {
            if (element < 0 || element >= count || component < 0 || component >= components)
                throw new InvalidDataException("GLB accessor element or component is out of range.");
            int offset = checked(start + element * stride + component * componentSize);
            return binary.Span.Slice(offset, componentSize);
        }
    }

    private static JsonElement[] Elements(JsonElement parent, string name, bool required)
    {
        if (!parent.TryGetProperty(name, out JsonElement property))
        {
            if (!required) return [];
            throw new InvalidDataException($"GLB is missing required {name} data.");
        }
        if (property.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"GLB {name} must be an array.");
        return property.EnumerateArray().ToArray();
    }

    private static int Integer(JsonElement parent, string name, bool required)
    {
        if (!parent.TryGetProperty(name, out JsonElement property))
        {
            if (!required) return 0;
            throw new InvalidDataException($"GLB is missing required {name}.");
        }
        return StrictInt(property, name);
    }

    private static int OptionalInteger(JsonElement parent, string name, int defaultValue) =>
        parent.TryGetProperty(name, out JsonElement property) ? StrictInt(property, name) : defaultValue;

    private static int StrictInt(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new InvalidDataException($"GLB {description} must be a 32-bit integer.");
        return result;
    }

    private static int[] IntegerArray(JsonElement parent, string name, bool required)
    {
        JsonElement[] values = Elements(parent, name, required);
        return values.Select((value, index) => StrictInt(value, $"{name}[{index}]")).ToArray();
    }

    private static string String(JsonElement parent, string name, bool required)
    {
        if (!parent.TryGetProperty(name, out JsonElement property))
        {
            if (!required) return string.Empty;
            throw new InvalidDataException($"GLB is missing required {name}.");
        }
        return StrictString(property, name);
    }

    private static string StrictString(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not string result)
            throw new InvalidDataException($"GLB {description} must be a string.");
        return result;
    }

    private static bool Boolean(JsonElement parent, string name, bool defaultValue)
    {
        if (!parent.TryGetProperty(name, out JsonElement property)) return defaultValue;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"GLB {name} must be a boolean.")
        };
    }

    private static Matrix4x4 Matrix(JsonElement value, string description)
    {
        float[] components = FloatArray(value, 16, description);
        return new Matrix4x4(
            components[0], components[1], components[2], components[3],
            components[4], components[5], components[6], components[7],
            components[8], components[9], components[10], components[11],
            components[12], components[13], components[14], components[15]);
    }

    private static Vector3 ReadVector3(JsonElement value, string description)
    {
        float[] components = FloatArray(value, 3, description);
        return new Vector3(components[0], components[1], components[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement value, string description)
    {
        float[] components = FloatArray(value, 4, description);
        return new Quaternion(components[0], components[1], components[2], components[3]);
    }

    private static float[] FloatArray(JsonElement value, int length, string description)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != length)
            throw new InvalidDataException($"GLB {description} must contain {length} numeric values.");
        var result = new float[length];
        int index = 0;
        foreach (JsonElement component in value.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Number || !component.TryGetSingle(out float number) || !float.IsFinite(number))
                throw new InvalidDataException($"GLB {description} contains a non-finite number.");
            result[index++] = number;
        }
        return result;
    }

    private static void RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{description} must be a JSON object.");
    }

    private static bool IsValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static Vector3 ConvertVector(Vector3 value) => new(value.X, -value.Z, value.Y);
    private static bool IsFinite(Vector2 position) => float.IsFinite(position.X) && float.IsFinite(position.Y);
    private static bool IsFinite(Vector3 position) => float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
    private static bool IsFinite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static bool IsFinite(Quaternion value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static bool IsFinite(Matrix4x4 value) => Reader.MatrixComponents(value).All(float.IsFinite);
}
