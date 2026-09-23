using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;

namespace Iw4Radiant.Materials;

internal static class MaterialCatalog
{
    private static readonly string[] ImageExtensions = [".dds", ".png", ".jpg", ".jpeg", ".bmp"];
    private const MaterialSamplerState ImagePreviewSampler = MaterialSamplerState.FilterLinear | MaterialSamplerState.MipMapLinear;

    internal static (Dictionary<string, MaterialSource> Materials, int Unsupported) Read(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Asset folder '{root}' does not exist.");
        string selectedName = Path.GetFileName(root);
        if (Directory.GetParent(root) is { } parent &&
            ((selectedName.Equals("images", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(parent.FullName, "materials"))) ||
             (selectedName.Equals("materials", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(parent.FullName, "images")))))
            root = parent.FullName;

        string? materialRoot = Directory.Exists(Path.Combine(root, "materials")) ? Path.Combine(root, "materials") :
            Path.GetFileName(root).Equals("materials", StringComparison.OrdinalIgnoreCase) ? root : null;
        string imageRoot = Directory.Exists(Path.Combine(root, "images")) ? Path.Combine(root, "images") :
            materialRoot == root ? Path.Combine(Path.GetDirectoryName(root) ?? root, "images") : root;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
        };
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(imageRoot))
            foreach (string path in Directory.EnumerateFiles(imageRoot, "*", options)
                         .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                         .OrderBy(path => Array.FindIndex(ImageExtensions, extension =>
                             extension.Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)))
                         .ThenBy(path => path, StringComparer.Ordinal))
            {
                string name = Path.ChangeExtension(Path.GetRelativePath(imageRoot, path), null).Replace('\\', '/');
                images.TryAdd(name, path);
            }
        if (materialRoot is null)
            return (Ordered(images.ToDictionary(pair => pair.Key,
                pair => new MaterialSource(pair.Key, pair.Value, false, ImagePreviewSampler), StringComparer.Ordinal)), 0);

        string[] materialFiles = Directory.EnumerateFiles(materialRoot, "*", options)
            .Order(StringComparer.Ordinal).ToArray();
        string[] jsonFiles = materialFiles.Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
        var materials = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
        int unsupported = 0;
        foreach (string path in jsonFiles)
        {
            string name = Path.ChangeExtension(Path.GetRelativePath(materialRoot, path), null).Replace('\\', '/');
            (string? colorMap, bool isSky, MaterialWater? water, Vector4 waterColor, Vector4 envMapParms,
                MaterialSamplerState samplerState, MaterialSurfaceState surface, MaterialGameFlags gameFlags,
                MaterialSurfaceTypeBits surfaceTypeBits, string techniqueSet) material;
            try { material = ReadMaterial(path); }
            catch (NotSupportedException) { unsupported++; continue; }
            var (colorMap, isSky, water, waterColor, envMapParms, samplerState, surface, gameFlags, surfaceTypeBits, techniqueSet) = material;
            string? image = colorMap is null ? null : ResolveImage(colorMap);
            if (image is not null || isSky || water is not null)
                materials[name] = new MaterialSource(name, image ?? "", isSky, samplerState)
                {
                    Water = water, WaterColor = waterColor, EnvMapParms = envMapParms, Surface = surface, GameFlags = gameFlags,
                    SurfaceTypeBits = surfaceTypeBits, TechniqueSet = techniqueSet,
                    OceanFoamImagePath = water is null ? "" : ResolveImage(WaterMaterialAuthoring.OceanFoamImageName) ?? ""
                };
        }
        return (Ordered(materials), unsupported);

        string? ResolveImage(string assetName)
        {
            string candidate = assetName.Replace('*', '_').Replace('\\', '/');
            string fullPath = Path.GetFullPath(Path.Combine(imageRoot, candidate));
            string prefix = Path.TrimEndingDirectorySeparator(imageRoot) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!fullPath.StartsWith(prefix, comparison))
                throw new InvalidDataException($"Image reference '{assetName}' must remain inside '{imageRoot}'.");
            string name = Path.GetRelativePath(imageRoot, fullPath).Replace('\\', '/');
            return images.GetValueOrDefault(name);
        }
    }

    private static Dictionary<string, MaterialSource> Ordered(Dictionary<string, MaterialSource> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static (string? Image, bool IsSky, MaterialWater? Water, Vector4 WaterColor, Vector4 EnvMapParms, MaterialSamplerState SamplerState,
        MaterialSurfaceState Surface, MaterialGameFlags GameFlags, MaterialSurfaceTypeBits SurfaceTypeBits,
        string TechniqueSet) ReadMaterial(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid("Expected a material JSON object");
            RequireString("_game", "iw4");
            RequireString("_platform", "ps3");
            RequireString("_type", "material");
            MaterialSurfaceState surface = MaterialSurfaceState.Read(root);
            string techniqueSet = root.GetProperty("techniqueSet").GetString() ?? throw Invalid("Expected a techniqueSet name");
            if (root.TryGetProperty("_version", out var version) &&
                (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 1))
                throw Invalid("Expected material version 1");
            MaterialSurfaceTypeBits surfaceTypeBits = root.TryGetProperty("surfaceTypeBits", out var surfaceTypes)
                ? (MaterialSurfaceTypeBits)surfaceTypes.GetUInt32() : MaterialSurfaceTypeBits.None;
            MaterialGameFlags gameFlags = MaterialGameFlags.None;
            if (root.TryGetProperty("gameFlags", out var flags))
            {
                if (flags.ValueKind != JsonValueKind.Array)
                    throw Invalid("Expected a gameFlags array");
                foreach (var flag in flags.EnumerateArray())
                {
                    if (flag.ValueKind != JsonValueKind.String || !byte.TryParse(flag.GetString(),
                            NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value))
                        throw Invalid("Expected hexadecimal gameFlags strings");
                    gameFlags |= (MaterialGameFlags)value;
                }
            }
            bool hasSkyFlag = (gameFlags & MaterialGameFlags.Sky) != 0;
            if (!root.TryGetProperty("textures", out var textures))
            {
                if (hasSkyFlag) throw Invalid("A sky requires a color-map texture");
                if (techniqueSet is "w_water" or "wc_water")
                    throw Invalid("A native water technique requires a waterMap texture");
                return (null, false, null, Vector4.Zero, Vector4.Zero, MaterialSamplerState.None, surface, gameFlags, surfaceTypeBits, techniqueSet);
            }
            if (textures.ValueKind != JsonValueKind.Array)
                throw Invalid("Expected a textures array");
            JsonElement? colorMap = null;
            JsonElement? waterMap = null;
            bool isSky = false;
            foreach (var texture in textures.EnumerateArray())
            {
                if (texture.ValueKind != JsonValueKind.Object)
                    throw Invalid("Expected a texture object");
                // Sun sprites also carry Sky, but use the 2D texture semantic.
                // World-surface skies carry a ColorMap-semantic image.
                string? semanticName = texture.TryGetProperty("semantic", out var semantic) &&
                    semantic.ValueKind == JsonValueKind.String ? semantic.GetString() : null;
                if (semanticName == "waterMap")
                {
                    if (waterMap is not null) throw Invalid("A native water material requires exactly one waterMap texture");
                    waterMap = texture;
                }
                if (hasSkyFlag && semanticName == "colorMap")
                {
                    if (isSky) throw Invalid("A sky requires exactly one color-map texture");
                    colorMap = texture;
                    isSky = true;
                }
                else if (colorMap is null && texture.TryGetProperty("name", out var name) &&
                         name.ValueKind == JsonValueKind.String && name.GetString() == "colorMap")
                    colorMap = texture;
            }
            MaterialWater? water = null;
            Vector4 waterColor = Vector4.Zero, envMapParms = Vector4.Zero;
            if (waterMap is { } nativeWaterMap)
            {
                if (techniqueSet is not ("w_water" or "wc_water"))
                    throw new NotSupportedException($"Material '{path}': the supported native water profile requires the w_water or wc_water technique set.");
                if (surfaceTypeBits != MaterialSurfaceTypeBits.Water ||
                    gameFlags != (MaterialGameFlags.NoMarks | MaterialGameFlags.HasReflection))
                    throw new NotSupportedException($"Material '{path}': the supported native water profile requires the proven Water surface type and 0x14 game flags.");
                water = ReadWaterMap(nativeWaterMap);
                waterColor = ReadWaterConstant("waterColor");
                envMapParms = ReadWaterConstant("envMapParms");
            }
            else if (techniqueSet is "w_water" or "wc_water")
                throw Invalid("A native water technique requires a waterMap texture");
            if ((waterMap ?? colorMap) is not { } selected)
                return (null, false, water, waterColor, envMapParms, MaterialSamplerState.None,
                    surface, gameFlags, surfaceTypeBits, techniqueSet);
            bool hasSampler = selected.TryGetProperty("samplerState", out var sampler);
            if (!hasSampler || sampler.ValueKind != JsonValueKind.Object)
                throw Invalid("Expected a colorMap samplerState object");
            MaterialSamplerState samplerState = ReadSamplerValue("filter") switch
            {
                "disabled" => MaterialSamplerState.FilterDisabled,
                "nearest" => MaterialSamplerState.FilterNearest,
                "linear" => MaterialSamplerState.FilterLinear,
                "aniso2x" => MaterialSamplerState.FilterAnisotropic2X,
                "aniso4x" => MaterialSamplerState.FilterAnisotropic4X,
                _ => throw Invalid("Unsupported colorMap filter")
            };
            samplerState |= ReadSamplerValue("mipMap") switch
            {
                "disabled" => MaterialSamplerState.MipMapDisabled,
                "nearest" => MaterialSamplerState.MipMapNearest,
                "linear" => MaterialSamplerState.MipMapLinear,
                _ => throw Invalid("Unsupported colorMap mipMap")
            };
            if (ReadClamp("clampU")) samplerState |= MaterialSamplerState.ClampU;
            if (ReadClamp("clampV")) samplerState |= MaterialSamplerState.ClampV;
            if (ReadClamp("clampW")) samplerState |= MaterialSamplerState.ClampW;
            if (!selected.TryGetProperty("image", out var image) || image.ValueKind == JsonValueKind.Null)
                return (null, isSky, water, waterColor, envMapParms, samplerState,
                    surface, gameFlags, surfaceTypeBits, techniqueSet);
            if (image.ValueKind != JsonValueKind.String)
                throw Invalid("Expected a colorMap image name");
            string? imageName = image.GetString();
            return (string.IsNullOrWhiteSpace(imageName) ? null : imageName, isSky, water, waterColor, envMapParms,
                samplerState, surface, gameFlags, surfaceTypeBits, techniqueSet);

            Vector4 ReadWaterConstant(string key)
            {
                if (!root.TryGetProperty("constants", out var constants) || constants.ValueKind != JsonValueKind.Array)
                    throw Invalid($"A native water material requires a {key} constant");
                JsonElement? match = null;
                foreach (JsonElement constant in constants.EnumerateArray())
                    if (constant.ValueKind == JsonValueKind.Object && constant.TryGetProperty("name", out var constantName) &&
                        constantName.ValueKind == JsonValueKind.String && constantName.GetString() == key)
                    {
                        if (match is not null) throw Invalid($"A native water material requires exactly one {key} constant");
                        match = constant;
                    }
                if (match is not { } selectedConstant || !selectedConstant.TryGetProperty("literal", out var literal) ||
                    literal.ValueKind != JsonValueKind.Array || literal.GetArrayLength() != 4)
                    throw Invalid($"A native water material requires a four-component {key} constant");
                var value = new Vector4(literal[0].GetSingle(), literal[1].GetSingle(),
                    literal[2].GetSingle(), literal[3].GetSingle());
                if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
                    !float.IsFinite(value.W))
                    throw Invalid($"A native water material has a non-finite {key} constant");
                return value;
            }

            MaterialWater ReadWaterMap(JsonElement texture)
            {
                if (!texture.TryGetProperty("image", out var waterImage) || waterImage.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(waterImage.GetString()))
                    throw Invalid("A native waterMap requires its simulation image name");
                if (!texture.TryGetProperty("water", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
                    throw Invalid("A native waterMap requires simulation parameters");
                if (!parameters.TryGetProperty("h0", out var h0) || h0.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(h0.GetString()) ||
                    !parameters.TryGetProperty("wTerm", out var wTerm) || wTerm.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(wTerm.GetString()))
                    throw Invalid("A native waterMap requires its exported H0 and WTerm spectra");
                int m = parameters.GetProperty("m").GetInt32(), n = parameters.GetProperty("n").GetInt32();
                float lx = parameters.GetProperty("lx").GetSingle(), lz = parameters.GetProperty("lz").GetSingle();
                float gravity = parameters.GetProperty("gravity").GetSingle();
                float windVelocity = parameters.GetProperty("windvel").GetSingle();
                float amplitude = parameters.GetProperty("amplitude").GetSingle();
                float floatTime = parameters.GetProperty("floatTime").GetSingle();
                JsonElement codeConstant = parameters.GetProperty("codeConstant");
                JsonElement wind = parameters.GetProperty("winddir");
                if (m <= 0 || n <= 0 || !float.IsFinite(lx) || lx <= 0 || !float.IsFinite(lz) || lz <= 0 ||
                    !float.IsFinite(gravity) || gravity <= 0 || !float.IsFinite(windVelocity) || windVelocity < 0 ||
                    !float.IsFinite(amplitude) || amplitude < 0 || !float.IsFinite(floatTime) ||
                    codeConstant.ValueKind != JsonValueKind.Array || codeConstant.GetArrayLength() != 4 ||
                    Enumerable.Range(0, 4).Any(index => !float.IsFinite(codeConstant[index].GetSingle())) ||
                    wind.ValueKind != JsonValueKind.Array ||
                    wind.GetArrayLength() != 2 || !float.IsFinite(wind[0].GetSingle()) ||
                    !float.IsFinite(wind[1].GetSingle()))
                    throw Invalid("A native waterMap has invalid simulation parameters");
                int count = checked(m * n);
                float[] h0Values = ReadSpectrum(h0.GetString() ?? throw Invalid("Missing H0 spectrum"), checked(count * 2));
                float[] frequencies = ReadSpectrum(wTerm.GetString() ?? throw Invalid("Missing WTerm spectrum"), count);
                var real = new float[count];
                var imaginary = new float[count];
                for (int index = 0; index < count; index++)
                {
                    real[index] = h0Values[index * 2];
                    imaginary[index] = h0Values[index * 2 + 1];
                }
                return new MaterialWater
                {
                    M = m, N = n, Lx = lx, Lz = lz, Gravity = gravity,
                    WindVelocity = windVelocity, WindDirection = new MaterialVec2(wind[0].GetSingle(), wind[1].GetSingle()),
                    Amplitude = amplitude, Writable = new MaterialWaterWritable(unchecked((uint)BitConverter.SingleToInt32Bits(floatTime))),
                    CodeConstant = new MaterialVec4(codeConstant[0].GetSingle(), codeConstant[1].GetSingle(),
                        codeConstant[2].GetSingle(), codeConstant[3].GetSingle()),
                    H0X = real, H0Y = imaginary, WTerm = frequencies
                };

                float[] ReadSpectrum(string encoded, int length)
                {
                    byte[] bytes = Convert.FromBase64String(encoded);
                    if (bytes.Length != checked(length * sizeof(float)))
                        throw Invalid("A native waterMap spectrum does not match its dimensions");
                    var values = new float[length];
                    for (int index = 0; index < length; index++)
                    {
                        values[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)));
                        if (!float.IsFinite(values[index]))
                            throw Invalid("A native waterMap spectrum contains a non-finite value");
                    }
                    return values;
                }
            }

            string ReadSamplerValue(string property)
            {
                if (!sampler.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
                    throw Invalid($"Expected a colorMap samplerState {property} string");
                return value.GetString() ?? "";
            }

            bool ReadClamp(string property)
            {
                if (!sampler.TryGetProperty(property, out var value) ||
                    value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw Invalid($"Expected a colorMap samplerState {property} boolean");
                return value.GetBoolean();
            }

            void RequireString(string property, string expected)
            {
                if (root.TryGetProperty(property, out var value) &&
                    (value.ValueKind != JsonValueKind.String || value.GetString() != expected))
                    throw Invalid($"Expected {property} '{expected}'");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Material '{path}' contains invalid JSON: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Material '{path}' has missing or invalid native fields: {exception.Message}", exception);
        }

        InvalidDataException Invalid(string reason) => new($"Material '{path}': {reason}.");
    }
}
