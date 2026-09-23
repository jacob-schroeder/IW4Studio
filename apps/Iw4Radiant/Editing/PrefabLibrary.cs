using System.Numerics;
using IW4.Formats.SourceFormat.Material;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal sealed class PrefabLibrary
{
    private const int MaximumDepth = 32, MaximumInstances = 4096;
    private readonly Dictionary<MapEntity, (string Signature, MapDocument? Preview, Dictionary<MapEntity, TargetScope>? Scopes, string? Error)> _previews = [];
    private readonly Dictionary<string, MapDocument> _sources = new(StringComparer.Ordinal);
    internal int Revision { get; private set; }

    internal static bool IsPrefab(MapEntity entity) => entity.ClassName == "misc_prefab";

    internal static MapDocument ExpandForCompilation(MapDocument document, string? mapPath)
    {
        var library = new PrefabLibrary();
        MapDocument result = document.Clone();
        var targetResolutions = result.Entities.Where(entity => !IsPrefab(entity) && entity.Properties.ContainsKey("target"))
            .ToDictionary(entity => entity, entity => result.ResolveTargets(entity).ToArray());
        var targetNames = result.Entities.Where(entity => !IsPrefab(entity))
            .Select(entity => entity.Properties.GetValueOrDefault("targetname", "")).Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        foreach (MapEntity instance in result.Entities.Where(IsPrefab).ToArray())
        {
            ValidateCompilationInstance(instance);
            MapDocument preview = library.GetPreview(instance, mapPath) ?? throw new InvalidDataException(library.Error(instance, mapPath));
            // Preview expansion intentionally consumes nested instances and prefab
            // world settings. A build must diagnose unsupported source data first.
            foreach (MapDocument sourceDocument in library._sources.Values)
            {
                foreach (MapEntity nested in sourceDocument.Entities.Where(IsPrefab)) ValidateCompilationInstance(nested);
                if (sourceDocument.World.Directives.Any(directive => !MapOrganization.IsLayerDirective(directive)) ||
                    sourceDocument.World.Properties.Keys.Any(key => key is not ("classname" or WaterMaterialAuthoring.MapPropertyName)))
                    throw new NotSupportedException("Prefab world properties or directives have no proven compile-time inheritance rule. Move them to the parent world before building.");
            }
            MapDocument expanded = preview.Clone();
            WaterMaterialAuthoring.MergeDefinitions(result.World.Properties, expanded.World.Properties);
            string[] names = expanded.Entities.Select(entity => entity.Properties.GetValueOrDefault("targetname", ""))
                .Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            if (names.Any(targetNames.Contains))
                throw new NotSupportedException("A prefab targetname collides with another instance or the parent map. Native scoped name rewriting is not yet recovered. Make the source targetnames unique or explode the instance and resolve its links before building.");
            var expandedEntities = preview.Entities.Zip(expanded.Entities)
                .ToDictionary(pair => pair.First, pair => pair.Second);
            foreach (MapEntity source in preview.Entities.Where(entity => entity.Properties.ContainsKey("target")))
            {
                targetResolutions.Add(expandedEntities[source], library.ResolveTargets(instance, mapPath, source)
                    .Select(target => expandedEntities[target]).ToArray());
            }
            targetNames.UnionWith(names);
            result.World.Brushes.AddRange(expanded.World.Brushes);
            result.World.Terrains.AddRange(expanded.World.Terrains);
            result.Entities.AddRange(expanded.Entities.Skip(1));
            result.Entities.Remove(instance);
        }
        // Compare identities only after every instance has been inserted: flattening
        // must not capture previously unresolved links in either direction, including
        // links from the parent map or between separate prefab instances.
        foreach (var (source, expected) in targetResolutions)
            if (!new HashSet<MapEntity>(expected, ReferenceEqualityComparer.Instance).SetEquals(result.ResolveTargets(source)))
                throw new NotSupportedException($"Target '{source.Properties["target"]}' changes resolution when prefabs are flattened. Explode the prefab and give scoped destinations unique targetnames before building.");
        return result;
    }

    private static void ValidateCompilationInstance(MapEntity instance)
    {
        foreach (string key in instance.Properties.Keys)
            if (key is not ("classname" or "model" or "origin" or "angles" or "angle" or "modelscale" or "modelscale_vec"))
                throw new NotSupportedException($"Prefab instance property '{key}' has no proven compile-time expansion rule. Edit or explode the instance before building.");
        if (instance.Brushes.Count != 0 || instance.Terrains.Count != 0 || instance.PreservedPrimitives.Count != 0 ||
            instance.Directives.Any(directive => !MapOrganization.IsLayerDirective(directive)))
            throw new NotSupportedException("Prefab instances with attached geometry or directives cannot be compiled by reference. Move that data to the prefab source before building.");
    }

    internal void Reload(MapDocument document, string? mapPath)
    {
        Revision++;
        _previews.Clear();
        _sources.Clear();
        foreach (MapEntity entity in document.Entities.Where(IsPrefab)) GetPreview(entity, mapPath);
    }

    internal MapDocument? GetPreview(MapEntity instance, string? mapPath)
    {
        string signature = string.Join('\n', new[] { mapPath ?? "", instance.Properties.GetValueOrDefault("model", ""),
            instance.Properties.GetValueOrDefault("origin", ""), instance.Properties.GetValueOrDefault("angles", ""),
            instance.Properties.GetValueOrDefault("angle", ""), instance.Properties.GetValueOrDefault("modelscale", ""),
            instance.Properties.GetValueOrDefault("modelscale_vec", "") });
        if (_previews.TryGetValue(instance, out var cached) && cached.Signature == signature) return cached.Preview;
        try
        {
            string source = GetSourcePath(instance, mapPath);
            var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mapPath is not null) chain.Add(CanonicalPath(mapPath));
            int count = 0;
            var scopes = new Dictionary<MapEntity, TargetScope>();
            MapDocument preview = Expand(source, SourceRoot(mapPath ?? source), chain, ref count, scopes);
            Matrix4x4 transform = InstanceTransform(instance);
            foreach (MapEntity entity in preview.Entities) SelectionTransforms.ApplyEntity(entity, transform, textureLock: true);
            _previews[instance] = (signature, preview, scopes, null);
            return preview;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or
                                           ArgumentException or InvalidOperationException or OverflowException)
        {
            _previews[instance] = (signature, null, null, exception.Message);
            return null;
        }
    }

    internal IReadOnlyList<MapEntity> ResolveTargets(MapEntity instance, string? mapPath, MapEntity source)
    {
        if (!source.Properties.TryGetValue("target", out string? target) || target.Length == 0 ||
            GetPreview(instance, mapPath) is null || _previews[instance].Scopes is not { } scopes ||
            !scopes.TryGetValue(source, out TargetScope? scope)) return [];
        for (; scope is not null; scope = scope.Parent)
        {
            if (scope.Targets.TryGetValue(target, out MapEntity[]? matches)) return matches;
            if (scope.DescendantTargets.TryGetValue(target, out matches)) return matches;
        }
        return [];
    }

    internal string? Error(MapEntity instance, string? mapPath)
    {
        GetPreview(instance, mapPath);
        return _previews.GetValueOrDefault(instance).Error;
    }

    internal string GetSourcePath(MapEntity instance, string? mapPath)
    {
        if (!IsPrefab(instance)) throw new ArgumentException("Select a prefab instance.");
        if (mapPath is null) throw new ArgumentException("Save the current map before resolving or placing prefab references.");
        return ResolveSourcePath(instance, SourceRoot(mapPath));
    }

    private static string ResolveSourcePath(MapEntity instance, string root)
    {
        string reference = instance.Properties.GetValueOrDefault("model", "").Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference) || reference.Contains(':') ||
            reference.Split('/').Any(segment => segment == ".."))
            throw new ArgumentException("Prefab models must be relative .map paths within the map source folder.");
        string path = Path.GetFullPath(Path.Combine(root, reference));
        RequireWithin(root, path);
        if (!string.Equals(Path.GetExtension(path), ".map", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A prefab must reference a .map source file.");
        return path;
    }

    internal static string SourceRoot(string mapPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(mapPath)) ?? throw new ArgumentException("The map path has no folder.");
        for (DirectoryInfo? parent = new(directory); parent is not null; parent = parent.Parent)
            if (parent.Name.Equals("map_source", StringComparison.OrdinalIgnoreCase)) return parent.FullName;
        for (DirectoryInfo? parent = new(directory); parent is not null; parent = parent.Parent)
            if (parent.Name.Equals("prefabs", StringComparison.OrdinalIgnoreCase) && parent.Parent is { } root) return root.FullName;
        return directory;
    }

    internal static string Reference(string mapPath, string sourcePath)
    {
        string root = SourceRoot(mapPath), path = Path.GetFullPath(sourcePath);
        RequireWithin(root, path);
        if (!Path.GetExtension(path).Equals(".map", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A prefab must be saved as a .map source file.");
        if (CanonicalPath(mapPath).Equals(CanonicalPath(path), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A map cannot reference itself as a prefab.");
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    internal string ValidatePaintSource(string mapPath, string sourcePath)
    {
        string reference = Reference(mapPath, sourcePath);
        var instance = new MapEntity();
        instance.Properties["classname"] = "misc_prefab";
        instance.Properties["model"] = reference;
        try
        {
            if (GetPreview(instance, mapPath) is null)
                throw new ArgumentException(Error(instance, mapPath) ?? "This prefab cannot be opened.");
            return reference;
        }
        finally { _previews.Remove(instance); }
    }

    internal MapEntity Place(EditorSession session, string path, Vector3 origin)
    {
        if (session.FilePath is not { } mapPath) throw new ArgumentException("Save the current map before placing a prefab.");
        MapEntity instance = CreateInstance(mapPath, path, origin);
        if (GetPreview(instance, mapPath) is null) throw new ArgumentException(Error(instance, mapPath));
        session.Edit(() => { session.Document.Entities.Add(instance); session.Selection.Set(instance); });
        return instance;
    }

    internal MapEntity AddPainted(EditorSession session, string path, Vector3 position, Vector3? normal,
        float yaw, float scale)
    {
        if (session.FilePath is not { } mapPath) throw new ArgumentException("Save the current map before painting prefabs.");
        if (!float.IsFinite(yaw) || !float.IsFinite(scale) || scale <= 0 ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("Prefab position, rotation and scale must be finite, and scale must be positive.");
        MapEntity instance = CreateInstance(mapPath, path, Vector3.Zero);
        if (scale != 1) instance.Properties["modelscale"] = scale.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        Vector3 supportNormal = normal is { } surfaceNormal && surfaceNormal.LengthSquared() > 0.000001f
            ? Vector3.Normalize(surfaceNormal) : Vector3.UnitZ;
        if (normal is not null)
        {
            Vector3 axis = Vector3.Cross(Vector3.UnitZ, supportNormal);
            float dot = Math.Clamp(Vector3.Dot(Vector3.UnitZ, supportNormal), -1, 1);
            if (axis.LengthSquared() > 0.000001f)
                EntityOrientation.Transform(instance, Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot)));
            else if (dot < 0)
                EntityOrientation.Transform(instance, Matrix4x4.CreateRotationX(MathF.PI));
        }
        if (yaw != 0)
            EntityOrientation.Transform(instance, Matrix4x4.CreateFromAxisAngle(supportNormal, yaw * (MathF.PI / 180)));
        MapDocument preview = GetPreview(instance, mapPath) ?? throw new ArgumentException(Error(instance, mapPath));
        var content = preview.Brushes.Cast<object>().Concat(preview.Terrains)
            .Concat(preview.Entities.Where(PointEntityGeometry.IsPointEntity));
        if (session.Scene.Bounds(content) is { } bounds)
        {
            Vector3 support = new(supportNormal.X >= 0 ? bounds.Min.X : bounds.Max.X,
                supportNormal.Y >= 0 ? bounds.Min.Y : bounds.Max.Y,
                supportNormal.Z >= 0 ? bounds.Min.Z : bounds.Max.Z);
            position -= supportNormal * Vector3.Dot(support, supportNormal);
        }
        instance.Properties["origin"] = FormattableString.Invariant($"{position.X:G9} {position.Y:G9} {position.Z:G9}");
        session.Document.Entities.Add(instance);
        session.Selection.Set(instance);
        return instance;
    }

    private static MapEntity CreateInstance(string mapPath, string path, Vector3 origin)
    {
        var instance = new MapEntity();
        instance.Properties["classname"] = "misc_prefab";
        instance.Properties["model"] = Reference(mapPath, path);
        instance.Properties["origin"] = FormattableString.Invariant($"{origin.X:G9} {origin.Y:G9} {origin.Z:G9}");
        return instance;
    }

    internal static MapDocument SelectionDocument(EditorSession session)
    {
        if (session.Selection.Count == 0 || session.Selection.Items.Any(item => item is not (MapBrush or MapTerrain or MapEntity)))
            throw new ArgumentException("Select whole brushes, terrain patches or entities to save as a prefab.");
        var result = MapDocument.Create();
        result.Header.Clear();
        result.Header.AddRange(session.Document.Header);
        var selected = session.Selection.Items.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (MapEntity entity in session.Document.Entities)
        {
            if (selected.Contains(entity))
            {
                if (entity.ClassName == "worldspawn") throw new ArgumentException("Select the world's objects instead of worldspawn.");
                result.Entities.Add(entity.Clone());
                continue;
            }
            MapBrush[] brushes = entity.Brushes.Where(selected.Contains).ToArray();
            MapTerrain[] terrains = entity.Terrains.Where(selected.Contains).ToArray();
            if (brushes.Length == 0 && terrains.Length == 0) continue;
            MapEntity copy = entity.ClassName == "worldspawn" ? result.World : entity.Clone();
            if (entity.ClassName != "worldspawn")
            {
                copy.Brushes.Clear(); copy.Terrains.Clear(); copy.PreservedPrimitives.Clear();
                result.Entities.Add(copy);
            }
            copy.Brushes.AddRange(brushes.Select(brush => brush.Clone()));
            copy.Terrains.AddRange(terrains.Select(terrain => terrain.Clone()));
        }
        Vector3 center = session.SelectionBounds is { } bounds ? bounds.Min + (bounds.Max - bounds.Min) / 2 : Vector3.Zero;
        Matrix4x4 transform = Matrix4x4.CreateTranslation(-center);
        foreach (MapEntity entity in result.Entities) SelectionTransforms.ApplyEntity(entity, transform, textureLock: true);
        CopyUsedWaterDefinitions(session.Document, result);
        return result;
    }

    internal void ValidateSave(MapDocument prefab, string path, string? currentMapPath)
    {
        if (currentMapPath is null) throw new ArgumentException("Save the current map before creating a reusable prefab.");
        Reference(currentMapPath, path);
        // Saving into another folder changes the reference root outside map_source. Rebase all existing references first.
        foreach (MapEntity nested in prefab.Entities.Where(IsPrefab))
        {
            string source = GetSourcePath(nested, currentMapPath);
            nested.Properties["model"] = Reference(path, source);
        }
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CanonicalPath(path), CanonicalPath(currentMapPath) };
        int count = 0;
        foreach (MapEntity nested in prefab.Entities.Where(IsPrefab)) Expand(GetSourcePath(nested, path), SourceRoot(path), chain, ref count);
    }

    internal void ValidateMapSave(MapDocument document, string? currentPath, string destination)
    {
        MapEntity[] instances = document.Entities.Where(IsPrefab).ToArray();
        if (instances.Length == 0 || currentPath is not null &&
            CanonicalPath(currentPath).Equals(CanonicalPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        string root = SourceRoot(destination);
        if (currentPath is not null && !CanonicalPath(SourceRoot(currentPath)).Equals(CanonicalPath(root), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("This map contains prefab references. Save it within the same map_source folder (or the same source folder) so all nested references and undo history remain valid.");
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string output = CanonicalPath(destination);
        foreach (MapEntity instance in instances) Visit(ResolveSourcePath(instance, root), 0);

        void Visit(string path, int depth)
        {
            string canonical = CanonicalPath(path);
            if (canonical.Equals(output, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("This destination is referenced by the map's prefab hierarchy. Choose another filename to avoid overwriting a prefab and creating a reference cycle.");
            if (!visited.Add(canonical)) return;
            if (depth >= MaximumDepth || visited.Count > MaximumInstances)
                throw new FormatException("The prefab nesting is too large to verify this save destination safely.");
            MapDocument source;
            try { source = MapFile.Read(path); }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { return; }
            foreach (MapEntity nested in source.Entities.Where(IsPrefab)) Visit(ResolveSourcePath(nested, root), depth + 1);
        }
    }

    internal void Explode(EditorSession session, MapEntity instance)
    {
        MapDocument source = (GetPreview(instance, session.FilePath) ?? throw new ArgumentException(Error(instance, session.FilePath))).Clone();
        session.Edit(() =>
        {
            foreach (object item in MapOrganization.Objects(source)) MapOrganization.Assign(item, MapOrganization.Layer(instance));
            WaterMaterialAuthoring.MergeDefinitions(session.Document.World.Properties, source.World.Properties);
            session.Document.World.Brushes.AddRange(source.World.Brushes);
            session.Document.World.Terrains.AddRange(source.World.Terrains);
            var added = source.World.Brushes.Cast<object>().Concat(source.World.Terrains).ToList();
            foreach (MapEntity entity in source.Entities.Skip(1)) { session.Document.Entities.Add(entity); added.Add(entity); }
            session.Document.Entities.Remove(instance);
            session.Selection.SetRange(added);
        });
    }

    private MapDocument Expand(string path, string referenceRoot, HashSet<string> chain, ref int count,
        Dictionary<MapEntity, TargetScope>? scopes = null, TargetScope? parentScope = null)
    {
        string canonical = CanonicalPath(path);
        if (chain.Count >= MaximumDepth || ++count > MaximumInstances) throw new FormatException("The prefab nesting is too large to expand safely.");
        if (!chain.Add(canonical)) throw new FormatException($"Prefab reference cycle at {Path.GetFileName(path)}.");
        try
        {
            if (!_sources.TryGetValue(canonical, out MapDocument? source))
                _sources.Add(canonical, source = MapFile.Read(path));
            var result = source.Clone();
            if (result.Entities.Any(entity => entity.PreservedPrimitives.Count > 0))
                throw new FormatException($"{Path.GetFileName(path)} contains unsupported primitives; open its source to inspect them before placing or exploding it.");
            TargetScope? scope = scopes is null ? null : new TargetScope(parentScope);
            if (scope is not null && scopes is not null)
            {
                MapEntity[] direct = result.Entities.Where(entity => !IsPrefab(entity)).ToArray();
                foreach (MapEntity entity in direct) scopes.Add(entity, scope);
                IndexTargets(direct, scope.Targets);
            }
            foreach (MapEntity instance in result.Entities.Where(IsPrefab).ToArray())
            {
                MapDocument child = Expand(ResolveSourcePath(instance, referenceRoot), referenceRoot, chain, ref count, scopes, scope);
                Matrix4x4 transform = InstanceTransform(instance);
                foreach (MapEntity entity in child.Entities) SelectionTransforms.ApplyEntity(entity, transform, textureLock: true);
                WaterMaterialAuthoring.MergeDefinitions(result.World.Properties, child.World.Properties);
                result.World.Brushes.AddRange(child.World.Brushes);
                result.World.Terrains.AddRange(child.World.Terrains);
                result.Entities.AddRange(child.Entities.Skip(1));
                result.Entities.Remove(instance);
            }
            if (scope is not null && scopes is not null)
                IndexTargets(result.Entities.Where(entity => !ReferenceEquals(scopes[entity], scope)), scope.DescendantTargets);
            return result;
        }
        finally { chain.Remove(canonical); }
    }

    private static void IndexTargets(IEnumerable<MapEntity> entities, Dictionary<string, MapEntity[]> targets)
    {
        foreach (var group in entities.Where(entity => entity.Properties.ContainsKey("targetname"))
                     .GroupBy(entity => entity.Properties["targetname"], StringComparer.Ordinal))
            targets.Add(group.Key, group.ToArray());
    }

    private static void CopyUsedWaterDefinitions(MapDocument source, MapDocument destination)
    {
        HashSet<string> used = destination.Brushes.SelectMany(brush => brush.Faces).Select(face => face.Material)
            .Concat(destination.Terrains.Select(terrain => terrain.Material)).ToHashSet(StringComparer.Ordinal);
        WaterMaterialDefinition[] definitions = WaterMaterialAuthoring.ReadDefinitions(source.World.Properties)
            .Where(pair => used.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
        WaterMaterialAuthoring.WriteDefinitions(destination.World.Properties, definitions);
    }

    private sealed class TargetScope(TargetScope? parent)
    {
        internal TargetScope? Parent { get; } = parent;
        internal Dictionary<string, MapEntity[]> Targets { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, MapEntity[]> DescendantTargets { get; } = new(StringComparer.Ordinal);
    }

    private static Matrix4x4 InstanceTransform(MapEntity instance)
    {
        Vector3 scale = XModelGeometry.Scale(instance);
        if (scale.X != scale.Y || scale.X != scale.Z) throw new ArgumentException("Prefab instances require a uniform modelscale.");
        return Matrix4x4.CreateScale(scale) * EntityOrientation.Rotation(instance) * Matrix4x4.CreateTranslation(EditorSession.EntityOrigin(instance));
    }

    private static void RequireWithin(string root, string path)
    {
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Keep prefabs inside the current map_source folder (or the saved map's folder), so their references remain portable.");
    }

    private static string CanonicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath) ? new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath : fullPath;
    }
}
