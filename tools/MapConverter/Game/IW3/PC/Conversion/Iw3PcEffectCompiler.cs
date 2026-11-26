using System.Text.Json;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using MapConverter.Game.IW3.PC.Extraction;
using MapConverter.Game.IW3.PS3.Rules;
using MapConverter.IW3.PC.Assets;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.Conversion;

internal sealed class Iw3PcEffectCompiler
{
    private readonly Dictionary<string, JsonElement> effects = new(StringComparer.OrdinalIgnoreCase);

    internal static Iw3PcEffectCompiler Capture(
        IReadOnlyList<string> roots, Iw3PcSourceLibrary? library, string mapPath, CancellationToken token)
    {
        var result = new Iw3PcEffectCompiler();
        var pending = roots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (pending.Count != 0)
        {
            var groups = pending.Select(name => (Name: name, Source: library?.EffectSourceRecord(mapPath, name)
                    ?? (Path: mapPath, RecordId: -1)))
                .GroupBy(row => row.Source.Path).ToArray();
            pending.Clear();
            foreach (var group in groups)
            {
                string[] names = group.Select(row => row.Name).ToArray();
                var zone = Iw3PcZone.Load(group.Key, token);
                byte[] bytes = Iw3PcEffectExporter.Export(zone, names,
                    library is null ? null : group.ToDictionary(row => row.Name,
                        row => row.Source.RecordId, StringComparer.OrdinalIgnoreCase), token);
                using var graph = JsonDocument.Parse(bytes);
                foreach (var effect in graph.RootElement.GetProperty("effects").EnumerateArray())
                {
                    string name = AssetName(effect.GetProperty("asset")) ??
                        throw new InvalidDataException("PC effect definition has no name.");
                    result.effects.TryAdd(name, effect.Clone());
                }
                foreach (string key in new[] { "externalEffectReferences", "unresolvedEffectReferences" })
                    foreach (var child in graph.RootElement.GetProperty(key).EnumerateArray())
                        pending.Add(child.GetString() ?? throw new InvalidDataException("PC effect reference has no name."));
            }
            pending.ExceptWith(result.effects.Keys);
            if (library is null && pending.Count != 0)
                throw new InvalidDataException($"IW3 PC fastfile '{mapPath}' has no definitions for effects: " +
                    string.Join(", ", pending.Order(StringComparer.OrdinalIgnoreCase)) + ".");
        }
        return result;
    }

    private IEnumerable<JsonElement> VisualRows => effects.Values.SelectMany(effect =>
        Items(effect.GetProperty("elemDefs"))).SelectMany(element => Items(element.GetProperty("visuals")));

    private IEnumerable<JsonElement> Visuals => VisualRows.Where(v => v.TryGetProperty("kind", out _));

    internal IEnumerable<string> MaterialNames => Visuals.Where(v => Text(v, "kind") == "material")
        .Select(v => AssetName(v.GetProperty("asset")))
        .Concat(VisualRows.Where(v => v.TryGetProperty("materials", out _))
            .SelectMany(v => Items(v.GetProperty("materials"))).Select(AssetName)).OfType<string>();

    internal IEnumerable<string> ModelNames => Visuals.Where(v => Text(v, "kind") == "model")
        .Select(v => AssetName(v.GetProperty("asset"))).OfType<string>();

    internal IEnumerable<string> SoundNames => Visuals.Where(v => Text(v, "kind") == "sound")
        .Select(v => Text(v, "name")?.TrimStart(',')).OfType<string>();

    internal FxEffectDefAsset[] Compile(string prefix, IReadOnlyDictionary<string, MaterialAsset> materials,
        IReadOnlyDictionary<string, XModelAsset> models)
    {
        return effects.Select(pair =>
        {
            JsonElement e = pair.Value;
            return new FxEffectDefAsset
            {
                Name = prefix + pair.Key, Flags = (FxEffectFlags)Int(e, "flags"),
                TotalSize = Int(e, "totalSize"), MsecLoopingLife = Int(e, "msecLoopingLife"),
                ElemDefCountLooping = Int(e, "elemDefCountLooping"), ElemDefCountOneShot = Int(e, "elemDefCountOneShot"),
                ElemDefCountEmission = Int(e, "elemDefCountEmission"),
                ElemDefs = Items(e.GetProperty("elemDefs")).Select(Element).ToArray()
            };
        }).ToArray();

        FxEffectDefRef Reference(JsonElement value) => new()
        {
            Name = AssetName(value) is { } name ? prefix + name : null
        };
        MaterialAsset? Material(JsonElement value) => AssetName(value) is { } name ? materials[name] : null;
        FxElemDefVisuals Visual(JsonElement v) => new()
        {
            Visual = Text(v, "kind") switch
            {
                "material" => new FxMaterialVisual { Material = Material(v.GetProperty("asset")) },
                "model" => new FxModelVisual { Model = AssetName(v.GetProperty("asset")) is { } name ? models[name] : null },
                "sound" => new FxSoundVisual { SoundName = Text(v, "name") is { } name ? prefix + name.TrimStart(',') : null },
                "effect" => new FxEffectVisual { EffectDef = Reference(v.GetProperty("asset")) },
                "none" => new FxNoChildVisual { Reserved = Int(v, "reserved") },
                var kind => throw new InvalidDataException($"Unsupported PC FX visual '{kind}'.")
            }
        };

        FxElemDef Element(JsonElement e)
        {
            var type = EffectRules.ElementType(Byte(e, "elemType"));
            if (type.IsError) throw new InvalidDataException(type.ErrorValue);
            var bounds = EffectRules.CollisionBounds(Vector(e.GetProperty("collMins")), Vector(e.GetProperty("collMaxs")), true);
            if (bounds.IsError) throw new InvalidDataException(bounds.ErrorValue);
            JsonElement atlas = e.GetProperty("atlas"), spawn = e.GetProperty("spawn");
            JsonElement[] rows = Items(e.GetProperty("visuals")).ToArray();
            byte count = Byte(e, "visualCount");
            bool mark = type.ResultValue == FxElemType.Decal;
            bool inlineVisual = !mark && count <= 1;
            if (rows.Length != (inlineVisual ? 1 : count))
                throw new InvalidDataException("PC FX visual count differs from its payload.");
            return new FxElemDef
            {
                Flags = new FxElemFlags(unchecked((int)e.GetProperty("flags").GetInt64())),
                Spawn = new FxSpawnDef(Int(spawn, "word0"), Int(spawn, "word1")),
                SpawnRange = Range(e.GetProperty("spawnRange")), FadeInRange = Range(e.GetProperty("fadeInRange")),
                FadeOutRange = Range(e.GetProperty("fadeOutRange")), SpawnFrustumCullRadius = Float(e, "spawnFrustumCullRadius"),
                SpawnDelayMsec = IntRange(e.GetProperty("spawnDelayMsec")), LifeSpanMsec = IntRange(e.GetProperty("lifeSpanMsec")),
                SpawnOrigin = Ranges(e, "spawnOrigin"), SpawnOffsetRadius = Range(e.GetProperty("spawnOffsetRadius")),
                SpawnOffsetHeight = Range(e.GetProperty("spawnOffsetHeight")), SpawnAngles = Ranges(e, "spawnAngles"),
                AngularVelocity = Ranges(e, "angularVelocity"), InitialRotation = Range(e.GetProperty("initialRotation")),
                Gravity = Range(e.GetProperty("gravity")), ReflectionFactor = Range(e.GetProperty("reflectionFactor")),
                Atlas = new FxElemAtlas((FxAtlasBehavior)Byte(atlas, "behavior"), Byte(atlas, "index"), Byte(atlas, "fps"),
                    Byte(atlas, "loopCount"), Byte(atlas, "colIndexBits"), Byte(atlas, "rowIndexBits"), atlas.GetProperty("entryCount").GetInt16()),
                ElemType = type.ResultValue, VisualCount = count,
                VelIntervalCount = Byte(e, "velIntervalCount"), VisStateIntervalCount = Byte(e, "visStateIntervalCount"),
                VelSamples = Items(e.GetProperty("velSamples")).Select(v =>
                    new FxElemVelStateSample(Frame(v.GetProperty("local")), Frame(v.GetProperty("world")))).ToArray(),
                VisSamples = Items(e.GetProperty("visSamples")).Select(v =>
                    new FxElemVisStateSample(State(v.GetProperty("base")), State(v.GetProperty("amplitude")))).ToArray(),
                Visuals = inlineVisual ? Visual(rows[0]) : new(),
                VisualArray = !mark && count > 1 ? rows.Select(Visual).ToArray() : [],
                MarkVisualArray = mark ? rows.Select(v => new FxElemMarkVisuals
                {
                    Material0 = Material(v.GetProperty("materials")[0]), Material1 = Material(v.GetProperty("materials")[1])
                }).ToArray() : [],
                CollBounds = bounds.ResultValue, EffectOnImpact = Reference(e.GetProperty("effectOnImpact")),
                EffectOnDeath = Reference(e.GetProperty("effectOnDeath")), EffectEmitted = Reference(e.GetProperty("effectEmitted")),
                EmitDist = Range(e.GetProperty("emitDist")), EmitDistVariance = Range(e.GetProperty("emitDistVariance")),
                Extended = Trail(e.GetProperty("trailDef")), SortOrder = Byte(e, "sortOrder"),
                LightingFrac = Byte(e, "lightingFrac"), UseItemClip = Byte(e, "useItemClip")
            };
        }
    }

    private static FxElemExtendedDef? Trail(JsonElement t)
    {
        if (t.ValueKind == JsonValueKind.Null) return null;
        var split = EffectRules.TrailSplit(Int(t, "splitDist"), true);
        if (split.IsError) throw new InvalidDataException(split.ErrorValue);
        var (distance, arcDistance, time) = split.ResultValue;
        return new FxElemExtendedDef
        {
            Kind = FxElemExtendedDefKind.Trail,
            TrailDef = new FxTrailDef
            {
                ScrollTimeMsec = Int(t, "scrollTimeMsec"), RepeatDist = Int(t, "repeatDist"),
                InvSplitDist = distance, InvSplitArcDist = arcDistance, InvSplitTime = time,
                VertCount = Int(t, "vertCount"), IndCount = Int(t, "indCount"),
                Verts = Items(t.GetProperty("verts")).Select(v =>
                    new FxTrailVertex(v.GetProperty("pos")[0].GetSingle(), v.GetProperty("pos")[1].GetSingle(),
                        v.GetProperty("normal")[0].GetSingle(), v.GetProperty("normal")[1].GetSingle(), Float(v, "texCoord"))).ToArray(),
                Inds = Items(t.GetProperty("inds")).Select(v => v.GetUInt16()).ToArray()
            }
        };
    }

    private static FxElemVisualState State(JsonElement value)
    {
        var color = value.GetProperty("color");
        var size = value.GetProperty("size");
        // IW3 PC FX visual samples use D3DCOLOR byte order, including the amplitude sample.
        return new FxElemVisualState(new FxElemColor(color[2].GetByte(), color[1].GetByte(), color[0].GetByte(),
            color[3].GetByte()), Float(value, "rotationDelta"), Float(value, "rotationTotal"), size[0].GetSingle(),
            size[1].GetSingle(), Float(value, "scale"));
    }
    private static FxElemVelStateInFrame Frame(JsonElement v) =>
        new(VectorRange(v.GetProperty("velocity")), VectorRange(v.GetProperty("totalDelta")));
    private static FxElemVec3Range VectorRange(JsonElement v) => new(Vector(v.GetProperty("base")), Vector(v.GetProperty("amplitude")));
    private static Vec3 Vector(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    private static FxFloatRange Range(JsonElement v) => new(Float(v, "base"), Float(v, "amplitude"));
    private static FxIntRange IntRange(JsonElement v) => new(Int(v, "base"), Int(v, "amplitude"));
    private static FxFloatRange[] Ranges(JsonElement v, string key) => Items(v.GetProperty(key)).Select(Range).ToArray();
    private static IEnumerable<JsonElement> Items(JsonElement v) => v.ValueKind == JsonValueKind.Null ? [] : v.EnumerateArray();
    private static int Int(JsonElement v, string key) => v.GetProperty(key).GetInt32();
    private static float Float(JsonElement v, string key) => v.GetProperty(key).GetSingle();
    private static byte Byte(JsonElement v, string key) => v.GetProperty(key).GetByte();
    private static string? Text(JsonElement v, string key) => v.GetProperty(key).GetString();
    private static string? AssetName(JsonElement v) => v.ValueKind == JsonValueKind.Null ? null : Text(v, "name");
}
