using System.Buffers.Binary;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using MapConverter.Game.IW3.PS3.Materials;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // COD4 PS3 d1748/d16b8: 0x20-byte effect and 0xfc-byte element rows.
        private Pointer? Fx(uint raw, Address cell) => AssetPointer(raw, cell, "fx", 0x20, root =>
        {
            bool previousConversion = _converting;
            Push(4);
            try
            {
                int start = root.FileOffset;
                string? sourceName = ReadString(U32(start));
                bool reference = sourceName?.StartsWith(',') == true;
                if (_effectNames is not null)
                    _converting = !reference && sourceName is not null &&
                        _effectNames.Contains(sourceName);
                int looping = Count(U32(start + 0x10));
                int oneShot = Count(U32(start + 0x14));
                int emission = Count(U32(start + 0x18));
                int count = checked(looping + oneShot + emission);
                var dependencies = new HashSet<int>();
                FxElemDef[] elements = FxElements(U32(start + 0x1c), count, dependencies);
                var effect = new FxEffectDefAsset
                {
                    Name = sourceName is null ? null :
                        EffectRules.ReferenceName(_ownedNamePrefix, sourceName),
                    Flags = unchecked((int)U32(start + 4)),
                    TotalSize = unchecked((int)U32(start + 8)),
                    MsecLoopingLife = unchecked((int)U32(start + 0xc)),
                    ElemDefCountLooping = looping,
                    ElemDefCountOneShot = oneShot,
                    ElemDefCountEmission = emission,
                    ElemDefs = elements
                };
                return Named("fx", sourceName, root, dependencies,
                    ConvertFx && !reference ? effect : null);
            }
            finally { _converting = previousConversion; Pop(); }
        });

        private FxElemDef[] FxElements(uint raw, int count, HashSet<int> dependencies)
        {
            Pointer? table = ReadArray(raw, 4, Size(count, FxElemDef.SerializedSize), "fx-elements");
            if (table is null)
            {
                if (ConvertFx && count != 0) throw Invalid("Fx element table is absent");
                return [];
            }
            var result = new FxElemDef[count];
            for (int index = 0; index < count; index++)
                result[index] = FxElement(table.Region.FileOffset + index * FxElemDef.SerializedSize,
                    table.Region.Address.Add(index * FxElemDef.SerializedSize), dependencies);
            return result;
        }

        private FxElemDef FxElement(int row, Address address, HashSet<int> dependencies)
        {
            // d1500: velocity, visual state, visual arm, three Fx names, trail.
            byte sourceType = _data[row + 0xb0];
            var mappedType = EffectRules.ElementType(sourceType);
            if (mappedType.IsError) throw Unsupported(mappedType.ErrorValue);
            FxElemType type = mappedType.ResultValue;
            byte visualCount = _data[row + 0xb1];
            byte velIntervals = _data[row + 0xb2];
            byte visIntervals = _data[row + 0xb3];
            FxElemVelStateSample[] velocities = FxVelocities(U32(row + 0xb4), velIntervals + 1);
            FxElemVisStateSample[] visualStates = FxVisualStates(U32(row + 0xb8), visIntervals + 1);
            (FxElemDefVisuals inline, FxElemDefVisuals[] visuals, FxElemMarkVisuals[] marks) =
                FxVisuals(U32(row + 0xbc), address.Add(0xbc), type, visualCount, dependencies);
            FxEffectDefRef impact = FxReference(U32(row + 0xd8));
            FxEffectDefRef death = FxReference(U32(row + 0xdc));
            FxEffectDefRef emitted = FxReference(U32(row + 0xe0));
            FxElemExtendedDef? extended = FxTrail(U32(row + 0xf4));
            if (ConvertFx && extended is not null && type != FxElemType.Trail)
                throw Unsupported("non-trail COD4 Fx extended data");

            return new FxElemDef
            {
                Flags = unchecked((int)U32(row)),
                Spawn = new FxSpawnDef(unchecked((int)U32(row + 4)), unchecked((int)U32(row + 8))),
                SpawnRange = FxRange(row + 0xc),
                FadeInRange = FxRange(row + 0x14),
                FadeOutRange = FxRange(row + 0x1c),
                SpawnFrustumCullRadius = MapFloat(row + 0x24),
                SpawnDelayMsec = FxIntRangeAt(row + 0x28),
                LifeSpanMsec = FxIntRangeAt(row + 0x30),
                SpawnOrigin = FxRanges(row + 0x38, 3),
                SpawnOffsetRadius = FxRange(row + 0x50),
                SpawnOffsetHeight = FxRange(row + 0x58),
                SpawnAngles = FxRanges(row + 0x60, 3),
                AngularVelocity = FxRanges(row + 0x78, 3),
                InitialRotation = FxRange(row + 0x90),
                Gravity = FxRange(row + 0x98),
                ReflectionFactor = FxRange(row + 0xa0),
                Atlas = new FxElemAtlas(_data[row + 0xa8], _data[row + 0xa9],
                    _data[row + 0xaa], _data[row + 0xab], _data[row + 0xac],
                    _data[row + 0xad], unchecked((short)U16(row + 0xae))),
                ElemType = type,
                VisualCount = visualCount,
                VelIntervalCount = velIntervals,
                VisStateIntervalCount = visIntervals,
                VelSamples = velocities,
                VisSamples = visualStates,
                Visuals = inline,
                VisualArray = visuals,
                MarkVisualArray = marks,
                CollBounds = FxBounds(row + 0xc0),
                EffectOnImpact = impact,
                EffectOnDeath = death,
                EffectEmitted = emitted,
                EmitDist = FxRange(row + 0xe4),
                EmitDistVariance = FxRange(row + 0xec),
                Extended = extended,
                SortOrder = _data[row + 0xf8],
                LightingFrac = _data[row + 0xf9],
                UseItemClip = _data[row + 0xfa]
            };
        }

        private FxElemVelStateSample[] FxVelocities(uint raw, int count)
        {
            if (raw == 0)
            {
                if (ConvertFx && count != 1)
                    throw Invalid("Fx velocity intervals have no sample array");
                return [];
            }
            byte[] bytes = FxBytes(raw, 4, Size(count, 0x60), "velocity samples");
            var result = new FxElemVelStateSample[count];
            for (int index = 0; index < result.Length; index++)
            {
                int row = index * 0x60;
                result[index] = new FxElemVelStateSample(
                    FxVelocityFrame(bytes, row), FxVelocityFrame(bytes, row + 0x30));
            }
            return result;
        }

        private FxElemVisStateSample[] FxVisualStates(uint raw, int count)
        {
            if (raw == 0)
            {
                if (ConvertFx && count != 1)
                    throw Invalid("Fx visual intervals have no sample array");
                return [];
            }
            byte[] bytes = FxBytes(raw, 4, Size(count, 0x30), "visual state samples");
            var result = new FxElemVisStateSample[count];
            for (int index = 0; index < result.Length; index++)
            {
                int row = index * 0x30;
                result[index] = new FxElemVisStateSample(
                    FxVisualState(bytes, row), FxVisualState(bytes, row + 0x18));
            }
            return result;
        }

        private (FxElemDefVisuals Inline, FxElemDefVisuals[] Array, FxElemMarkVisuals[] Marks)
            FxVisuals(uint raw, Address cell, FxElemType type, byte count,
                HashSet<int> dependencies)
        {
            // COD4 d1400: decal has 8-byte mark rows; multiple visuals use
            // 4-byte rows; otherwise the union itself is the one visual.
            if (type == FxElemType.Decal)
            {
                Pointer? table = ReadArray(raw, 4, Size(count, 8), "fx-mark-visuals");
                if (ConvertFx && table is null && count != 0)
                    throw Invalid("Fx decal marks are absent");
                var marks = new FxElemMarkVisuals[table is null ? 0 : count];
                if (table is { } present)
                    for (int index = 0; index < count; index++)
                    {
                        int row = present.Region.FileOffset + index * 8;
                        Address markCell = present.Region.Address.Add(index * 8);
                        Pointer? first = Material(U32(row), markCell);
                        Pointer? second = Material(U32(row + 4), markCell.Add(4));
                        Add(dependencies, first);
                        Add(dependencies, second);
                        marks[index] = new FxElemMarkVisuals
                        {
                            Material0 = FxAsset<MaterialAsset>(first, "decal material"),
                            Material1 = FxAsset<MaterialAsset>(second, "decal material")
                        };
                    }
                return (new FxElemDefVisuals(), [], marks);
            }
            if (count > 1)
            {
                Pointer? table = ReadArray(raw, 4, Size(count, 4), "fx-visuals");
                if (table is null)
                {
                    if (ConvertFx) throw Invalid("Fx visual table is absent");
                    return (new FxElemDefVisuals(), [], []);
                }
                var visuals = new FxElemDefVisuals[count];
                for (int index = 0; index < count; index++)
                {
                    int row = table.Region.FileOffset + index * 4;
                    visuals[index] = FxVisual(U32(row), table.Region.Address.Add(index * 4),
                        type, dependencies);
                }
                return (new FxElemDefVisuals(), visuals, []);
            }
            return (FxVisual(raw, cell, type, dependencies), [], []);
        }

        private FxElemDefVisuals FxVisual(uint raw, Address cell, FxElemType type,
            HashSet<int> dependencies)
        {
            FxElemVisual? visual;
            switch (type)
            {
                case FxElemType.Model:
                {
                    Pointer? model = Model(raw, cell);
                    Add(dependencies, model);
                    visual = new FxModelVisual { Model = FxAsset<XModelAsset>(model, "visual model") };
                    break;
                }
                case FxElemType.OmniLight:
                case FxElemType.SpotLight:
                    visual = new FxNoChildVisual { Reserved = unchecked((int)raw) };
                    break;
                case FxElemType.Sound:
                {
                    string? soundName = ReadString(raw);
                    visual = new FxSoundVisual
                    {
                        SoundName = soundName is null ? null :
                            EffectRules.ReferenceName(_ownedNamePrefix, soundName)
                    };
                    break;
                }
                case FxElemType.Runner:
                    visual = new FxEffectVisual { EffectDef = FxReference(raw) };
                    break;
                default:
                {
                    Pointer? material = Material(raw, cell);
                    Add(dependencies, material);
                    visual = new FxMaterialVisual
                    {
                        Material = FxAsset<MaterialAsset>(material, "visual material")
                    };
                    break;
                }
            }
            return new FxElemDefVisuals { Visual = visual };
        }

        private FxEffectDefRef FxReference(uint raw)
        {
            string? name = ReadString(raw);
            return new FxEffectDefRef
            {
                Name = name is null ? null :
                    EffectRules.ReferenceName(_ownedNamePrefix, name)
            };
        }

        private FxElemExtendedDef? FxTrail(uint raw)
        {
            if (raw == 0) return null;
            Pointer? pointer = ReadArray(raw, 4, 0x1c, "fx-trail");
            if (pointer is null) throw Invalid("Fx trail is absent");
            int row = pointer.Region.FileOffset;
            int splitDistance = unchecked((int)U32(row + 8));
            var split = EffectRules.TrailSplit(splitDistance, ConvertFx);
            if (split.IsError) throw Invalid(split.ErrorValue);
            var (inverseDistance, inverseArcDistance, inverseTime) = split.ResultValue;
            int vertCount = Count(U32(row + 0xc));
            uint rawVerts = U32(row + 0x10);
            byte[] verts = rawVerts == 0 && !ConvertFx ? [] : FxBytes(rawVerts, 4,
                Size(vertCount, FxTrailDef.VertexSerializedSize), "trail vertices");
            var vertices = new FxTrailVertex[rawVerts == 0 ? 0 : vertCount];
            for (int index = 0; index < vertices.Length; index++)
            {
                int offset = index * FxTrailDef.VertexSerializedSize;
                vertices[index] = new FxTrailVertex(
                    FxFloat(verts, offset), FxFloat(verts, offset + 4),
                    FxFloat(verts, offset + 8), FxFloat(verts, offset + 12),
                    FxFloat(verts, offset + 16));
            }
            int indCount = Count(U32(row + 0x14));
            uint rawIndices = U32(row + 0x18);
            byte[] inds = rawIndices == 0 && !ConvertFx ? [] :
                FxBytes(rawIndices, 2, Size(indCount, 2), "trail indices");
            var indices = new ushort[rawIndices == 0 ? 0 : indCount];
            for (int index = 0; index < indices.Length; index++)
                indices[index] = BinaryPrimitives.ReadUInt16BigEndian(inds.AsSpan(index * 2));
            return new FxElemExtendedDef
            {
                Kind = FxElemExtendedDefKind.Trail,
                TrailDef = new FxTrailDef
                {
                    ScrollTimeMsec = unchecked((int)U32(row)),
                    RepeatDist = unchecked((int)U32(row + 4)),
                    InvSplitDist = inverseDistance,
                    InvSplitArcDist = inverseArcDistance,
                    InvSplitTime = inverseTime,
                    VertCount = vertCount,
                    Verts = vertices,
                    IndCount = indCount,
                    Inds = indices
                }
            };
        }

        private byte[] FxBytes(uint raw, int alignment, int size, string field)
        {
            Pointer? pointer = ReadArray(raw, alignment, size, "fx-" + field);
            if (pointer is null)
            {
                if (size != 0) throw Invalid($"Fx {field} are absent");
                return [];
            }
            if (pointer.Region.Length != size)
                throw Invalid($"Fx {field} has an unexpected byte count");
            return Bytes(pointer.Region).ToArray();
        }

        private static FxElemVelStateInFrame FxVelocityFrame(byte[] data, int row) => new(
            new FxElemVec3Range(FxVector(data, row), FxVector(data, row + 12)),
            new FxElemVec3Range(FxVector(data, row + 24), FxVector(data, row + 36)));

        private static FxElemVisualState FxVisualState(byte[] data, int row) => new(
            new FxElemColor(data[row], data[row + 1], data[row + 2], data[row + 3]),
            FxFloat(data, row + 4), FxFloat(data, row + 8), FxFloat(data, row + 12),
            FxFloat(data, row + 16), FxFloat(data, row + 20));

        private FxFloatRange FxRange(int row) => new(MapFloat(row), MapFloat(row + 4));
        private FxIntRange FxIntRangeAt(int row) => new(unchecked((int)U32(row)),
            unchecked((int)U32(row + 4)));
        private FxFloatRange[] FxRanges(int row, int count) =>
            Enumerable.Range(0, count).Select(index => FxRange(row + index * 8)).ToArray();
        private Bounds FxBounds(int row)
        {
            var minimum = new Vec3(MapFloat(row), MapFloat(row + 4), MapFloat(row + 8));
            var maximum = new Vec3(MapFloat(row + 12), MapFloat(row + 16), MapFloat(row + 20));
            var bounds = EffectRules.CollisionBounds(minimum, maximum, ConvertFx);
            return bounds.IsOk ? bounds.ResultValue : throw Invalid(bounds.ErrorValue);
        }

        private static Vec3 FxVector(byte[] data, int row) => new(
            FxFloat(data, row), FxFloat(data, row + 4), FxFloat(data, row + 8));
        private static float FxFloat(byte[] data, int row) =>
            BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(row, 4));

        private T? FxAsset<T>(Pointer? pointer, string field) where T : class
        {
            if (pointer is null) return null;
            if (!ConvertFx) return null;
            if (pointer.Value is T converted)
            {
                if (converted is MaterialAsset material) RetainFxMaterial(material);
                if (converted is XModelAsset model)
                    foreach (MaterialAsset? modelMaterial in model.Materials)
                        if (modelMaterial is not null) RetainFxMaterial(modelMaterial);
                return converted;
            }
            string name = _assets[pointer.Dependencies.Single()].Name;
            Iw3Ps3ShaderLibrary sources = _stockSources ??
                throw Invalid("Fx conversion has no stock source library");
            if (typeof(T) == typeof(MaterialAsset))
            {
                Iw3Ps3MaterialSource source = pointer.Value as Iw3Ps3MaterialSource ??
                    sources.ResolveMaterial(name);
                MaterialAsset material = (_materialCompiler ??
                    throw Invalid("Fx conversion has no material compiler")).Compile(source);
                RetainFxMaterial(material);
                return (T)(object)material;
            }
            if (typeof(T) == typeof(XModelAsset))
            {
                Iw3Ps3ModelSource source = pointer.Value as Iw3Ps3ModelSource ??
                    sources.ResolveModel(name);
                XModelAsset model = CompileModelSource(source, null);
                if (!_definitions.Contains(model)) _definitions.Add(model);
                foreach (MaterialAsset? modelMaterial in model.Materials)
                    if (modelMaterial is not null) RetainFxMaterial(modelMaterial);
                return (T)(object)model;
            }
            throw Invalid($"Fx {field} has no converted {typeof(T).Name}");
        }

        private void RetainFxMaterial(MaterialAsset material)
        {
            if (!_definitions.Contains(material)) _definitions.Add(material);
            if (material.TechniqueSet is { } set && !_definitions.Contains(set))
                _definitions.Add(set);
        }
    }
}
