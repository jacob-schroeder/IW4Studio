using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IW4.Game.Assets.Sound;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal sealed record MapSoundVariant(string Name, string SourceName, SoundEmitterSettings Settings);

internal static class MapSoundVariantAuthoring
{
    internal static string WriteTo(string directory, string assetLibrary, MapSoundVariant variant)
    {
        string[] parts = variant.SourceName.Split('/');
        if (parts.Any(part => part is "" or "." or ".."))
            throw new InvalidDataException($"Sound alias '{variant.SourceName}' has an invalid source path.");
        string sourcePath = Path.Combine(assetLibrary, "soundaliases", variant.SourceName + ".json");
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Sound alias '{variant.SourceName}' is missing from the asset library.", sourcePath);
        if (File.Exists(Path.Combine(assetLibrary, "soundaliases", variant.Name + ".json")))
            throw new InvalidDataException($"Generated sound alias '{variant.Name}' conflicts with an asset library alias.");

        JsonObject document = JsonNode.Parse(File.ReadAllText(sourcePath)) as JsonObject ??
            throw new InvalidDataException($"Sound alias '{variant.SourceName}' has no JSON object.");
        if (document["name"]?.GetValue<string>() != variant.SourceName)
            throw new InvalidDataException($"Sound source name does not match '{variant.SourceName}'.");
        JsonArray aliases = document["aliases"] as JsonArray ??
            throw new InvalidDataException($"Sound alias '{variant.SourceName}' has no alias rows.");
        document["name"] = variant.Name;
        foreach (JsonNode? node in aliases)
        {
            JsonObject row = node as JsonObject ??
                throw new InvalidDataException($"Sound alias '{variant.SourceName}' contains a non-object row.");
            row["aliasName"] = variant.Name;
            if (variant.Settings.Volume is float volume)
            {
                row["volumeMin"] = volume;
                row["volumeMax"] = volume;
            }
            if (variant.Settings.Pitch is float pitch)
            {
                row["pitchMin"] = pitch;
                row["pitchMax"] = pitch;
            }
            if (variant.Settings.DistanceMin is float min)
                row["distanceMin"] = min;
            if (variant.Settings.DistanceMax is float max)
                row["distanceMax"] = max;
            if (variant.Settings.DistanceMin.HasValue || variant.Settings.DistanceMax.HasValue)
            {
                float effectiveMin = row["distanceMin"]?.GetValue<float>() ??
                    throw new InvalidDataException($"Sound alias '{variant.SourceName}' has no inner range.");
                float effectiveMax = row["distanceMax"]?.GetValue<float>() ??
                    throw new InvalidDataException($"Sound alias '{variant.SourceName}' has no outer range.");
                if (!float.IsFinite(effectiveMin) || !float.IsFinite(effectiveMax) ||
                    effectiveMin < 0 || effectiveMax <= effectiveMin)
                    throw new InvalidDataException($"Sound alias '{variant.SourceName}' needs an outer range greater than its inner range.");
            }
            int rawFlags = row["flags"]?.GetValue<int>() ??
                throw new InvalidDataException($"Sound alias '{variant.SourceName}' has no flags.");
            SndAliasBits flags = new SndAliasBits(rawFlags).WithLooping(variant.Settings.Looping);
            if (variant.Settings.Channel is byte channel)
                flags = flags.WithEntityChannelIndex(channel);
            row["flags"] = flags.RawValue;
        }

        string path = Path.Combine(directory, "soundaliases", variant.Name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return path;
    }
}
