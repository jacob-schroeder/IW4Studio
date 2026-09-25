using System.Text.Json;
using System.Text;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Fx;

/// <summary>Exports the materialized IW4 FxEffectDef graph as versioned JSON.</summary>
public sealed class FxExchange
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Imports a version-one native Fx graph from fx/&lt;assetName&gt;.json.</summary>
    public FxEffectDefAsset Link(string sourceDirectory, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "Fx");
        string path = Path.Combine(Path.GetFullPath(sourceDirectory), "fx", $"{name}.json");
        return LinkJson(File.ReadAllText(path), name);
    }

    /// <summary>Validates and imports an in-memory version-one native Fx graph.</summary>
    public FxEffectDefAsset LinkJson(string sourceJson, string assetName)
    {
        ArgumentNullException.ThrowIfNull(sourceJson);
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "Fx");
        using JsonDocument document = JsonDocument.Parse(sourceJson);
        JsonElement root = Object(document.RootElement, "Fx");
        if (String(root, "format", "Fx") != "iw4-fx-native-graph" ||
            Int(root, "version", "Fx") != 1)
            throw new InvalidDataException("Fx requires iw4-fx-native-graph version 1.");
        string storedName = String(root, "name", "Fx");
        if (!string.Equals(SourceOutput.NormalizeOwnedAssetName(storedName, "Fx"), name,
                StringComparison.Ordinal))
            throw new InvalidDataException($"Fx name '{storedName}' does not match requested name '{name}'.");
        FxElemDef[] elements = Array(root, "elemDefs", "Fx")
            .EnumerateArray().Select((value, index) => ReadElement(value, $"Fx.ElemDefs[{index}]")).ToArray();
        var asset = new FxEffectDefAsset
        {
            Name = storedName,
            Flags = Int(root, "flags", "Fx"),
            TotalSize = Int(root, "totalSize", "Fx"),
            MsecLoopingLife = Int(root, "msecLoopingLife", "Fx"),
            ElemDefCountLooping = Int(root, "elemDefCountLooping", "Fx"),
            ElemDefCountOneShot = Int(root, "elemDefCountOneShot", "Fx"),
            ElemDefCountEmission = Int(root, "elemDefCountEmission", "Fx"),
            ElemDefs = elements
        };
        int count;
        try { count = asset.ElemDefCount; }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Fx element counts overflow.", exception);
        }
        if (count < 0 || count != elements.Length)
            throw new InvalidDataException($"Fx declares {count} elements but contains {elements.Length}.");
        return asset;
    }

    private static FxElemDef ReadElement(JsonElement value, string path)
    {
        value = Object(value, path);
        int typeValue = Int(value, "elemType", path);
        if (typeValue < byte.MinValue || typeValue > byte.MaxValue ||
            !Enum.IsDefined((FxElemType)typeValue))
            throw new InvalidDataException($"{path}.ElemType has unsupported value {typeValue}.");
        FxElemType type = (FxElemType)typeValue;
        byte visualCount = Byte(value, "visualCount", path);
        byte velIntervals = Byte(value, "velIntervalCount", path);
        byte visIntervals = Byte(value, "visStateIntervalCount", path);
        FxElemVelStateSample[] velocities = ReadArray(value, "velSamples", path,
            (sample, samplePath) => new FxElemVelStateSample(
                ReadVelocityFrame(Property(sample, "local", samplePath), $"{samplePath}.Local"),
                ReadVelocityFrame(Property(sample, "world", samplePath), $"{samplePath}.World")));
        FxElemVisStateSample[] states = ReadArray(value, "visSamples", path,
            (sample, samplePath) => new FxElemVisStateSample(
                ReadVisualState(Property(sample, "base", samplePath), $"{samplePath}.Base"),
                ReadVisualState(Property(sample, "amplitude", samplePath), $"{samplePath}.Amplitude")));
        if (!ValidSampleCount(velocities.Length, velIntervals) ||
            !ValidSampleCount(states.Length, visIntervals))
            throw new InvalidDataException($"{path} sample arrays do not match interval counts.");
        FxElemDefVisuals inline = ReadVisuals(Property(value, "visuals", path), $"{path}.Visuals");
        FxElemDefVisuals[] visuals = ReadArray(value, "visualArray", path, ReadVisuals);
        FxElemMarkVisuals[] marks = ReadArray(value, "markVisualArray", path, ReadMark);
        if (type == FxElemType.Decal
            ? marks.Length != visualCount || visuals.Length != 0 || inline.Visual is not null
            : visualCount > 1
                ? visuals.Length != visualCount || marks.Length != 0 || inline.Visual is not null
                : visuals.Length != 0 || marks.Length != 0 || inline.Visual is null)
            throw new InvalidDataException($"{path} visual count or arm is incomplete.");
        if (type != FxElemType.Decal)
        {
            if (visualCount > 1)
            {
                for (int index = 0; index < visuals.Length; index++)
                    ValidateVisual(visuals[index].Visual, type, $"{path}.VisualArray[{index}]");
            }
            else
                ValidateVisual(inline.Visual, type, $"{path}.Visuals");
        }
        FxElemExtendedDef? extended = ReadExtended(Property(value, "extended", path), type, $"{path}.Extended");
        return new FxElemDef
        {
            Flags = Int(value, "flags", path),
            Spawn = Record<FxSpawnDef>(Property(value, "spawn", path), $"{path}.Spawn", "loopingIntervalMsec", "count"),
            SpawnRange = FloatRange(Property(value, "spawnRange", path), $"{path}.SpawnRange"),
            FadeInRange = FloatRange(Property(value, "fadeInRange", path), $"{path}.FadeInRange"),
            FadeOutRange = FloatRange(Property(value, "fadeOutRange", path), $"{path}.FadeOutRange"),
            SpawnFrustumCullRadius = Float(value, "spawnFrustumCullRadius", path),
            SpawnDelayMsec = IntRange(Property(value, "spawnDelayMsec", path), $"{path}.SpawnDelayMsec"),
            LifeSpanMsec = IntRange(Property(value, "lifeSpanMsec", path), $"{path}.LifeSpanMsec"),
            SpawnOrigin = ReadRanges(value, "spawnOrigin", path),
            SpawnOffsetRadius = FloatRange(Property(value, "spawnOffsetRadius", path), $"{path}.SpawnOffsetRadius"),
            SpawnOffsetHeight = FloatRange(Property(value, "spawnOffsetHeight", path), $"{path}.SpawnOffsetHeight"),
            SpawnAngles = ReadRanges(value, "spawnAngles", path),
            AngularVelocity = ReadRanges(value, "angularVelocity", path),
            InitialRotation = FloatRange(Property(value, "initialRotation", path), $"{path}.InitialRotation"),
            Gravity = FloatRange(Property(value, "gravity", path), $"{path}.Gravity"),
            ReflectionFactor = FloatRange(Property(value, "reflectionFactor", path), $"{path}.ReflectionFactor"),
            Atlas = Record<FxElemAtlas>(Property(value, "atlas", path), $"{path}.Atlas",
                "behavior", "index", "fps", "loopCount", "colIndexBits", "rowIndexBits", "entryCount"),
            ElemType = type,
            VisualCount = visualCount,
            VelIntervalCount = velIntervals,
            VisStateIntervalCount = visIntervals,
            VelSamples = velocities,
            VisSamples = states,
            Visuals = inline,
            VisualArray = visuals,
            MarkVisualArray = marks,
            CollBounds = new Bounds(
                Vec(Property(Property(value, "collBounds", path), "midPoint", $"{path}.CollBounds"), $"{path}.CollBounds.MidPoint"),
                Vec(Property(Property(value, "collBounds", path), "halfSize", $"{path}.CollBounds"), $"{path}.CollBounds.HalfSize")),
            EffectOnImpact = ReadReference(Property(value, "effectOnImpact", path), $"{path}.EffectOnImpact"),
            EffectOnDeath = ReadReference(Property(value, "effectOnDeath", path), $"{path}.EffectOnDeath"),
            EffectEmitted = ReadReference(Property(value, "effectEmitted", path), $"{path}.EffectEmitted"),
            EmitDist = FloatRange(Property(value, "emitDist", path), $"{path}.EmitDist"),
            EmitDistVariance = FloatRange(Property(value, "emitDistVariance", path), $"{path}.EmitDistVariance"),
            Extended = extended,
            SortOrder = Byte(value, "sortOrder", path),
            LightingFrac = Byte(value, "lightingFrac", path),
            UseItemClip = Byte(value, "useItemClip", path),
            FadeInfo = Byte(value, "fadeInfo", path)
        };
    }

    private static FxElemVelStateInFrame ReadVelocityFrame(JsonElement value, string path) => new(
        ReadVecRange(Property(value, "velocity", path), $"{path}.Velocity"),
        ReadVecRange(Property(value, "totalDelta", path), $"{path}.TotalDelta"));

    private static FxElemVec3Range ReadVecRange(JsonElement value, string path) => new(
        Vec(Property(value, "base", path), $"{path}.Base"),
        Vec(Property(value, "amplitude", path), $"{path}.Amplitude"));

    private static FxElemVisualState ReadVisualState(JsonElement value, string path) => new(
        Record<FxElemColor>(Property(value, "color", path), $"{path}.Color", "r", "g", "b", "a"),
        Float(value, "rotationDelta", path), Float(value, "rotationTotal", path),
        Float(value, "size0", path), Float(value, "size1", path), Float(value, "scale", path));

    private static FxElemDefVisuals ReadVisuals(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return new FxElemDefVisuals();
        value = Object(value, path);
        JsonElement arm = Property(value, "visual", path);
        return new FxElemDefVisuals { Visual = arm.ValueKind == JsonValueKind.Null ? null : ReadVisual(arm, $"{path}.Visual") };
    }

    private static FxElemVisual ReadVisual(JsonElement value, string path)
    {
        value = Object(value, path);
        return String(value, "kind", path) switch
        {
            "material" => new FxMaterialVisual { Material = MaterialReference(value, path) },
            "model" => new FxModelVisual { Model = ModelReference(value, path) },
            "effect" => new FxEffectVisual { EffectDef = ReadReference(Property(value, "effectDef", path), $"{path}.EffectDef") },
            "sound" => new FxSoundVisual { SoundName = OptionalName(value, "name", path) },
            "noChild" => new FxNoChildVisual { Reserved = Int(value, "reserved", path) },
            string kind => throw new InvalidDataException($"{path} has unsupported visual kind '{kind}'.")
        };
    }

    private static FxElemMarkVisuals ReadMark(JsonElement value, string path) => new()
    {
        Material0 = MaterialReference(Property(value, "material0", path), $"{path}.Material0"),
        Material1 = MaterialReference(Property(value, "material1", path), $"{path}.Material1")
    };

    private static MaterialAsset? MaterialReference(JsonElement value, string path)
    {
        string? name = OptionalName(value, "name", path);
        return name is null ? null : new MaterialAsset { Info = new MaterialInfo { Name = name } };
    }

    private static XModelAsset? ModelReference(JsonElement value, string path)
    {
        string? name = OptionalName(value, "name", path);
        return name is null ? null : new XModelAsset { Name = name };
    }

    private static FxEffectDefRef ReadReference(JsonElement value, string path) =>
        new() { Name = OptionalName(value, "name", path) };

    private static FxElemExtendedDef? ReadExtended(JsonElement value, FxElemType type, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        value = Object(value, path);
        FxElemExtendedDefKind expected = type switch
        {
            FxElemType.Trail => FxElemExtendedDefKind.Trail,
            FxElemType.SparkFountain => FxElemExtendedDefKind.SparkFountain,
            _ => FxElemExtendedDefKind.DefaultBytePayload
        };
        if (Int(value, "kind", path) != (int)expected)
            throw new InvalidDataException($"{path} requires {expected} for {type}.");
        JsonElement trail = Property(value, "trailDef", path);
        JsonElement spark = Property(value, "sparkFountainDef", path);
        JsonElement singleByte = Property(value, "defaultBytePayload", path);
        if ((trail.ValueKind != JsonValueKind.Null ? 1 : 0) +
            (spark.ValueKind != JsonValueKind.Null ? 1 : 0) +
            (singleByte.ValueKind != JsonValueKind.Null ? 1 : 0) != 1)
            throw new InvalidDataException($"{path} requires exactly one payload arm.");
        return expected switch
        {
            FxElemExtendedDefKind.Trail when trail.ValueKind != JsonValueKind.Null =>
                new FxElemExtendedDef { Kind = expected, TrailDef = ReadTrail(trail, $"{path}.TrailDef") },
            FxElemExtendedDefKind.SparkFountain when spark.ValueKind != JsonValueKind.Null =>
                new FxElemExtendedDef { Kind = expected, SparkFountainDef = Record<FxSparkFountainDef>(spark,
                    $"{path}.SparkFountainDef", "gravity", "bounceFrac", "bounceRand", "sparkSpacing",
                    "sparkLength", "sparkCount", "loopTime", "velMin", "velMax", "velConeFrac",
                    "restSpeed", "boostTime", "boostFactor") },
            FxElemExtendedDefKind.DefaultBytePayload when singleByte.ValueKind != JsonValueKind.Null =>
                new FxElemExtendedDef { Kind = expected, DefaultBytePayload = JsonByte(singleByte, $"{path}.DefaultBytePayload") },
            _ => throw new InvalidDataException($"{path} has the wrong payload arm for {type}.")
        };
    }

    private static FxTrailDef ReadTrail(JsonElement value, string path)
    {
        FxTrailVertex[] vertices = ReadArray(value, "verts", path,
            (vertex, vertexPath) => Record<FxTrailVertex>(vertex, vertexPath,
                "pos0", "pos1", "normal0", "normal1", "texCoord"));
        ushort[] indices = ReadArray(value, "inds", path, (index, indexPath) => JsonUInt16(index, indexPath));
        int vertexCount = Int(value, "vertCount", path);
        int indexCount = Int(value, "indCount", path);
        if (vertexCount != vertices.Length || indexCount != indices.Length ||
            indices.Any(index => index >= vertices.Length))
            throw new InvalidDataException($"{path} counts or indices do not match its arrays.");
        return new FxTrailDef
        {
            ScrollTimeMsec = Int(value, "scrollTimeMsec", path),
            RepeatDist = Int(value, "repeatDist", path),
            InvSplitDist = Float(value, "invSplitDist", path),
            InvSplitArcDist = Float(value, "invSplitArcDist", path),
            InvSplitTime = Float(value, "invSplitTime", path),
            VertCount = vertexCount,
            Verts = vertices,
            IndCount = indexCount,
            Inds = indices
        };
    }

    private static bool ValidSampleCount(int count, byte intervals) =>
        count == 0 ? intervals == 0 : count == intervals + 1;

    private static FxFloatRange[] ReadRanges(JsonElement value, string name, string path)
    {
        FxFloatRange[] ranges = ReadArray(value, name, path, FloatRange);
        if (ranges.Length != 3)
            throw new InvalidDataException($"{path}.{name} requires three ranges.");
        return ranges;
    }

    private static FxFloatRange FloatRange(JsonElement value, string path) =>
        Record<FxFloatRange>(value, path, "base", "amplitude");

    private static FxIntRange IntRange(JsonElement value, string path) =>
        Record<FxIntRange>(value, path, "base", "amplitude");

    private static Vec3 Vec(JsonElement value, string path) =>
        Record<Vec3>(value, path, "x", "y", "z");

    private static T Record<T>(JsonElement value, string path, params string[] fields) where T : class
    {
        value = Object(value, path);
        foreach (string field in fields)
        {
            if (Property(value, field, path).ValueKind == JsonValueKind.Null)
                throw new InvalidDataException($"{path}.{field} cannot be null.");
        }
        try { return value.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException($"{path} is null."); }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} contains an invalid {typeof(T).Name}.", exception);
        }
    }

    private static T[] ReadArray<T>(JsonElement parent, string name, string path,
        Func<JsonElement, string, T> read) => Array(parent, name, path).EnumerateArray()
            .Select((value, index) => read(value, $"{path}.{name}[{index}]")).ToArray();

    private static JsonElement Object(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Object ? value :
        throw new InvalidDataException($"{path} must be an object.");

    private static JsonElement Property(JsonElement parent, string name, string path)
    {
        Object(parent, path);
        return parent.TryGetProperty(name, out JsonElement value) ? value :
            throw new InvalidDataException($"{path}.{name} is missing.");
    }

    private static JsonElement Array(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind == JsonValueKind.Array ? value :
            throw new InvalidDataException($"{path}.{name} must be an array.");
    }

    private static string String(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! :
            throw new InvalidDataException($"{path}.{name} must be a string.");
    }

    private static string? OptionalName(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        string result = String(parent, name, path);
        SourceOutput.NormalizeReferencedAssetName(result, $"{path}.{name}");
        return result;
    }

    private static int Int(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result :
            throw new InvalidDataException($"{path}.{name} must be an Int32.");
    }

    private static byte Byte(JsonElement parent, string name, string path)
    {
        int value = Int(parent, name, path);
        return value is >= byte.MinValue and <= byte.MaxValue ? (byte)value :
            throw new InvalidDataException($"{path}.{name} must be a byte.");
    }

    private static byte JsonByte(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetByte(out byte result) ? result :
        throw new InvalidDataException($"{path} must be a byte.");

    private static ushort JsonUInt16(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt16(out ushort result) ? result :
        throw new InvalidDataException($"{path} must be a UInt16.");

    private static float Float(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float result) &&
            float.IsFinite(result) ? result :
            throw new InvalidDataException($"{path}.{name} must be a finite Single.");
    }

    public IReadOnlyList<string> Unlink(string sourceDirectory, FxEffectDefAsset asset)
    {
        IReadOnlyList<FxElemDef> elements = ValidatedElements(asset);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, "Fx");
        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            ($"fx/{name}.json", stream => WriteJson(stream, asset, elements))
        ]);
    }

    /// <summary>Projects a materialized FX into the same version-one graph without writing a source file.</summary>
    public string ToJson(FxEffectDefAsset asset)
    {
        IReadOnlyList<FxElemDef> elements = ValidatedElements(asset);
        using var stream = new MemoryStream();
        WriteJson(stream, asset, elements);
        return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\n');
    }

    private static IReadOnlyList<FxElemDef> ValidatedElements(FxEffectDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, "Fx");
        IReadOnlyList<FxElemDef> elements = asset.ElemDefs ??
            throw new InvalidDataException($"Fx '{name}' has no materialized element array.");
        int count;
        try
        {
            count = checked(asset.ElemDefCountLooping + asset.ElemDefCountOneShot + asset.ElemDefCountEmission);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException($"Fx '{name}' element counts overflow.", exception);
        }
        if (count < 0 || count != elements.Count)
        {
            throw new InvalidDataException(
                $"Fx '{name}' declares {count} elements but materialized {elements.Count}.");
        }

        return elements;
    }

    private static void WriteJson(
        Stream stream,
        FxEffectDefAsset asset,
        IReadOnlyList<FxElemDef> elements)
    {
        var graph = new
        {
            Format = "iw4-fx-native-graph",
            Version = 1,
            Name = asset.Name,
            SourceOffset = asset.Offset,
            SourceNamePointer = Pointer(asset.NamePointer),
            asset.Flags,
            asset.TotalSize,
            asset.MsecLoopingLife,
            asset.ElemDefCountLooping,
            asset.ElemDefCountOneShot,
            asset.ElemDefCountEmission,
            SourceElemDefsPointer = Pointer(asset.ElemDefsPointer),
            ElemDefs = elements.Select((element, index) =>
                Element(element ?? throw new InvalidDataException($"Fx.ElemDefs[{index}] is null."), index)).ToArray()
        };
        JsonSerializer.Serialize(stream, graph, JsonOptions);
        stream.WriteByte((byte)'\n');
    }

    private static object Element(FxElemDef element, int index)
    {
        string path = $"Fx.ElemDefs[{index}]";
        RequireCount(element.SpawnOrigin, 3, $"{path}.SpawnOrigin");
        RequireCount(element.SpawnAngles, 3, $"{path}.SpawnAngles");
        RequireCount(element.AngularVelocity, 3, $"{path}.AngularVelocity");
        RequireSamples(element.VelSamples, element.VelIntervalCount, $"{path}.VelSamples");
        RequireSamples(element.VisSamples, element.VisStateIntervalCount, $"{path}.VisSamples");
        if (!Enum.IsDefined(element.ElemType))
            throw new InvalidDataException($"{path} has unknown element type {(byte)element.ElemType}.");
        IReadOnlyList<FxElemDefVisuals> visuals = element.VisualArray ??
            throw new InvalidDataException($"{path}.VisualArray is null.");
        IReadOnlyList<FxElemMarkVisuals> marks = element.MarkVisualArray ??
            throw new InvalidDataException($"{path}.MarkVisualArray is null.");
        if (element.ElemType == FxElemType.Decal)
        {
            if (marks.Count != element.VisualCount || visuals.Count != 0 ||
                element.Visuals?.Visual is not null)
                throw new InvalidDataException($"{path} decal visual count or arm is incomplete.");
        }
        else if (element.VisualCount > 1)
        {
            if (visuals.Count != element.VisualCount || marks.Count != 0 ||
                element.Visuals?.Visual is not null)
                throw new InvalidDataException($"{path} array visual count or arm is incomplete.");
        }
        else if (visuals.Count != 0 || marks.Count != 0)
        {
            throw new InvalidDataException($"{path} inline visual arm is incomplete.");
        }
        if (element.ElemType != FxElemType.Decal)
        {
            if (element.VisualCount > 1)
            {
                for (int visualIndex = 0; visualIndex < visuals.Count; visualIndex++)
                    ValidateVisual(visuals[visualIndex]?.Visual, element.ElemType, $"{path}.VisualArray[{visualIndex}]");
            }
            else
            {
                ValidateVisual(element.Visuals?.Visual, element.ElemType, $"{path}.Visuals");
            }
        }
        if (element.Extended is null && element.ExtendedPointer.Raw != 0)
            throw new InvalidDataException($"{path}.Extended has a pointer but no materialized payload.");

        return new
        {
            element.Offset,
            element.Flags,
            Spawn = Required(element.Spawn, $"{path}.Spawn"),
            SpawnRange = Required(element.SpawnRange, $"{path}.SpawnRange"),
            FadeInRange = Required(element.FadeInRange, $"{path}.FadeInRange"),
            FadeOutRange = Required(element.FadeOutRange, $"{path}.FadeOutRange"),
            element.SpawnFrustumCullRadius,
            SpawnDelayMsec = Required(element.SpawnDelayMsec, $"{path}.SpawnDelayMsec"),
            LifeSpanMsec = Required(element.LifeSpanMsec, $"{path}.LifeSpanMsec"),
            element.SpawnOrigin,
            SpawnOffsetRadius = Required(element.SpawnOffsetRadius, $"{path}.SpawnOffsetRadius"),
            SpawnOffsetHeight = Required(element.SpawnOffsetHeight, $"{path}.SpawnOffsetHeight"),
            element.SpawnAngles,
            element.AngularVelocity,
            InitialRotation = Required(element.InitialRotation, $"{path}.InitialRotation"),
            Gravity = Required(element.Gravity, $"{path}.Gravity"),
            ReflectionFactor = Required(element.ReflectionFactor, $"{path}.ReflectionFactor"),
            Atlas = Required(element.Atlas, $"{path}.Atlas"),
            ElemType = (byte)element.ElemType,
            element.VisualCount,
            element.VelIntervalCount,
            element.VisStateIntervalCount,
            SourceVelSamplesPointer = Pointer(element.VelSamplesPointer),
            element.VelSamples,
            SourceVisSamplesPointer = Pointer(element.VisSamplesPointer),
            element.VisSamples,
            Visuals = Visuals(element.Visuals, $"{path}.Visuals"),
            SourceVisualArrayPointer = OptionalPointer(element.VisualArrayPointer),
            VisualArray = visuals.Select((visual, visualIndex) =>
                Visuals(visual, $"{path}.VisualArray[{visualIndex}]")).ToArray(),
            SourceMarkVisualArrayPointer = OptionalPointer(element.MarkVisualArrayPointer),
            MarkVisualArray = marks.Select((mark, markIndex) =>
                Mark(mark, $"{path}.MarkVisualArray[{markIndex}]")).ToArray(),
            CollBounds = Required(element.CollBounds, $"{path}.CollBounds"),
            EffectOnImpact = Reference(element.EffectOnImpact, $"{path}.EffectOnImpact"),
            EffectOnDeath = Reference(element.EffectOnDeath, $"{path}.EffectOnDeath"),
            EffectEmitted = Reference(element.EffectEmitted, $"{path}.EffectEmitted"),
            EmitDist = Required(element.EmitDist, $"{path}.EmitDist"),
            EmitDistVariance = Required(element.EmitDistVariance, $"{path}.EmitDistVariance"),
            SourceExtendedPointer = Pointer(element.ExtendedPointer),
            Extended = Extended(element.Extended, $"{path}.Extended"),
            element.SortOrder,
            element.LightingFrac,
            element.UseItemClip,
            element.FadeInfo
        };
    }

    private static object? Visuals(FxElemDefVisuals? visuals, string path)
    {
        if (visuals is null)
            return null;
        return new { visuals.Offset, Visual = Visual(visuals.Visual, $"{path}.Visual") };
    }

    private static object? Visual(FxElemVisual? visual, string path) => visual switch
    {
        null => null,
        FxMaterialVisual material => new
        {
            Kind = "material",
            SourcePointer = Pointer(material.MaterialPointer),
            Name = ReferencedName(material.MaterialPointer.Raw, material.Material?.SerializedAssetName, path)
        },
        FxModelVisual model => new
        {
            Kind = "model",
            SourcePointer = Pointer(model.ModelPointer),
            Name = ReferencedName(model.ModelPointer.Raw, model.Model?.SerializedAssetName, path)
        },
        FxEffectVisual effect => new
        {
            Kind = "effect",
            EffectDef = Reference(effect.EffectDef, $"{path}.EffectDef")
        },
        FxSoundVisual sound => new
        {
            Kind = "sound",
            SourcePointer = Pointer(sound.SoundNamePointer),
            Name = ReferencedName(sound.SoundNamePointer.Raw, sound.SoundName, path)
        },
        FxNoChildVisual noChild => new { Kind = "noChild", noChild.Reserved },
        _ => throw new InvalidDataException($"{path} has an unknown visual arm.")
    };

    private static void ValidateVisual(FxElemVisual? visual, FxElemType type, string path)
    {
        bool valid = type switch
        {
            FxElemType.Model => visual is FxModelVisual,
            FxElemType.OmniLight or FxElemType.SpotLight => visual is FxNoChildVisual,
            FxElemType.Sound => visual is FxSoundVisual,
            FxElemType.Runner => visual is FxEffectVisual,
            _ => visual is FxMaterialVisual
        };
        if (!valid)
            throw new InvalidDataException($"{path} has no visual arm for {type}.");
    }

    private static object Mark(FxElemMarkVisuals mark, string path)
    {
        ArgumentNullException.ThrowIfNull(mark);
        return new
        {
            mark.Offset,
            Material0 = new
            {
                SourcePointer = Pointer(mark.Material0Pointer),
                Name = ReferencedName(mark.Material0Pointer.Raw, mark.Material0?.SerializedAssetName, $"{path}.Material0")
            },
            Material1 = new
            {
                SourcePointer = Pointer(mark.Material1Pointer),
                Name = ReferencedName(mark.Material1Pointer.Raw, mark.Material1?.SerializedAssetName, $"{path}.Material1")
            }
        };
    }

    private static object Reference(FxEffectDefRef? reference, string path)
    {
        reference = Required(reference, path);
        return new
        {
            SourcePointer = Pointer(reference.NamePointer),
            Name = ReferencedName(reference.NamePointer.Raw, reference.Name, path)
        };
    }

    private static object? Extended(FxElemExtendedDef? extended, string path)
    {
        if (extended is null)
            return null;
        FxElemExtendedDefKind kind = extended.Kind;
        bool validPayload = kind switch
        {
            FxElemExtendedDefKind.Trail => extended.TrailDef is not null &&
                extended.SparkFountainDef is null && extended.DefaultBytePayload is null,
            FxElemExtendedDefKind.SparkFountain => extended.SparkFountainDef is not null &&
                extended.TrailDef is null && extended.DefaultBytePayload is null,
            FxElemExtendedDefKind.DefaultBytePayload => extended.DefaultBytePayload is not null &&
                extended.TrailDef is null && extended.SparkFountainDef is null,
            _ => false
        };
        if (!validPayload)
            throw new InvalidDataException($"{path} has no complete payload for {kind}.");
        FxTrailDef? trail = extended.TrailDef;
        if (trail is not null &&
            (trail.Verts is null || trail.Inds is null ||
             trail.VertCount != trail.Verts.Count || trail.IndCount != trail.Inds.Count))
            throw new InvalidDataException($"{path}.TrailDef counts do not match materialized arrays.");
        return new
        {
            Kind = (int)extended.Kind,
            TrailDef = trail is null ? null : new
            {
                trail.ScrollTimeMsec,
                trail.RepeatDist,
                trail.InvSplitDist,
                trail.InvSplitArcDist,
                trail.InvSplitTime,
                trail.VertCount,
                SourceVertsPointer = Pointer(trail.VertsPointer),
                trail.Verts,
                trail.IndCount,
                SourceIndsPointer = Pointer(trail.IndsPointer),
                trail.Inds
            },
            extended.SparkFountainDef,
            extended.DefaultBytePayload
        };
    }

    private static object Pointer<T>(XPointer<T> pointer) => new
    {
        pointer.Raw,
        ResolutionMode = (int)pointer.ResolutionMode,
        pointer.CellAddress
    };

    private static object? OptionalPointer<T>(XPointer<T>? pointer) =>
        pointer is { } value ? Pointer(value) : null;

    private static string? ReferencedName(int raw, string? name, string path)
    {
        if (name is null)
        {
            if (raw != 0)
                throw new InvalidDataException($"{path} has a pointer but no symbolic name.");
            return null;
        }
        SourceOutput.NormalizeReferencedAssetName(name, path);
        return name;
    }

    private static T Required<T>(T? value, string path) where T : class =>
        value ?? throw new InvalidDataException($"{path} is null.");

    private static void RequireCount<T>(IReadOnlyList<T>? items, int count, string path)
    {
        if (items is null || items.Count != count || items.Any(item => item is null))
            throw new InvalidDataException($"{path} requires {count} materialized values.");
    }

    private static void RequireSamples<T>(IReadOnlyList<T>? items, byte intervals, string path)
    {
        if (items is null || (items.Count != 0 && items.Count != intervals + 1) ||
            (items.Count == 0 && intervals != 0) || items.Any(item => item is null))
            throw new InvalidDataException($"{path} does not match its interval count.");
    }
}
