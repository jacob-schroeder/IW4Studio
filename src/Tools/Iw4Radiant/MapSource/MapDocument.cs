namespace Iw4Radiant.MapSource;

internal sealed class MapDocument
{
    public List<string> Header { get; } = ["iwmap 4", "\"000_Global\" flags active"];
    public List<MapEntity> Entities { get; } = [];
    public IEnumerable<MapBrush> Brushes => Entities.SelectMany(entity => entity.Brushes);
    public IEnumerable<MapTerrain> Terrains => Entities.SelectMany(entity => entity.Terrains);
    public MapEntity World => Entities.First(entity => entity.ClassName == "worldspawn");

    public static MapDocument Create()
    {
        var document = new MapDocument();
        var world = new MapEntity();
        world.Properties["classname"] = "worldspawn";
        document.Entities.Add(world);
        return document;
    }

    public MapDocument Clone()
    {
        var copy = new MapDocument();
        copy.Header.Clear();
        copy.Header.AddRange(Header);
        copy.Entities.AddRange(Entities.Select(entity => entity.Clone()));
        return copy;
    }
}
