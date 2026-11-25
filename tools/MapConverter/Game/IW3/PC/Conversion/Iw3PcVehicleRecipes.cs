using System.Text;
using IW4.Formats.D3dbsp;
using IW4.Linker.D3dbsp;
using MapConverter.Game.IW3.PC.Extraction;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PC.Conversion;

internal sealed record Iw3PcVehicleRecipes(
    string Script,
    string[] ModelNames,
    string[] EffectNames,
    string[] SoundNames,
    IReadOnlySet<string> SourceTypes)
{
    internal static Iw3PcVehicleRecipes None { get; } = new("", [], [], [], new HashSet<string>());

    internal static async Task<Iw3PcVehicleRecipes> ReadAsync(string mapFF, string bspPath,
        string mapName, Iw3PcSourceLibrary? sourceLibrary, CancellationToken token)
    {
        byte[] entities = D3dbspFile.Read(bspPath).GetRequiredData(D3dbspLumpType.Entities).ToArray();
        string[] sourceTypes = D3dbspAssetLinker.ReadEntities(entities)
            .Select(DestructibleVehicleRules.SourceType)
            .Where(type => type is not null)
            .Select(type => type!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (sourceTypes.Length == 0)
            return None;
        if (sourceLibrary is null)
            throw new NotSupportedException("Source-authored destructible vehicles require --source-library.");

        const string recipeName = "maps/mp/_destructible_types.gsc";
        byte[] recipeBytes = await sourceLibrary.ReadRawFileAsync(mapFF, recipeName, token).ConfigureAwait(false);
        string typePrefix = $"iw3/{mapName}/";
        string effectAndSoundPrefix = mapName + "/";
        var (script, models, effects, sounds) = DestructibleVehicleRules.CompileWithPrefixes(
            Encoding.Latin1.GetString(recipeBytes), typePrefix, "", effectAndSoundPrefix,
            effectAndSoundPrefix, sourceTypes);
        await sourceLibrary.IncludeMapVehicleAssetsAsync(mapFF, models, effects, token).ConfigureAwait(false);
        return new Iw3PcVehicleRecipes(script, models, effects, sounds,
            sourceTypes.ToHashSet(StringComparer.Ordinal));
    }

    internal string RewriteEntityProperty(IReadOnlyDictionary<string, string> entity, string key, string value,
        string mapName)
    {
        string? sourceType = DestructibleVehicleRules.SourceType(entity);
        if (sourceType is null || !SourceTypes.Contains(sourceType))
            return value;
        return DestructibleVehicleRules.EntityPropertyValue($"iw3/{mapName}/", entity, key, value);
    }
}
