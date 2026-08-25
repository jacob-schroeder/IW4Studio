using System.Globalization;
using System.Numerics;
using System.Text;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;

namespace IW4.Assets.D3dbsp;

internal sealed record D3dbspStaticModelEntity(
    string ModelName,
    Vec3 Origin,
    Vec3 Angles,
    IReadOnlyList<Vec3> Axis,
    IReadOnlyList<uint> PackedAxis,
    float Scale,
    int SpawnFlags,
    GfxStaticModelDrawInstFlags Flags,
    bool HasGroundLighting,
    GfxColor GroundLighting,
    byte PrimaryLightIndex);

internal sealed record D3dbspSyntheticBrushModel(
    Bounds Bounds,
    int FirstBrush,
    int BrushCount);

internal sealed record D3dbspTriggerCollisionExport(
    IReadOnlyList<ClipMaterial> Materials,
    IReadOnlyList<CPlane> Planes,
    IReadOnlyList<CBrushSide> BrushSides,
    IReadOnlyList<byte> BrushEdges,
    IReadOnlyList<CBrush> Brushes,
    IReadOnlyList<Bounds> BrushBounds,
    IReadOnlyList<uint> BrushContents,
    IReadOnlyList<D3dbspSyntheticBrushModel> SyntheticModels,
    IReadOnlyList<int> CollisionModelsByTrigger);

internal static class D3dbspMapEntsCodec
{
    private const float PackedAxisXyStep = 1.0f / 1023.0f;
    private const float PackedAxisZStep = 1.0f / 511.0f;
    private const float PackedAxisRoundTripEpsilon = 0.00001f;

    private static readonly string[] MultiplayerSpawnFallbackClassnames =
    [
        "mp_dm_spawn",
        "mp_tdm_spawn",
        "mp_tdm_spawn_allies_start",
        "mp_tdm_spawn_axis_start"
    ];

    private static readonly HashSet<string> PurgeableClassnames = new(
        [
            "misc_model",
            "misc_prefab",
            "dyn_brushmodel",
            "dyn_model",
            "reflection_probe",
            "info_null",
            "func_group",
            "glass"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static byte[] DecodeEntityString(ReadOnlySpan<byte> source)
    {
        IReadOnlyList<ParsedEntity> entities = ParseEntities(source);
        return DecodeEntityString(source, entities, null);
    }

    public static (byte[] EntityString, MapTriggers Trigger) DecodeMapTriggers(
        ReadOnlySpan<byte> source,
        IReadOnlyList<CModel> collisionModels,
        IReadOnlyList<CLeafBrushNode> leafBrushNodes,
        IReadOnlyList<CBrush> brushes,
        IReadOnlyList<Bounds> brushBounds,
        IReadOnlyList<uint> brushContents)
    {
        ArgumentNullException.ThrowIfNull(collisionModels);
        ArgumentNullException.ThrowIfNull(leafBrushNodes);
        ArgumentNullException.ThrowIfNull(brushes);
        ArgumentNullException.ThrowIfNull(brushBounds);
        ArgumentNullException.ThrowIfNull(brushContents);
        if (brushes.Count != brushBounds.Count || brushes.Count != brushContents.Count)
        {
            throw new InvalidDataException(
                "The collision brush, bounds, and contents tables must have equal row counts.");
        }

        IReadOnlyList<ParsedEntity> entities = ParseEntities(source);
        Dictionary<int, int> brushModelReferenceCounts = CountBrushModelReferences(entities);
        var replacements = new Dictionary<int, string>();
        var triggerModels = new List<TriggerModel>();
        var triggerHulls = new List<TriggerHull>();
        var triggerSlabs = new List<TriggerSlab>();
        var usedCollisionModels = new HashSet<int>();
        var triggerEntities = new List<(ParsedEntity Entity, int CollisionModelIndex)>();

        foreach (ParsedEntity entity in entities)
        {
            if (!entity.TryGetValue("model", out string modelReference))
                continue;
            if (modelReference.StartsWith('?'))
            {
                throw new NotSupportedException(
                    $"Entity model reference '{modelReference}' names a linked MapTriggers row; " +
                    "a d3dbsp collision-model source mapping is required to reconstruct it.");
            }
            if (!IsMapTriggerEntity(entity) ||
                !modelReference.StartsWith('*'))
            {
                continue;
            }

            int collisionModelIndex = ParseBrushModelReference(modelReference);
            if (!brushModelReferenceCounts.TryGetValue(collisionModelIndex, out int referenceCount) ||
                referenceCount != 1 ||
                !usedCollisionModels.Add(collisionModelIndex))
            {
                throw new InvalidDataException(
                    $"Brush model '*{collisionModelIndex}' does not have one unambiguous entity owner.");
            }
            triggerEntities.Add((entity, collisionModelIndex));
        }

        triggerEntities.Sort((left, right) =>
            left.CollisionModelIndex.CompareTo(right.CollisionModelIndex));
        foreach ((ParsedEntity entity, int collisionModelIndex) in triggerEntities)
        {
            if ((uint)collisionModelIndex >= (uint)collisionModels.Count)
            {
                throw new InvalidDataException(
                    $"Brush-backed trigger references collision model {collisionModelIndex}, but the " +
                    $"collision-model table has {collisionModels.Count} rows.");
            }

            CModel collisionModel = collisionModels[collisionModelIndex] ??
                throw new InvalidDataException(
                    $"Collision model {collisionModelIndex} is null.");
            CLeaf leaf = collisionModel.Leaf ??
                throw new InvalidDataException(
                    $"Collision model {collisionModelIndex} has no collision leaf.");
            if (leaf.CollAabbCount != 0 || leaf.TerrainContents != 0)
            {
                throw new NotSupportedException(
                    $"Brush-backed trigger collision model {collisionModelIndex} contains terrain/AABB " +
                    "collision that MapTriggers cannot represent.");
            }
            if (leaf.LeafBrushNode <= 0 || leaf.LeafBrushNode >= leafBrushNodes.Count)
            {
                throw new InvalidDataException(
                    $"Brush-backed trigger collision model {collisionModelIndex} has invalid leaf-brush " +
                    $"node {leaf.LeafBrushNode}.");
            }

            CLeafBrushNode leafBrushNode = leafBrushNodes[leaf.LeafBrushNode] ??
                throw new InvalidDataException(
                    $"Leaf-brush node {leaf.LeafBrushNode} is null.");
            IReadOnlyList<ushort> modelBrushes = leafBrushNode.Data?.Brushes ??
                throw new InvalidDataException(
                    $"Leaf-brush node {leaf.LeafBrushNode} has no brush list.");
            if (leafBrushNode.LeafBrushCount <= 0 ||
                leafBrushNode.LeafBrushCount != modelBrushes.Count)
            {
                throw new InvalidDataException(
                    $"Brush-backed trigger collision model {collisionModelIndex} has an invalid terminal " +
                    $"leaf-brush node count {leafBrushNode.LeafBrushCount}.");
            }
            if (triggerHulls.Count > ushort.MaxValue - modelBrushes.Count)
            {
                throw new InvalidDataException(
                    "The reconstructed MapTriggers hull table exceeds the IW4 ushort index range.");
            }

            int firstHull = triggerHulls.Count;
            uint combinedContents = 0;
            foreach (ushort brushIndex in modelBrushes)
            {
                if (brushIndex >= brushes.Count)
                {
                    throw new InvalidDataException(
                        $"Brush-backed trigger collision model {collisionModelIndex} references brush " +
                        $"{brushIndex}, but the brush table has {brushes.Count} rows.");
                }

                CBrush brush = brushes[brushIndex] ??
                    throw new InvalidDataException($"Collision brush {brushIndex} is null.");
                Bounds bounds = brushBounds[brushIndex] ??
                    throw new InvalidDataException($"Collision brush {brushIndex} has null bounds.");
                ValidateTriggerBounds(bounds, $"Collision brush {brushIndex}");
                if (brush.NumSides != brush.Sides.Count)
                {
                    throw new InvalidDataException(
                        $"Collision brush {brushIndex} declares {brush.NumSides} non-axial sides but " +
                        $"retains {brush.Sides.Count} rows.");
                }
                IReadOnlyList<TriggerSlab> decodedSlabs = DecodeTriggerSlabs(
                    brush.Sides,
                    bounds,
                    brushIndex);
                if (triggerSlabs.Count > ushort.MaxValue - decodedSlabs.Count)
                {
                    throw new InvalidDataException(
                        "The reconstructed MapTriggers slab table exceeds the IW4 ushort index range.");
                }

                int firstSlab = triggerSlabs.Count;
                triggerSlabs.AddRange(decodedSlabs);

                uint contents = brushContents[brushIndex];
                combinedContents |= contents;
                triggerHulls.Add(new TriggerHull
                {
                    Bounds = new Bounds
                    {
                        MidPoint = bounds.MidPoint,
                        HalfSize = bounds.HalfSize
                    },
                    Contents = unchecked((int)contents),
                    SlabCount = checked((ushort)decodedSlabs.Count),
                    FirstSlab = checked((ushort)firstSlab)
                });
            }

            if (unchecked((uint)leaf.BrushContents) != combinedContents ||
                unchecked((uint)leafBrushNode.Contents) != combinedContents)
            {
                throw new InvalidDataException(
                    $"Brush-backed trigger collision model {collisionModelIndex} has inconsistent " +
                    "aggregate brush contents.");
            }

            int triggerModelIndex = triggerModels.Count;
            if (IsRuntimeStage(entity))
            {
                ushort scriptIndex = ParseUInt16(
                    entity.GetRequiredValue("script_index"),
                    "stage script_index");
                if (scriptIndex != triggerModelIndex)
                {
                    throw new InvalidDataException(
                        $"Stage script_index {scriptIndex} does not match reconstructed MapTriggers " +
                        $"model {triggerModelIndex}.");
                }
            }

            triggerModels.Add(new TriggerModel
            {
                Contents = unchecked((int)combinedContents),
                HullCount = checked((ushort)modelBrushes.Count),
                FirstHull = checked((ushort)firstHull)
            });
            replacements.Add(entity.Begin, $"?{triggerModelIndex.ToString(CultureInfo.InvariantCulture)}");
        }

        return (
            DecodeEntityString(source, entities, replacements),
            new MapTriggers
            {
                Count = checked((uint)triggerModels.Count),
                Models = triggerModels.AsReadOnly(),
                HullCount = checked((uint)triggerHulls.Count),
                Hulls = triggerHulls.AsReadOnly(),
                SlabCount = checked((uint)triggerSlabs.Count),
                Slabs = triggerSlabs.AsReadOnly()
            });
    }

    private static byte[] DecodeEntityString(
        ReadOnlySpan<byte> source,
        IReadOnlyList<ParsedEntity> entities,
        IReadOnlyDictionary<int, string>? modelReplacements)
    {
        using var output = new MemoryStream(source.Length);
        foreach (ParsedEntity entity in entities)
        {
            if (CanPurge(entity))
                continue;

            if (modelReplacements is not null &&
                modelReplacements.TryGetValue(entity.Begin, out string? replacement))
            {
                if (!entity.TryGetValueSpan("model", out int valueBegin, out int valueEnd))
                {
                    throw new InvalidDataException(
                        "A brush-backed trigger entity lost its model-token source span.");
                }

                output.Write(source[entity.Begin..valueBegin]);
                WriteLatin1(output, $"\"{replacement}\"");
                output.Write(source[valueEnd..entity.End]);
            }
            else
            {
                output.Write(source[entity.Begin..entity.End]);
            }
        }

        AppendMultiplayerSpawnFallbacks(output, entities);
        output.WriteByte(0);
        return output.ToArray();
    }

    private static Dictionary<int, int> CountBrushModelReferences(
        IReadOnlyList<ParsedEntity> entities)
    {
        var counts = new Dictionary<int, int>();
        foreach (ParsedEntity entity in entities)
        {
            if (!entity.TryGetValue("model", out string modelReference) ||
                !modelReference.StartsWith('*'))
            {
                continue;
            }

            int index = ParseBrushModelReference(modelReference);
            counts[index] = counts.TryGetValue(index, out int count)
                ? checked(count + 1)
                : 1;
        }
        return counts;
    }

    private static int ParseBrushModelReference(string value)
    {
        if (value.Length < 2 || value[0] != '*' ||
            !int.TryParse(
                value.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int index) ||
            index <= 0)
        {
            throw new InvalidDataException(
                $"Brush model reference '{value}' must contain a positive decimal collision-model index.");
        }
        return index;
    }

    private static int ParseMapTriggerReference(string value)
    {
        if (value.Length < 2 || value[0] != '?' ||
            !int.TryParse(
                value.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int index) ||
            index < 0)
        {
            throw new InvalidDataException(
                $"MapTriggers model reference '{value}' must contain a non-negative decimal row index.");
        }
        return index;
    }

    public static D3dbspTriggerCollisionExport CreateTriggerCollisionExport(
        MapEntsAsset mapEnts,
        ClipMapAsset clipMap)
    {
        ArgumentNullException.ThrowIfNull(mapEnts);
        ArgumentNullException.ThrowIfNull(clipMap);
        var (_, entities) = ReadRetainedEntityPayload(mapEnts);
        var occupiedCollisionModels = new HashSet<int>();
        foreach (ParsedEntity entity in entities)
        {
            if (entity.TryGetValue("model", out string modelReference) &&
                modelReference.StartsWith('*'))
            {
                occupiedCollisionModels.Add(ParseBrushModelReference(modelReference));
            }
        }

        return CreateTriggerCollisionExport(
            mapEnts.Trigger,
            clipMap,
            occupiedCollisionModels);
    }

    private static D3dbspTriggerCollisionExport CreateTriggerCollisionExport(
        MapTriggers trigger,
        ClipMapAsset clipMap,
        IReadOnlySet<int> occupiedCollisionModels)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(clipMap);
        ArgumentNullException.ThrowIfNull(occupiedCollisionModels);
        if (trigger.Count != trigger.Models.Count ||
            trigger.HullCount != trigger.Hulls.Count ||
            trigger.SlabCount != trigger.Slabs.Count)
        {
            throw new InvalidDataException(
                "MapTriggers counts must equal their semantic table counts before d3dbsp export.");
        }
        if (clipMap.PlaneCount != clipMap.Planes.Count ||
            clipMap.NumMaterials != clipMap.Materials.Count ||
            clipMap.NumBrushSides != clipMap.BrushSides.Count ||
            clipMap.NumBrushEdges != clipMap.BrushEdges.Count ||
            clipMap.NumSubModels < 0 || clipMap.NumSubModels != clipMap.CModels.Count ||
            clipMap.NumBrushes != clipMap.Brushes.Count ||
            clipMap.Brushes.Count != clipMap.BrushBounds.Count ||
            clipMap.Brushes.Count != clipMap.BrushContents.Count ||
            clipMap.LeafBrushNodesCount < 0 ||
            clipMap.LeafBrushNodesCount != clipMap.LeafBrushNodes.Count)
        {
            throw new InvalidDataException(
                "ClipMap collision model, brush, or leaf-brush-node counts are inconsistent.");
        }

        ValidateTriggerTableRanges(trigger);
        var materials = new List<ClipMaterial>(clipMap.Materials);
        var planes = new List<CPlane>(clipMap.Planes);
        var brushSides = new List<CBrushSide>(clipMap.BrushSides);
        var brushes = new List<CBrush>(clipMap.Brushes);
        var brushBounds = new List<Bounds>(clipMap.BrushBounds);
        var brushContents = new List<uint>(clipMap.BrushContents);
        var syntheticModels = new List<D3dbspSyntheticBrushModel>();
        var collisionModelsByTrigger = new int[trigger.Models.Count];
        var usedCollisionModels = new bool[clipMap.CModels.Count];
        foreach (int collisionModelIndex in occupiedCollisionModels)
        {
            if (collisionModelIndex <= 0 || collisionModelIndex >= usedCollisionModels.Length)
            {
                throw new InvalidDataException(
                    $"Retained entity collision-model reference '*{collisionModelIndex}' exceeds " +
                    $"the ClipMap collision-model count {clipMap.CModels.Count}.");
            }
            usedCollisionModels[collisionModelIndex] = true;
        }
        for (int triggerModelIndex = 0; triggerModelIndex < trigger.Models.Count; triggerModelIndex++)
        {
            int match = -1;
            bool hasMultipleMatches = false;
            for (int collisionModelIndex = 1;
                 collisionModelIndex < clipMap.CModels.Count;
                 collisionModelIndex++)
            {
                if (usedCollisionModels[collisionModelIndex] ||
                    !CollisionModelMatchesTrigger(
                        clipMap,
                        collisionModelIndex,
                        trigger,
                        triggerModelIndex))
                {
                    continue;
                }

                if (match >= 0)
                {
                    hasMultipleMatches = true;
                    break;
                }
                match = collisionModelIndex;
            }

            if (match >= 0 && !hasMultipleMatches)
            {
                collisionModelsByTrigger[triggerModelIndex] = match;
                usedCollisionModels[match] = true;
                continue;
            }

            TriggerModel model = trigger.Models[triggerModelIndex];
            if (model.HullCount == 0)
            {
                throw new InvalidDataException(
                    $"MapTriggers model {triggerModelIndex} has no hulls and cannot be represented " +
                    "as a d3dbsp brush model.");
            }
            int finalBrushCount = checked(brushes.Count + model.HullCount);
            if (finalBrushCount > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Synthesizing MapTriggers model {triggerModelIndex} would produce " +
                    $"{finalBrushCount} collision brushes, exceeding the IW4 ushort range.");
            }

            int firstBrush = brushes.Count;
            uint combinedContents = 0;
            for (int hullOffset = 0; hullOffset < model.HullCount; hullOffset++)
            {
                int hullIndex = model.FirstHull + hullOffset;
                TriggerHull hull = trigger.Hulls[hullIndex];
                ValidateTriggerBounds(hull.Bounds, $"MapTriggers hull {hullIndex}");
                int nonAxialSideCount = checked(hull.SlabCount * 2);
                if (6 + nonAxialSideCount > short.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MapTriggers hull {hullIndex} has too many sides for the v22 short field.");
                }

                int materialIndex = FindOrAppendTriggerMaterial(materials, hull.Contents);
                short axialMaterial = checked((short)materialIndex);
                var sides = new CBrushSide[nonAxialSideCount];
                for (int slabOffset = 0; slabOffset < hull.SlabCount; slabOffset++)
                {
                    int slabIndex = hull.FirstSlab + slabOffset;
                    (CPlane upperPlane, CPlane lowerPlane) = CreateTriggerPlanes(
                        trigger.Slabs[slabIndex],
                        slabIndex);
                    planes.Add(upperPlane);
                    planes.Add(lowerPlane);
                    var upperSide = new CBrushSide
                    {
                        Plane = upperPlane,
                        MaterialNum = checked((ushort)materialIndex),
                        FirstAdjacentSideOffset = 0,
                        EdgeCount = 0
                    };
                    var lowerSide = new CBrushSide
                    {
                        Plane = lowerPlane,
                        MaterialNum = checked((ushort)materialIndex),
                        FirstAdjacentSideOffset = 0,
                        EdgeCount = 0
                    };
                    int firstSideIndex = slabOffset * 2;
                    sides[firstSideIndex] = upperSide;
                    sides[firstSideIndex + 1] = lowerSide;
                    brushSides.Add(upperSide);
                    brushSides.Add(lowerSide);
                }

                brushes.Add(new CBrush
                {
                    NumSides = checked((ushort)nonAxialSideCount),
                    GlassPieceIndex = 0,
                    Sides = Array.AsReadOnly(sides),
                    BaseAdjacentSide = Array.Empty<byte>(),
                    AxialMaterialNum = new[]
                    {
                        axialMaterial,
                        axialMaterial,
                        axialMaterial,
                        axialMaterial,
                        axialMaterial,
                        axialMaterial
                    },
                    FirstAdjacentSideOffsets = new byte[6],
                    EdgeCount = new byte[6]
                });
                brushBounds.Add(CopyBounds(hull.Bounds));
                uint contents = unchecked((uint)hull.Contents);
                brushContents.Add(contents);
                combinedContents |= contents;
            }
            if (unchecked((uint)model.Contents) != combinedContents)
            {
                throw new InvalidDataException(
                    $"MapTriggers model {triggerModelIndex} contents 0x{model.Contents:X8} do not " +
                    $"equal the union of its hull contents 0x{combinedContents:X8}.");
            }

            int syntheticModelIndex = checked(clipMap.CModels.Count + syntheticModels.Count);
            syntheticModels.Add(new D3dbspSyntheticBrushModel(
                UnionTriggerHullBounds(trigger, model, triggerModelIndex),
                firstBrush,
                model.HullCount));
            collisionModelsByTrigger[triggerModelIndex] = syntheticModelIndex;
        }

        return new D3dbspTriggerCollisionExport(
            materials.AsReadOnly(),
            planes.AsReadOnly(),
            brushSides.AsReadOnly(),
            clipMap.BrushEdges,
            brushes.AsReadOnly(),
            brushBounds.AsReadOnly(),
            brushContents.AsReadOnly(),
            syntheticModels.AsReadOnly(),
            Array.AsReadOnly(collisionModelsByTrigger));
    }

    private static void ValidateTriggerTableRanges(MapTriggers trigger)
    {
        for (int modelIndex = 0; modelIndex < trigger.Models.Count; modelIndex++)
        {
            TriggerModel model = trigger.Models[modelIndex] ??
                throw new InvalidDataException($"MapTriggers model {modelIndex} is null.");
            if ((uint)model.FirstHull + model.HullCount > trigger.Hulls.Count)
            {
                throw new InvalidDataException(
                    $"MapTriggers model {modelIndex} hull range exceeds the hull table.");
            }
        }
        for (int hullIndex = 0; hullIndex < trigger.Hulls.Count; hullIndex++)
        {
            TriggerHull hull = trigger.Hulls[hullIndex] ??
                throw new InvalidDataException($"MapTriggers hull {hullIndex} is null.");
            if (hull.Bounds is null)
                throw new InvalidDataException($"MapTriggers hull {hullIndex} has null bounds.");
            if ((uint)hull.FirstSlab + hull.SlabCount > trigger.Slabs.Count)
            {
                throw new InvalidDataException(
                    $"MapTriggers hull {hullIndex} slab range exceeds the slab table.");
            }
        }
        for (int slabIndex = 0; slabIndex < trigger.Slabs.Count; slabIndex++)
        {
            if (trigger.Slabs[slabIndex] is null)
                throw new InvalidDataException($"MapTriggers slab {slabIndex} is null.");
        }
    }

    private static int FindOrAppendTriggerMaterial(
        List<ClipMaterial> materials,
        int contents)
    {
        for (int index = 0; index < materials.Count; index++)
        {
            ClipMaterial material = materials[index] ??
                throw new InvalidDataException($"Collision material row {index} is null.");
            if (material.Contents != contents)
                continue;
            if (index > short.MaxValue)
            {
                throw new InvalidDataException(
                    $"Trigger collision material selector {index} exceeds the v22 short range.");
            }
            return index;
        }

        if (materials.Count > short.MaxValue)
        {
            throw new InvalidDataException(
                "The synthetic trigger collision material exceeds the v22 short index range.");
        }
        int materialIndex = materials.Count;
        materials.Add(new ClipMaterial
        {
            Name = "trigger",
            SurfaceFlags = 0x00040080,
            Contents = contents
        });
        return materialIndex;
    }

    private static Bounds UnionTriggerHullBounds(
        MapTriggers trigger,
        TriggerModel model,
        int triggerModelIndex)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;
        for (int hullOffset = 0; hullOffset < model.HullCount; hullOffset++)
        {
            int hullIndex = model.FirstHull + hullOffset;
            Bounds bounds = trigger.Hulls[hullIndex].Bounds;
            ValidateTriggerBounds(bounds, $"MapTriggers hull {hullIndex}");
            minX = System.Math.Min(minX, (double)bounds.MidPoint.X - bounds.HalfSize.X);
            minY = System.Math.Min(minY, (double)bounds.MidPoint.Y - bounds.HalfSize.Y);
            minZ = System.Math.Min(minZ, (double)bounds.MidPoint.Z - bounds.HalfSize.Z);
            maxX = System.Math.Max(maxX, (double)bounds.MidPoint.X + bounds.HalfSize.X);
            maxY = System.Math.Max(maxY, (double)bounds.MidPoint.Y + bounds.HalfSize.Y);
            maxZ = System.Math.Max(maxZ, (double)bounds.MidPoint.Z + bounds.HalfSize.Z);
        }

        return new Bounds
        {
            MidPoint = new Vec3
            {
                X = ToFiniteSingle((minX + maxX) * 0.5, $"MapTriggers model {triggerModelIndex} midpoint X"),
                Y = ToFiniteSingle((minY + maxY) * 0.5, $"MapTriggers model {triggerModelIndex} midpoint Y"),
                Z = ToFiniteSingle((minZ + maxZ) * 0.5, $"MapTriggers model {triggerModelIndex} midpoint Z")
            },
            HalfSize = new Vec3
            {
                X = ToFiniteSingle((maxX - minX) * 0.5, $"MapTriggers model {triggerModelIndex} half-size X"),
                Y = ToFiniteSingle((maxY - minY) * 0.5, $"MapTriggers model {triggerModelIndex} half-size Y"),
                Z = ToFiniteSingle((maxZ - minZ) * 0.5, $"MapTriggers model {triggerModelIndex} half-size Z")
            }
        };
    }

    private static Bounds CopyBounds(Bounds source) => new()
    {
        MidPoint = source.MidPoint,
        HalfSize = source.HalfSize
    };

    private static float ToFiniteSingle(double value, string description)
    {
        float result = (float)value;
        if (!float.IsFinite(result))
            throw new InvalidDataException($"The {description} is not finite.");
        return result;
    }

    private static bool CollisionModelMatchesTrigger(
        ClipMapAsset clipMap,
        int collisionModelIndex,
        MapTriggers trigger,
        int triggerModelIndex)
    {
        CModel? collisionModel = clipMap.CModels[collisionModelIndex];
        if (collisionModel is null ||
            collisionModel.Leaf is not { } leaf ||
            leaf.CollAabbCount != 0 ||
            leaf.TerrainContents != 0)
        {
            return false;
        }

        IReadOnlyList<ushort> modelBrushes =
            D3dbspCollisionCodec.GetTerminalBrushesForEncoding(
                clipMap,
                leaf,
                $"Collision model row {collisionModelIndex}");

        TriggerModel model = trigger.Models[triggerModelIndex];
        if (model.HullCount != modelBrushes.Count)
            return false;

        uint combinedContents = 0;
        for (int brushOffset = 0; brushOffset < modelBrushes.Count; brushOffset++)
        {
            int brushIndex = modelBrushes[brushOffset];
            if ((uint)brushIndex >= (uint)clipMap.Brushes.Count)
                return false;

            CBrush? brush = clipMap.Brushes[brushIndex];
            Bounds? bounds = clipMap.BrushBounds[brushIndex];
            TriggerHull hull = trigger.Hulls[model.FirstHull + brushOffset];
            uint contents = clipMap.BrushContents[brushIndex];
            if (brush is null || bounds is null || brush.NumSides != brush.Sides.Count ||
                hull.Contents != unchecked((int)contents) ||
                !SameBounds(bounds, hull.Bounds))
            {
                return false;
            }

            combinedContents |= contents;
            IReadOnlyList<TriggerSlab> decodedSlabs;
            try
            {
                decodedSlabs = DecodeTriggerSlabs(
                    brush.Sides,
                    bounds,
                    brushIndex);
            }
            catch (InvalidDataException)
            {
                return false;
            }
            if (hull.SlabCount != decodedSlabs.Count)
                return false;
            for (int slabOffset = 0; slabOffset < decodedSlabs.Count; slabOffset++)
            {
                TriggerSlab expected = decodedSlabs[slabOffset];
                TriggerSlab actual = trigger.Slabs[hull.FirstSlab + slabOffset];
                if (!SameVec3(expected.Dir, actual.Dir) ||
                    !NearlySameTriggerFloat(expected.MidPoint, actual.MidPoint) ||
                    !NearlySameTriggerFloat(expected.HalfSize, actual.HalfSize))
                {
                    return false;
                }
            }
        }

        return model.Contents == unchecked((int)combinedContents) &&
            leaf.BrushContents == unchecked((int)combinedContents);
    }

    private static bool SameBounds(Bounds left, Bounds right) =>
        SameVec3(left.MidPoint, right.MidPoint) &&
        SameVec3(left.HalfSize, right.HalfSize);

    private static bool SameVec3(Vec3 left, Vec3 right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    private static bool NearlySameTriggerFloat(float left, float right)
    {
        if (!float.IsFinite(left) || !float.IsFinite(right))
            return false;
        float scale = MathF.Max(1.0f, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
        return MathF.Abs(left - right) <= scale * 0.000001f;
    }

    private static bool IsMapTriggerEntity(ParsedEntity entity)
    {
        if (!entity.TryGetValue("classname", out string classname))
            return false;
        return classname.StartsWith("trigger_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(classname, "info_volume", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(classname, "stage", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateTriggerBounds(Bounds bounds, string description)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        RequireFinite(bounds.MidPoint.X, $"{description} midpoint X");
        RequireFinite(bounds.MidPoint.Y, $"{description} midpoint Y");
        RequireFinite(bounds.MidPoint.Z, $"{description} midpoint Z");
        float halfX = RequireFinite(bounds.HalfSize.X, $"{description} half-size X");
        float halfY = RequireFinite(bounds.HalfSize.Y, $"{description} half-size Y");
        float halfZ = RequireFinite(bounds.HalfSize.Z, $"{description} half-size Z");
        if (halfX < 0.0f || halfY < 0.0f || halfZ < 0.0f)
            throw new InvalidDataException($"{description} has negative bounds half-size.");
    }

    private static (CPlane Upper, CPlane Lower) CreateTriggerPlanes(
        TriggerSlab slab,
        int slabIndex)
    {
        float normalX = RequireFinite(slab.Dir.X, $"MapTriggers slab {slabIndex} direction X");
        float normalY = RequireFinite(slab.Dir.Y, $"MapTriggers slab {slabIndex} direction Y");
        float normalZ = RequireFinite(slab.Dir.Z, $"MapTriggers slab {slabIndex} direction Z");
        float midpoint = RequireFinite(slab.MidPoint, $"MapTriggers slab {slabIndex} midpoint");
        float halfSize = RequireFinite(slab.HalfSize, $"MapTriggers slab {slabIndex} half-size");
        if (halfSize < 0.0f)
            throw new InvalidDataException($"MapTriggers slab {slabIndex} has a negative half-size.");

        float normalLengthSquared =
            normalX * normalX + normalY * normalY + normalZ * normalZ;
        if (!float.IsFinite(normalLengthSquared) || normalLengthSquared < 0.999f ||
            normalLengthSquared > 1.001f)
        {
            throw new InvalidDataException(
                $"MapTriggers slab {slabIndex} direction is not unit length.");
        }

        float lower = midpoint - halfSize;
        float upper = midpoint + halfSize;
        if (!float.IsFinite(lower) || !float.IsFinite(upper))
            throw new InvalidDataException($"MapTriggers slab {slabIndex} plane interval is not finite.");

        return (
            CreateTriggerPlane(slab.Dir, upper),
            CreateTriggerPlane(
                new Vec3 { X = -normalX, Y = -normalY, Z = -normalZ },
                -lower));
    }

    private static CPlane CreateTriggerPlane(Vec3 normal, float distance) => new()
    {
        Normal = normal,
        Dist = distance,
        Type = normal.X == 1.0f
            ? (byte)0
            : normal.Y == 1.0f
                ? (byte)1
                : normal.Z == 1.0f
                    ? (byte)2
                    : (byte)3,
        SignBits = 0,
        Pad12 = new byte[2]
    };

    private static IReadOnlyList<TriggerSlab> DecodeTriggerSlabs(
        IReadOnlyList<CBrushSide> sides,
        Bounds bounds,
        int brushIndex)
    {
        ArgumentNullException.ThrowIfNull(sides);
        ValidateTriggerBounds(bounds, $"Collision brush {brushIndex}");

        var slabs = new List<TriggerSlab>(sides.Count);
        var usedSides = new bool[sides.Count];
        for (int sideIndex = 0; sideIndex < sides.Count; sideIndex++)
        {
            if (usedSides[sideIndex])
                continue;

            CPlane upperPlane = GetValidatedTriggerPlane(sides, brushIndex, sideIndex);
            int oppositeSideIndex = -1;
            for (int candidateIndex = 0; candidateIndex < sides.Count; candidateIndex++)
            {
                if (candidateIndex == sideIndex || usedSides[candidateIndex])
                    continue;
                CPlane candidate = GetValidatedTriggerPlane(sides, brushIndex, candidateIndex);
                if (!NearlySameTriggerFloat(upperPlane.Normal.X, -candidate.Normal.X) ||
                    !NearlySameTriggerFloat(upperPlane.Normal.Y, -candidate.Normal.Y) ||
                    !NearlySameTriggerFloat(upperPlane.Normal.Z, -candidate.Normal.Z))
                {
                    continue;
                }
                if (oppositeSideIndex >= 0)
                {
                    throw new InvalidDataException(
                        $"Collision brush {brushIndex} side {sideIndex} has more than one " +
                        "opposite non-axial plane.");
                }
                oppositeSideIndex = candidateIndex;
            }
            if (oppositeSideIndex < 0)
            {
                slabs.Add(DecodeSingleSidedTriggerSlab(
                    upperPlane,
                    bounds,
                    brushIndex,
                    sideIndex));
                usedSides[sideIndex] = true;
                continue;
            }

            CPlane lowerPlane = GetValidatedTriggerPlane(
                sides,
                brushIndex,
                oppositeSideIndex);
            float upper = upperPlane.Dist;
            float lower = -lowerPlane.Dist;
            float midpoint = (lower + upper) * 0.5f;
            float halfSize = (upper - lower) * 0.5f;
            if (!float.IsFinite(midpoint) || !float.IsFinite(halfSize) ||
                (halfSize < 0.0f && !NearlySameTriggerFloat(lower, upper)))
            {
                throw new InvalidDataException(
                    $"Collision brush {brushIndex} sides {sideIndex} and {oppositeSideIndex} " +
                    "produce an invalid trigger slab interval.");
            }
            if (halfSize < 0.0f)
                halfSize = 0.0f;

            usedSides[sideIndex] = true;
            usedSides[oppositeSideIndex] = true;
            slabs.Add(new TriggerSlab
            {
                Dir = upperPlane.Normal,
                MidPoint = midpoint,
                HalfSize = halfSize
            });
        }

        return slabs.AsReadOnly();
    }

    private static TriggerSlab DecodeSingleSidedTriggerSlab(
        CPlane upperPlane,
        Bounds bounds,
        int brushIndex,
        int sideIndex)
    {
        Vec3 normal = upperPlane.Normal;
        float lower =
            normal.X * bounds.MidPoint.X +
            normal.Y * bounds.MidPoint.Y +
            normal.Z * bounds.MidPoint.Z -
            MathF.Abs(normal.X) * bounds.HalfSize.X -
            MathF.Abs(normal.Y) * bounds.HalfSize.Y -
            MathF.Abs(normal.Z) * bounds.HalfSize.Z;
        float upper = upperPlane.Dist;
        float midpoint = (lower + upper) * 0.5f;
        float halfSize = (upper - lower) * 0.5f;
        if (!float.IsFinite(lower) || !float.IsFinite(midpoint) ||
            !float.IsFinite(halfSize) ||
            (halfSize < 0.0f && !NearlySameTriggerFloat(lower, upper)))
        {
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} and its axial bounds " +
                "produce an invalid trigger slab interval.");
        }
        if (halfSize < 0.0f)
            halfSize = 0.0f;

        return new TriggerSlab
        {
            Dir = normal,
            MidPoint = midpoint,
            HalfSize = halfSize
        };
    }

    private static CPlane GetValidatedTriggerPlane(
        IReadOnlyList<CBrushSide> sides,
        int brushIndex,
        int sideIndex)
    {
        CBrushSide side = sides[sideIndex] ??
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} is null.");
        CPlane plane = side.Plane ??
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} has no plane.");
        float normalX = RequireFinite(
            plane.Normal.X,
            $"collision brush {brushIndex} side {sideIndex} plane normal X");
        float normalY = RequireFinite(
            plane.Normal.Y,
            $"collision brush {brushIndex} side {sideIndex} plane normal Y");
        float normalZ = RequireFinite(
            plane.Normal.Z,
            $"collision brush {brushIndex} side {sideIndex} plane normal Z");
        RequireFinite(
            plane.Dist,
            $"collision brush {brushIndex} side {sideIndex} plane distance");
        float normalLengthSquared =
            normalX * normalX + normalY * normalY + normalZ * normalZ;
        if (!float.IsFinite(normalLengthSquared) || normalLengthSquared < 0.999f ||
            normalLengthSquared > 1.001f)
        {
            throw new InvalidDataException(
                $"Collision brush {brushIndex} side {sideIndex} plane normal is not unit length.");
        }
        return plane;
    }

    public static byte[] EncodeEntityString(
        MapEntsAsset mapEnts,
        ClipMapAsset clipMap,
        GfxWorldAsset gfxWorld,
        IReadOnlyList<int> collisionModelsByTrigger)
    {
        ArgumentNullException.ThrowIfNull(mapEnts);
        ArgumentNullException.ThrowIfNull(clipMap);
        ArgumentNullException.ThrowIfNull(gfxWorld);
        ArgumentNullException.ThrowIfNull(collisionModelsByTrigger);
        if (collisionModelsByTrigger.Count != mapEnts.Trigger.Models.Count)
        {
            throw new InvalidDataException(
                $"The trigger collision-model map has {collisionModelsByTrigger.Count} rows; " +
                $"MapTriggers has {mapEnts.Trigger.Models.Count} models.");
        }
        if (collisionModelsByTrigger.Any(index => index <= 0) ||
            collisionModelsByTrigger.Distinct().Count() != collisionModelsByTrigger.Count)
        {
            throw new InvalidDataException(
                "Each MapTriggers model must resolve to one distinct positive d3dbsp collision-model index.");
        }

        var (retained, retainedEntities) = ReadRetainedEntityPayload(mapEnts);
        var emittedTriggerOwners = new bool[collisionModelsByTrigger.Count];
        var triggerReferenceReplacements = new Dictionary<int, string>();
        foreach (ParsedEntity entity in retainedEntities)
        {
            if (!entity.TryGetValue("model", out string modelReference) ||
                !modelReference.StartsWith('?'))
            {
                continue;
            }

            int triggerModelIndex = ParseMapTriggerReference(modelReference);
            if ((uint)triggerModelIndex >= (uint)collisionModelsByTrigger.Count)
            {
                throw new InvalidDataException(
                    $"Entity model reference '?{triggerModelIndex}' exceeds the MapTriggers model " +
                    $"count {collisionModelsByTrigger.Count}.");
            }
            if (!IsMapTriggerEntity(entity))
            {
                throw new InvalidDataException(
                    $"Entity model reference '?{triggerModelIndex}' is not owned by a trigger, " +
                    "info_volume, or stage entity.");
            }
            if (!CanPurge(entity))
            {
                if (emittedTriggerOwners[triggerModelIndex])
                {
                    throw new InvalidDataException(
                        $"MapTriggers model {triggerModelIndex} has more than one retained entity owner.");
                }
                emittedTriggerOwners[triggerModelIndex] = true;
            }
            triggerReferenceReplacements.Add(
                entity.Begin,
                $"*{collisionModelsByTrigger[triggerModelIndex].ToString(CultureInfo.InvariantCulture)}");
        }

        byte[] filtered = DecodeEntityString(
            retained,
            retainedEntities,
            triggerReferenceReplacements);
        IReadOnlyList<D3dbspStaticModelEntity> staticModels =
            ReconstructStaticModelEntities(clipMap, gfxWorld);
        ValidateStages(mapEnts);
        var stageCollisionModels = new int?[mapEnts.Stages.Count];
        for (int stageIndex = 0; stageIndex < mapEnts.Stages.Count; stageIndex++)
        {
            Stage stage = mapEnts.Stages[stageIndex];
            if (stage.TriggerIndex < collisionModelsByTrigger.Count)
            {
                if (!emittedTriggerOwners[stage.TriggerIndex])
                {
                    stageCollisionModels[stageIndex] =
                        collisionModelsByTrigger[stage.TriggerIndex];
                    emittedTriggerOwners[stage.TriggerIndex] = true;
                }
            }
            else if (stage.TriggerIndex != 1024)
            {
                throw new InvalidDataException(
                    $"Stage trigger index {stage.TriggerIndex} exceeds the reconstructed " +
                    $"trigger-model count {collisionModelsByTrigger.Count}.");
            }
        }
        for (int triggerModelIndex = 0;
             triggerModelIndex < emittedTriggerOwners.Length;
             triggerModelIndex++)
        {
            if (!emittedTriggerOwners[triggerModelIndex])
            {
                throw new InvalidDataException(
                    $"MapTriggers model {triggerModelIndex} has no emitted d3dbsp entity owner.");
            }
        }

        using var output = new MemoryStream(
            checked(filtered.Length + staticModels.Count * 192 + mapEnts.Stages.Count * 160));
        output.Write(filtered.AsSpan(0, filtered.Length - 1));
        foreach (D3dbspStaticModelEntity staticModel in staticModels)
            WriteStaticModel(output, staticModel);
        for (int stageIndex = 0; stageIndex < mapEnts.Stages.Count; stageIndex++)
            WriteStage(output, mapEnts.Stages[stageIndex], stageCollisionModels[stageIndex]);
        output.WriteByte(0);
        return output.ToArray();
    }

    private static (byte[] Retained, IReadOnlyList<ParsedEntity> Entities)
        ReadRetainedEntityPayload(MapEntsAsset mapEnts)
    {
        byte[] retained = mapEnts.EntityStringBytes.ToArray();
        if (mapEnts.NumEntityChars != retained.Length)
        {
            throw new InvalidDataException(
                $"MapEnts declares {mapEnts.NumEntityChars} entity bytes but retains {retained.Length}.");
        }
        if (retained.Length == 0 || retained[^1] != 0 ||
            retained.AsSpan(0, retained.Length - 1).Contains((byte)0))
        {
            throw new InvalidDataException(
                "The retained MapEnts payload must contain exactly one terminating NUL byte.");
        }

        return (retained, ParseEntities(retained));
    }

    public static IReadOnlyList<D3dbspStaticModelEntity> DecodeStaticModels(
        ReadOnlySpan<byte> source,
        int defaultSunPrimaryLightIndex)
    {
        if ((uint)defaultSunPrimaryLightIndex > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSunPrimaryLightIndex),
                defaultSunPrimaryLightIndex,
                "The default static-model sun-primary-light index must fit in one byte.");
        }

        IReadOnlyList<ParsedEntity> entities = ParseEntities(source);
        var staticModels = new List<D3dbspStaticModelEntity>();
        foreach (ParsedEntity entity in entities)
        {
            if (!entity.HasClassname("misc_model"))
                continue;

            string modelName = NormalizeModelName(entity.GetRequiredValue("model"));
            Vec3 origin = ParseVec3(entity.GetRequiredValue("origin"), "misc_model origin");
            Vec3 collisionAngles = entity.TryGetValue("angles", out string authoredAngles)
                ? ParseVec3(authoredAngles, "misc_model angles")
                : new Vec3();
            float authoredAngle = entity.TryGetValue("angle", out string authoredYaw)
                ? ParseSingle(authoredYaw, "misc_model angle")
                : 0.0f;
            Vec3 renderAngles = authoredAngle == 0.0f
                ? collisionAngles
                : new Vec3 { Y = authoredAngle };
            IReadOnlyList<Vec3> collisionAxis = AnglesToAxis(collisionAngles);
            IReadOnlyList<Vec3> renderAxis = AnglesToAxis(renderAngles);
            RequireEquivalentAxes(
                collisionAxis,
                renderAxis,
                "misc_model 'angle' and 'angles' keys produce different collision and render transforms");

            float scale = entity.TryGetValue("modelscale", out string authoredScale)
                ? ParseSingle(authoredScale, "misc_model modelscale")
                : 1.0f;
            if (scale <= 0.0f)
            {
                throw new InvalidDataException(
                    $"The misc_model modelscale {FormatFloat(scale)} must be positive.");
            }

            int spawnFlags = entity.TryGetValue("spawnflags", out string authoredSpawnFlags)
                ? ParseInt32(authoredSpawnFlags, "misc_model spawnflags")
                : 0;
            bool hasGroundLighting = entity.TryGetValue("gndLt", out string groundLightingText);
            (GfxColor groundLighting, byte primaryLightIndex) = hasGroundLighting
                ? ParseGroundLighting(groundLightingText, (byte)defaultSunPrimaryLightIndex)
                : (new GfxColor(0), (byte)defaultSunPrimaryLightIndex);
            GfxStaticModelDrawInstFlags flags =
                (spawnFlags & 2) != 0
                    ? GfxStaticModelDrawInstFlags.NoCastShadow
                    : GfxStaticModelDrawInstFlags.None;
            if (hasGroundLighting && groundLighting.Packed != 0)
                flags |= GfxStaticModelDrawInstFlags.GroundLighting;

            staticModels.Add(new D3dbspStaticModelEntity(
                modelName,
                origin,
                renderAngles,
                renderAxis,
                EncodePackedAxis(renderAxis),
                scale,
                spawnFlags,
                flags,
                hasGroundLighting,
                groundLighting,
                primaryLightIndex));
        }

        return staticModels.AsReadOnly();
    }

    private static void AppendMultiplayerSpawnFallbacks(
        Stream output,
        IReadOnlyList<ParsedEntity> entities)
    {
        ParsedEntity[] sourceStarts = entities
            .Where(entity => entity.HasClassname("info_player_start"))
            .ToArray();
        if (sourceStarts.Length == 0)
            return;

        foreach (string classname in MultiplayerSpawnFallbackClassnames)
        {
            if (entities.Any(entity => entity.HasClassname(classname)))
                continue;

            foreach (ParsedEntity sourceStart in sourceStarts)
                WriteSyntheticSpawn(output, sourceStart, classname);
        }

        const string intermissionClassname = "mp_global_intermission";
        if (!entities.Any(entity => entity.HasClassname(intermissionClassname)))
            WriteSyntheticSpawn(output, sourceStarts[0], intermissionClassname);
    }

    private static void WriteSyntheticSpawn(
        Stream output,
        ParsedEntity source,
        string classname)
    {
        string origin = source.GetRequiredValue("origin");
        ParseVec3(origin, $"{classname} fallback origin");

        string angles;
        if (source.TryGetValue("angles", out string authoredAngles))
        {
            ParseVec3(authoredAngles, $"{classname} fallback angles");
            angles = authoredAngles;
        }
        else if (source.TryGetValue("angle", out string authoredYaw))
        {
            float yaw = ParseSingle(authoredYaw, $"{classname} fallback angle");
            angles = $"0 {yaw.ToString("R", CultureInfo.InvariantCulture)} 0";
        }
        else
        {
            angles = "0 0 0";
        }

        WriteLatin1(
            output,
            $"\n{{\n\"origin\" \"{origin}\"\n" +
            $"\"angles\" \"{angles}\"\n" +
            $"\"classname\" \"{classname}\"\n}}");
    }

    private static void WriteLatin1(Stream output, string value) =>
        output.Write(Encoding.Latin1.GetBytes(value));

    private static IReadOnlyList<D3dbspStaticModelEntity> ReconstructStaticModelEntities(
        ClipMapAsset clipMap,
        GfxWorldAsset gfxWorld)
    {
        if (clipMap.NumStaticModels < 0 ||
            clipMap.NumStaticModels != clipMap.StaticModelList.Count)
        {
            throw new InvalidDataException(
                $"ClipMap declares {clipMap.NumStaticModels} static models but retains " +
                $"{clipMap.StaticModelList.Count} rows.");
        }

        int count = gfxWorld.Dpvs.SModelDrawInsts.Count;
        if (gfxWorld.Dpvs.SModelCount != (uint)count ||
            gfxWorld.Dpvs.SModelInsts.Count != count)
        {
            throw new InvalidDataException(
                "GfxWorld does not retain one shared render static-model object-index space " +
                $"(header {gfxWorld.Dpvs.SModelCount}, draw {count}, instance " +
                $"{gfxWorld.Dpvs.SModelInsts.Count}).");
        }

        var staticModels = new D3dbspStaticModelEntity[count];
        for (int index = 0; index < count; index++)
        {
            GfxStaticModelDrawInst draw = gfxWorld.Dpvs.SModelDrawInsts[index] ??
                throw new InvalidDataException($"GfxWorld static-model draw instance {index} is null.");

            XModelAsset gfxXModel = draw.Model ??
                throw new InvalidDataException(
                    $"GfxWorld static-model draw instance {index} has no XModel definition.");
            string gfxModelName = NormalizeModelName(
                gfxXModel.Name ??
                throw new InvalidDataException(
                    $"GfxWorld static-model draw instance {index} XModel has no name."));

            GfxPackedPlacement placement = draw.Placement ??
                throw new InvalidDataException(
                    $"GfxWorld static-model draw instance {index} has no placement.");
            if (placement.Origin.Count != 3 || placement.PackedAxis.Count != 3)
            {
                throw new InvalidDataException(
                    $"GfxWorld static-model placement {index} must contain three origin and " +
                    "three packed-axis components.");
            }
            if (!float.IsFinite(placement.Scale) || placement.Scale <= 0.0f)
            {
                throw new InvalidDataException(
                    $"GfxWorld static-model placement {index} has invalid scale " +
                    $"{FormatFloat(placement.Scale)}.");
            }

            Vec3 gfxOrigin = new()
            {
                X = RequireFinite(placement.Origin[0], $"static model {index} origin X"),
                Y = RequireFinite(placement.Origin[1], $"static model {index} origin Y"),
                Z = RequireFinite(placement.Origin[2], $"static model {index} origin Z")
            };

            IReadOnlyList<Vec3> gfxAxis = DecodePackedAxis(placement.PackedAxis);
            Vec3 angles = AxisToCanonicalAngles(gfxAxis);
            IReadOnlyList<Vec3> rebuiltAxis = AnglesToAxis(angles);
            RequireEquivalentAxes(
                gfxAxis,
                rebuiltAxis,
                $"static model {index} packed axis cannot be represented by canonical Euler angles");

            const GfxStaticModelDrawInstFlags supportedFlags =
                GfxStaticModelDrawInstFlags.NoCastShadow |
                GfxStaticModelDrawInstFlags.GroundLighting;
            if ((draw.Flags & ~supportedFlags) != 0)
            {
                throw new NotSupportedException(
                    $"Static model {index} has unsupported GfxWorld flags 0x{(byte)draw.Flags:X2}.");
            }

            bool usesGroundLighting =
                (draw.Flags & GfxStaticModelDrawInstFlags.GroundLighting) != 0;
            if (usesGroundLighting != (draw.GroundLighting.Packed != 0))
            {
                throw new InvalidDataException(
                    $"Static model {index} has inconsistent GroundLighting flag and packed value " +
                    $"0x{draw.GroundLighting.Packed:X8}.");
            }
            if (usesGroundLighting &&
                (gfxXModel.Flags & XModelFlags.GroundLighting) == 0)
            {
                throw new InvalidDataException(
                    $"Static model {index} uses authored ground lighting but XModel " +
                    $"'{gfxModelName}' is not ground-lit.");
            }
            if (gfxWorld.PrimaryLightCount <= 0
                ? draw.PrimaryLightIndex != 0
                : draw.PrimaryLightIndex >= gfxWorld.PrimaryLightCount)
            {
                throw new InvalidDataException(
                    $"Static model {index} primary-light index {draw.PrimaryLightIndex} is outside " +
                    $"the GfxWorld primary-light count {gfxWorld.PrimaryLightCount}.");
            }

            int spawnFlags =
                (draw.Flags & GfxStaticModelDrawInstFlags.NoCastShadow) != 0 ? 2 : 0;
            staticModels[index] = new D3dbspStaticModelEntity(
                gfxModelName,
                gfxOrigin,
                angles,
                gfxAxis,
                placement.PackedAxis.ToArray(),
                placement.Scale,
                spawnFlags,
                draw.Flags,
                true,
                draw.GroundLighting,
                draw.PrimaryLightIndex);
        }

        ValidateCollisionStaticModelSubset(clipMap, staticModels);
        return Array.AsReadOnly(staticModels);
    }

    private static void ValidateCollisionStaticModelSubset(
        ClipMapAsset clipMap,
        IReadOnlyList<D3dbspStaticModelEntity> renderModels)
    {
        var renderIndicesByIdentity =
            new Dictionary<(string ModelName, int OriginX, int OriginY, int OriginZ), List<int>>();
        for (int renderIndex = 0; renderIndex < renderModels.Count; renderIndex++)
        {
            D3dbspStaticModelEntity render = renderModels[renderIndex];
            (string, int, int, int) key = GetStaticModelIdentity(render.ModelName, render.Origin);
            if (!renderIndicesByIdentity.TryGetValue(key, out List<int>? indices))
            {
                indices = [];
                renderIndicesByIdentity.Add(key, indices);
            }
            indices.Add(renderIndex);
        }

        var matchedRenderModels = new bool[renderModels.Count];
        for (int clipIndex = 0; clipIndex < clipMap.StaticModelList.Count; clipIndex++)
        {
            ClipStaticModel clip = clipMap.StaticModelList[clipIndex] ??
                throw new InvalidDataException($"ClipMap static model {clipIndex} is null.");
            XModelAsset clipXModel = clip.XModel ??
                throw new InvalidDataException(
                    $"ClipMap static model {clipIndex} has no XModel definition.");
            string clipModelName = NormalizeModelName(
                clipXModel.Name ??
                throw new InvalidDataException(
                    $"ClipMap static model {clipIndex} XModel has no name."));
            RequireFinite(clip.Origin.X, $"ClipMap static model {clipIndex} origin X");
            RequireFinite(clip.Origin.Y, $"ClipMap static model {clipIndex} origin Y");
            RequireFinite(clip.Origin.Z, $"ClipMap static model {clipIndex} origin Z");
            if (clip.InvScaledAxis.Count != 3)
            {
                throw new InvalidDataException(
                    $"ClipMap static model {clipIndex} must contain three inverse-scaled-axis rows.");
            }
            for (int row = 0; row < 3; row++)
            {
                RequireFinite(clip.InvScaledAxis[row].X, $"ClipMap static model {clipIndex} inverse axis [{row},0]");
                RequireFinite(clip.InvScaledAxis[row].Y, $"ClipMap static model {clipIndex} inverse axis [{row},1]");
                RequireFinite(clip.InvScaledAxis[row].Z, $"ClipMap static model {clipIndex} inverse axis [{row},2]");
            }

            (string, int, int, int) key = GetStaticModelIdentity(clipModelName, clip.Origin);
            if (!renderIndicesByIdentity.TryGetValue(key, out List<int>? candidates))
            {
                throw new InvalidDataException(
                    $"ClipMap static model {clipIndex} '{clipModelName}' at " +
                    $"'{FormatVec3(clip.Origin)}' has no matching GfxWorld placement.");
            }

            int matchedIndex = -1;
            foreach (int candidateIndex in candidates)
            {
                if (matchedRenderModels[candidateIndex] ||
                    !IsCompatibleCollisionTransform(clip, renderModels[candidateIndex]))
                {
                    continue;
                }

                matchedIndex = candidateIndex;
                break;
            }
            if (matchedIndex < 0)
            {
                int unmatchedCandidateCount = candidates.Count(
                    candidateIndex => !matchedRenderModels[candidateIndex]);
                throw new InvalidDataException(
                    $"ClipMap static model {clipIndex} '{clipModelName}' at " +
                    $"'{FormatVec3(clip.Origin)}' has no unused GfxWorld placement with a " +
                    $"compatible scale and axis ({unmatchedCandidateCount} identity candidates remain).");
            }

            matchedRenderModels[matchedIndex] = true;
        }
    }

    private static (string ModelName, int OriginX, int OriginY, int OriginZ)
        GetStaticModelIdentity(string modelName, Vec3 origin) =>
        (
            modelName,
            GetOriginComponentIdentity(origin.X),
            GetOriginComponentIdentity(origin.Y),
            GetOriginComponentIdentity(origin.Z)
        );

    private static int GetOriginComponentIdentity(float value) =>
        value == 0.0f ? 0 : BitConverter.SingleToInt32Bits(value);

    private static bool IsCompatibleCollisionTransform(
        ClipStaticModel clip,
        D3dbspStaticModelEntity render)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int component = 0; component < 3; component++)
            {
                float clipAxisValue = GetComponent(
                    clip.InvScaledAxis[component],
                    row) * render.Scale;
                if (!float.IsFinite(clipAxisValue))
                    return false;
                float renderAxisValue = GetComponent(render.Axis[row], component);
                float tolerance = (component == 2 ? PackedAxisZStep : PackedAxisXyStep) +
                    PackedAxisRoundTripEpsilon;
                if (MathF.Abs(clipAxisValue - renderAxisValue) > tolerance)
                    return false;
            }
        }
        return true;
    }

    private static void ValidateStages(MapEntsAsset mapEnts)
    {
        if (mapEnts.StageCount != mapEnts.Stages.Count)
        {
            throw new InvalidDataException(
                $"MapEnts declares {mapEnts.StageCount} stages but retains " +
                $"{mapEnts.Stages.Count} rows.");
        }
        if (mapEnts.Stages.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"MapEnts retains {mapEnts.Stages.Count} stages; IW4 supports at most " +
                $"{byte.MaxValue}.");
        }

        for (int index = 0; index < mapEnts.Stages.Count; index++)
        {
            Stage stage = mapEnts.Stages[index] ??
                throw new InvalidDataException($"MapEnts stage {index} is null.");
            ValidateEntityValue(
                stage.StageName ??
                throw new InvalidDataException($"MapEnts stage {index} has no name."),
                $"stage {index} name");
            RequireFinite(stage.Origin.X, $"stage {index} origin X");
            RequireFinite(stage.Origin.Y, $"stage {index} origin Y");
            RequireFinite(stage.Origin.Z, $"stage {index} origin Z");
            if (stage.Pad13 != 0)
            {
                throw new InvalidDataException(
                    $"MapEnts stage {index} has nonzero reserved byte 0x{stage.Pad13:X2}.");
            }
        }
    }

    private static void WriteStaticModel(
        Stream output,
        D3dbspStaticModelEntity staticModel)
    {
        WriteLatin1(output, "\n{\n");
        WriteEntityKeyValue(output, "classname", "misc_model", "static-model classname");
        WriteEntityKeyValue(output, "model", staticModel.ModelName, "static-model name");
        WriteEntityKeyValue(output, "origin", FormatVec3(staticModel.Origin), "static-model origin");
        WriteEntityKeyValue(output, "angles", FormatVec3(staticModel.Angles), "static-model angles");
        WriteEntityKeyValue(output, "modelscale", FormatFloat(staticModel.Scale), "static-model scale");
        WriteEntityKeyValue(
            output,
            "spawnflags",
            staticModel.SpawnFlags.ToString(CultureInfo.InvariantCulture),
            "static-model spawn flags");
        WriteEntityKeyValue(
            output,
            "gndLt",
            FormatGroundLighting(staticModel.GroundLighting, staticModel.PrimaryLightIndex),
            "static-model ground lighting");
        WriteLatin1(output, "}\n");
    }

    private static void WriteStage(
        Stream output,
        Stage stage,
        int? collisionModelIndex)
    {
        string stageName = stage.StageName ??
            throw new InvalidDataException("A MapEnts stage has no name.");
        WriteLatin1(output, "\n{\n");
        WriteEntityKeyValue(output, "classname", "stage", "stage classname");
        WriteEntityKeyValue(output, "name", stageName, "stage name");
        WriteEntityKeyValue(output, "origin", FormatVec3(stage.Origin), "stage origin");
        if (collisionModelIndex is int modelIndex)
        {
            WriteEntityKeyValue(
                output,
                "model",
                $"*{modelIndex.ToString(CultureInfo.InvariantCulture)}",
                "stage collision model");
        }
        WriteEntityKeyValue(
            output,
            "script_index",
            stage.TriggerIndex.ToString(CultureInfo.InvariantCulture),
            "stage script index");
        WriteEntityKeyValue(
            output,
            "sunPrimaryLightIndex",
            stage.SunPrimaryLightIndex.ToString(CultureInfo.InvariantCulture),
            "stage sun-primary-light index");
        WriteLatin1(output, "}\n");
    }

    private static void WriteEntityKeyValue(
        Stream output,
        string key,
        string value,
        string description)
    {
        ValidateEntityValue(value, description);
        string escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        WriteLatin1(output, $"\"{key}\" \"{escaped}\"\n");
    }

    private static string NormalizeModelName(string value)
    {
        if (value.StartsWith("xmodel/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("xmodel\\", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..];
        }
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("A misc_model has no usable model name.");
        ValidateEntityValue(value, "misc_model model name");
        return value;
    }

    private static void ValidateEntityValue(string value, string description)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character < 32 || character > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"The {description} contains an unsupported character U+{(int)character:X4}.");
            }
        }
    }

    private static string FormatVec3(Vec3 value) =>
        $"{FormatFloat(value.X)} {FormatFloat(value.Y)} {FormatFloat(value.Z)}";

    private static string FormatFloat(float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"Cannot encode non-finite entity float {value}.");
        if (value == 0.0f)
            return "0";
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatGroundLighting(GfxColor color, byte primaryLightIndex) =>
        $"{color.Blue:X2}{color.Green:X2}{color.Red:X2}{color.Alpha:X2}{primaryLightIndex:X2}";

    private static (GfxColor Color, byte PrimaryLightIndex) ParseGroundLighting(
        string value,
        byte defaultSunPrimaryLightIndex)
    {
        if (value.Length is not (8 or 10))
        {
            throw new InvalidDataException(
                $"The misc_model gndLt value '{value}' must contain four or five hex bytes.");
        }

        byte encodedBlue = ParseHexByte(value.AsSpan(0, 2), "misc_model gndLt blue");
        byte green = ParseHexByte(value.AsSpan(2, 2), "misc_model gndLt green");
        byte encodedRed = ParseHexByte(value.AsSpan(4, 2), "misc_model gndLt red");
        byte alpha = ParseHexByte(value.AsSpan(6, 2), "misc_model gndLt alpha");
        byte primaryLightIndex = value.Length == 10
            ? ParseHexByte(value.AsSpan(8, 2), "misc_model gndLt primary-light index")
            : defaultSunPrimaryLightIndex;
        return (
            GfxColor.FromRgba(encodedRed, green, encodedBlue, alpha),
            primaryLightIndex);
    }

    private static byte ParseHexByte(ReadOnlySpan<char> value, string description)
    {
        if (!byte.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte result))
            throw new InvalidDataException($"The {description} value '{value.ToString()}' is not a hex byte.");
        return result;
    }

    public static IReadOnlyList<Stage> DecodeStages(
        ReadOnlySpan<byte> source,
        int defaultSunPrimaryLightIndex)
    {
        if ((uint)defaultSunPrimaryLightIndex > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSunPrimaryLightIndex),
                defaultSunPrimaryLightIndex,
                "The default stage sun-primary-light index must fit in one byte.");
        }

        IReadOnlyList<ParsedEntity> entities = ParseEntities(source);
        var stages = new List<Stage>();
        foreach (ParsedEntity entity in entities)
        {
            if (!IsRuntimeStage(entity))
                continue;

            stages.Add(new Stage
            {
                StageName = entity.GetRequiredValue("name"),
                Origin = ParseVec3(entity.GetRequiredValue("origin"), "stage origin"),
                TriggerIndex = ParseUInt16(
                    entity.GetRequiredValue("script_index"),
                    "stage script_index"),
                SunPrimaryLightIndex = ParseByte(
                    entity.GetRequiredValue("sunPrimaryLightIndex"),
                    "stage sunPrimaryLightIndex"),
                Pad13 = 0
            });
        }

        if (stages.Count == 0)
        {
            // R_GetActiveStageIndex returns row zero for an empty table, and
            // R_UpdateActiveStage then dereferences it without a count guard.
            stages.Add(new Stage
            {
                StageName = "stage 0",
                Origin = new Vec3(),
                TriggerIndex = 1024,
                SunPrimaryLightIndex = (byte)defaultSunPrimaryLightIndex,
                Pad13 = 0
            });
        }

        if (stages.Count > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"The entity lump defines {stages.Count} runtime stages; IW4 MapEnts supports at most {byte.MaxValue}.");
        }

        return stages.AsReadOnly();
    }

    private static bool CanPurge(ParsedEntity entity)
    {
        string classname = entity.TryGetValue("classname", out string value)
            ? value
            : string.Empty;
        return PurgeableClassnames.Contains(classname) ||
            (string.Equals(classname, "light", StringComparison.OrdinalIgnoreCase) &&
                !entity.ContainsKey("pl#")) ||
            IsRuntimeStage(entity);
    }

    private static bool IsRuntimeStage(ParsedEntity entity) =>
        entity.TryGetValue("classname", out string classname) &&
        string.Equals(classname, "stage", StringComparison.OrdinalIgnoreCase) &&
        entity.ContainsKey("name") &&
        entity.ContainsKey("origin") &&
        entity.ContainsKey("script_index") &&
        entity.ContainsKey("sunPrimaryLightIndex");

    private static IReadOnlyList<ParsedEntity> ParseEntities(ReadOnlySpan<byte> source)
    {
        int terminator = source.IndexOf((byte)0);
        int length = terminator < 0 ? source.Length : terminator;
        if (terminator >= 0 && source[(terminator + 1)..].IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("The entity lump contains data after its first NUL terminator.");

        var parser = new EntityParser(source[..length]);
        var entities = new List<ParsedEntity>();
        while (parser.ParseEntity() is { } entity)
            entities.Add(entity);
        return entities.AsReadOnly();
    }

    private static IReadOnlyList<Vec3> DecodePackedAxis(IReadOnlyList<uint> packedAxis)
    {
        var axis = new Vec3[3];
        for (int row = 0; row < axis.Length; row++)
        {
            Vector3 decoded = new PackedSigned11_11_10(packedAxis[row]).DecodePlacement();
            axis[row] = new Vec3
            {
                X = decoded.X,
                Y = decoded.Y,
                Z = decoded.Z
            };
        }
        return Array.AsReadOnly(axis);
    }

    private static IReadOnlyList<uint> EncodePackedAxis(IReadOnlyList<Vec3> axis)
    {
        if (axis.Count != 3)
            throw new InvalidDataException("A static-model axis must contain three rows.");

        var packedAxis = new uint[3];
        for (int row = 0; row < packedAxis.Length; row++)
        {
            int x = PackPlacementComponent(
                axis[row].X,
                1023,
                PackedAxisXyStep,
                $"static-model axis [{row},0]");
            int y = PackPlacementComponent(
                axis[row].Y,
                1023,
                PackedAxisXyStep,
                $"static-model axis [{row},1]");
            int z = PackPlacementComponent(
                axis[row].Z,
                511,
                PackedAxisZStep,
                $"static-model axis [{row},2]");
            packedAxis[row] =
                ((uint)x & 0x7ffu) |
                (((uint)y & 0x7ffu) << 11) |
                (((uint)z & 0x3ffu) << 22);
        }
        return Array.AsReadOnly(packedAxis);
    }

    private static int PackPlacementComponent(
        float value,
        int positiveMaximum,
        float tolerance,
        string description)
    {
        RequireFinite(value, description);
        if (value < -1.0f - tolerance || value > 1.0f + tolerance)
        {
            throw new InvalidDataException(
                $"The {description} value {FormatFloat(value)} is outside the packed unit range.");
        }

        float clamped = System.Math.Clamp(value, -1.0f, 1.0f);
        return checked((int)MathF.Round(
            clamped * positiveMaximum,
            MidpointRounding.AwayFromZero));
    }

    private static Vec3 AxisToCanonicalAngles(IReadOnlyList<Vec3> axis)
    {
        Vec3 forward = axis[0];
        float pitch;
        float yaw;
        if (forward.X == 0.0f && forward.Y == 0.0f)
        {
            yaw = 0.0f;
            pitch = forward.Z > 0.0f ? 270.0f : 90.0f;
        }
        else
        {
            yaw = RadiansToDegrees(MathF.Atan2(forward.Y, forward.X));
            if (yaw < 0.0f)
                yaw += 360.0f;
            float forwardLength = MathF.Sqrt(
                forward.X * forward.X + forward.Y * forward.Y);
            pitch = -RadiansToDegrees(MathF.Atan2(forward.Z, forwardLength));
            if (pitch < 0.0f)
                pitch += 360.0f;
        }

        Vec3 right = axis[1];
        float yawRadians = DegreesToRadians(-yaw);
        float yawCos = MathF.Cos(yawRadians);
        float yawSin = MathF.Sin(yawRadians);
        float transformedX = yawCos * right.X - yawSin * right.Y;
        float transformedY = yawSin * right.X + yawCos * right.Y;

        float pitchRadians = DegreesToRadians(-pitch);
        float pitchCos = MathF.Cos(pitchRadians);
        float pitchSin = MathF.Sin(pitchRadians);
        float rolledX = pitchSin * right.Z + pitchCos * transformedX;
        float rolledZ = pitchCos * right.Z - pitchSin * transformedX;
        float signedRollPitch;
        if (rolledX == 0.0f && transformedY == 0.0f)
        {
            signedRollPitch = rolledZ > 0.0f ? -90.0f : 90.0f;
        }
        else
        {
            float horizontal = MathF.Sqrt(
                rolledX * rolledX + transformedY * transformedY);
            signedRollPitch = -RadiansToDegrees(MathF.Atan2(rolledZ, horizontal));
        }

        float roll = transformedY >= 0.0f
            ? -signedRollPitch
            : signedRollPitch >= 0.0f
                ? signedRollPitch - 180.0f
                : signedRollPitch + 180.0f;
        return new Vec3
        {
            X = NormalizeDegrees(pitch),
            Y = NormalizeDegrees(yaw),
            Z = NormalizeDegrees(roll)
        };
    }

    private static IReadOnlyList<Vec3> AnglesToAxis(Vec3 angles)
    {
        float yaw = DegreesToRadians(angles.Y);
        float pitch = DegreesToRadians(angles.X);
        float roll = DegreesToRadians(angles.Z);
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);
        float cr = MathF.Cos(roll);
        float sr = MathF.Sin(roll);
        return Array.AsReadOnly(
        [
            new Vec3 { X = cp * cy, Y = cp * sy, Z = -sp },
            new Vec3
            {
                X = sr * sp * cy - sy * cr,
                Y = sr * sp * sy + cr * cy,
                Z = sr * cp
            },
            new Vec3
            {
                X = cr * sp * cy + sr * sy,
                Y = cr * sp * sy - sr * cy,
                Z = cr * cp
            }
        ]);
    }

    private static void RequireEquivalentAxes(
        IReadOnlyList<Vec3> left,
        IReadOnlyList<Vec3> right,
        string description)
    {
        if (left.Count != 3 || right.Count != 3)
            throw new InvalidDataException($"The {description} does not contain three rows.");

        for (int row = 0; row < 3; row++)
        {
            for (int component = 0; component < 3; component++)
            {
                float leftValue = RequireFinite(
                    GetComponent(left[row], component),
                    $"{description} left [{row},{component}]");
                float rightValue = RequireFinite(
                    GetComponent(right[row], component),
                    $"{description} right [{row},{component}]");
                float tolerance = 2.0f *
                    (component == 2 ? PackedAxisZStep : PackedAxisXyStep) +
                    PackedAxisRoundTripEpsilon;
                if (MathF.Abs(leftValue - rightValue) > tolerance)
                {
                    throw new InvalidDataException(
                        $"The {description}: axis [{row},{component}] differs by " +
                        $"{FormatFloat(MathF.Abs(leftValue - rightValue))}, beyond packed-axis " +
                        $"tolerance {FormatFloat(tolerance)}.");
                }
            }
        }
    }

    private static void RequireSameOrigin(Vec3 clipOrigin, Vec3 gfxOrigin, int index)
    {
        RequireFinite(clipOrigin.X, $"static model {index} ClipMap origin X");
        RequireFinite(clipOrigin.Y, $"static model {index} ClipMap origin Y");
        RequireFinite(clipOrigin.Z, $"static model {index} ClipMap origin Z");
        if (BitConverter.SingleToInt32Bits(clipOrigin.X) != BitConverter.SingleToInt32Bits(gfxOrigin.X) ||
            BitConverter.SingleToInt32Bits(clipOrigin.Y) != BitConverter.SingleToInt32Bits(gfxOrigin.Y) ||
            BitConverter.SingleToInt32Bits(clipOrigin.Z) != BitConverter.SingleToInt32Bits(gfxOrigin.Z))
        {
            throw new InvalidDataException(
                $"Static model {index} origins differ between ClipMap " +
                $"'{FormatVec3(clipOrigin)}' and GfxWorld '{FormatVec3(gfxOrigin)}'.");
        }
    }

    private static float GetComponent(Vec3 value, int component) => component switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, null)
    };

    private static float NormalizeDegrees(float value)
    {
        value %= 360.0f;
        if (value >= 180.0f)
            value -= 360.0f;
        else if (value < -180.0f)
            value += 360.0f;
        return MathF.Abs(value) < 0.000001f ? 0.0f : value;
    }

    private static float DegreesToRadians(float value) => value * (MathF.PI / 180.0f);

    private static float RadiansToDegrees(float value) => value * (180.0f / MathF.PI);

    private static float RequireFinite(float value, string description)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"The {description} is not finite.");
        return value;
    }

    private static Vec3 ParseVec3(string value, string description)
    {
        string[] components = value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (components.Length != 3)
        {
            throw new InvalidDataException(
                $"The {description} value '{value}' does not contain three components.");
        }

        return new Vec3
        {
            X = ParseSingle(components[0], description),
            Y = ParseSingle(components[1], description),
            Z = ParseSingle(components[2], description)
        };
    }

    private static float ParseSingle(string value, string description)
    {
        if (!float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result) ||
            !float.IsFinite(result))
        {
            throw new InvalidDataException($"The {description} value '{value}' is not a finite float.");
        }

        return result;
    }

    private static ushort ParseUInt16(string value, string description)
    {
        if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort result))
            throw new InvalidDataException($"The {description} value '{value}' is not a ushort.");
        return result;
    }

    private static int ParseInt32(string value, string description)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new InvalidDataException($"The {description} value '{value}' is not an int.");
        return result;
    }

    private static byte ParseByte(string value, string description)
    {
        if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte result))
            throw new InvalidDataException($"The {description} value '{value}' is not a byte.");
        return result;
    }

    private sealed class ParsedEntity
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Begin, int End)> _valueSpans =
            new(StringComparer.OrdinalIgnoreCase);

        public ParsedEntity(int begin)
        {
            Begin = begin;
        }

        public int Begin { get; }
        public int End { get; set; }

        public void Add(string key, string value, int valueBegin, int valueEnd)
        {
            if (_values.TryAdd(key, value))
                _valueSpans.Add(key, (valueBegin, valueEnd));
        }

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool HasClassname(string classname) =>
            TryGetValue("classname", out string value) &&
            string.Equals(value, classname, StringComparison.OrdinalIgnoreCase);

        public bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out string? found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }

        public bool TryGetValueSpan(string key, out int begin, out int end)
        {
            if (_valueSpans.TryGetValue(key, out (int Begin, int End) span))
            {
                begin = span.Begin;
                end = span.End;
                return true;
            }

            begin = 0;
            end = 0;
            return false;
        }

        public string GetRequiredValue(string key) =>
            TryGetValue(key, out string value)
                ? value
                : throw new InvalidDataException($"An entity has no '{key}' value.");
    }

    private ref struct EntityParser
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;
        private int _tokenBegin;
        private int _tokenEnd;

        public EntityParser(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public ParsedEntity? ParseEntity()
        {
            int begin = _position;
            string? token = ReadToken();
            if (token is null)
                return null;
            if (token != "{")
                throw new InvalidDataException($"Expected '{{' at entity byte {_position}, found '{token}'.");

            var entity = new ParsedEntity(begin);
            while (true)
            {
                string key = ReadToken() ??
                    throw new InvalidDataException("The entity lump ended before a closing brace.");
                if (key == "}")
                {
                    entity.End = _position;
                    return entity;
                }

                string value = ReadToken() ??
                    throw new InvalidDataException($"The entity key '{key}' has no value.");
                if (value == "}")
                    throw new InvalidDataException($"The entity key '{key}' has no value.");
                entity.Add(key, value, _tokenBegin, _tokenEnd);
            }
        }

        private string? ReadToken()
        {
            SkipTrivia();
            if (_position >= _data.Length)
                return null;

            _tokenBegin = _position;
            byte first = _data[_position++];
            if (first is (byte)'{' or (byte)'}')
            {
                _tokenEnd = _position;
                return ((char)first).ToString();
            }
            if (first == (byte)'"')
            {
                string quoted = ReadQuotedToken();
                _tokenEnd = _position;
                return quoted;
            }

            int begin = _position - 1;
            while (_position < _data.Length &&
                   _data[_position] > 32 &&
                   _data[_position] is not (byte)'{' and not (byte)'}')
            {
                _position++;
            }

            _tokenEnd = _position;
            return Encoding.Latin1.GetString(_data[begin.._position]);
        }

        private string ReadQuotedToken()
        {
            var token = new StringBuilder();
            while (_position < _data.Length)
            {
                byte value = _data[_position++];
                if (value == (byte)'"')
                    return token.ToString();
                if (value == (byte)'\\' &&
                    _position < _data.Length &&
                    _data[_position] is (byte)'"' or (byte)'\\')
                {
                    value = _data[_position++];
                }

                token.Append((char)value);
            }

            throw new InvalidDataException("The entity lump ended inside a quoted token.");
        }

        private void SkipTrivia()
        {
            while (true)
            {
                while (_position < _data.Length && _data[_position] <= 32)
                    _position++;
                if (_position + 1 >= _data.Length || _data[_position] != (byte)'/')
                    return;

                if (_data[_position + 1] == (byte)'/')
                {
                    _position += 2;
                    while (_position < _data.Length && _data[_position] != (byte)'\n')
                        _position++;
                    continue;
                }
                if (_data[_position + 1] != (byte)'*')
                    return;

                _position += 2;
                while (_position + 1 < _data.Length &&
                       (_data[_position] != (byte)'*' || _data[_position + 1] != (byte)'/'))
                {
                    _position++;
                }
                if (_position + 1 >= _data.Length)
                    throw new InvalidDataException("The entity lump ended inside a block comment.");
                _position += 2;
            }
        }
    }
}
