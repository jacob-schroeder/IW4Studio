using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal sealed class EditorVisibility
{
    private readonly HashSet<object> _hidden = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _frozen = new(ReferenceEqualityComparer.Instance);
    private HashSet<object>? _isolated;
    private MapDocument? _document;
    private readonly Dictionary<object, MapEntity> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, string> _objectLayers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, (bool Hidden, bool Frozen)> _layerFlags = new(StringComparer.Ordinal);

    internal bool IsActive => _hidden.Count > 0 || _frozen.Count > 0 || _isolated is not null;
    internal bool IsVisible(MapDocument document, object item)
    {
        object owner = EditorSelection.Owner(item);
        Ensure(document);
        MapEntity? entity = owner as MapEntity ?? _owners.GetValueOrDefault(owner);
        return !Flags(owner).Hidden && (entity is null || entity.ClassName == "worldspawn" || !Flags(entity).Hidden) && !_hidden.Contains(owner) &&
            (entity is null || !_hidden.Contains(entity)) &&
            (_isolated is null || _isolated.Contains(owner) || entity is not null && _isolated.Contains(entity) ||
             owner is MapEntity parent && parent.Brushes.Cast<object>().Concat(parent.Terrains).Any(_isolated.Contains));
    }

    internal bool CanSelect(MapDocument document, object item)
    {
        object owner = EditorSelection.Owner(item);
        Ensure(document);
        MapEntity? entity = owner as MapEntity ?? _owners.GetValueOrDefault(owner);
        return IsVisible(document, owner) && !Flags(owner).Frozen && !_frozen.Contains(owner) &&
            (entity is null || !_frozen.Contains(entity) && (entity.ClassName == "worldspawn" || !Flags(entity).Frozen)) &&
            (owner is not MapEntity parent || parent.Brushes.Cast<object>().Concat(parent.Terrains).All(child => CanSelect(document, child)));
    }

    internal void Hide(IEnumerable<object> items) => _hidden.UnionWith(items.Select(EditorSelection.Owner));
    internal void Freeze(IEnumerable<object> items) => _frozen.UnionWith(items.Select(EditorSelection.Owner));
    internal void Isolate(IEnumerable<object> items) => _isolated = new(items.Select(EditorSelection.Owner), ReferenceEqualityComparer.Instance);
    internal void Clear() { _hidden.Clear(); _frozen.Clear(); _isolated = null; Invalidate(); }
    internal void Invalidate() { _document = null; _owners.Clear(); _objectLayers.Clear(); _layerFlags.Clear(); }

    private void Ensure(MapDocument document)
    {
        if (ReferenceEquals(_document, document)) return;
        Invalidate();
        _document = document;
        foreach (MapEntity entity in document.Entities)
        {
            _objectLayers[entity] = MapOrganization.Layer(entity);
            foreach (object item in entity.Brushes.Cast<object>().Concat(entity.Terrains))
            { _owners[item] = entity; _objectLayers[item] = MapOrganization.Layer(item); }
        }
    }

    private (bool Hidden, bool Frozen) Flags(object item)
    {
        string layer = _objectLayers.GetValueOrDefault(item, MapOrganization.GlobalLayer);
        if (!_layerFlags.TryGetValue(layer, out var flags) && _document is { } document)
            _layerFlags.Add(layer, flags = (MapOrganization.LayerHasFlag(document, layer, "hidden"),
                MapOrganization.LayerHasFlag(document, layer, "frozen")));
        return flags;
    }
}
