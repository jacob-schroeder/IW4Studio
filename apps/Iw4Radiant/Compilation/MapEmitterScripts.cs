using System.Globalization;
using System.Numerics;
using System.Text;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal sealed record MapEmitterScripts(
    string MapFxName, string MapFxSource, string CreateFxName, string CreateFxSource,
    string[] FxNames, string[] SoundNames)
{
    internal IReadOnlyList<(string Name, string Path)> WriteTo(string directory)
    {
        string mapFxPath = Path.Combine(directory, MapFxName.Replace('/', Path.DirectorySeparatorChar));
        string createFxPath = Path.Combine(directory, CreateFxName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(mapFxPath) ??
            throw new InvalidDataException("The map FX output has no directory."));
        Directory.CreateDirectory(Path.GetDirectoryName(createFxPath) ??
            throw new InvalidDataException("The CreateFX output has no directory."));
        File.WriteAllText(mapFxPath, MapFxSource, new UTF8Encoding(false));
        File.WriteAllText(createFxPath, CreateFxSource, new UTF8Encoding(false));
        return [(MapFxName, mapFxPath), (CreateFxName, createFxPath)];
    }
}

internal static class MapEmitterScriptAuthoring
{
    internal static MapEmitterScripts? Create(MapDocument source, string sourcePath, string mapName)
    {
        MapDocument expanded = PrefabLibrary.ExpandForCompilation(source, sourcePath);
        MapEntity[] markers = expanded.Entities.Where(entity => entity.ClassName == "fx_origin").ToArray();
        if (markers.Length == 0) return null;
        if (mapName.Length == 0 || mapName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidDataException("A map with FX or sound markers needs a filename containing only letters, numbers, and underscores.");

        var effects = new List<(string Name, Vector3 Origin, Vector3 Angles, string? StartDelay, string? TriggerKey)>();
        var sounds = new List<(string Name, Vector3 Origin, Vector3 Angles)>();
        var triggerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (MapEntity marker in markers)
        {
            if (marker.Brushes.Count != 0 || marker.Terrains.Count != 0 || marker.PreservedPrimitives.Count != 0)
                throw new InvalidDataException("An FX or sound marker must be a point entity without brushes or terrain.");
            if (!marker.TryGetOrigin(out Vector3 origin))
                throw new InvalidDataException("An FX or sound marker needs a finite three-component origin.");
            Vector3 angles = EntityOrientation.Read(marker);
            string soundMode = marker.Properties.GetValueOrDefault("is_sound", "");
            if (soundMode is not ("" or "0" or "1"))
                throw new InvalidDataException("An FX or sound marker's is_sound field must be 0 or 1.");
            bool isSound = soundMode == "1";
            string key = isSound ? "soundalias" : "fx";
            string name = marker.Properties.GetValueOrDefault(key) ?? "";
            RequireAssetName(name, key);
            if ((isSound && !string.IsNullOrWhiteSpace(marker.Properties.GetValueOrDefault("fx"))) ||
                (!isSound && !string.IsNullOrWhiteSpace(marker.Properties.GetValueOrDefault("soundalias"))))
                throw new InvalidDataException("Choose either an FX or a sound alias for each marker, not both.");
            string playback = marker.Properties.GetValueOrDefault("fx_playback", "");
            string startDelay = marker.Properties.GetValueOrDefault("fx_start_delay", "");
            string triggerKey = marker.Properties.GetValueOrDefault("fx_trigger_key", "");
            if (isSound)
            {
                if (playback.Length != 0 || startDelay.Length != 0 || triggerKey.Length != 0)
                    throw new InvalidDataException("FX playback settings cannot be used on a sound marker.");
                sounds.Add((name, origin, angles));
            }
            else if (playback == "script")
            {
                if (startDelay.Length != 0)
                    throw new InvalidDataException("A script-triggered FX marker cannot have a map-start delay.");
                if (!ValidTriggerKey(triggerKey))
                    throw new InvalidDataException("A script-triggered FX marker needs a key of letters, numbers and underscores, starting with a letter or underscore.");
                if (!triggerKeys.Add(triggerKey))
                    throw new InvalidDataException($"FX script key '{triggerKey}' is duplicated after prefab expansion. Give each callable marker a unique key.");
                effects.Add((name, origin, angles, null, triggerKey));
            }
            else
            {
                if (playback is not ("" or "start") || triggerKey.Length != 0)
                    throw new InvalidDataException("FX playback must be map start or script call; only script-call markers may have a trigger key.");
                string? delay = null;
                if (startDelay.Length != 0)
                {
                    if (!float.TryParse(startDelay, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) ||
                        !float.IsFinite(seconds) || seconds < 0)
                        throw new InvalidDataException("FX start delay must be a finite number of seconds, zero or greater.");
                    delay = seconds.ToString("G9", CultureInfo.InvariantCulture);
                }
                effects.Add((name, origin, angles, delay, null));
            }
        }

        string[] fxNames = effects.Select(effect => effect.Name).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        string[] soundNames = sounds.Select(sound => sound.Name).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var mapFx = new StringBuilder("main()\r\n{\r\n");
        foreach (string name in fxNames)
            mapFx.Append("\tlevel._effect[ \"").Append(name).Append("\" ] = loadfx( \"")
                .Append(name).Append("\" );\r\n");
        // Authored markers are omitted from MapEnts. Map-start markers use
        // CreateFX records; script-call markers use the function below.
        mapFx.Append("\tmaps\\createfx\\").Append(mapName).Append("_fx::main();\r\n}\r\n");
        if (triggerKeys.Count != 0)
        {
            mapFx.Append("\r\n// Call maps\\mp\\").Append(mapName)
                .Append("_fx::trigger( \"key\" ) after main() has registered the map FX.\r\n")
                .Append("trigger( key )\r\n{\r\n")
                .Append("\tif ( !isdefined( level.iw4radiant_").Append(mapName)
                .Append("_emitters_loaded ) )\r\n\t\treturn;\r\n");
            foreach (var effect in effects.Where(effect => effect.TriggerKey is not null))
            {
                mapFx.Append("\tif ( key == \"").Append(effect.TriggerKey).Append("\" )\r\n\t{\r\n")
                    .Append("\t\tfx = spawnFx( level._effect[ \"").Append(effect.Name)
                    .Append("\" ], ").Append(Vector(effect.Origin)).Append(", anglestoforward( ")
                    .Append(Vector(effect.Angles)).Append(" ), anglestoup( ")
                    .Append(Vector(effect.Angles)).Append(" ) );\r\n")
                    .Append("\t\ttriggerFx( fx, -15 );\r\n")
                    .Append("\t\tfx willNeverChange();\r\n\t\treturn;\r\n\t}\r\n");
            }
            mapFx.Append("}\r\n");
        }

        var createFx = new StringBuilder("#include common_scripts\\utility;\r\n#include common_scripts\\_createfx;\r\nmain()\r\n{\r\n");
        // Both the map FX entry point and custom scripts can call this file.
        // Register this map's placements once; distinct authored markers remain distinct.
        string initialized = "level.iw4radiant_" + mapName + "_emitters_loaded";
        createFx.Append("\tif ( isdefined( ").Append(initialized).Append(" ) )\r\n\t\treturn;\r\n")
            .Append('\t').Append(initialized).Append(" = true;\r\n\r\n");
        foreach (var effect in effects.Where(effect => effect.TriggerKey is null))
        {
            createFx.Append("\tent = createOneshotEffect( \"").Append(effect.Name).Append("\" );\r\n")
                .Append("\tent.v[ \"origin\" ] = ").Append(Vector(effect.Origin)).Append(";\r\n")
                .Append("\tent.v[ \"angles\" ] = ").Append(Vector(effect.Angles)).Append(";\r\n")
                .Append("\tent.v[ \"fxid\" ] = \"").Append(effect.Name).Append("\";\r\n");
            if (effect.StartDelay is not null)
                createFx.Append("\tent.v[ \"delay\" ] = ").Append(effect.StartDelay).Append(";\r\n");
            createFx.Append("\r\n");
        }
        foreach (var sound in sounds)
        {
            createFx.Append("\tent = createLoopSound();\r\n")
                .Append("\tent.v[ \"origin\" ] = ").Append(Vector(sound.Origin)).Append(";\r\n")
                .Append("\tent.v[ \"angles\" ] = ").Append(Vector(sound.Angles)).Append(";\r\n")
                .Append("\tent.v[ \"soundalias\" ] = \"").Append(sound.Name).Append("\";\r\n\r\n");
        }
        createFx.Append("}\r\n");
        return new MapEmitterScripts(
            $"maps/mp/{mapName}_fx.gsc", mapFx.ToString(),
            $"maps/createfx/{mapName}_fx.gsc", createFx.ToString(), fxNames, soundNames);
    }

    private static string Vector(Vector3 value) => string.Format(CultureInfo.InvariantCulture,
        "( {0:G9}, {1:G9}, {2:G9} )", value.X, value.Y, value.Z);

    private static bool ValidTriggerKey(string key) => key.Length != 0 &&
        (char.IsAsciiLetter(key[0]) || key[0] == '_') &&
        key.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static void RequireAssetName(string name, string key)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
            name.Any(character => character is '"' or '\\' || char.IsControl(character)))
            throw new InvalidDataException($"An FX or sound marker needs a valid exact {key} asset name.");
    }
}
