using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Assets.Fx;

namespace IW4.Studio.Fx;

public enum FxSourceField
{
    Material, SizeX, SizeY, Red, Green, Blue, Opacity,
    LifeBase, LifeAmplitude, SpawnInterval, SpawnCount,
    Gravity, Rotation, Speed
}

public sealed record FxSourceLayer(int Index, string Group, string Type, string Material,
    bool Editable, string Timing);

/// <summary>Local FX layer edits. Whole-tree snapshots retain unsupported source fields.</summary>
public sealed class FxSourceDocument
{
    private const int HasGravityFlag = 0x04000000;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly FxExchange _exchange = new();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private JsonObject _root;
    private FxEffectDefAsset _effect;
    private readonly string _initialJson;

    private FxSourceDocument(JsonObject root, FxEffectDefAsset effect, string assetName)
    {
        _root = root;
        _effect = effect;
        AssetName = assetName;
        _initialJson = Serialize(root);
    }

    public string AssetName { get; }
    public FxEffectDefAsset Effect => _effect;
    public string Json => Serialize(_root);
    public bool IsDirty => !string.Equals(Json, _initialJson,
        StringComparison.Ordinal);
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;

    public static FxSourceDocument FromJson(string json, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        JsonObject root = JsonNode.Parse(json) as JsonObject ??
            throw new InvalidDataException("The FX graph root must be a JSON object.");
        FxEffectDefAsset effect = new FxExchange().LinkJson(Serialize(root), assetName);
        return new FxSourceDocument(root, effect, assetName);
    }

    public IReadOnlyList<FxSourceLayer> Layers
    {
        get
        {
            JsonArray array = Elements(_root);
            int looping = Number(_root["elemDefCountLooping"]);
            int oneShot = Number(_root["elemDefCountOneShot"]);
            return Enumerable.Range(0, array.Count).Select(index =>
            {
                JsonObject element = Element(_root, index);
                int typeNumber = Number(element["elemType"]);
                string type = (FxElemType)typeNumber switch
                {
                    FxElemType.SpriteBillboard => "Billboard",
                    FxElemType.SpriteOriented => "Oriented sprite",
                    FxElemType.SparkCloud => "Spark cloud",
                    FxElemType.SparkFountain => "Spark fountain",
                    FxElemType.OmniLight => "Omni light",
                    FxElemType.SpotLight => "Spot light",
                    FxElemType value when Enum.IsDefined(value) => value.ToString(),
                    _ => $"Type {typeNumber}"
                };
                string group = index < looping ? "Looping" : index < looping + oneShot
                    ? "One shot" : "Emitted";
                bool editable = group != "Emitted" && IsEditable(element);
                string material = editable ? Text(((JsonObject)((JsonObject)element["visuals"]!)["visual"]!)["name"])
                    .TrimStart(',') : "Preserved source layer";
                JsonObject spawn = (JsonObject)element["spawn"]!;
                string timing = group == "Looping"
                    ? $"Every {Number(spawn["loopingIntervalMsec"])} ms · " +
                      (Number(spawn["count"]) == int.MaxValue ? "continuous" : $"{Number(spawn["count"])} spawns")
                    : group == "One shot" ? "Triggered once" : "Child emission";
                return new FxSourceLayer(index, group, type, material, editable, timing);
            }).ToArray();
        }
    }

    public string ReadField(int index, FxSourceField field)
    {
        JsonObject element = EditableElement(_root, index);
        JsonObject spawn = (JsonObject)element["spawn"]!;
        JsonObject life = (JsonObject)element["lifeSpanMsec"]!;
        return field switch
        {
            FxSourceField.Material => Text(((JsonObject)((JsonObject)element["visuals"]!)["visual"]!)["name"]).TrimStart(','),
            FxSourceField.SizeX => Format(VisualPeak(element, "size0")),
            FxSourceField.SizeY => Format((Number(element["flags"]) & 0x10000000) != 0
                ? VisualPeak(element, "size1") : VisualPeak(element, "size0")),
            FxSourceField.Red => ColorPeak(element, "r").ToString(CultureInfo.InvariantCulture),
            FxSourceField.Green => ColorPeak(element, "g").ToString(CultureInfo.InvariantCulture),
            FxSourceField.Blue => ColorPeak(element, "b").ToString(CultureInfo.InvariantCulture),
            FxSourceField.Opacity => ColorPeak(element, "a").ToString(CultureInfo.InvariantCulture),
            FxSourceField.LifeBase => Number(life["base"]).ToString(CultureInfo.InvariantCulture),
            FxSourceField.LifeAmplitude => Number(life["amplitude"]).ToString(CultureInfo.InvariantCulture),
            FxSourceField.SpawnInterval => Number(spawn["loopingIntervalMsec"]).ToString(CultureInfo.InvariantCulture),
            FxSourceField.SpawnCount => Number(spawn["count"]) == int.MaxValue ? "∞" :
                Number(spawn["count"]).ToString(CultureInfo.InvariantCulture),
            FxSourceField.Gravity => Format(NumberFloat(((JsonObject)element["gravity"]!)["base"])),
            FxSourceField.Rotation => Format(NumberFloat(((JsonObject)element["initialRotation"]!)["base"])),
            FxSourceField.Speed => Format(MotionSpeed(element)),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    public void SetField(int index, FxSourceField field, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Apply(root =>
        {
            JsonObject element = EditableElement(root, index);
            JsonObject spawn = (JsonObject)element["spawn"]!;
            JsonObject life = (JsonObject)element["lifeSpanMsec"]!;
            switch (field)
            {
                case FxSourceField.Material:
                    string name = value.Trim().TrimStart(',');
                    if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
                        throw new InvalidDataException("Choose a material name.");
                    ((JsonObject)((JsonObject)element["visuals"]!)["visual"]!)["name"] = name;
                    break;
                case FxSourceField.SizeX:
                    ScaleVisual(element, "size0", PositiveFloat(value));
                    break;
                case FxSourceField.SizeY:
                    if ((Number(element["flags"]) & 0x10000000) == 0)
                        foreach (JsonNode? sample in (JsonArray)element["visSamples"]!)
                            foreach (string arm in new[] { "base", "amplitude" })
                            {
                                JsonObject values = (JsonObject)sample![arm]!;
                                values["size1"] = values["size0"]!.DeepClone();
                            }
                    ScaleVisual(element, "size1", PositiveFloat(value));
                    element["flags"] = Number(element["flags"]) | 0x10000000;
                    break;
                case FxSourceField.Red:
                case FxSourceField.Green:
                case FxSourceField.Blue:
                case FxSourceField.Opacity:
                    string channel = field switch
                    {
                        FxSourceField.Red => "r", FxSourceField.Green => "g",
                        FxSourceField.Blue => "b", _ => "a"
                    };
                    ScaleColor(element, channel, Byte(value));
                    break;
                case FxSourceField.LifeBase:
                    life["base"] = NonNegativeInt(value);
                    break;
                case FxSourceField.LifeAmplitude:
                    life["amplitude"] = NonNegativeInt(value);
                    break;
                case FxSourceField.SpawnInterval:
                    spawn["loopingIntervalMsec"] = NonNegativeInt(value);
                    break;
                case FxSourceField.SpawnCount:
                    spawn["count"] = value.Trim() == "∞" ? int.MaxValue : NonNegativeInt(value);
                    break;
                case FxSourceField.Gravity:
                    float gravity = FiniteFloat(value);
                    ((JsonObject)element["gravity"]!)["base"] = gravity;
                    if (gravity != 0)
                        element["flags"] = Number(element["flags"]) | HasGravityFlag;
                    break;
                case FxSourceField.Rotation:
                    ((JsonObject)element["initialRotation"]!)["base"] = FiniteFloat(value);
                    break;
                case FxSourceField.Speed:
                    ScaleMotion(element, PositiveFloat(value));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }
            if ((field is FxSourceField.SpawnInterval or FxSourceField.SpawnCount) &&
                index < Number(root["elemDefCountLooping"]))
                AdjustLoopingLife(root, _root);
        });
    }

    public void DuplicateLayer(int index)
    {
        Apply(root =>
        {
            JsonArray layers = Elements(root);
            JsonObject original = EditableElement(root, index);
            layers.Insert(index + 1, original.DeepClone());
            string group = index < Number(root["elemDefCountLooping"])
                ? "elemDefCountLooping" : "elemDefCountOneShot";
            root[group] = Number(root[group]) + 1;
            root["totalSize"] = checked(Number(root["totalSize"]) + SpriteStorageSize(original));
            if (group == "elemDefCountLooping") AdjustLoopingLife(root, _root);
        });
    }

    public void RemoveLayer(int index)
    {
        Apply(root =>
        {
            EditableElement(root, index);
            string group = index < Number(root["elemDefCountLooping"])
                ? "elemDefCountLooping" : "elemDefCountOneShot";
            JsonObject removed = Element(root, index);
            Elements(root).RemoveAt(index);
            root[group] = Number(root[group]) - 1;
            root["totalSize"] = checked(Number(root["totalSize"]) - SpriteStorageSize(removed));
            if (group == "elemDefCountLooping") AdjustLoopingLife(root, _root);
        });
    }

    public void StartFromTemplate(FxEffectDefAsset template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (Elements(_root).Count != 0)
            throw new InvalidOperationException("A template can only be chosen for an empty FX.");
        JsonObject candidate = JsonNode.Parse(_exchange.ToJson(template)) as JsonObject ??
            throw new InvalidDataException("The selected FX has no complete graph.");
        if (Elements(candidate).Count == 0)
            throw new InvalidDataException("Choose an FX with at least one layer.");
        string templateName = Text(candidate["name"]);
        candidate["name"] = AssetName;
        candidate["totalSize"] = checked(Number(candidate["totalSize"]) +
            Encoding.UTF8.GetByteCount(AssetName) - Encoding.UTF8.GetByteCount(templateName));
        string json = Serialize(candidate);
        FxEffectDefAsset effect = _exchange.LinkJson(json, AssetName);
        _undo.Push(Serialize(_root));
        _redo.Clear();
        _root = candidate;
        _effect = effect;
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Serialize(_root));
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Serialize(_root));
        Restore(_redo.Pop());
    }

    private void Apply(Action<JsonObject> edit)
    {
        string before = Serialize(_root);
        JsonObject candidate = (JsonObject)_root.DeepClone();
        edit(candidate);
        string after = Serialize(candidate);
        if (string.Equals(before, after, StringComparison.Ordinal)) return;
        FxEffectDefAsset effect = _exchange.LinkJson(after, AssetName);
        _undo.Push(before);
        _redo.Clear();
        _root = candidate;
        _effect = effect;
    }

    private void Restore(string snapshot)
    {
        JsonObject root = JsonNode.Parse(snapshot) as JsonObject ??
            throw new InvalidDataException("The FX undo snapshot is invalid.");
        FxEffectDefAsset effect = _exchange.LinkJson(Serialize(root), AssetName);
        _root = root;
        _effect = effect;
    }

    private static void AdjustLoopingLife(JsonObject root, JsonObject original)
    {
        int originalSchedule = LastLoopSpawn(original);
        if (Number(original["msecLoopingLife"]) == originalSchedule)
            root["msecLoopingLife"] = LastLoopSpawn(root);
    }

    private static int LastLoopSpawn(JsonObject root)
    {
        int loopingCount = Number(root["elemDefCountLooping"]);
        long end = 0;
        for (int index = 0; index < loopingCount; index++)
        {
            JsonObject spawn = (JsonObject)Element(root, index)["spawn"]!;
            int count = Number(spawn["count"]);
            int interval = Number(spawn["loopingIntervalMsec"]);
            if (count == int.MaxValue)
                return int.MaxValue;
            if (count > 0)
                end = Math.Max(end, (long)(count - 1) * interval);
        }
        return (int)Math.Min(end, int.MaxValue);
    }

    private static int SpriteStorageSize(JsonObject element)
    {
        int vel = ((JsonArray)element["velSamples"]!).Count;
        int vis = ((JsonArray)element["visSamples"]!).Count;
        // FX_AdditionalBytesNeededForElemDef for a single material visual:
        // fixed FxElemDef plus 0x30 bytes per vis and twice per velocity sample.
        // The IW4 linker writes each retained sample, including interval-zero rows.
        return checked(FxElemDef.SerializedSize + 0x30 * (vis + 2 * vel));
    }

    private static bool IsEditable(JsonObject element)
    {
        FxElemType type = (FxElemType)Number(element["elemType"]);
        if (type is not (FxElemType.SpriteBillboard or FxElemType.SpriteOriented or
                FxElemType.Tail or FxElemType.Cloud or FxElemType.SparkCloud) ||
            Number(element["visualCount"]) != 1)
            return false;
        return element["visuals"] is JsonObject visuals && visuals["visual"] is JsonObject visual &&
            Text(visual["kind"]) == "material" && visual["name"] is not null &&
            element["visSamples"] is JsonArray { Count: > 0 } && element["extended"] is null;
    }

    private static JsonObject EditableElement(JsonObject root, int index)
    {
        JsonObject element = Element(root, index);
        if (index >= Number(root["elemDefCountLooping"]) + Number(root["elemDefCountOneShot"]) ||
            !IsEditable(element))
            throw new InvalidOperationException("This FX layer is preserved read-only.");
        return element;
    }

    private static JsonObject Element(JsonObject root, int index) =>
        Elements(root)[index] as JsonObject ?? throw new InvalidDataException("FX layer is not an object.");

    private static JsonArray Elements(JsonObject root) =>
        root["elemDefs"] as JsonArray ?? throw new InvalidDataException("FX elements are missing.");

    private static float VisualPeak(JsonObject element, string property) =>
        ((JsonArray)element["visSamples"]!).SelectMany(sample =>
        {
            JsonObject value = (JsonObject)sample!;
            float basis = NumberFloat(((JsonObject)value["base"]!)[property]);
            float amplitude = NumberFloat(((JsonObject)value["amplitude"]!)[property]);
            return new[] { MathF.Abs(basis), MathF.Abs(basis + amplitude) };
        }).Max();

    private static int ColorPeak(JsonObject element, string channel) =>
        ((JsonArray)element["visSamples"]!).SelectMany(sample =>
            new[] { "base", "amplitude" }.Select(arm => Number((
                (JsonObject)((JsonObject)((JsonObject)sample!)[arm]!)["color"]!)[channel]))).Max();

    private static void ScaleVisual(JsonObject element, string property, float target)
    {
        float original = VisualPeak(element, property);
        foreach (JsonNode? sampleNode in (JsonArray)element["visSamples"]!)
        {
            JsonObject sample = (JsonObject)sampleNode!;
            JsonObject baseState = (JsonObject)sample["base"]!;
            JsonObject amplitude = (JsonObject)sample["amplitude"]!;
            baseState[property] = original == 0 ? target : NumberFloat(baseState[property]) * target / original;
            amplitude[property] = original == 0 ? 0 : NumberFloat(amplitude[property]) * target / original;
        }
    }

    private static void ScaleColor(JsonObject element, string channel, byte target)
    {
        int original = ColorPeak(element, channel);
        foreach (JsonNode? sampleNode in (JsonArray)element["visSamples"]!)
            foreach (string arm in new[] { "base", "amplitude" })
            {
                JsonObject color = (JsonObject)((JsonObject)((JsonObject)sampleNode!)[arm]!)["color"]!;
                color[channel] = original == 0 ? target :
                    Math.Clamp((int)MathF.Round(Number(color[channel]) * target / (float)original), 0, 255);
            }
    }

    private static float MotionSpeed(JsonObject element)
    {
        if (element["velSamples"] is not JsonArray { Count: > 0 } samples) return 0;
        return samples.Select(sample =>
        {
            JsonObject velocity = (JsonObject)((JsonObject)((JsonObject)sample!)["local"]!)["velocity"]!;
            JsonObject basis = (JsonObject)velocity["base"]!;
            JsonObject amplitude = (JsonObject)velocity["amplitude"]!;
            float x = NumberFloat(basis["x"]), y = NumberFloat(basis["y"]), z = NumberFloat(basis["z"]);
            float ax = x + NumberFloat(amplitude["x"]);
            float ay = y + NumberFloat(amplitude["y"]);
            float az = z + NumberFloat(amplitude["z"]);
            return MathF.Max(MathF.Sqrt(x * x + y * y + z * z),
                MathF.Sqrt(ax * ax + ay * ay + az * az));
        }).Max();
    }

    private static void ScaleMotion(JsonObject element, float target)
    {
        float original = MotionSpeed(element);
        if (original <= 0)
            throw new InvalidDataException("This layer has no authored local motion to scale.");
        float ratio = target / original;
        foreach (JsonNode? sampleNode in (JsonArray)element["velSamples"]!)
        {
            JsonObject local = (JsonObject)((JsonObject)sampleNode!)["local"]!;
            foreach (string field in new[] { "velocity", "totalDelta" })
                foreach (string arm in new[] { "base", "amplitude" })
                {
                    JsonObject vector = (JsonObject)((JsonObject)local[field]!)[arm]!;
                    foreach (string axis in new[] { "x", "y", "z" })
                        vector[axis] = NumberFloat(vector[axis]) * ratio;
                }
        }
    }

    private static string Serialize(JsonObject root) => root.ToJsonString(JsonOptions);
    private static string Text(JsonNode? value) => value?.GetValue<string>() ?? "";
    private static int Number(JsonNode? value) => value?.GetValue<int>() ?? 0;
    private static float NumberFloat(JsonNode? value) => value?.GetValue<float>() ?? 0;
    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static int NonNegativeInt(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value : throw new InvalidDataException("Enter a nonnegative whole number.");
    private static byte Byte(string text) =>
        byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value)
            ? value : throw new InvalidDataException("Enter a color channel from 0 to 255.");
    private static float FiniteFloat(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
        float.IsFinite(value) ? value : throw new InvalidDataException("Enter a finite number.");
    private static float PositiveFloat(string text)
    {
        float value = FiniteFloat(text);
        if (value <= 0) throw new InvalidDataException("Enter a value above zero.");
        return value;
    }
}
