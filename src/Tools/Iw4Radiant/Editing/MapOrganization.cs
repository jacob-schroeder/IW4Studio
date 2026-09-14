using Iw4Radiant.MapSource;
using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.Editing;

internal static class MapOrganization
{
    internal const string GlobalLayer = "000_Global";

    internal static string[] Layers(MapDocument document) => LayerLines(document).Select(layer => layer.Name)
        .Concat(Objects(document).Select(item => Layer(item)))
        .Append(GlobalLayer).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static IEnumerable<object> Objects(MapDocument document)
    {
        foreach (MapEntity entity in document.Entities)
        {
            if (entity.ClassName != "worldspawn") yield return entity;
            foreach (MapBrush brush in entity.Brushes) yield return brush;
            foreach (MapTerrain terrain in entity.Terrains) yield return terrain;
        }
    }

    internal static MapEntity? Entity(MapDocument document, object item)
    {
        object owner = EditorSelection.Owner(item);
        return owner is MapEntity entity ? entity : document.Entities.FirstOrDefault(candidate =>
            owner is MapBrush brush && candidate.Brushes.Contains(brush) ||
            owner is MapTerrain terrain && candidate.Terrains.Contains(terrain));
    }

    internal static string Layer(object item)
    {
        foreach (string directive in Directives(EditorSelection.Owner(item)))
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (tokens.Count == 2 && tokens[0].Value == "layer" && tokens[1].Quoted)
                return tokens[1].Value;
        }
        return GlobalLayer;
    }

    internal static void Assign(object item, string layer)
    {
        var directives = Directives(EditorSelection.Owner(item));
        directives.RemoveAll(line => MapTokenizer.Tokenize(line).FirstOrDefault().Value == "layer");
        if (layer != GlobalLayer) directives.Add("layer " + Quote(layer));
    }

    internal static bool LayerHasFlag(MapDocument document, string name, string flag) =>
        LayerLines(document).Any(layer =>
            (name == layer.Name || name.StartsWith(layer.Name + "/", StringComparison.Ordinal)) && layer.Flags.Contains(flag));

    internal static void CreateLayer(MapDocument document, string name)
    {
        ValidateName(name);
        if (Layers(document).Contains(name, StringComparer.Ordinal))
            throw new ArgumentException("A layer with that name already exists.");
        document.Header.Add(Quote(name) + " flags");
    }

    internal static void RenameLayer(MapDocument document, string oldName, string name)
    {
        ValidateName(name);
        if (oldName == GlobalLayer) throw new ArgumentException("The global layer cannot be renamed.");
        if (oldName == name) return;
        if (Layers(document).Contains(name, StringComparer.Ordinal) || name.StartsWith(oldName + "/", StringComparison.Ordinal))
            throw new ArgumentException("Choose a new layer name outside the layer being renamed.");
        string Remap(string value) => value == oldName ? name : value.StartsWith(oldName + "/", StringComparison.Ordinal)
            ? name + value[oldName.Length..] : value;
        string[] layers = Layers(document);
        if (layers.Select(Remap).Distinct(StringComparer.Ordinal).Count() != layers.Length)
            throw new ArgumentException("Renaming would merge existing child layers; choose another name.");
        foreach (var layer in LayerLines(document).ToArray())
        {
            if (Remap(layer.Name) != layer.Name)
                document.Header[layer.Index] = Quote(Remap(layer.Name)) + " flags " + string.Join(' ', layer.Flags);
        }
        foreach (object item in Objects(document))
        {
            string current = Layer(item);
            if (Remap(current) != current) Assign(item, Remap(current));
        }
        RemapPreservedLayers(document, Remap);
    }

    internal static void DeleteLayer(MapDocument document, string name)
    {
        if (name == GlobalLayer) throw new ArgumentException("The global layer cannot be deleted.");
        bool Matches(string? value) => value == name || value is not null && value.StartsWith(name + "/", StringComparison.Ordinal);
        foreach (int index in LayerLines(document).Where(layer => Matches(layer.Name)).Select(layer => layer.Index).OrderDescending().ToArray())
            document.Header.RemoveAt(index);
        foreach (object item in Objects(document).Where(item => Matches(Layer(item)))) Assign(item, GlobalLayer);
        RemapPreservedLayers(document, value => Matches(value) ? GlobalLayer : value);
    }

    internal static void SetLayerFlag(MapDocument document, string name, string flag, bool value)
    {
        var declaration = LayerLines(document).Where(layer => layer.Name == name).ToArray();
        int index = declaration.Length == 0 ? -1 : declaration[0].Index;
        var flags = declaration.Length == 0 ? new List<string>() : declaration[0].Flags.ToList();
        flags.RemoveAll(existing => existing == flag);
        if (value) flags.Add(flag);
        string line = Quote(name) + " flags " + string.Join(' ', flags);
        if (index < 0) document.Header.Add(line); else document.Header[index] = line;
    }

    internal static MapEntity Group(MapDocument document, IEnumerable<object> selection, string name)
    {
        object[] objects = selection.Select(EditorSelection.Owner).Distinct(ReferenceEqualityComparer.Instance).ToArray();
        if (objects.Length == 0 || objects.Any(item => item is not (MapBrush or MapTerrain)))
            throw new ArgumentException("Select whole brushes or terrain patches to group. Use layers to organize point entities and prefab instances.");
        if (objects.Any(item => Entity(document, item)?.ClassName is not ("worldspawn" or "func_group")))
            throw new ArgumentException("Brushes belonging to gameplay entities cannot be moved into an editor group.");
        var group = new MapEntity();
        group.Properties["classname"] = "func_group";
        if (!string.IsNullOrWhiteSpace(name)) { ValidateName(name); group.Properties["targetname"] = name; }
        foreach (object item in objects)
        {
            var entity = Entity(document, item) ?? throw new ArgumentException("A selected object no longer exists.");
            if (entity.ClassName != "worldspawn" && Layer(item) == GlobalLayer) Assign(item, Layer(entity));
            if (item is MapBrush brush) { entity.Brushes.Remove(brush); group.Brushes.Add(brush); }
            if (item is MapTerrain terrain) { entity.Terrains.Remove(terrain); group.Terrains.Add(terrain); }
        }
        document.Entities.RemoveAll(entity => entity.ClassName == "func_group" && entity.Brushes.Count == 0 &&
            entity.Terrains.Count == 0 && entity.PreservedPrimitives.Count == 0);
        document.Entities.Add(group);
        return group;
    }

    internal static object[] Ungroup(MapDocument document, MapEntity group)
    {
        if (group.ClassName != "func_group" || !document.Entities.Contains(group))
            throw new ArgumentException("Select a brush group to ungroup.");
        if (group.PreservedPrimitives.Count > 0)
            throw new ArgumentException("This group contains unsupported primitives. Keep it grouped to preserve their source metadata.");
        foreach (object item in group.Brushes.Cast<object>().Concat(group.Terrains))
            if (Layer(item) == GlobalLayer) Assign(item, Layer(group));
        object[] items = group.Brushes.Cast<object>().Concat(group.Terrains).ToArray();
        document.World.Brushes.AddRange(group.Brushes);
        document.World.Terrains.AddRange(group.Terrains);
        document.World.PreservedPrimitives.AddRange(group.PreservedPrimitives);
        document.Entities.Remove(group);
        return items;
    }

    private static List<string> Directives(object item) => item switch
    {
        MapEntity entity => entity.Directives, MapBrush brush => brush.Directives, MapTerrain terrain => terrain.Directives,
        _ => throw new ArgumentException("Select an entity, brush or terrain patch.")
    };

    private static void RemapPreservedLayers(MapDocument document, Func<string, string> remap)
    {
        foreach (MapEntity entity in document.Entities)
        for (int primitive = 0; primitive < entity.PreservedPrimitives.Count; primitive++)
        {
            string raw = entity.PreservedPrimitives[primitive];
            var tokens = MapTokenizer.Tokenize(raw);
            for (int index = tokens.Count - 1; index > 0; index--)
            {
                MapToken name = tokens[index], directive = tokens[index - 1];
                if (!directive.Quoted && directive.Value == "layer" && name.Quoted && remap(name.Value) is { } changed && changed != name.Value)
                    raw = raw[..name.Start] + Quote(changed) + raw[name.End..];
            }
            entity.PreservedPrimitives[primitive] = raw;
        }
    }

    private static IEnumerable<(int Index, string Name, string[] Flags)> LayerLines(MapDocument document)
    {
        var tokens = MapTokenizer.Tokenize(string.Join('\n', document.Header));
        foreach (var line in tokens.GroupBy(token => token.Line))
        {
            MapToken[] values = line.ToArray();
            if (values.Length >= 2 && values[0].Quoted && values[1].Value == "flags")
                yield return (values[0].Line - 1, values[0].Value, values.Skip(2).Select(token => token.Value).ToArray());
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(character => char.IsControl(character) || character is '"' or '\\') ||
            name.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
            throw new ArgumentException("Enter a name without quotes, backslashes or empty path segments. Use / for nested layers.");
    }

    private static string Quote(string text) => '"' + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"';
}
