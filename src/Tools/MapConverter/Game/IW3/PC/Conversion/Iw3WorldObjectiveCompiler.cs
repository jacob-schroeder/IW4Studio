using System.Globalization;
using System.Text;
using IW4.Assets.D3dbsp;
using static MapConverter.Game.IW3.PC.Conversion.Iw3WorldFxCompiler;

namespace MapConverter.Game.IW3.PC.Conversion;

internal sealed record Iw3WorldObjectiveCompilation(
    string RawFileName, string Path, IReadOnlyList<string> ModelNames, IReadOnlyList<string> MaterialNames);

internal static class Iw3WorldObjectiveCompiler
{
    private static readonly HashSet<string> ObjectiveTags = new(StringComparer.Ordinal)
        { "sd", "bombzone", "sab", "ctf", "dom", "hq" };

    internal static Iw3WorldObjectiveCompilation? Compile(string bspPath, string mapName, string outputDirectory)
    {
        IReadOnlyList<IReadOnlyDictionary<string, string>> entities = D3dbspFile.Read(bspPath).GetEntities();
        var objectives = entities.Where(entity => HasNamedModel(entity) &&
            entity.TryGetValue("script_gameobjectname", out string? tags) &&
            tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(ObjectiveTags.Contains)).ToList();
        if (objectives.Count == 0)
            return null;

        // Bomb sites use a normal/destroyed model pair. The hidden replacement
        // has no gameobject tag, only the same exploder ID as its normal model.
        HashSet<string> exploders = objectives.Where(entity => entity.ContainsKey("script_exploder"))
            .Select(entity => entity["script_exploder"]).ToHashSet(StringComparer.Ordinal);
        objectives.AddRange(entities.Where(entity => HasNamedModel(entity) &&
            !objectives.Contains(entity) && entity.TryGetValue("script_exploder", out string? id) &&
            exploders.Contains(id)).ToArray());

        foreach (IReadOnlyDictionary<string, string> entity in entities.Where(entity =>
                     entity.TryGetValue("script_gameobjectname", out string? tags) &&
                     tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(ObjectiveTags.Contains)))
        {
            if (!entity.TryGetValue("target", out string? target))
                continue;
            IReadOnlyDictionary<string, string>[] targets = entities.Where(candidate =>
                candidate.TryGetValue("targetname", out string? name) && name == target).ToArray();
            if (targets.Length == 0 || targets.Any(candidate => HasNamedModel(candidate) && !objectives.Contains(candidate)))
                throw new InvalidDataException($"Objective target '{target}' would be missing from the converted map.");
        }

        var models = objectives.Select(entity => NativeModel(entity["model"]))
            .ToHashSet(StringComparer.Ordinal);
        bool hasFlags = objectives.Any(entity => entity.TryGetValue("script_gameobjectname", out string? tags) &&
            tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(tag => tag is "ctf" or "dom"));
        if (hasFlags)
        {
            // These are the native flag names for the factions in the generated
            // map main. CTF also requests the corresponding carry variants.
            models.UnionWith(["prop_flag_ranger", "prop_flag_ranger_carry",
                "prop_flag_speznas", "prop_flag_speznas_carry"]);
        }

        var script = new StringBuilder("main()\n{\n");
        foreach (string model in models.Order(StringComparer.Ordinal))
            script.AppendLine($"\tprecachemodel({Quote(model)});");
        foreach (IReadOnlyDictionary<string, string> entity in objectives)
        {
            string classname = entity["classname"];
            string origin = ParseVector(entity["origin"]);
            if (classname == "script_model")
            {
                script.AppendLine($"\tent = spawn(\"script_model\", {origin});");
                script.AppendLine($"\tent setmodel({Quote(NativeModel(entity["model"]))});");
            }
            else if (classname == "trigger_radius")
            {
                // DOM's source trigger carries a model name, but the native
                // gametype creates its flag visual separately from this trigger.
                script.AppendLine($"\tent = spawn(\"trigger_radius\", {origin}, 0, " +
                    $"{PositiveNumber(entity["radius"])}, {PositiveNumber(entity["height"])});");
            }
            else
                throw new InvalidDataException($"Cannot reconstruct objective entity class '{classname}'.");
            script.AppendLine($"\tent.angles = {ParseVector(entity.GetValueOrDefault("angles", "0 0 0"))};");
            foreach ((string key, string value) in entity)
            {
                switch (key.ToLowerInvariant())
                {
                    case "classname": case "model": case "origin": case "angles": case "radius": case "height":
                        break;
                    case "targetname": case "target": case "script_gameobjectname": case "script_label":
                        script.AppendLine($"\tent.{key.ToLowerInvariant()} = {Quote(value)};");
                        break;
                    case "spawnflags": case "script_exploder":
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                            throw new InvalidDataException($"Invalid objective {key} '{value}'.");
                        script.AppendLine($"\tent.{key.ToLowerInvariant()} = {number.ToString(CultureInfo.InvariantCulture)};");
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported objective entity field '{key}'.");
                }
            }
        }
        AppendDemolitionSetup(script, entities);
        script.AppendLine("}");
        string rawFileName = $"maps/mp/{mapName}_objectives.gsc";
        string path = System.IO.Path.Combine(outputDirectory, mapName + "_objectives.gsc");
        File.WriteAllText(path, script.ToString());
        // common_mp owns these two models and native gametype scripts already
        // precache them. Keep that global ownership instead of cloning them.
        models.ExceptWith(["prop_suitcase_bomb", "prop_flag_neutral"]);
        return new Iw3WorldObjectiveCompilation(rawFileName, path, models.Order(StringComparer.Ordinal).ToArray(),
            hasFlags ? ["objpoint_flag_rangers", "objpoint_flag_ussr"] : []);
    }

    private static void AppendDemolitionSetup(
        StringBuilder script, IReadOnlyList<IReadOnlyDictionary<string, string>> entities)
    {
        // IW3 does not supply MW2's DD spawn classes. Reuse the distinct source
        // SAB respawn/start sets through the native _spawnlogic extension hook.
        (string Native, string Source)[] spawnSets =
        [
            ("mp_dd_spawn_attacker", "mp_sab_spawn_allies"),
            ("mp_dd_spawn_attacker_start", "mp_sab_spawn_allies_start"),
            ("mp_dd_spawn_defender", "mp_sab_spawn_axis"),
            ("mp_dd_spawn_defender_start", "mp_sab_spawn_axis_start")
        ];
        bool HasClass(string name) => entities.Any(entity => entity.GetValueOrDefault("classname") == name);
        var missing = spawnSets.Where(set => !HasClass(set.Native)).ToArray();
        if (missing.Length == 0 || missing.Any(set => !HasClass(set.Source)))
            return;
        var sites = entities.Where(entity => entity.GetValueOrDefault("targetname") == "bombzone")
            .GroupBy(entity => entity.GetValueOrDefault("script_label", ""), StringComparer.Ordinal).ToArray();
        if (sites.Length != 2 || !sites.Select(group => group.Key).ToHashSet(StringComparer.Ordinal).SetEquals(["_a", "_b"]))
            return;

        script.AppendLine("\tif (getdvar(\"g_gametype\") == \"dd\")\n\t{");
        script.AppendLine("\t\tif (!isdefined(level.extraspawnpoints))\n\t\t\tlevel.extraspawnpoints = [];");
        foreach ((string native, string source) in missing)
            script.AppendLine($"\t\tlevel.extraspawnpoints[{Quote(native)}] = getentarray({Quote(source)}, \"classname\");");

        // DD has exactly two timers and wins after two detonations. Co-located
        // copies of a source SD site must not become two separate DD objectives.
        // Leave the source SD graph untouched in every other gametype.
        foreach (var group in sites.Where(group => group.Count() > 1))
        {
            IReadOnlyDictionary<string, string> first = group.First();
            IReadOnlyDictionary<string, string> firstVisual = Visual(first);
            foreach (IReadOnlyDictionary<string, string> duplicate in group.Skip(1))
            {
                IReadOnlyDictionary<string, string> visual = Visual(duplicate);
                if (ParseVector(first["origin"]) != ParseVector(duplicate["origin"]) ||
                    NativeModel(firstVisual["model"]) != NativeModel(visual["model"]) ||
                    ParseVector(firstVisual["origin"]) != ParseVector(visual["origin"]) ||
                    ParseVector(firstVisual.GetValueOrDefault("angles", "0 0 0")) != ParseVector(visual.GetValueOrDefault("angles", "0 0 0")))
                    throw new InvalidDataException($"Demolition requires one unambiguous {group.Key} bomb site.");
                string target = duplicate["target"];
                string defuse = visual["target"];
                string exploder = visual["script_exploder"];
                if (entities.Count(entity => entity.GetValueOrDefault("target") == target) != 1 ||
                    entities.Count(entity => entity.GetValueOrDefault("target") == defuse) != 1 ||
                    entities.Count(entity => entity.GetValueOrDefault("targetname") == defuse) != 1 ||
                    entities.Count(entity => entity.GetValueOrDefault("script_exploder") == exploder) != 2)
                    throw new InvalidDataException("A duplicate bomb site shares its target or exploder graph with other entities.");
                script.AppendLine("\t\tzones = getentarray(\"bombzone\", \"targetname\");");
                script.AppendLine($"\t\tfor (i = 0; i < zones.size; i++)\n\t\t\tif (zones[i].target == {Quote(target)})\n\t\t\t\tzones[i] delete();");
                script.AppendLine($"\t\tdefuse = getent({Quote(defuse)}, \"targetname\");\n\t\tdefuse delete();");
                script.AppendLine("\t\tvisuals = getentarray(\"script_model\", \"classname\");");
                if (!int.TryParse(exploder, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                    throw new InvalidDataException("Invalid duplicate bomb-site exploder ID.");
                script.AppendLine($"\t\tfor (i = 0; i < visuals.size; i++)\n\t\t\tif (isdefined(visuals[i].script_exploder) && visuals[i].script_exploder == {id.ToString(CultureInfo.InvariantCulture)})\n\t\t\t\tvisuals[i] delete();");
            }
        }
        script.AppendLine("\t}");

        IReadOnlyDictionary<string, string> Visual(IReadOnlyDictionary<string, string> site) =>
            entities.Single(entity => entity.GetValueOrDefault("targetname") == site["target"] &&
                entity.GetValueOrDefault("classname") == "script_model");
    }

    private static bool HasNamedModel(IReadOnlyDictionary<string, string> entity) =>
        entity.TryGetValue("model", out string? model) && model.Length != 0 && model[0] is not ('*' or '?');

    private static string NativeModel(string source) => source switch
    {
        "p_glo_bomb_stack" => "com_bomb_objective",
        "p_glo_bomb_stack_d" => "com_bomb_objective_d",
        "mp_supplydrop_hq" => "com_plasticcase_beige_big",
        "prop_suitcase_bomb" or "prop_flag_neutral" or "mil_tntbomb_mp" or
            "com_laptop_2_open" or "com_cellphone_on" => source,
        _ => throw new InvalidDataException($"No native IW4 objective model mapping exists for '{source}'.")
    };

    private static string ParseVector(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            throw new InvalidDataException($"Invalid objective vector '{value}'.");
        float[] elements = parts.Select(part => float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        if (elements.Any(element => !float.IsFinite(element)))
            throw new InvalidDataException($"Non-finite objective vector '{value}'.");
        return Vector(elements);
    }

    private static string PositiveNumber(string value)
    {
        float number = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!float.IsFinite(number) || number <= 0)
            throw new InvalidDataException($"Invalid objective trigger dimension '{value}'.");
        return number.ToString("R", CultureInfo.InvariantCulture);
    }
}
