using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.AssetExchange.SourceFormat.Material;

public sealed record WaterMaterialDefinition(
    string Name,
    string SourceMaterial,
    float Red,
    float Green,
    float Blue,
    float WaveIntensity,
    float AnimationSpeed,
    float FresnelMinimum,
    float FresnelMaximum,
    float FresnelExponent,
    OceanWaveSettings? Ocean = null);

public static class WaterMaterialAuthoring
{
    public const string MapPropertyName = "_iw4radiant_water";
    public const float MaximumWaveIntensity = 8;
    public const float MinimumAnimationSpeed = 0.05f;
    public const float MaximumAnimationSpeed = 8;
    public const float MinimumFresnelExponent = 0.01f;
    public const float MaximumFresnelExponent = 32;

    // Presentation opacity shared by the editor and the generated native water script.
    public const float UnderwaterOpacity = 0.24f;
    public const float UnderwaterFadeSeconds = 0.25f;
    public const float UnderwaterBoundaryInset = 0.5f;

    private const int ManifestVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool IsAuthoredMaterialName(string name) =>
        name.StartsWith("w/iw4r_", StringComparison.Ordinal) ||
        name.StartsWith("wc/iw4r_", StringComparison.Ordinal);

    public static MaterialVec4 CreateUnderwaterTint(float red, float green, float blue) =>
        new(red, green, blue, UnderwaterOpacity);

    public static WaterMaterialDefinition CreateDefinition(
        string sourceMaterial,
        float red,
        float green,
        float blue,
        float waveIntensity,
        float animationSpeed,
        float fresnelMinimum,
        float fresnelMaximum,
        float fresnelExponent,
        OceanWaveSettings? ocean = null)
    {
        ocean = ocean?.Normalize();
        ValidateSourceName(sourceMaterial);
        ValidateSettings(red, green, blue, waveIntensity, animationSpeed,
            fresnelMinimum, fresnelMaximum, fresnelExponent);
        red = NormalizeZero(red);
        green = NormalizeZero(green);
        blue = NormalizeZero(blue);
        waveIntensity = NormalizeZero(waveIntensity);
        fresnelMinimum = NormalizeZero(fresnelMinimum);
        fresnelMaximum = NormalizeZero(fresnelMaximum);
        float[] values = [red, green, blue, waveIntensity, animationSpeed, fresnelMinimum, fresnelMaximum, fresnelExponent];
        if (ocean is not null) values = [.. values, ocean.Height, ocean.Wavelength, ocean.Speed, ocean.Direction];
        string name = CreateMaterialName(sourceMaterial, values);
        return new WaterMaterialDefinition(name, sourceMaterial, red, green, blue,
            waveIntensity, animationSpeed, fresnelMinimum, fresnelMaximum, fresnelExponent, ocean);
    }

    public static IReadOnlyDictionary<string, WaterMaterialDefinition> ReadDefinitions(
        IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var result = new Dictionary<string, WaterMaterialDefinition>(StringComparer.Ordinal);
        if (!properties.TryGetValue(MapPropertyName, out string? source) || string.IsNullOrWhiteSpace(source))
            return result;
        try
        {
            using JsonDocument document = JsonDocument.Parse(source);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.Number || version.GetInt32() != ManifestVersion ||
                !root.TryGetProperty("materials", out JsonElement materials) ||
                materials.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Expected a version 1 authored-water manifest.");
            foreach (JsonElement material in materials.EnumerateArray())
            {
                if (material.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("An authored-water entry must be an object.");
                string name = RequiredString(material, "name");
                WaterMaterialDefinition definition = CreateDefinition(
                    RequiredString(material, "source"),
                    RequiredFloat(material, "red"),
                    RequiredFloat(material, "green"),
                    RequiredFloat(material, "blue"),
                    RequiredFloat(material, "intensity"),
                    RequiredFloat(material, "speed"),
                    RequiredFloat(material, "fresnelMinimum"),
                    RequiredFloat(material, "fresnelMaximum"),
                    RequiredFloat(material, "fresnelExponent"),
                    material.TryGetProperty("ocean", out JsonElement ocean)
                        ? new OceanWaveSettings(RequiredFloat(ocean, "height"), RequiredFloat(ocean, "wavelength"),
                            RequiredFloat(ocean, "speed"), RequiredFloat(ocean, "direction"))
                        : null);
                if (!string.Equals(name, definition.Name, StringComparison.Ordinal))
                    throw new InvalidDataException($"Authored water material '{name}' does not match its saved values.");
                if (!result.TryAdd(name, definition))
                    throw new InvalidDataException($"Authored water material '{name}' is defined more than once.");
            }
            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The authored-water map property is not valid JSON.", exception);
        }
    }

    public static void WriteDefinitions(
        IDictionary<string, string> properties,
        IEnumerable<WaterMaterialDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(definitions);
        WaterMaterialDefinition[] ordered = definitions.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (WaterMaterialDefinition definition in ordered)
        {
            WaterMaterialDefinition validated = CreateDefinition(definition.SourceMaterial,
                definition.Red, definition.Green, definition.Blue, definition.WaveIntensity,
                definition.AnimationSpeed, definition.FresnelMinimum, definition.FresnelMaximum,
                definition.FresnelExponent, definition.Ocean);
            if (!string.Equals(definition.Name, validated.Name, StringComparison.Ordinal))
                throw new InvalidDataException($"Authored water material '{definition.Name}' does not match its values.");
            if (!distinct.Add(definition.Name))
                throw new InvalidDataException($"Authored water material '{definition.Name}' is defined more than once.");
        }
        if (ordered.Length == 0)
        {
            properties.Remove(MapPropertyName);
            return;
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", ManifestVersion);
            writer.WriteStartArray("materials");
            foreach (WaterMaterialDefinition definition in ordered)
            {
                writer.WriteStartObject();
                writer.WriteString("name", definition.Name);
                writer.WriteString("source", definition.SourceMaterial);
                writer.WriteNumber("red", definition.Red);
                writer.WriteNumber("green", definition.Green);
                writer.WriteNumber("blue", definition.Blue);
                writer.WriteNumber("intensity", definition.WaveIntensity);
                writer.WriteNumber("speed", definition.AnimationSpeed);
                writer.WriteNumber("fresnelMinimum", definition.FresnelMinimum);
                writer.WriteNumber("fresnelMaximum", definition.FresnelMaximum);
                writer.WriteNumber("fresnelExponent", definition.FresnelExponent);
                if (definition.Ocean is { } ocean)
                {
                    writer.WriteStartObject("ocean");
                    writer.WriteNumber("height", ocean.Height);
                    writer.WriteNumber("wavelength", ocean.Wavelength);
                    writer.WriteNumber("speed", ocean.Speed);
                    writer.WriteNumber("direction", ocean.Direction);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        properties[MapPropertyName] = StrictUtf8.GetString(stream.ToArray());
    }

    public static void MergeDefinitions(
        IDictionary<string, string> destination,
        IReadOnlyDictionary<string, string> source)
    {
        var destinationProperties = new Dictionary<string, string>(destination, StringComparer.Ordinal);
        var merged = ReadDefinitions(destinationProperties).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach ((string name, WaterMaterialDefinition definition) in ReadDefinitions(source))
            if (merged.TryGetValue(name, out WaterMaterialDefinition? existing) && existing != definition)
                throw new InvalidDataException($"Authored water material '{name}' has conflicting definitions.");
            else
                merged[name] = definition;
        WriteDefinitions(destination, merged.Values);
    }

    public static MaterialWater CreateWater(MaterialWater source, WaterMaterialDefinition definition,
        GfxImageAsset? image = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateDefinition(definition);
        int count = checked(source.M * source.N);
        if (source.M <= 0 || source.N <= 0 || source.H0X.Count != count || source.H0Y.Count != count ||
            source.WTerm.Count != count)
            throw new InvalidDataException($"Water material '{definition.SourceMaterial}' has inconsistent spectrum dimensions.");
        return new MaterialWater
        {
            Writable = source.Writable,
            M = source.M,
            N = source.N,
            Lx = source.Lx,
            Lz = source.Lz,
            Gravity = source.Gravity,
            WindVelocity = source.WindVelocity,
            WindDirection = source.WindDirection,
            Amplitude = source.Amplitude,
            CodeConstant = source.CodeConstant,
            H0X = Scale(source.H0X, definition.WaveIntensity, definition.SourceMaterial, "H0X"),
            H0Y = Scale(source.H0Y, definition.WaveIntensity, definition.SourceMaterial, "H0Y"),
            WTerm = Scale(source.WTerm, definition.AnimationSpeed, definition.SourceMaterial, "WTerm"),
            Image = image ?? source.Image
        };
    }

    public static MaterialAsset CreateMaterial(MaterialAsset source, WaterMaterialDefinition definition,
        out GfxImageAsset waterImage)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateDefinition(definition);
        if (!string.Equals(source.Info.Name, definition.SourceMaterial, StringComparison.Ordinal))
            throw new InvalidDataException($"Authored water material '{definition.Name}' requires source '{definition.SourceMaterial}'.");
        string? techniqueSet = source.TechniqueSet?.Name;
        if (techniqueSet is not ("w_water" or "wc_water") ||
            source.Info.SurfaceTypeBits != MaterialSurfaceTypeBits.Water ||
            source.Info.GameFlags != (MaterialGameFlags.NoMarks | MaterialGameFlags.HasReflection))
            throw new NotSupportedException($"Source material '{definition.SourceMaterial}' is outside the supported native water profile.");
        MaterialTextureDef[] sourceWater = source.Textures.Where(texture => texture.Semantic == TextureSemantic.WaterMap).ToArray();
        if (sourceWater.Length != 1 || sourceWater[0].Image is not null ||
            sourceWater[0].Water is not { Image: { } sourceImage } water)
            throw new InvalidDataException($"Source material '{definition.SourceMaterial}' requires exactly one native waterMap.");
        GfxImageAsset createdWaterImage = CreateWaterImage(definition, sourceImage);
        waterImage = createdWaterImage;
        MaterialTextureDef[] textures = source.Textures.Select(texture => new MaterialTextureDef
        {
            NameHash = texture.NameHash,
            NameStart = texture.NameStart,
            NameEnd = texture.NameEnd,
            SamplerState = texture.SamplerState,
            Semantic = texture.Semantic,
            Image = texture.Image,
            Water = texture.Semantic == TextureSemantic.WaterMap
                ? CreateWater(water, definition, createdWaterImage)
                : null
        }).ToArray();
        int waterColorCount = 0, environmentCount = 0;
        MaterialConstantDef[] constants = source.Constants.Select(constant =>
        {
            MaterialVec4 literal = constant.Literal;
            if (MatchesName(constant.NameBytes, "waterColor"))
            {
                waterColorCount++;
                literal = new MaterialVec4(definition.Red, definition.Green, definition.Blue, literal.W);
            }
            else if (MatchesName(constant.NameBytes, "envMapParms"))
            {
                environmentCount++;
                literal = new MaterialVec4(definition.FresnelMinimum, definition.FresnelMaximum,
                    definition.FresnelExponent, literal.W);
            }
            return new MaterialConstantDef { NameHash = constant.NameHash, NameBytes = constant.NameBytes.ToArray(), Literal = literal };
        }).ToArray();
        if (waterColorCount != 1 || environmentCount != 1)
            throw new InvalidDataException($"Source material '{definition.SourceMaterial}' requires one waterColor and one envMapParms constant.");
        return new MaterialAsset
        {
            Info = new MaterialInfo
            {
                Name = definition.Name,
                GameFlags = source.Info.GameFlags,
                SortKey = MaterialSortKey.Opaque,
                TextureAtlasRowCount = source.Info.TextureAtlasRowCount,
                TextureAtlasColumnCount = source.Info.TextureAtlasColumnCount,
                DrawSurf = source.Info.DrawSurf,
                SurfaceTypeBits = source.Info.SurfaceTypeBits,
                HashIndex = source.Info.HashIndex,
                Pad16 = source.Info.Pad16
            },
            StateBitsEntries = source.StateBitsEntries.ToArray(),
            TextureCount = checked((byte)textures.Length),
            ConstantCount = checked((byte)constants.Length),
            StateBitsCount = checked((byte)source.StateBits.Count),
            StateFlags = (source.StateFlags & ~(MaterialStateFlags.CullBack | MaterialStateFlags.CullFront)) |
                MaterialStateFlags.WritesDepth | MaterialStateFlags.UsesDepthBuffer,
            CameraRegion = GfxCameraRegionType.LitOpaque,
            XStringCount = checked((byte)source.XStrings.Count),
            Pad43 = source.Pad43,
            InlineTechniqueSlotStateBits = source.InlineTechniqueSlotStateBits.ToArray(),
            Pad8E = source.Pad8E,
            RuntimeTechniqueSlotStateBits = source.RuntimeTechniqueSlotStateBits.ToArray(),
            TechniqueSet = OceanMaterialShaders.Create(source.TechniqueSet ??
                throw new InvalidDataException("Missing source water technique set."), definition),
            Textures = textures,
            Constants = constants,
            StateBits = CreateSurfaceStates(source),
            XStrings = source.XStrings.Select(value => new MaterialXStringEntry(value.Index, default, value.Value)).ToArray()
        };
    }

    private static GfxStateBits[] CreateSurfaceStates(MaterialAsset source)
    {
        HashSet<byte> colorStates = source.StateBitsEntries.Where((_, slot) => slot == (int)MaterialTechniqueType.Unlit ||
                slot >= (int)MaterialTechniqueType.Lit && slot <= (int)MaterialTechniqueType.LitInstancedSunDfog)
            .Select(entry => entry.StateBitsIndex).ToHashSet();
        return source.StateBits.Select((state, index) =>
        {
            uint[] bits = state.LoadBits.ToArray();
            if (colorStates.Contains(checked((byte)index)))
            {
                // Brush water has opaque coverage. Write depth before glass/FX,
                // and shade the same surface from either side without duplicate faces.
                bits[0] = (bits[0] & ~(GfxStateBitsEncoding.CullFaceMask |
                    GfxStateBitsEncoding.BlendOperationRgbMask | GfxStateBitsEncoding.BlendOperationAlphaMask |
                    GfxStateBitsEncoding.SourceBlendRgbMask | GfxStateBitsEncoding.DestinationBlendRgbMask |
                    GfxStateBitsEncoding.SourceBlendAlphaMask | GfxStateBitsEncoding.DestinationBlendAlphaMask |
                    GfxStateBitsEncoding.AlphaTestMask)) |
                    ((uint)GfxCullFace.None << GfxStateBitsEncoding.CullFaceShift) | (uint)GfxStateBits0Flags.AlphaTestDisabled;
                bits[1] |= (uint)GfxStateBits1Flags.DepthWrite;
            }
            return new GfxStateBits { LoadBits = bits, CommandWordCount = 0 };
        }).ToArray();
    }

    private static GfxImageAsset CreateWaterImage(WaterMaterialDefinition definition, GfxImageAsset source)
    {
        if (source.TextureSemantic != TextureSemantic.WaterMap || source.PayloadByteCount <= 0 ||
            source.Width == 0 || source.Height == 0 || source.Depth == 0)
            throw new InvalidDataException($"Water material '{definition.SourceMaterial}' has no complete native backing-image layout.");
        return new GfxImageAsset
        {
            Format = source.Format,
            LevelCount = source.LevelCount,
            DimensionCount = source.DimensionCount,
            MultiFaceControl = source.MultiFaceControl,
            TextureControl1 = source.TextureControl1,
            Width = source.Width,
            Height = source.Height,
            Depth = source.Depth,
            MemoryLocation = source.SerializedMemoryLocation,
            MinLodControl = source.MinLodControl,
            RenderTargetPitch = source.RenderTargetPitch,
            PixelsOffset = source.SerializedPixelsOffset,
            MapType = source.MapType,
            TextureSemantic = source.TextureSemantic,
            Category = source.Category,
            UseSrgbReads = source.UseSrgbReads,
            CardMemory = source.CardMemory,
            BaseWidth = source.BaseWidth,
            BaseHeight = source.BaseHeight,
            BaseDepth = source.BaseDepth,
            BaseLevelCount = source.BaseLevelCount,
            Cached = source.Cached,
            StreamData = source.StreamData.Select(entry => new GfxImageStreamData(
                entry.Width, entry.Height, entry.LevelSizeAndOffset)).ToArray(),
            PayloadByteCount = source.PayloadByteCount,
            PayloadBytes = new byte[source.PayloadByteCount],
            Name = CreateWaterImageName(definition.Name)
        };
    }

    private static IReadOnlyList<float> Scale(IReadOnlyList<float> values, float multiplier,
        string material, string field)
    {
        var result = new float[values.Count];
        for (int index = 0; index < result.Length; index++)
        {
            float value = values[index] * multiplier;
            if (!float.IsFinite(value))
                throw new InvalidDataException($"Water material '{material}' produces a non-finite {field} spectrum value.");
            result[index] = value;
        }
        return result;
    }

    private static string CreateMaterialName(string source, params float[] values)
    {
        string family = source.StartsWith("wc/", StringComparison.Ordinal) ? "wc/" : "w/";
        string stem = source[(source.IndexOf('/') + 1)..].Split('/').Last();
        stem = new string(stem.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_').ToArray()).Trim('_');
        if (stem.Length == 0) stem = "water";
        if (stem.Length > 20) stem = stem[..20];
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
        {
            writer.Write(source);
            foreach (float value in values) writer.Write(value);
        }
        string hash = Convert.ToHexString(SHA256.HashData(stream.ToArray()).AsSpan(0, 12)).ToLowerInvariant();
        string name = $"{family}iw4r_{stem}_{hash}";
        if (Encoding.Latin1.GetByteCount(name) >= 64)
            throw new InvalidDataException("The generated authored water material name exceeds the native BSP limit.");
        return name;
    }

    private static string CreateWaterImageName(string materialName) =>
        "iw4r_water_" + Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(materialName)).AsSpan(0, 12)).ToLowerInvariant();

    private static void ValidateDefinition(WaterMaterialDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        WaterMaterialDefinition expected = CreateDefinition(definition.SourceMaterial, definition.Red, definition.Green,
            definition.Blue, definition.WaveIntensity, definition.AnimationSpeed, definition.FresnelMinimum,
            definition.FresnelMaximum, definition.FresnelExponent, definition.Ocean);
        if (!string.Equals(expected.Name, definition.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"Authored water material '{definition.Name}' does not match its values.");
    }

    private static void ValidateSourceName(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if ((!source.StartsWith("w/", StringComparison.Ordinal) && !source.StartsWith("wc/", StringComparison.Ordinal)) ||
            source.StartsWith("w/iw4r_", StringComparison.Ordinal) || source.StartsWith("wc/iw4r_", StringComparison.Ordinal) ||
            source.Contains('\0') || Encoding.Latin1.GetByteCount(source) >= 64 || source.Any(character => character > byte.MaxValue))
            throw new ArgumentException("A source water material requires a native w/ or wc/ name within the 63-byte BSP limit.", nameof(source));
    }

    private static void ValidateSettings(float red, float green, float blue, float intensity, float speed,
        float minimum, float maximum, float exponent)
    {
        RequireRange(red, 0, 1, "red");
        RequireRange(green, 0, 1, "green");
        RequireRange(blue, 0, 1, "blue");
        RequireRange(intensity, 0, MaximumWaveIntensity, "wave intensity");
        RequireRange(speed, MinimumAnimationSpeed, MaximumAnimationSpeed, "animation speed");
        RequireRange(minimum, 0, 1, "Fresnel minimum");
        RequireRange(maximum, 0, 1, "Fresnel maximum");
        if (minimum > maximum) throw new ArgumentException("Fresnel minimum cannot exceed Fresnel maximum.");
        RequireRange(exponent, MinimumFresnelExponent, MaximumFresnelExponent, "Fresnel exponent");
    }

    private static void RequireRange(float value, float minimum, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
    }

    private static float NormalizeZero(float value) => value == 0 ? 0 : value;

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { } text || string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"An authored-water entry requires '{name}'.");
        return text;
    }

    private static float RequiredFloat(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(out float result))
            throw new InvalidDataException($"An authored-water entry requires finite numeric '{name}'.");
        return result;
    }

    private static bool MatchesName(IReadOnlyList<byte> bytes, string name)
    {
        if (bytes.Count != 12 || name.Length >= bytes.Count) return false;
        for (int index = 0; index < bytes.Count; index++)
        {
            byte expected = index < name.Length ? (byte)name[index] : (byte)0;
            if (bytes[index] != expected) return false;
        }
        return true;
    }
}
