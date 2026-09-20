using System.Numerics;
using System.Text;
using IW4.AssetExchange.SourceFormat.Material;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.D3dbsp;
using IW4.Unlinker.D3dbsp;
using Iw4Radiant.Materials;
using Iw4Radiant.Editing;
using Iw4Radiant.Rendering;
using Iw4Radiant.MapSource;
using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.Compilation;

internal static class MapCompiler
{
    private static readonly string[] TeamSpawns = ["mp_tdm_spawn", "mp_tdm_spawn_allies_start", "mp_tdm_spawn_axis_start"];

    internal static string GetGameModeSummary(MapDocument document)
    {
        bool freeForAll = document.Entities.Any(entity => entity.ClassName == "mp_dm_spawn");
        bool teamDeathmatch = TeamSpawns.All(name => document.Entities.Any(entity => entity.ClassName == name));
        return freeForAll && teamDeathmatch ? "Free-for-all and Team Deathmatch" :
            freeForAll ? "Free-for-all only. Add all three TDM spawn types to enable Team Deathmatch." :
            "Free-for-all spawns are required before building.";
    }

    internal const string Scope = "Structural, detail, noncolliding, weapon-clip and player-clip world brushes; native all-face water volumes and GPU ocean tops; solid terrain, painted overlays, decals, cutouts and static glass with native materials, skies and static models. " +
        "Bakes point and targeted spot lights, sky ambient and reflections; requires authored sunlight and a reflection probe. " +
        "Native multiplayer points, script entities, brush/trigger models, groups and unambiguous prefabs. One render cell; stage volumes, primary local lights, curves, breakable glass and bounced lighting are not compiled yet.";

    internal static IEnumerable<MapEntity> BrushEntities(MapDocument document) =>
        document.Entities.Where(entity => entity != document.World && entity.Brushes.Count > 0);

    internal static D3dbspFile Compile(MapDocument document, string assetName,
        IReadOnlyDictionary<string, MaterialSource> materials, IReadOnlyDictionary<string, XModelSource> models,
        CancellationToken cancellationToken = default, string? sourcePath = null)
    {
        document = PrefabLibrary.ExpandForCompilation(document, sourcePath);
        foreach (MapEntity group in document.Entities.Where(entity => entity.ClassName == "func_group").ToArray())
        {
            ValidateDirectives(group.Directives);
            if (group.PreservedPrimitives.Count != 0)
                throw new NotSupportedException("Editor groups containing preserved primitives cannot be compiled yet.");
            foreach (string key in group.Properties.Keys)
                if (key is not ("classname" or "targetname" or "origin" or "angles" or "angle"))
                    throw new NotSupportedException($"Editor group property '{key}' has no proven world-geometry compilation rule.");
            MapOrganization.Ungroup(document, group);
        }
        Vector3[] probeOrigins = Validate(document, assetName, materials);
        materials = PrepareWaterMaterials(document, materials);
        ClipMaterial[] baseMaterials = document.Brushes.SelectMany(brush => brush.Faces)
            .Select(face => face.Material).Concat(document.World.Terrains.Select(terrain => terrain.Material))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(name =>
            {
                if (ClipBrushMaterial.IsPlayerClip(name))
                    return new ClipMaterial { Name = name, Contents = ClipBrushMaterial.Contents,
                        SurfaceFlags = ClipBrushMaterial.SurfaceFlags };
                if (CaulkMaterial.IsCaulk(name))
                    return new ClipMaterial { Name = name, Contents = CaulkMaterial.Contents,
                        SurfaceFlags = CaulkMaterial.SurfaceFlags };
                if (!materials.TryGetValue(name, out MaterialSource? material) ||
                    !material.IsWater && !File.Exists(material.ImagePath))
                    throw new InvalidDataException($"Material '{name}' is unavailable. Load its material and image in the asset browser before building.");
                MaterialSurfaceState state = material.Surface;
                if (material.IsWater && (material.TechniqueSet is not ("w_water" or "wc_water") ||
                    material.SurfaceTypeBits != MaterialSurfaceTypeBits.Water ||
                    material.GameFlags != (MaterialGameFlags.NoMarks | MaterialGameFlags.HasReflection)))
                    throw new NotSupportedException($"Water material '{name}' is outside the proven stock PS3 w_water/wc_water profile.");
                if (!material.IsSky && (state.TechniqueType != MaterialTechniqueType.Lit ||
                    !(material.TechniqueSet.StartsWith("w_", StringComparison.Ordinal) || material.TechniqueSet.StartsWith("wc_", StringComparison.Ordinal)) ||
                    state.IsBlended && !state.SupportsAlpha || !state.IsBlended && !state.DepthWrite ||
                    state.DepthTest is not (GfxDepthTest.Less or GfxDepthTest.LessThanOrEqual) || state.SortKey is < 0 or >= 39))
                    throw new NotSupportedException($"Material '{name}' is outside the native lit world profile. Use an opaque, alpha-tested or standard alpha-blended world material.");
                // Native PS3 Rust and Highrise sky brushes use SKY|NOIMPACT|NOMARKS
                // and CONTENTS_SKY without CONTENTS_SOLID.
                return new ClipMaterial { Name = name, Contents = material.IsSky ? 0x800 : material.GetCollisionContents(),
                    SurfaceFlags = material.IsSky ? 0x34 : material.GetCollisionSurfaceFlags() };
            }).ToArray();
        var baseMaterialsByName = baseMaterials.ToDictionary(material =>
            material.Name ?? throw new InvalidDataException("A collision material has no name."), StringComparer.Ordinal);
        ClipMaterial[] clipMaterials = document.Brushes.SelectMany(brush =>
            {
                BrushKind kind = BrushContents.ReadForCompilation(brush);
                return brush.Faces.Select(face =>
                {
                    ClipMaterial material = baseMaterialsByName[face.Material];
                    return (face.Material, Contents: BrushContents.Compile(kind, material.Contents));
                });
            })
            .Concat(document.World.Terrains.Select(terrain =>
                (terrain.Material, baseMaterialsByName[terrain.Material].Contents)))
            .Distinct()
            .OrderBy(item => item.Material, StringComparer.Ordinal).ThenBy(item => item.Contents)
            .Select(item => new ClipMaterial
            {
                Name = item.Material,
                Contents = item.Contents,
                SurfaceFlags = baseMaterialsByName[item.Material].SurfaceFlags
            }).ToArray();
        MapEntsAsset entities = CompileEntities(document, assetName);
        ClipMapAsset collision = BrushCollisionCompiler.Compile(document, assetName, clipMaterials,
            baseMaterialsByName, entities);
        collision = TerrainCollisionCompiler.Append(collision,
            document.World.Terrains.Where(terrain => !TerrainContents.ReadNonColliding(terrain)).ToArray());
        var sun = BrushRenderCompiler.CompileSun(document, assetName);
        var graphics = BrushRenderCompiler.Compile(document, assetName, collision, sun, materials, models, probeOrigins, cancellationToken);
        return MapStaticModelCompiler.Append(document, D3dbspUnlinker.Unlink([
            collision, sun, graphics, entities,
            new GameWorldMpAsset { Name = assetName, GlassData = new GGlassData() },
            new FxWorldAsset { Name = assetName }
        ]));
    }

    private static IReadOnlyDictionary<string, MaterialSource> PrepareWaterMaterials(MapDocument document,
        IReadOnlyDictionary<string, MaterialSource> materials)
    {
        // Give unedited stock water the same two-sided native surface path as
        // inspector-authored water, without modifying the user's source document.
        var prepared = new Dictionary<string, MaterialSource>(materials, StringComparer.Ordinal);
        var definitions = new Dictionary<string, WaterMaterialDefinition>(
            WaterMaterialAuthoring.ReadDefinitions(document.World.Properties), StringComparer.Ordinal);
        foreach (MapFace face in document.World.Brushes.SelectMany(brush => brush.Faces))
        {
            if (!prepared.TryGetValue(face.Material, out MaterialSource? source) || !source.IsWater ||
                WaterMaterialAuthoring.IsAuthoredMaterialName(face.Material)) continue;
            Vector4 color = source.WaterColor, reflection = source.EnvMapParms;
            WaterMaterialDefinition definition = WaterMaterialAuthoring.CreateDefinition(source.Name,
                color.X, color.Y, color.Z, 1, 1, reflection.X, reflection.Y, reflection.Z);
            definitions[definition.Name] = definition;
            prepared.TryAdd(definition.Name, source with { Name = definition.Name });
            face.Material = definition.Name;
        }
        WaterMaterialAuthoring.WriteDefinitions(document.World.Properties, definitions.Values);
        return prepared;
    }

    private static Vector3[] Validate(MapDocument document, string assetName, IReadOnlyDictionary<string, MaterialSource> materials)
    {
        var probeOrigins = new List<Vector3>();
        if (!assetName.StartsWith("maps/mp/mp_", StringComparison.Ordinal) ||
            !assetName.EndsWith(".d3dbsp", StringComparison.Ordinal) ||
            assetName[8..^7].Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new ArgumentException("Use a map filename beginning with mp_ and containing only lowercase letters, numbers and underscores.");
        if (document.Entities.Count == 0 || document.Entities[0].ClassName != "worldspawn" ||
            document.Entities.Count(entity => entity.ClassName == "worldspawn") != 1)
            throw new InvalidDataException("The map must begin with exactly one worldspawn.");
        if (document.World.Brushes.Count == 0)
            throw new InvalidDataException("Create solid world brushes before building.");
        if (!document.Entities.Any(entity => entity.ClassName == "reflection_probe"))
            throw new InvalidDataException("Add a reflection_probe in Create → Gameplay entities → Lighting before building. It captures the map's reflections during compilation.");
        foreach (MapBrush brush in document.Brushes)
        {
            BrushGeometry.Validate(brush);
            BrushKind kind = BrushContents.ReadForCompilation(brush);
            ValidateDirectives(brush.Directives, brushContents: true);
            if (brush.Faces.Any(face => ClipBrushMaterial.IsPlayerClip(face.Material)) &&
                !brush.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material)))
                throw new NotSupportedException("Apply clip_player to every face of a player clip brush. Mixed player clip and visible faces are not supported by compilation.");
            bool playerClip = brush.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material));
            bool anySky = brush.Faces.Any(face => materials.TryGetValue(face.Material, out var material) && material.IsSky);
            bool sky = brush.Faces.All(face => materials.TryGetValue(face.Material, out var material) && material.IsSky);
            bool anyWater = brush.Faces.Any(face => materials.TryGetValue(face.Material, out var material) && material.IsWater);
            bool water = brush.Faces.All(face => materials.TryGetValue(face.Material, out var material) && material.IsWater);
            if (anySky && !sky)
                throw new NotSupportedException("Apply sky materials to every face of a sky brush. Mixed sky and solid faces are not supported by compilation yet.");
            if (anyWater && !water)
                throw new NotSupportedException("Apply native water materials to every face of a water brush. Mixed water and solid faces have no proven collision contents.");
            if (water && !document.World.Brushes.Contains(brush))
                throw new NotSupportedException("Native water is supported only on world brushes, not brush entities.");
            if (brush.Faces.Any(face => materials.TryGetValue(face.Material, out var material) && material.Ocean is not null) &&
                !brush.GetPolygons().Any(OceanSurfaceGeometry.IsTop))
                throw new NotSupportedException("Ocean waves require an upward horizontal water surface. Select a horizontal water top and apply ocean waves.");
            if (water && kind != BrushKind.Structural)
                throw new NotSupportedException("Native water already supplies its proven DETAIL|WATER contents. Remove the brush contents override.");
            if (kind != BrushKind.Structural && (playerClip || sky))
                throw new NotSupportedException("Brush contents directives require visible, non-sky world materials. Player clip and sky already define their native contents.");
            foreach (MapFace face in brush.Faces)
            {
                var projection = SurfaceProjection.Parse(face.Projection);
                _ = projection.GetMapping(face.Normal);
                var suffix = MapTokenizer.Tokenize(projection.Suffix);
                if (suffix.Count != 7 || suffix[0].Value != "lightmap_gray")
                    throw new NotSupportedException("Compilation requires the standard lightmap_gray face projection without extra face attributes.");
                _ = SurfaceProjection.Parse(string.Join(' ', suffix.Skip(1).Select(token => token.Value))).GetMapping(face.Normal);
            }
        }
        foreach (MapEntity entity in document.Entities)
        {
            ValidateDirectives(entity.Directives);
            if (entity.PreservedPrimitives.Count != 0)
                throw new NotSupportedException("Compilation does not yet support preserved primitives.");
            if (entity == document.World)
            {
                foreach (MapTerrain terrain in entity.Terrains)
                {
                    MapSurfaceCompiler.ValidateTerrain(terrain);
                    _ = TerrainContents.ReadNonColliding(terrain);
                    if (materials.TryGetValue(terrain.Material, out var paintedMaterial) && !paintedMaterial.UsesVertexColor &&
                        terrain.Colors.Any(color => color != Vector4.One))
                        throw new NotSupportedException($"Material '{terrain.Material}' does not consume painted vertex color or alpha. Apply a wc_ world material before painting, or reset its vertex colors to white and alpha to 100%.");
                    if (ClipBrushMaterial.IsPlayerClip(terrain.Material) || materials.TryGetValue(terrain.Material, out var material) && material.IsSky)
                        throw new NotSupportedException("Use world brushes for sky and player clip. These materials are not supported on meshes.");
                    if (materials.TryGetValue(terrain.Material, out var terrainMaterial) && terrainMaterial.IsWater)
                        throw new NotSupportedException("Native water requires a closed world-brush volume; water materials are not supported on terrain meshes.");
                }
                continue;
            }
            if (entity.Terrains.Count != 0)
                throw new NotSupportedException("Terrain must belong to worldspawn before compilation.");
            GameplayEntityType? type = GameplayEntityEditing.Types.FirstOrDefault(type => type.Name == entity.ClassName);
            if (type is null)
                throw new NotSupportedException($"Entity '{entity.ClassName}' is not supported by compilation.");
            if (entity.ClassName == "stage")
                throw new NotSupportedException("Authored stage volumes require matching MapEnts Stage rows, stage-local ComWorld sun lights and a stage-aware lighting bake. This compiler currently builds only stage 0; the source volume and lighting properties are preserved but cannot yet be compiled.");
            if (!entity.TryGetOrigin(out Vector3 origin))
                throw new InvalidDataException($"Entity '{entity.ClassName}' needs a finite three-component origin.");
            _ = EntityOrientation.Read(entity);
            if (type.UsesBrushes)
            {
                if (entity.Brushes.Count == 0) throw new InvalidDataException($"Entity '{entity.ClassName}' needs at least one convex brush.");
                if (entity.Properties.ContainsKey("model"))
                    throw new InvalidDataException($"Entity '{entity.ClassName}' has a manual model reference. Remove it; compilation assigns its brush model.");
                continue;
            }
            if (entity.Brushes.Count != 0)
                throw new InvalidDataException($"Point entity '{entity.ClassName}' cannot own brushes.");
            if (entity.ClassName == "misc_model") continue;
            if (entity.ClassName is "script_model" or "misc_turret")
            {
                if (!XModelGeometry.IsModel(entity) || string.IsNullOrWhiteSpace(entity.Properties.GetValueOrDefault("model")))
                    throw new InvalidDataException($"A {entity.ClassName} requires a named XModel.");
                if (XModelGeometry.Scale(entity) != Vector3.One)
                    throw new NotSupportedException($"Runtime {entity.ClassName} scaling is not proven for this PS3 build. Set modelscale to 1 before compiling.");
                if (entity.ClassName == "misc_turret" && string.IsNullOrWhiteSpace(entity.Properties.GetValueOrDefault("weaponinfo")))
                    throw new InvalidDataException("A misc_turret requires a native weaponinfo asset name.");
                continue;
            }
            if (entity.ClassName == "trigger_radius" && !GameplayEntityEditing.TryRadiusDimensions(entity, out _, out _))
                throw new InvalidDataException("A trigger_radius requires positive finite radius and height.");
            if (entity.ClassName == "info_null")
            {
                ValidateLightTarget(document, entity);
                continue;
            }
            if (entity.ClassName == "light") ValidateLight(document, entity);
            if (entity.Properties.ContainsKey("model"))
                throw new NotSupportedException($"Entity '{entity.ClassName}' has a model reference; use misc_model for compiled static models.");
            foreach (MapBrush brush in document.World.Brushes.Where(_ => type.Category == "Spawns" || entity.ClassName == "reflection_probe"))
                if (!brush.Faces.All(face => materials.TryGetValue(face.Material, out var material) && material.IsSky) &&
                    !brush.Faces.All(face => materials.TryGetValue(face.Material, out var material) && material.IsWater) &&
                    !(entity.ClassName == "reflection_probe" && brush.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material))) &&
                    (brush.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material)) ||
                     BrushContents.BlocksPlayer(BrushContents.ReadForCompilation(brush))) &&
                    brush.Faces.All(face => BrushGeometry.Dot(face.Normal, origin - face.A) <= BrushGeometry.PlaneTolerance))
                    throw new InvalidDataException($"Entity '{entity.ClassName}' at {entity.Properties["origin"]} is inside a blocking brush. Move it into playable space.");
            if (entity.ClassName == "reflection_probe")
            {
                foreach (string key in entity.Properties.Keys)
                    if (key is not ("classname" or "origin" or "angles" or "angle"))
                        throw new NotSupportedException($"Reflection probe property '{key}' is not supported by compilation.");
                probeOrigins.Add(origin);
            }
        }
        RequireEntity("mp_dm_spawn");
        RequireEntity("mp_global_intermission");
        if (document.Entities.Any(entity => TeamSpawns.Contains(entity.ClassName)))
            foreach (string name in TeamSpawns) RequireEntity(name);
        return probeOrigins.ToArray();

        void RequireEntity(string name)
        {
            if (!document.Entities.Any(entity => entity.ClassName == name))
                throw new InvalidDataException($"Add a {name} entity in Create → Gameplay entities before building.");
        }
    }

    private static void ValidateLight(MapDocument document, MapEntity entity)
    {
        foreach (string key in entity.Properties.Keys)
            if (key is not ("classname" or "origin" or "angles" or "angle" or "targetname" or "target" or
                "def" or "radius" or "intensity" or "_color" or "fov_outer" or "fov_inner" or "exponent" or "spawnflags"))
                throw new NotSupportedException($"Light property '{key}' is not supported by static light compilation.");
        if (!MapLightProperties.TryRead(entity, out MapLightProperties properties, out string? error))
            throw new InvalidDataException(error);
        if (properties.SpawnFlags != 0)
            throw new NotSupportedException("Local lights currently bake static illumination. Clear Primary omni/spot and other light spawnflags before building.");
        if (!MapLight.TryCreate(entity, document.ResolveTargets(entity), out _, out error) && error is not null)
            throw new InvalidDataException(error);
    }

    private static void ValidateLightTarget(MapDocument document, MapEntity entity)
    {
        string? name = entity.Properties.GetValueOrDefault("targetname");
        if (string.IsNullOrWhiteSpace(name) || !document.Entities.Any(light => light.ClassName == "light" &&
            document.ResolveTargets(light).Contains(entity)))
            throw new NotSupportedException("An info_null must be a named light aim target before compilation. Use Create aim target in the Light inspector.");
        foreach (string key in entity.Properties.Keys)
            if (key is not ("classname" or "origin" or "angles" or "angle" or "targetname"))
                throw new NotSupportedException($"Light aim target property '{key}' is not supported by compilation.");
    }

    private static void ValidateDirectives(IEnumerable<string> directives, bool brushContents = false)
    {
        foreach (string directive in directives)
        {
            var tokens = MapTokenizer.Tokenize(directive);
            if (MapOrganization.IsLayerDirective(directive)) continue;
            if (brushContents && tokens.Count > 0 && !tokens[0].Quoted && tokens[0].Value == "contents") continue;
            if (brushContents && MapToolFlags.ReadForCompilation(directive)) continue;
            throw new NotSupportedException($"Source directive '{directive}' is not supported by compilation.");
        }
    }

    private static MapEntsAsset CompileEntities(MapDocument document, string assetName)
    {
        var source = MapDocument.Create();
        source.Header.Clear();
        source.Entities.Clear();
        int brushModel = 0;
        foreach (MapEntity entity in document.Entities)
        {
            // Static light entities and their aim markers are consumed by the bake;
            // retaining them in MapEnts would imply runtime light/script behavior.
            if (entity.ClassName is "light" or "info_null") continue;
            var point = new MapEntity();
            foreach (var property in entity.Properties) point.Properties.Add(property.Key, property.Value);
            if (entity.Brushes.Count > 0 && entity != document.World)
                point.Properties["model"] = $"*{++brushModel}";
            source.Entities.Add(point);
        }
        string text = MapWriter.Serialize(source);
        byte[] bytes = Encoding.GetEncoding(Encoding.Latin1.CodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(text + '\0');
        return new MapEntsAsset
        {
            Name = assetName, EntityString = text, EntityStringBytes = bytes, NumEntityChars = bytes.Length,
            StageCount = 1,
            Stages = [new Stage { StageName = "stage 0", TriggerIndex = 1024, SunPrimaryLightIndex = 1 }]
        };
    }
}
