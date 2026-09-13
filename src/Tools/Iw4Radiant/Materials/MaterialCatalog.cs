using System.Text.Json;

namespace Iw4Radiant.Materials;

internal static class MaterialCatalog
{
    private static readonly string[] ImageExtensions = [".dds", ".png", ".jpg", ".jpeg", ".bmp"];

    internal static Dictionary<string, string> Read(string root)
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
            return Ordered(images);

        string[] materialFiles = Directory.EnumerateFiles(materialRoot, "*", options)
            .Order(StringComparer.Ordinal).ToArray();
        string[] jsonFiles = materialFiles.Where(path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
        var materials = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in jsonFiles)
        {
            string name = Path.ChangeExtension(Path.GetRelativePath(materialRoot, path), null).Replace('\\', '/');
            string? colorMap = ReadColorMap(path);
            if (colorMap is not null && ResolveImage(colorMap) is { } image)
                materials[name] = image;
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

    private static Dictionary<string, string> Ordered(Dictionary<string, string> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string? ReadColorMap(string path)
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
            if (root.TryGetProperty("_version", out var version) &&
                (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 1))
                throw Invalid("Expected material version 1");
            if (!root.TryGetProperty("textures", out var textures))
                return null;
            if (textures.ValueKind != JsonValueKind.Array)
                throw Invalid("Expected a textures array");
            foreach (var texture in textures.EnumerateArray())
            {
                if (texture.ValueKind != JsonValueKind.Object)
                    throw Invalid("Expected a texture object");
                if (!texture.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != "colorMap")
                    continue;
                if (!texture.TryGetProperty("image", out var image) || image.ValueKind == JsonValueKind.Null)
                    return null;
                if (image.ValueKind != JsonValueKind.String)
                    throw Invalid("Expected a colorMap image name");
                string? value = image.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            return null;

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
