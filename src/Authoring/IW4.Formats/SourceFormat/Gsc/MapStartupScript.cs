using System.Text;
using IW4.Formats.SourceFormat.Character;

namespace IW4.Formats.SourceFormat.Gsc;

public static class MapStartupScript
{
    public static string Create(string scriptName, bool hasMapFxScript, bool hasMapMovingLightsScript,
        bool hasMapFogScript, MapFactionSettings factions, IReadOnlyList<string> destructiblePrecacheScripts,
        string? waterScriptName = null)
    {
        string scriptPrefix = scriptName[..^".gsc".Length];
        bool authoredAssault = factions.AlliesAssaultA is not null || factions.AxisAssaultA is not null;
        var appearancePrecache = new StringBuilder();
        if (factions.AlliesAssaultA is { } alliesPrecache)
            AppendRangersAssaultPrecache(appearancePrecache, alliesPrecache);
        if (factions.AxisAssaultA is { } axisPrecache)
            AppendRangersAssaultPrecache(appearancePrecache, axisPrecache);
        // These factions own the player-model closure selected below.
        string script =
            "main()\r\n" +
            "{\r\n" +
            string.Concat(destructiblePrecacheScripts.Select(name =>
                "\t" + name[..^".gsc".Length].Replace('/', '\\') + "::main();\r\n")) +
            (hasMapFxScript ? "\t" + ScriptStartup(scriptPrefix + "_fx.gsc") + "\r\n" : "") +
            (hasMapMovingLightsScript ? "\t" + ScriptStartup(scriptPrefix + "_lights.gsc") + "\r\n" : "") +
            "\tmaps\\mp\\_load::main();\r\n" +
            (hasMapFogScript ? "\t" + ScriptStartup(scriptPrefix + "_fog.gsc") + "\r\n" : "") +
            $"\tgame[\"allies\"] = \"{factions.Allies}\";\r\n" +
            $"\tgame[\"axis\"] = \"{factions.Axis}\";\r\n" +
            "\tgame[\"attackers\"] = \"allies\";\r\n" +
            "\tgame[\"defenders\"] = \"axis\";\r\n" +
            appearancePrecache.ToString() +
            (authoredAssault ? "\tlevel.iw4radiant_originalOnStartGameType = level.onStartGameType;\r\n\tlevel.onStartGameType = ::iw4radiant_onStartGameType;\r\n" : "") +
            (waterScriptName is null ? "" : "\t" + ScriptStartup(waterScriptName) + "\r\n") +
            "}\r\n";
        if (authoredAssault)
        {
            var additions = new StringBuilder();
            additions.Append("\r\niw4radiant_onStartGameType()\r\n{\r\n");
            if (factions.AlliesAssaultA is not null)
                additions.Append("\tif ( game[\"allies\"] == \"us_army\" )\r\n\t\tgame[\"allies_model\"][\"ASSAULT\"] = ::iw4radiant_allies_assault;\r\n");
            if (factions.AxisAssaultA is not null)
                additions.Append("\tif ( game[\"axis\"] == \"us_army\" )\r\n\t\tgame[\"axis_model\"][\"ASSAULT\"] = ::iw4radiant_axis_assault;\r\n");
            additions.Append("\t[[level.iw4radiant_originalOnStartGameType]]();\r\n");
            additions.Append("}\r\n");
            if (factions.AlliesAssaultA is { } alliesAppearance)
                AppendRangersAssaultCallback(additions, "allies", alliesAppearance);
            if (factions.AxisAssaultA is { } axisAppearance)
                AppendRangersAssaultCallback(additions, "axis", axisAppearance);
            script += additions.ToString();
        }
        return script;
    }

    private static string ScriptStartup(string scriptName) =>
        scriptName[..^".gsc".Length].Replace('/', '\\') + "::main();";

    private static void AppendRangersAssaultPrecache(StringBuilder script, FactionAppearance appearance)
    {
        script.Append("\tprecacheModel(\"").Append(appearance.Body).Append("\");\r\n");
        if (appearance.Head is { } head)
            script.Append("\tprecacheModel(\"").Append(head).Append("\");\r\n");
        else if (!appearance.HeadIncluded)
            script.Append("\tcodescripts\\character::precacheModelArray(xmodelalias\\alias_us_army_heads::main());\r\n");
        script.Append("\tprecacheModel(\"").Append(appearance.ViewHands).Append("\");\r\n");
    }

    private static void AppendRangersAssaultCallback(StringBuilder script, string team, FactionAppearance appearance)
    {
        script.Append("\r\niw4radiant_").Append(team).Append("_assault()\r\n{\r\n")
            .Append("\tswitch( codescripts\\character::get_random_character(3) )\r\n\t{\r\n")
            .Append("\tcase 0:\r\n")
            .Append("\t\tself setModel(\"").Append(appearance.Body).Append("\");\r\n");
        if (appearance.Head is { } head)
            script.Append("\t\tiw4radiant_heads = [];\r\n")
                .Append("\t\tiw4radiant_heads[0] = \"").Append(head).Append("\";\r\n")
                .Append("\t\tcodescripts\\character::attachHead(\"iw4radiant_").Append(team)
                .Append("_assault_head\", iw4radiant_heads);\r\n");
        else if (!appearance.HeadIncluded)
            script.Append("\t\tcodescripts\\character::attachHead(\"alias_us_army_heads\", xmodelalias\\alias_us_army_heads::main());\r\n");
        script.Append("\t\tself setViewmodel(\"").Append(appearance.ViewHands).Append("\");\r\n")
            .Append("\t\tself.voice = \"american\";\r\n")
            .Append("\t\tbreak;\r\n")
            .Append("\tcase 1:\r\n\t\tcharacter\\mp_character_us_army_assault_b::main();\r\n\t\tbreak;\r\n")
            .Append("\tcase 2:\r\n\t\tcharacter\\mp_character_us_army_assault_c::main();\r\n\t\tbreak;\r\n")
            .Append("\t}\r\n}\r\n");
    }
}
