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
    private IReadOnlyDictionary<MapEntity, MapEntity>? _physicsPlacementPreview;
    private IReadOnlyDictionary<MapBrush, MapEntity>? _physicsPlacementClips;
    private IReadOnlySet<MapBrush>? _shatterSources;
    private IReadOnlyDictionary<MapBrush, MapEntity>? _shatterFragments;
    private EditorSelection _selection = new();
    private MapEntity? _destructiblePreviewSource;
    private MapEntity? _destructiblePreviewEntity;
    private XModelSource? _destructibleIntactModel;
    private XModelSource? _destructibleWreckModel;
    private XModelSource? _destructiblePreviewModel;
    private MapEntity? _preparedDestructibleSource;
    private XModelSource? _preparedDestructibleIntact, _preparedDestructibleWreck;
    private IReadOnlyDictionary<DestructibleWindowState, XModelSource>? _preparedDestructibleWindows;
    private DestructiblePreviewSettings? _destructiblePreviewSettings;
    internal Func<string, XModelSource?>? ResolveModel { get; set; }
    internal Func<string, MaterialSource?>? ResolveMaterial { get; set; }
    internal string? Notice { get; private set; }
    internal long ModelPreviewRevision { get; private set; }
    internal MapDocument Document { get { EnsureCurrent(); return _document ?? throw new InvalidOperationException("Scene is unavailable."); } }
    internal IReadOnlyDictionary<MapEntity, MapEntity>? PhysicsPlacementPreview => _physicsPlacementPreview;
    internal IReadOnlyDictionary<MapBrush, MapEntity>? ShatterFragments => _shatterFragments;
    internal MapEntity? DestructiblePreviewSource => _destructiblePreviewSource;
    internal XModelSource? DestructiblePreviewModel => _destructiblePreviewModel;
    internal XModelSource? DestructibleWreckModel => _destructibleWreckModel;
    internal MapEntity? DestructiblePreviewEntity
    {
        get
        {
            EnsureCurrent();
            return _physicsPlacementPreview is null ? _destructiblePreviewEntity : null;
        }
    }
    internal XModelSource? PreviewModelForEntity(MapEntity entity) =>
        ReferenceEquals(entity, DestructiblePreviewEntity) && _destructiblePreviewModel is { } preview
            ? preview : ResolveModel?.Invoke(entity.Properties["model"]);
    internal bool IsShatterSource(MapBrush brush) => _shatterSources?.Contains(brush) == true;
    internal MapEntity? ShatterFragmentOwner(MapBrush brush) =>
        _shatterFragments?.GetValueOrDefault(brush);
    internal EditorSelection Selection { get { EnsureCurrent(); return _selection; } }
    internal void Invalidate() => _document = null;
    internal void SetPhysicsPlacementPreview(IReadOnlyDictionary<MapEntity, MapEntity>? poses,
        IReadOnlyDictionary<MapBrush, MapEntity>? clips = null)
    {
        if (!ReferenceEquals(_physicsPlacementPreview, poses) && _physicsPlacementPreview is { } previous)
            foreach (MapEntity pose in previous.Values.Where(PrefabLibrary.IsPrefab))
                session.Prefabs.DiscardPreview(pose);
        _physicsPlacementPreview = poses;
        _physicsPlacementClips = poses is null ? null : clips;
        ModelPreviewRevision++;
        Invalidate();
    }

    internal void SetGlassShatterPreview(IReadOnlySet<MapBrush>? sources,
        IReadOnlyDictionary<MapBrush, MapEntity>? fragments = null)
    {
        _shatterSources = sources;
        _shatterFragments = sources is null ? null : fragments;
        Invalidate();
    }

    internal bool SetDestructiblePreview(MapEntity? source, DestructiblePreviewSettings? settings)
    {
        if (source is null)
        {
            if (settings is not null) throw new ArgumentException("A preview setting requires an entity.", nameof(settings));
            if (_destructiblePreviewSource is null) return false;
            _destructiblePreviewSource = null;
            _destructiblePreviewSettings = null;
            _destructibleIntactModel = null;
            _destructibleWreckModel = null;
            _destructiblePreviewModel = null;
            _destructiblePreviewEntity = null;
            ModelPreviewRevision++;
            return true;
        }
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (settings.FrontLeftTireFlat)
            throw new InvalidDataException("The front-left flat tire animation cannot be shown in the map model preview.");
        if (!session.Document.Entities.Contains(source) || !DestructiblePresets.HasDiscoveryName(source))
            throw new InvalidDataException("Choose a placed LAPD police car destructible to preview.");
        if (ReferenceEquals(source, _destructiblePreviewSource) && _destructiblePreviewSettings is { } previous)
        {
            if (settings == previous) return false;
            bool isWreck = settings.Appearance == DestructibleAppearance.Wreck;
            if (settings.FrontLeftTireFlat == previous.FrontLeftTireFlat &&
                isWreck == (previous.Appearance == DestructibleAppearance.Wreck) &&
                (isWreck || settings.Windshield == previous.Windshield))
            {
                _destructiblePreviewSettings = settings;
                return false;
            }
        }

        XModelSource intact = ResolveModel?.Invoke(source.Properties["model"]) ??
            throw new InvalidDataException("The intact police car model is unavailable.");
        XModelSource wreck = ResolveModel?.Invoke("vehicle_policecar_lapd_destroy") ??
            throw new InvalidDataException("The destroyed police car model is unavailable.");
        XModelSource preview = ReferenceEquals(source, _preparedDestructibleSource) &&
            ReferenceEquals(intact, _preparedDestructibleIntact) && ReferenceEquals(wreck, _preparedDestructibleWreck) &&
            _preparedDestructibleWindows is { } windows
            ? settings.Appearance == DestructibleAppearance.Wreck ? wreck : windows[settings.Windshield]
            : DestructibleModelPreview.Create(intact, wreck, settings);
        _destructiblePreviewSource = source;
        _destructiblePreviewSettings = settings;
        _destructibleIntactModel = intact;
        _destructibleWreckModel = wreck;
        _destructiblePreviewModel = preview;
        EnsureCurrent();
        _destructiblePreviewEntity = _document!.Entities.FirstOrDefault(entity =>
            _owners.TryGetValue(entity, out object? owner) && ReferenceEquals(owner, source));
        ModelPreviewRevision++;
        return true;
    }

    internal void SetPreparedDestructibleModels(MapEntity source, XModelSource intact, XModelSource wreck,
        IReadOnlyDictionary<DestructibleWindowState, XModelSource> windows)
    {
        _preparedDestructibleSource = source;
        _preparedDestructibleIntact = intact;
        _preparedDestructibleWreck = wreck;
        _preparedDestructibleWindows = windows;
    }

    internal IReadOnlyDictionary<DestructibleWindowState, XModelSource>? GetPreparedDestructibleModels(
        MapEntity source, XModelSource intact, XModelSource wreck) =>
        ReferenceEquals(source, _preparedDestructibleSource) &&
            ReferenceEquals(intact, _preparedDestructibleIntact) && ReferenceEquals(wreck, _preparedDestructibleWreck)
            ? _preparedDestructibleWindows : null;

    internal void ClearPreparedDestructibleModels()
    {
        _preparedDestructibleSource = null;
        _preparedDestructibleIntact = _preparedDestructibleWreck = null;
        _preparedDestructibleWindows = null;
    }

    internal bool TryGetDestructibleTagTransform(string tag, out Matrix4x4 transform)
    {
        if (_destructiblePreviewSource is { } source && _destructibleIntactModel is { } intact)
            return DestructibleModelPreview.TryGetTagTransform(intact, tag, XModelGeometry.Transform(source), out transform);
        transform = default;
        return false;
    }

    internal MapEntity? PhysicsPlacementOwner(object item)
    {
        if (item is MapBrush brush && _physicsPlacementClips?.TryGetValue(brush, out MapEntity? model) == true)
            return model;
        return Owner(item) is MapEntity entity && _physicsPlacementPreview?.ContainsKey(entity) == true
            ? entity : null;
    }

    internal bool UpdatePointEntities(IEnumerable<MapEntity> sources, out bool modelsChanged)
    {
        modelsChanged = false;
        if (_document is null) return false;
        var remaining = new HashSet<MapEntity>(sources, ReferenceEqualityComparer.Instance);
        modelsChanged = remaining.Any(XModelGeometry.IsModel);
        foreach (MapEntity visible in _document.Entities)
        {
            if (!_owners.TryGetValue(visible, out object? owner) || owner is not MapEntity source ||
                !remaining.Remove(source)) continue;
            visible.Properties.Clear();
            foreach (var property in source.Properties)
                visible.Properties.Add(property.Key, property.Value);
        }
        if (remaining.Count != 0) return false;
        if (modelsChanged) ModelPreviewRevision++;
        return true;
    }

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
            MapEntity pose = _physicsPlacementPreview?.GetValueOrDefault(entity) ?? entity;
            MapDocument? preview = session.Prefabs.GetPreview(pose, session.FilePath);
            return preview is null ? null : Bounds(preview.Brushes.Cast<object>().Concat(preview.Terrains)
                .Concat(preview.Entities.Where(PointEntityGeometry.IsPointEntity)));
        }
        if (XModelGeometry.IsModel(entity))
            return ResolveModel?.Invoke(entity.Properties["model"]) is { } model
                ? XModelGeometry.Bounds(_physicsPlacementPreview?.GetValueOrDefault(entity) ?? entity, model)
                : EditorSession.EntityBounds(entity);
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
        _destructiblePreviewEntity = null;
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
                MapEntity pose = _physicsPlacementPreview?.GetValueOrDefault(source) ?? source;
                if (session.Prefabs.GetPreview(pose, session.FilePath) is { } preview)
                {
                    MapEntity[] originals = ReferenceEquals(pose, source) ? preview.Entities.ToArray() :
                        (session.Prefabs.GetPreview(source, session.FilePath) ??
                         throw new InvalidOperationException("The original prefab preview is unavailable.")).Entities.ToArray();
                    foreach ((MapEntity child, MapEntity original) in preview.Entities.Zip(originals))
                        Add(child, source, original);
                }
                else if (session.Prefabs.Error(pose, session.FilePath) is { } error) notices.Add(error);
            }
            else Add(source, null, source, _physicsPlacementPreview?.GetValueOrDefault(source));
        }
        foreach (var group in visible.Entities.Where(entity => entity.Properties.ContainsKey("targetname"))
                     .GroupBy(entity => entity.Properties["targetname"], StringComparer.Ordinal))
            _targets.Add(group.Key, group.ToArray());
        _document = visible;
        Notice = notices.Count == 0 ? null : string.Join('\n', notices.Distinct());

        void Add(MapEntity source, MapEntity? instance, MapEntity original, MapEntity? display = null)
        {
            MapEntity shown = display ?? source;
            if (XModelGeometry.IsModel(shown) && ResolveModel?.Invoke(shown.Properties["model"]) is null)
                notices.Add($"Model unavailable: {shown.Properties["model"]}");
            var entity = new MapEntity();
            foreach (var pair in shown.Properties) entity.Properties.Add(pair.Key, pair.Value);
            if (ReferenceEquals(original, _destructiblePreviewSource) && instance is null)
                _destructiblePreviewEntity = entity;
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
                    _prefabEntities[entity] = (instance, original);
                    _expandedEntities[original] = entity;
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
