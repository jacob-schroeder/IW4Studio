using System.Text.Json;
using System.Text.Json.Nodes;
using IW4.Formats.SourceFormat.Fx;

namespace Iw4Radiant.Editing;

internal static class MistFxPreset
{
    // Preserve the stock graph and its dependencies; the accepted mist changes only the spawn interval.
    internal static void Ensure(string library)
    {
        string destination = Path.Combine(library, "fx", MistPainting.AssetName + ".json");
        var exchange = new FxExchange();
        if (File.Exists(destination))
        {
            exchange.LinkJson(File.ReadAllText(destination), MistPainting.AssetName);
            return;
        }

        const string sourceName = "smoke/room_smoke_200";
        string source = Path.Combine(library, "fx", sourceName + ".json");
        if (!File.Exists(source))
            throw new InvalidDataException("This FX library needs smoke/room_smoke_200 to create the mist preset.");
        string json = File.ReadAllText(source);
        var effect = exchange.LinkJson(json, sourceName);
        if (effect.ElemDefCountLooping != 1 || effect.ElemDefs.Count != 1)
            throw new InvalidDataException("The mist preset requires the stock single-layer room_smoke_200 effect.");
        JsonObject root = JsonNode.Parse(json) as JsonObject ??
            throw new InvalidDataException("The mist source must be an FX graph.");
        if (root["elemDefs"] is not JsonArray elements || elements[0] is not JsonObject element ||
            element["spawn"] is not JsonObject spawn)
            throw new InvalidDataException("The mist source has no looping spawn definition.");
        root["name"] = MistPainting.AssetName;
        spawn["loopingIntervalMsec"] = 125;
        string preset = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        exchange.LinkJson(preset, MistPainting.AssetName);
        string directory = Path.GetDirectoryName(destination) ??
            throw new ArgumentException("Choose a raw asset library directory.", nameof(library));
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".mist-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, preset);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
