using System.Globalization;
using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed class MapEntity
{
    public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
    public List<string> Directives { get; } = [];
    public List<MapBrush> Brushes { get; } = [];
    public List<MapTerrain> Terrains { get; } = [];
    // Unsupported source primitives are retained verbatim so opening a map does not discard them.
    public List<string> PreservedPrimitives { get; } = [];
    public string ClassName => Properties.GetValueOrDefault("classname", "entity");

    internal bool TryGetOrigin(out Vector3 origin)
    {
        origin = default;
        if (!Properties.TryGetValue("origin", out string? text)) return false;
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return false;
        origin = new Vector3(x, y, z);
        return true;
    }

    public MapEntity Clone()
    {
        var copy = new MapEntity();
        foreach (var property in Properties)
            copy.Properties.Add(property.Key, property.Value);
        copy.Directives.AddRange(Directives);
        copy.Brushes.AddRange(Brushes.Select(brush => brush.Clone()));
        copy.Terrains.AddRange(Terrains.Select(terrain => terrain.Clone()));
        copy.PreservedPrimitives.AddRange(PreservedPrimitives);
        return copy;
    }
}
