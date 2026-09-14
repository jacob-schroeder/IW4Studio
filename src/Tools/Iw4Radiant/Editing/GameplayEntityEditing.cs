using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Editing;

internal sealed record GameplayEntityType(string Name, string Category, string Description, bool UsesBrushes = false)
{
    public string Label => Name;
}

internal static class GameplayEntityEditing
{
    // Native gameplay classes and compiler-owned lighting markers.
    internal static IReadOnlyList<GameplayEntityType> Types { get; } =
    [
        new("mp_dm_spawn", "Spawns", "Free-for-all spawn. Set position and facing angles."),
        new("mp_tdm_spawn", "Spawns", "Team deathmatch spawn. Set position and facing angles."),
        new("mp_tdm_spawn_allies_start", "Spawns", "Allies starting spawn. Set position and facing angles."),
        new("mp_tdm_spawn_axis_start", "Spawns", "Axis starting spawn. Set position and facing angles."),
        new("mp_dom_spawn", "Spawns", "Domination spawn. Set position and facing angles."),
        new("mp_sd_spawn_attacker", "Spawns", "Search and Destroy attacker spawn. Set position and facing angles."),
        new("mp_sd_spawn_defender", "Spawns", "Search and Destroy defender spawn. Set position and facing angles."),
        new("mp_global_intermission", "Spawns", "Intermission camera position and facing angles."),
        new("reflection_probe", "Lighting", "Bakes reflections from the surrounding map for shiny surfaces. Place where reflections should be sampled."),
        new("script_origin", "Script", "Named script position. Set targetname for map scripts to find it."),
        new("script_struct", "Script", "Script data marker. Set targetname and map-specific script fields in Entity properties."),
        new("script_model", "Script", "Convert selected XModel instances to script_model, preserving their model and transform."),
        new("script_brushmodel", "Script", "Create a script brush model from selected world brushes; use targetname and script fields as needed.", true),
        new("trigger_multiple", "Triggers", "Create a trigger volume from selected world brushes. Configure targetname, target and script_gameobjectname as required by your scripts.", true),
        new("trigger_use_touch", "Triggers", "Create a use/touch volume from selected world brushes. Configure targetname, target and script fields.", true),
        new("trigger_radius", "Triggers", "Place a cylindrical trigger with its origin at the bottom center. Radius and height set its volume; targetname and target connect gameplay entities."),
        new("trigger_hurt", "Triggers", "Create a damage volume from selected world brushes. Edit dmg to choose damage.", true)
    ];

    internal static MapEntity Place(EditorSession session, string className, Vector3 position)
    {
        var type = RequireType(className);
        if (type.UsesBrushes || className == "script_model")
            throw new ArgumentException("This entity requires selected brushes or an existing XModel instance.");
        var entity = new MapEntity();
        entity.Properties["classname"] = className;
        entity.Properties["angles"] = "0 0 0";
        SetOrigin(entity, position);
        if (className == "trigger_radius")
        {
            entity.Properties["radius"] = "64";
            entity.Properties["height"] = "128";
        }
        session.Edit(() => { session.Document.Entities.Add(entity); session.Selection.Set(entity); });
        return entity;
    }

    internal static MapEntity CreateBrushEntity(EditorSession session, string className)
    {
        if (!RequireType(className).UsesBrushes) throw new ArgumentException("Choose a brush entity type.");
        MapBrush[] brushes = session.Selection.Items.OfType<MapBrush>().ToArray();
        if (brushes.Length == 0 || brushes.Length != session.Selection.Count ||
            brushes.Any(brush => !session.Document.World.Brushes.Contains(brush)))
            throw new ArgumentException("Select whole world brushes for this entity. Existing entity brushes must first be moved to the world.");
        var entity = new MapEntity();
        entity.Properties["classname"] = className;
        var bounds = SelectionGeometry.Bounds(brushes.Cast<object>()) ?? throw new ArgumentException("The selected brushes have no bounds.");
        SetOrigin(entity, (bounds.Min + bounds.Max) * 0.5f);
        if (className == "trigger_hurt") entity.Properties["dmg"] = "10000";
        session.Edit(() =>
        {
            foreach (var brush in brushes) { session.Document.World.Brushes.Remove(brush); entity.Brushes.Add(brush); }
            session.Document.Entities.Add(entity);
            session.Selection.Set(entity);
        });
        return entity;
    }

    internal static int ConvertModels(EditorSession session)
    {
        MapEntity[] entities = session.Selection.Items.OfType<MapEntity>().Where(entity =>
            entity.ClassName == "misc_model" && XModelGeometry.IsModel(entity)).ToArray();
        if (entities.Length == 0) throw new ArgumentException("Select placed XModel instances to convert them to script_model.");
        session.Edit(() => { foreach (var entity in entities) entity.Properties["classname"] = "script_model"; });
        return entities.Length;
    }

    internal static void ApplyFields(EditorSession session, MapEntity entity, string name, string target,
        string angles, string spawnFlags, string radius, string height, string damage)
    {
        if (entity.ClassName == "worldspawn") throw new ArgumentException("Choose a gameplay entity.");
        name = Name(name); target = Name(target);
        if (spawnFlags.Length > 0 && !uint.TryParse(spawnFlags, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Spawnflags must be an unsigned integer.");
        var parsed = new MapEntity();
        parsed.Properties["angles"] = angles;
        Vector3 orientation = EntityOrientation.Read(parsed);
        string canonicalAngles = FormattableString.Invariant($"{orientation.X:G9} {orientation.Y:G9} {orientation.Z:G9}");
        if (entity.ClassName == "trigger_radius") { radius = Positive(radius, "Radius"); height = Positive(height, "Height"); }
        if (entity.ClassName == "trigger_hurt") damage = Positive(damage, "Damage");
        string previousName = entity.Properties.GetValueOrDefault("targetname", "");
        bool relink = previousName.Length > 0 && name.Length > 0 && previousName != name &&
            session.Document.Entities.Count(candidate => candidate.Properties.GetValueOrDefault("targetname") == previousName) == 1;
        if (relink && target == previousName) target = name;
        session.Edit(() =>
        {
            if (relink)
                foreach (var source in session.Document.Entities.Where(source => source.Properties.GetValueOrDefault("target") == previousName))
                    source.Properties["target"] = name;
            Set("targetname", name); Set("target", target); Set("spawnflags", spawnFlags.Trim());
            entity.Properties["angles"] = canonicalAngles; entity.Properties.Remove("angle");
            if (entity.ClassName == "trigger_radius") { entity.Properties["radius"] = radius; entity.Properties["height"] = height; }
            if (entity.ClassName == "trigger_hurt") entity.Properties["dmg"] = damage;
        });

        void Set(string property, string value)
        {
            if (value.Length == 0) entity.Properties.Remove(property);
            else entity.Properties[property] = value;
        }
    }

    internal static int Connect(EditorSession session)
    {
        MapEntity[] entities = session.Selection.Items.OfType<MapEntity>().Where(entity => entity.ClassName != "worldspawn").ToArray();
        if (entities.Length < 2 || entities.Length != session.Selection.Count || session.Selection.Active is not MapEntity target)
            throw new ArgumentException("Select source entities, then add the destination last. Connect points the sources at the active destination.");
        string name = target.Properties.GetValueOrDefault("targetname", "");
        if (name.Length > 0 && session.Document.Entities.Any(entity => !ReferenceEquals(entity, target) && entity.Properties.GetValueOrDefault("targetname") == name))
            throw new ArgumentException("The destination targetname is shared by other entities. Assign it a unique name before connecting.");
        if (name.Length == 0)
        {
            int index = 1;
            var names = session.Document.Entities.Select(entity => entity.Properties.GetValueOrDefault("targetname")).ToHashSet(StringComparer.Ordinal);
            do { name = $"radiant_{index++}"; } while (names.Contains(name));
        }
        session.Edit(() =>
        {
            target.Properties["targetname"] = name;
            foreach (var source in entities.Where(entity => !ReferenceEquals(entity, target))) source.Properties["target"] = name;
        });
        return entities.Length - 1;
    }

    internal static int Disconnect(EditorSession session)
    {
        MapEntity[] sources = session.Selection.Items.OfType<MapEntity>().Where(entity => entity.Properties.ContainsKey("target")).ToArray();
        if (sources.Length > 0) session.Edit(() => { foreach (var source in sources) source.Properties.Remove("target"); });
        return sources.Length;
    }

    private static GameplayEntityType RequireType(string className) => Types.FirstOrDefault(type => type.Name == className)
        ?? throw new ArgumentException($"'{className}' is not in the verified IW4 gameplay palette.");
    private static string Name(string value)
    {
        value = value.Trim();
        if (value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) || value.Contains('"'))
            throw new ArgumentException("Target names must be single names without whitespace, quotes or control characters.");
        return value;
    }
    internal static bool TryRadiusDimensions(MapEntity entity, out float radius, out float height)
    {
        radius = height = 0;
        return entity.ClassName == "trigger_radius" && TryPositive(entity.Properties.GetValueOrDefault("radius", ""), out radius) &&
            TryPositive(entity.Properties.GetValueOrDefault("height", ""), out height);
    }

    private static string Positive(string text, string label) => TryPositive(text, out float value)
        ? value.ToString("G9", CultureInfo.InvariantCulture) : throw new ArgumentException($"{label} must be a positive finite number.");

    private static bool TryPositive(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        float.IsFinite(value) && value > 0;
    private static void SetOrigin(MapEntity entity, Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new ArgumentException("Entity position must be finite.");
        entity.Properties["origin"] = FormattableString.Invariant($"{position.X:G9} {position.Y:G9} {position.Z:G9}");
    }
}
