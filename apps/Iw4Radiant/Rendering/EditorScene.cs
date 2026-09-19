using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Rendering;

internal sealed class EditorScene(EditorSession session)
{
    private readonly Dictionary<object, object> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, MapEntity> _containers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MapEntity, (MapEntity Instance, MapEntity Source)> _prefabEntities = [];
    private readonly Dictionary<MapEntity, MapEntity> _expandedEntities = [];
    private readonly Dictionary<string, MapEntity[]> _targets = new(StringComparer.Ordinal);
    private MapDocument? _document;
    private EditorSelection _selection = new();
    internal Func<string, XModelSource?>? ResolveModel { get; set; }
    internal Func<string, MaterialSource?>? ResolveMaterial { get; set; }
    internal string? Notice { get; private set; }
    internal MapDocument Document { get { EnsureCurrent(); return _document ?? throw new InvalidOperationException("Scene is unavailable."); } }
    internal EditorSelection Selection { get { EnsureCurrent(); return _selection; } }
    internal void Invalidate() => _document = null;

    internal object Owner(object item)
    {
        EnsureCurrent();
        object owner = EditorSelection.Owner(item);
        if (_owners.TryGetValue(owner, out object? source) && !ReferenceEquals(owner, source)) return source;
        if (session.Tool == EditorTool.Select && item is MapBrush or MapTerrain &&
            _containers.TryGetValue(item, out MapEntity? container)) return container;
        return item;
    }

    internal bool CanSelect(object item) => session.Visibility.CanSelect(session.Document, Owner(item));
    internal bool IsPatchVertexLocked(TerrainVertexSelection vertex) => session.IsPatchVertexLocked(vertex);

    internal IReadOnlyList<MapEntity> ResolveTargets(MapEntity source)
    {
        EnsureCurrent();
        if (!source.Properties.TryGetValue("target", out string? target) || target.Length == 0) return [];
        if (_prefabEntities.TryGetValue(source, out var prefab))
        {
            IReadOnlyList<MapEntity> matches = session.Prefabs.ResolveTargets(prefab.Instance, session.FilePath, prefab.Source);
            if (matches.Count > 0)
                return matches.Where(_expandedEntities.ContainsKey).Select(entity => _expandedEntities[entity]).ToArray();
        }
        return _targets.GetValueOrDefault(target) ?? [];
    }

    internal (Vector3 Min, Vector3 Max)? Bounds(IEnumerable<object> items)
    {
        var bounds = items.Select(Bounds).OfType<(Vector3 Min, Vector3 Max)>().ToArray();
        return bounds.Length == 0 ? null : (bounds.Select(b => b.Min).Aggregate(Vector3.Min),
            bounds.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    internal (Vector3 Min, Vector3 Max)? Bounds(object item)
    {
        if (item is MapTerrain terrain) return terrain.GetSurface().GetBounds();
        if (item is not MapEntity entity) return SelectionGeometry.Bounds(item);
        if (entity.ClassName == "misc_prefab")
        {
            MapDocument? preview = session.Prefabs.GetPreview(entity, session.FilePath);
            return preview is null ? null : Bounds(preview.Brushes.Cast<object>().Concat(preview.Terrains)
                .Concat(preview.Entities.Where(PointEntityGeometry.IsPointEntity)));
        }
        if (XModelGeometry.IsModel(entity))
            return ResolveModel?.Invoke(entity.Properties["model"]) is { } model
                ? XModelGeometry.Bounds(entity, model) : EditorSession.EntityBounds(entity);
        return EditorSession.EntityBounds(entity);
    }

    internal (Vector3 Min, Vector3 Max)? VisibleBounds => Bounds(Document.Brushes.Cast<object>().Concat(Document.Terrains)
        .Concat(Document.Entities.Where(PointEntityGeometry.IsPointEntity)));

    private void EnsureCurrent()
    {
        if (_document is not null) return;
        _owners.Clear();
        _containers.Clear();
        _prefabEntities.Clear();
        _expandedEntities.Clear();
        _targets.Clear();
        _selection = new EditorSelection();
        var visible = new MapDocument();
        visible.Header.Clear();
        visible.Header.AddRange(session.Document.Header);
        var notices = new List<string>();
        foreach (MapEntity source in session.Document.Entities)
        {
            if (source.ClassName != "worldspawn" && !session.Visibility.IsVisible(session.Document, source)) continue;
            if (source.ClassName == "misc_prefab")
            {
                if (session.Prefabs.GetPreview(source, session.FilePath) is { } preview)
                    foreach (MapEntity child in preview.Entities) Add(child, source);
                else if (session.Prefabs.Error(source, session.FilePath) is { } error) notices.Add(error);
            }
            else Add(source, null);
        }
        foreach (var group in visible.Entities.Where(entity => entity.Properties.ContainsKey("targetname"))
                     .GroupBy(entity => entity.Properties["targetname"], StringComparer.Ordinal))
            _targets.Add(group.Key, group.ToArray());
        _document = visible;
        Notice = notices.Count == 0 ? null : string.Join('\n', notices.Distinct());

        void Add(MapEntity source, MapEntity? instance)
        {
            if (XModelGeometry.IsModel(source) && ResolveModel?.Invoke(source.Properties["model"]) is null)
                notices.Add($"Model unavailable: {source.Properties["model"]}");
            var entity = new MapEntity();
            foreach (var pair in source.Properties) entity.Properties.Add(pair.Key, pair.Value);
            entity.Directives.AddRange(source.Directives);
            entity.PreservedPrimitives.AddRange(source.PreservedPrimitives);
            foreach (MapBrush brush in source.Brushes)
                if (instance is not null || session.Visibility.IsVisible(session.Document, brush))
                {
                    entity.Brushes.Add(brush);
                    if (instance is null && source.ClassName != "worldspawn") _containers[brush] = source;
                    MapOwner(brush, instance ?? (object)brush, session.Selection.Contains(source));
                }
            foreach (MapTerrain terrain in source.Terrains)
                if (instance is not null || session.Visibility.IsVisible(session.Document, terrain))
                {
                    entity.Terrains.Add(terrain);
                    if (instance is null && source.ClassName != "worldspawn") _containers[terrain] = source;
                    MapOwner(terrain, instance ?? (object)terrain, session.Selection.Contains(source));
                }
            if (source.ClassName == "worldspawn" && visible.Entities.Any(e => e.ClassName == "worldspawn"))
            {
                visible.World.Brushes.AddRange(entity.Brushes);
                visible.World.Terrains.AddRange(entity.Terrains);
            }
            else
            {
                visible.Entities.Add(entity);
                if (instance is not null)
                {
                    _prefabEntities[entity] = (instance, source);
                    _expandedEntities[source] = entity;
                }
                MapOwner(entity, instance ?? source, false);
            }
        }

        void MapOwner(object item, object owner, bool parentSelected)
        {
            _owners[item] = owner;
            if (parentSelected || session.Selection.Contains(owner)) _selection.Set(item, additive: true);
            if (ReferenceEquals(item, owner))
                foreach (object selected in session.Selection.Items.Where(selected => !ReferenceEquals(selected, item) &&
                             ReferenceEquals(EditorSelection.Owner(selected), item)))
                    _selection.Set(selected, additive: true);
        }
    }
}
