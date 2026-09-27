using System.Text.Json;
using IW4.Formats.SourceFormat.PhysPreset;

namespace Iw4Radiant.Materials;

internal static class GlassPhysicsPresets
{
    internal static string[] ReadAvailable(string? libraryRoot, string? bootstrapRoot)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var available = new List<string>();
        var exchange = new PhysPresetExchange();
        foreach (string? root in new[] { libraryRoot, bootstrapRoot })
        {
            if (root is null || !Directory.Exists(Path.Combine(root, "physic"))) continue;
            string folder = Path.Combine(root, "physic");
            foreach (string path in Directory.EnumerateFiles(folder, "*.physic.json", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
                string name = relative[..^".physic.json".Length];
                if (!names.Add(name)) continue;
                try
                {
                    exchange.Link(root, name);
                    available.Add(name);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or JsonException or ArgumentException or FormatException or NotSupportedException) { }
            }
        }
        return available.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
