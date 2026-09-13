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
