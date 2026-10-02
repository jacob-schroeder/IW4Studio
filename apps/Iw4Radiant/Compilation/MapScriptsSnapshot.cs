using System.Text;
using IW4.Formats.SourceFormat.Character;
using IW4.Formats.SourceFormat.Gsc;
using IW4.Formats.SourceFormat.Material;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal sealed record MapScriptSource(string Role, string Name, string? Source,
    string? Origin = null, string? Error = null);

internal readonly record struct MapScriptEntitySpan(string ScriptName, int Start, int Length,
    int EntityIndex, bool IsPrimary = true);

internal sealed record MapScriptsSnapshot(string MapName, IReadOnlyList<MapScriptSource> Generated,
    IReadOnlyList<MapScriptSource> AnimationHelpers, string? Warning, IReadOnlyList<MapScriptEntitySpan> EntitySpans)
{
    internal static MapScriptsSnapshot Create(MapDocument document, string sourcePath,
        string? assetLibrary, string? bootstrapDirectory)
    {
        ArgumentNullException.ThrowIfNull(document);
        sourcePath = Path.GetFullPath(sourcePath);
        string mapName = Path.GetFileNameWithoutExtension(sourcePath);
        if (mapName.Length == 0 || mapName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidDataException("A map with generated scripts needs a filename containing only letters, numbers, and underscores.");

        var entitySpans = new List<MapScriptEntitySpan>();
        MapEmitterScripts? emitters = MapEmitterScriptAuthoring.Create(document, sourcePath, mapName, entitySpans);
        MapMovingLightScripts? movingLights = MapMovingLightScripts.Create(document, sourcePath, mapName, entitySpans);
        MapFogScripts? fog = MapFogScripts.Create(document, mapName);
        MapDocument expanded = PrefabLibrary.ExpandForCompilation(document, sourcePath);
        DestructiblePreset[] destructibles = expanded.Entities.Select(entity =>
        {
            DestructiblePresets.Validate(entity.Properties);
            return DestructiblePresets.Find(entity.Properties);
        }).OfType<DestructiblePreset>().Distinct().ToArray();
        string[] precacheScripts = destructibles.Select(preset => preset.PrecacheRawFileName)
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<MapScriptSource> helpers = [];
        if (precacheScripts.Length != 0)
        {
            foreach (string name in precacheScripts.Append("animtrees/destructibles.atr"))
                helpers.Add(ReadHelper(name, assetLibrary, bootstrapDirectory));
        }

        bool possibleWater = WaterMaterialAuthoring.ReadDefinitions(expanded.World.Properties).Count != 0 ||
            expanded.Brushes.SelectMany(brush => brush.Faces)
                .Any(face => WaterMaterialAuthoring.IsAuthoredMaterialName(face.Material));
        string mainName = $"maps/mp/{mapName}.gsc";
        MapFactionSettings factions = MapFactionAuthoring.Read(expanded.World.Properties);
        List<MapScriptSource> generated =
        [
            new("Main", mainName, MapStartupScript.Create(mainName, emitters is not null,
                movingLights is not null, fog is not null, factions, precacheScripts))
        ];
        if (emitters is not null)
        {
            generated.Add(new("FX setup", emitters.MapFxName, emitters.MapFxSource));
            generated.Add(new("FX / sound placements", emitters.CreateFxName, emitters.CreateFxSource));
        }
        if (movingLights is not null)
            generated.Add(new("Moving lights", movingLights.Name, movingLights.Source));
        if (fog is not null)
        {
            generated.Add(new("Global fog", fog.Name, fog.Source));
            entitySpans.Add(new(fog.Name, 0, fog.Source.Length, document.Entities.IndexOf(document.World)));
        }

        string? warning = null;
        if (possibleWater)
        {
            generated.Add(new("Water", $"maps/mp/{mapName}_water.gsc", null,
                Error: "Water script source requires compiled BSP collision data."));
            warning = "Main source is incomplete for water until compiled BSP collision data is available.";
        }
        return new MapScriptsSnapshot(mapName, generated, helpers, warning, entitySpans);
    }

    private static MapScriptSource ReadHelper(string name, string? assetLibrary, string? bootstrapDirectory)
    {
        string role = Path.GetFileName(name);
        string? source = null;
        if (!string.IsNullOrWhiteSpace(assetLibrary))
        {
            string candidate = Path.Combine(assetLibrary, name);
            if (File.Exists(candidate)) source = candidate;
        }
        if (source is null && !string.IsNullOrWhiteSpace(bootstrapDirectory))
        {
            string candidate = Path.Combine(bootstrapDirectory, name);
            if (File.Exists(candidate)) source = candidate;
        }
        if (source is null)
            return new MapScriptSource(role, name, null,
                Error: $"RawFile '{name}' is missing from the asset library and bootstrap.");
        source = Path.GetFullPath(source);
        try
        {
            return new MapScriptSource(role, name,
                File.ReadAllText(source, new UTF8Encoding(false, true)), source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return new MapScriptSource(role, name, null, source,
                $"Could not read RawFile '{name}': {exception.Message}");
        }
    }
}
