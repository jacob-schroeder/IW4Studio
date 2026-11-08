using System.Buffers.Binary;
using MapConverter.Game.IW3.PS3.Materials;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Math;
using IW4.Game.ScriptStrings;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        private sealed record ModelSurfaceSource(
            Region Root,
            Pointer? Blend,
            Pointer? Verts0,
            Pointer? Verts1,
            Pointer? Rigids,
            Pointer? Triangles,
            bool Verts1Physical);

        private sealed record ModelRigidSource(Region Root, Pointer? Tree);
        private sealed record ModelTreeSource(Region Root, Pointer? Nodes, Pointer? Leaves);

        private Iw3Ps3ModelSource CaptureModelSource(
            string name, Region rootRegion, Pointer? boneNames, Pointer? parents,
            Pointer? quats, Pointer? translations, Pointer? classification,
            Pointer? baseMatrices, IReadOnlyList<ModelSurfaceSource> surfaces,
            IReadOnlyList<Pointer?> materials, Pointer? collision, Pointer? boneInfo,
            Pointer? preset)
        {
            ReadOnlySpan<byte> root = Bytes(rootRegion).Span;
            int bones = root[4], childBones = bones - root[5], surfaceCount = root[6];
            int collisionCount = checked((int)Be32(root, 0x8c));
            ReadOnlyMemory<byte> names = Raw(boneNames, bones * 2, $"model '{name}' bone names");
            var boneValues = new string?[bones];
            for (int index = 0; index < bones; index++)
            {
                ushort local = Be16(names.Span, index * 2);
                if (local >= _scriptStrings.Count)
                    throw Invalid($"model '{name}' bone {index} has unknown script string {local}");
                boneValues[index] = _scriptStrings[local];
            }
            if (surfaces.Count != surfaceCount || materials.Count != surfaceCount)
                throw Invalid($"model '{name}' has incomplete surfaces or material handles");
            return new Iw3Ps3ModelSource(name, Bytes(rootRegion), names, boneValues,
                Raw(parents, childBones, $"model '{name}' parents"),
                Raw(quats, childBones * 8, $"model '{name}' quaternions"),
                Raw(translations, childBones * 16, $"model '{name}' translations"),
                Raw(classification, bones, $"model '{name}' part classification"),
                Raw(baseMatrices, bones * 32, $"model '{name}' base matrices"),
                surfaces.Select(CaptureSurface).ToArray(),
                materials.Select(pointer => pointer is null ? null :
                    _assets[pointer.Dependencies.Single()].Name).ToArray(),
                Raw(collision, collisionCount * 0x24, $"model '{name}' collision surfaces"),
                Raw(boneInfo, bones * 40, $"model '{name}' bone info"),
                preset is null ? null : _assets[preset.Dependencies.Single()].Name,
                Be32(root, 0xc8) != 0);
        }

        private Iw3Ps3ModelSurfaceSource CaptureSurface(ModelSurfaceSource surface)
        {
            ReadOnlySpan<byte> root = Bytes(surface.Root).Span;
            int blendWords = Be16(root, 12) + Be16(root, 14) * 3 +
                Be16(root, 16) * 5 + Be16(root, 18) * 7;
            int vertCount = Be16(root, 2), triCount = Be16(root, 4);
            int listCount = checked((int)Be32(root, 0x30));
            var rigids = surface.Rigids?.Value as IReadOnlyList<ModelRigidSource>;
            if (listCount != 0 && (rigids is null || rigids.Count != listCount))
                throw Unsupported("model surface has unresolved rigid-list rows");
            return new Iw3Ps3ModelSurfaceSource(Bytes(surface.Root),
                Raw(surface.Blend, blendWords * 2, "surface blend indices"),
                Raw(surface.Verts0, vertCount * 16, "surface vertex stream 0"),
                Raw(surface.Verts1, vertCount * 16, "surface vertex stream 1"),
                rigids?.Select(CaptureRigid).ToArray() ?? [],
                Raw(surface.Triangles, triCount * 6, "surface triangle indices"),
                surface.Verts1Physical);
        }

        private Iw3Ps3ModelRigidSource CaptureRigid(ModelRigidSource rigid)
        {
            Iw3Ps3ModelTreeSource? tree = null;
            if (rigid.Tree is not null)
            {
                if (rigid.Tree.Value is not ModelTreeSource source)
                    throw Unsupported("model rigid surface has an unresolved collision tree");
                ReadOnlySpan<byte> root = Bytes(source.Root).Span;
                tree = new Iw3Ps3ModelTreeSource(Bytes(source.Root),
                    Raw(source.Nodes, checked((int)Be32(root, 0x18)) * 16,
                        "surface collision nodes"),
                    Raw(source.Leaves, checked((int)Be32(root, 0x20)) * 2,
                        "surface collision leaves"));
            }
            return new Iw3Ps3ModelRigidSource(Bytes(rigid.Root), tree);
        }

        private XModelAsset CompileModelSource(
            Iw3Ps3ModelSource source,
            PhysCollmapAsset? physCollmap,
            IReadOnlyList<Pointer?>? mapMaterials = null,
            Pointer? mapPreset = null)
        {
            if (source.HasPhysCollmap && physCollmap is null)
                throw Unsupported($"model '{source.Name}' has unresolved PhysCollmap geometry");
            var materials = new MaterialAsset?[source.MaterialNames.Count];
            for (int index = 0; index < materials.Length; index++)
            {
                string? materialName = source.MaterialNames[index];
                if (mapMaterials is not null)
                {
                    materials[index] = mapMaterials[index]?.Value as MaterialAsset;
                    if (mapMaterials[index] is not null && materials[index] is null)
                        throw Unsupported($"model '{source.Name}' material {index} has no converted definition");
                }
                else if (materialName is not null)
                {
                    Iw3Ps3MaterialSource material = (_stockSources ??
                        throw Invalid("conversion has no stock source library"))
                        .ResolveMaterial(materialName);
                    materials[index] = (_materialCompiler ??
                        throw Invalid("conversion has no material compiler")).Compile(material);
                    MaterialAsset compiled = materials[index] ??
                        throw Invalid($"model '{source.Name}' material {index} is unresolved");
                    if (!_definitions.Contains(compiled)) _definitions.Add(compiled);
                    if (compiled.TechniqueSet is { } set && !_definitions.Contains(set))
                        _definitions.Add(set);
                }
            }
            PhysPresetAsset? preset = mapPreset?.Value as PhysPresetAsset;
            if (mapPreset is not null && preset is null)
                throw Unsupported($"model '{source.Name}' has an unresolved physics preset");
            if (mapMaterials is null && source.PresetName is { } presetName)
            {
                var stock = (_stockSources ?? throw Invalid("conversion has no stock source library"))
                    .ResolvePreset(presetName);
                preset = CompilePresetSource(presetName, stock.Root, stock.SoundPrefix);
                if (!_definitions.Contains(preset)) _definitions.Add(preset);
            }
            return BuildModel(source, materials, preset, physCollmap);
        }

        private XModelAsset BuildModel(
            Iw3Ps3ModelSource source,
            IReadOnlyList<MaterialAsset?> targetMaterials,
            PhysPresetAsset? targetPreset,
            PhysCollmapAsset? physCollmap)
        {
            string name = source.Name;
            ReadOnlySpan<byte> root = source.Root.Span;
            int bones = root[4], rootBones = root[5], surfaceCount = root[6];
            if (source.Surfaces.Count != surfaceCount || targetMaterials.Count != surfaceCount)
                throw Invalid($"model '{name}' has incomplete surfaces or material handles");
            if (root[7] >= (byte)XModelLodRampType.Count)
                throw Unsupported($"model '{name}' has an unknown LOD ramp type");
            int numLods = Be16(root, 0xb4);
            int collLod = BinaryPrimitives.ReadInt16BigEndian(root[0xb6..]);
            if (numLods > 4 || collLod is < -1 or > 3)
                throw Invalid($"model '{name}' has invalid LOD metadata");
            int collisionCount = checked((int)Be32(root, 0x8c));

            ReadOnlySpan<byte> sourceNames = source.BoneNames.Span;
            if (sourceNames.Length != bones * 2 || source.BoneNameValues.Count != bones)
                throw Invalid($"model '{name}' has incomplete bone names");
            var targetNames = new ScriptStringReference[bones];
            for (int index = 0; index < bones; index++)
            {
                ushort local = Be16(sourceNames, index * 2);
                targetNames[index] = new ScriptStringReference(
                    local, source.BoneNameValues[index], ScriptStringHandle.Null, default);
            }

            int childBones = bones - rootBones;
            byte[] parentBytes = source.Parents.ToArray();
            ReadOnlySpan<byte> sourceQuats = source.Quats.Span;
            var targetQuats = new short[childBones * 4];
            for (int index = 0; index < targetQuats.Length; index++)
                targetQuats[index] = BinaryPrimitives.ReadInt16BigEndian(sourceQuats[(index * 2)..]);
            ReadOnlySpan<byte> sourceTrans = source.Translations.Span;
            var targetTrans = new float[childBones * 3];
            // Native allocation/serialization reserves four floats per child,
            // but the skeleton consumer indexes the packed XYZ values at
            // trans[3 * childBone]. The extra floats trail the logical array.
            for (int index = 0; index < targetTrans.Length; index++)
                targetTrans[index] = BeFloat(sourceTrans, index * 4);
            byte[] classifications = source.Classification.ToArray();
            ReadOnlySpan<byte> sourceMatrices = source.BaseMatrices.Span;
            var targetMatrices = new DObjAnimMat[bones];
            for (int index = 0; index < bones; index++)
            {
                int offset = index * 32;
                targetMatrices[index] = new DObjAnimMat(
                    new DObjQuat(BeFloat(sourceMatrices, offset), BeFloat(sourceMatrices, offset + 4),
                        BeFloat(sourceMatrices, offset + 8), BeFloat(sourceMatrices, offset + 12)),
                    ReadVec3(sourceMatrices, offset + 16), BeFloat(sourceMatrices, offset + 28));
            }

            var lods = new XModelLodInfo[4];
            for (int index = 0; index < 4; index++)
            {
                int offset = 0x28 + index * 0x18;
                ushort count = Be16(root, offset + 4);
                ushort first = Be16(root, offset + 6);
                if (first + count > surfaceCount)
                    throw Invalid($"model '{name}' LOD {index} references surfaces beyond its table");
                uint[] partBits = PartBits(root, offset + 8);
                XModelSurfsAsset? modelSurfs = null;
                if (count != 0)
                {
                    XSurface[] targetSurfaces = source.Surfaces.Skip(first).Take(count)
                        .Select((surface, surfaceIndex) => BuildSurface(surface,
                            $"model '{name}' LOD {index} surface {surfaceIndex}"))
                        .ToArray();
                    modelSurfs = new XModelSurfsAsset
                    {
                        Name = $"{_ownedNamePrefix}{name}_lod{index}",
                        NumSurfs = count,
                        PartBits = partBits,
                        Surfaces = Array.AsReadOnly(targetSurfaces)
                    };
                    _definitions.Add(modelSurfs);
                }
                lods[index] = new XModelLodInfo
                {
                    Dist = BeFloat(root, offset),
                    NumSurfs = count,
                    SurfIndex = first,
                    PartBits = partBits,
                    ModelSurfs = modelSurfs
                };
            }

            ReadOnlySpan<byte> collisionBytes = source.Collision.Span;
            var targetCollision = new XModelCollSurf[collisionCount];
            for (int index = 0; index < collisionCount; index++)
            {
                int offset = index * 0x24;
                targetCollision[index] = new XModelCollSurf(
                    ReadMinMaxBounds(collisionBytes, offset, offset + 12),
                    BinaryPrimitives.ReadInt32BigEndian(collisionBytes[(offset + 24)..]),
                    BinaryPrimitives.ReadInt32BigEndian(collisionBytes[(offset + 28)..]),
                    BinaryPrimitives.ReadInt32BigEndian(collisionBytes[(offset + 32)..]));
            }
            ReadOnlySpan<byte> sourceBoneInfo = source.BoneInfo.Span;
            var targetBoneInfo = new XBoneInfo[bones];
            for (int index = 0; index < bones; index++)
            {
                int offset = index * 40;
                targetBoneInfo[index] = new XBoneInfo(
                    ReadMinMaxBounds(sourceBoneInfo, offset, offset + 12),
                    BeFloat(sourceBoneInfo, offset + 36));
            }

            return new XModelAsset
            {
                Name = _ownedNamePrefix + name,
                NumBones = (byte)bones,
                NumRootBones = (byte)rootBones,
                NumSurfs = (byte)surfaceCount,
                LodRampType = (XModelLodRampType)root[7],
                Scale = 1,
                NoScalePartBits = new uint[6],
                BoneNames = Array.AsReadOnly(targetNames),
                ParentList = parentBytes,
                Quats = Array.AsReadOnly(targetQuats),
                Trans = Array.AsReadOnly(targetTrans),
                PartClassification = classifications,
                BaseMat = Array.AsReadOnly(targetMatrices),
                Materials = Array.AsReadOnly(targetMaterials.ToArray()),
                Lods = Array.AsReadOnly(lods),
                MaxLoadedLod = 0,
                NumLods = (byte)numLods,
                CollLod = collLod < 0 ? byte.MaxValue : (byte)collLod,
                Flags = (XModelFlags)root[0xc0],
                NumCollSurfs = collisionCount,
                Contents = BinaryPrimitives.ReadInt32BigEndian(root[0x90..]),
                CollSurfs = Array.AsReadOnly(targetCollision),
                BoneInfo = Array.AsReadOnly(targetBoneInfo),
                Radius = BeFloat(root, 0x98),
                Bounds = ReadMinMaxBounds(root, 0x9c, 0xa8),
                InvHighMipRadius = new ushort[surfaceCount],
                MemUsage = BinaryPrimitives.ReadInt32BigEndian(root[0xbc..]),
                PhysPreset = targetPreset,
                PhysCollmap = physCollmap
            };
        }

        private XSurface BuildSurface(Iw3Ps3ModelSurfaceSource source, string path)
        {
            ReadOnlySpan<byte> root = source.Root.Span;
            ushort vertCount = Be16(root, 2), triCount = Be16(root, 4);
            ReadOnlySpan<byte> blend = source.Blend.Span;
            ushort[] blendIndices = ReadUshorts(blend);
            ReadOnlySpan<byte> triangles = source.Triangles.Span;
            ushort[] triangleIndices = ReadUshorts(triangles);
            if (triangleIndices.Any(index => index >= vertCount))
                throw Invalid($"{path} contains a triangle index outside its vertices");
            int listCount = checked((int)Be32(root, 0x30));
            if (source.Rigids.Count != listCount)
                throw Unsupported($"{path} has unresolved rigid-list rows");
            XRigidVertList[] rigidLists = source.Rigids.Select(row => BuildRigid(row, path)).ToArray();
            return new XSurface
            {
                TileMode = (XSurfaceTileMode)root[0],
                DeformedRaw = root[1],
                StreamFlags = XSurfaceStreamFlags.Verts0InLarge |
                    XSurfaceStreamFlags.TriIndicesInLarge |
                    (source.Verts1Physical ? XSurfaceStreamFlags.None : XSurfaceStreamFlags.Verts1InLarge),
                VertCount = vertCount,
                TriCount = triCount,
                TriIndices = Array.AsReadOnly(triangleIndices),
                VertexInfo = new XSurfaceVertexInfo
                {
                    Blend0 = Be16(root, 12),
                    Blend1 = Be16(root, 14),
                    Blend2 = Be16(root, 16),
                    Blend3 = Be16(root, 18),
                    VertsBlend = Array.AsReadOnly(blendIndices)
                },
                Verts0 = source.Verts0.ToArray(),
                Vb0 = new GfxVertexBuffer
                {
                    StreamSource = BinaryPrimitives.ReadInt32BigEndian(root[0x1c..]),
                    DataOffset = BinaryPrimitives.ReadInt32BigEndian(root[0x20..])
                },
                Verts1 = source.Verts1.ToArray(),
                Vb1 = new GfxVertexBuffer
                {
                    StreamSource = BinaryPrimitives.ReadInt32BigEndian(root[0x28..]),
                    DataOffset = BinaryPrimitives.ReadInt32BigEndian(root[0x2c..])
                },
                VertListCount = listCount,
                VertList = Array.AsReadOnly(rigidLists),
                IndexBuffer = new GfxIndexBuffer
                {
                    DataOffset = BinaryPrimitives.ReadInt32BigEndian(root[0x38..])
                },
                PartBits = PartBits(root, 0x3c)
            };
        }

        private XRigidVertList BuildRigid(Iw3Ps3ModelRigidSource source, string path)
        {
            ReadOnlySpan<byte> row = source.Root.Span;
            return new XRigidVertList
            {
                BoneOffset = Be16(row, 0),
                VertCount = Be16(row, 2),
                TriOffset = Be16(row, 4),
                TriCount = Be16(row, 6),
                CollisionTree = source.Tree is null ? null : BuildTree(source.Tree, path)
            };
        }

        private XSurfaceCollisionTree BuildTree(Iw3Ps3ModelTreeSource source, string path)
        {
            ReadOnlySpan<byte> root = source.Root.Span;
            int nodeCount = checked((int)Be32(root, 0x18));
            int leafCount = checked((int)Be32(root, 0x20));
            ReadOnlySpan<byte> nodeBytes = source.Nodes.Span;
            var nodes = new XSurfaceCollisionNode[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                int offset = index * 16;
                var box = new XSurfaceCollisionAabb(
                    Be16(nodeBytes, offset), Be16(nodeBytes, offset + 2),
                    Be16(nodeBytes, offset + 4), Be16(nodeBytes, offset + 6),
                    Be16(nodeBytes, offset + 8), Be16(nodeBytes, offset + 10));
                nodes[index] = new XSurfaceCollisionNode(box,
                    Be16(nodeBytes, offset + 12), Be16(nodeBytes, offset + 14));
            }
            ushort[] leaves = ReadUshorts(source.Leaves.Span);
            return new XSurfaceCollisionTree
            {
                Trans = ReadVec3(root, 0),
                Scale = ReadVec3(root, 12),
                NodeCount = nodeCount,
                Nodes = Array.AsReadOnly(nodes),
                LeafCount = leafCount,
                Leafs = Array.AsReadOnly(leaves.Select(value =>
                    new XSurfaceCollisionLeaf(value)).ToArray())
            };
        }

        private ReadOnlyMemory<byte> Raw(Pointer? pointer, int length, string path)
        {
            if (length == 0)
            {
                if (pointer is not null && pointer.Region.Length != 0)
                    throw Invalid($"{path} has unexpected bytes");
                return ReadOnlyMemory<byte>.Empty;
            }
            if (pointer is null || pointer.Region.Length != length)
                throw Unsupported($"{path} has no exact resolved payload");
            return Bytes(pointer.Region);
        }

        private static ushort[] ReadUshorts(ReadOnlySpan<byte> bytes)
        {
            var result = new ushort[bytes.Length / 2];
            for (int index = 0; index < result.Length; index++)
                result[index] = Be16(bytes, index * 2);
            return result;
        }

        private static uint[] PartBits(ReadOnlySpan<byte> bytes, int offset) =>
        [Be32(bytes, offset), Be32(bytes, offset + 4),
            Be32(bytes, offset + 8), Be32(bytes, offset + 12), 0, 0];

        private static Bounds ReadMinMaxBounds(ReadOnlySpan<byte> bytes, int minOffset, int maxOffset)
        {
            Vec3 min = ReadVec3(bytes, minOffset), max = ReadVec3(bytes, maxOffset);
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                throw new InvalidDataException("An IW3 PS3 model has inverted bounds.");
            return new Bounds
            {
                MidPoint = new Vec3 { X = (min.X + max.X) / 2, Y = (min.Y + max.Y) / 2,
                    Z = (min.Z + max.Z) / 2 },
                HalfSize = new Vec3 { X = (max.X - min.X) / 2, Y = (max.Y - min.Y) / 2,
                    Z = (max.Z - min.Z) / 2 }
            };
        }

        private static Vec3 ReadVec3(ReadOnlySpan<byte> bytes, int offset) =>
            new() { X = BeFloat(bytes, offset), Y = BeFloat(bytes, offset + 4),
                Z = BeFloat(bytes, offset + 8) };

        private static ushort Be16(ReadOnlySpan<byte> bytes, int offset) =>
            BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
        private static uint Be32(ReadOnlySpan<byte> bytes, int offset) =>
            BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
        private static float BeFloat(ReadOnlySpan<byte> bytes, int offset) =>
            BinaryPrimitives.ReadSingleBigEndian(bytes[offset..]);
    }
}
