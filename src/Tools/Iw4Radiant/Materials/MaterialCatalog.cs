using System.Globalization;
using System.Text.Json;
using IW4.Assets.Assets.Material;

namespace Iw4Radiant.Materials;

internal static class MaterialCatalog
{
    private static readonly string[] ImageExtensions = [".dds", ".png", ".jpg", ".jpeg", ".bmp"];
    private const MaterialSamplerState ImagePreviewSampler = MaterialSamplerState.FilterLinear | MaterialSamplerState.MipMapLinear;

    internal static Dictionary<string, MaterialSource> Read(string root)
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
            return Ordered(images.ToDictionary(pair => pair.Key,
                pair => new MaterialSource(pair.Key, pair.Value, false, ImagePreviewSampler), StringComparer.Ordinal));

        string[] materialFiles = Directory.EnumerateFiles(materialRoot, "*", options)
            .Order(StringComparer.Ordinal).ToArray();
        string[] jsonFiles = materialFiles.Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
        var materials = new Dictionary<string, MaterialSource>(StringComparer.Ordinal);
        foreach (string path in jsonFiles)
        {
            string name = Path.ChangeExtension(Path.GetRelativePath(materialRoot, path), null).Replace('\\', '/');
            var (colorMap, isSky, samplerState, surface) = ReadMaterial(path);
            string? image = colorMap is null ? null : ResolveImage(colorMap);
            if (image is not null || isSky)
                materials[name] = new MaterialSource(name, image ?? "", isSky, samplerState) { Surface = surface };
        }
        return Ordered(materials);

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

    private static (string? Image, bool IsSky, MaterialSamplerState SamplerState, MaterialSurfaceState Surface) ReadMaterial(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid("Expected a material JSON object");
            RequireString("_game", "iw4");
            RequireString("_type", "material");
            MaterialSurfaceState surface = MaterialSurfaceState.Read(root);
            if (root.TryGetProperty("_version", out var version) &&
                (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 1))
                throw Invalid("Expected material version 1");
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
                return (null, false, MaterialSamplerState.None, surface);
            }
            if (textures.ValueKind != JsonValueKind.Array)
                throw Invalid("Expected a textures array");
            JsonElement? colorMap = null;
            bool isSky = false;
            foreach (var texture in textures.EnumerateArray())
            {
                if (texture.ValueKind != JsonValueKind.Object)
                    throw Invalid("Expected a texture object");
                // Sun sprites also carry Sky, but use the 2D texture semantic.
                // World-surface skies carry a ColorMap-semantic image.
                if (hasSkyFlag && texture.TryGetProperty("semantic", out var semantic) &&
                    semantic.ValueKind == JsonValueKind.String && semantic.GetString() == "colorMap")
                {
                    if (isSky) throw Invalid("A sky requires exactly one color-map texture");
                    colorMap = texture;
                    isSky = true;
                }
                else if (colorMap is null && texture.TryGetProperty("name", out var name) &&
                         name.ValueKind == JsonValueKind.String && name.GetString() == "colorMap")
                    colorMap = texture;
                if (!hasSkyFlag && colorMap is not null) break;
            }
            if (colorMap is not { } selected)
                return (null, false, MaterialSamplerState.None, surface);
            bool hasSampler = selected.TryGetProperty("samplerState", out var sampler);
            if (isSky && (!hasSampler || sampler.ValueKind != JsonValueKind.Object))
                throw Invalid("Expected a colorMap samplerState object");
            MaterialSamplerState samplerState = !isSky ? ImagePreviewSampler : ReadSamplerValue("filter") switch
            {
                "disabled" => MaterialSamplerState.FilterDisabled,
                "nearest" => MaterialSamplerState.FilterNearest,
                "linear" => MaterialSamplerState.FilterLinear,
                "aniso2x" => MaterialSamplerState.FilterAnisotropic2X,
                "aniso4x" => MaterialSamplerState.FilterAnisotropic4X,
                _ => throw Invalid("Unsupported colorMap filter")
            };
            if (isSky)
            {
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
            }
            if (!selected.TryGetProperty("image", out var image) || image.ValueKind == JsonValueKind.Null)
                return (null, isSky, samplerState, surface);
            if (image.ValueKind != JsonValueKind.String)
                throw Invalid("Expected a colorMap image name");
            string? imageName = image.GetString();
            return (string.IsNullOrWhiteSpace(imageName) ? null : imageName, isSky, samplerState, surface);

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

        InvalidDataException Invalid(string reason) => new($"Material '{path}': {reason}.");
    }
}
