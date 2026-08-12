using IW4.FastFiles.Strings;
using IW4.Runtime.Assets;

namespace IW4.Runtime.Database;

/// <summary>
/// Immutable ownership ledger published only after DB_PostLoadXZone succeeds.
/// It is the managed equivalent needed to retire one zone without discarding
/// providers or script strings still claimed by another resident zone.
/// </summary>
public sealed class DbZoneContributions
{
    public DbZoneContributions(
        DbZoneHandle zone,
        IEnumerable<XAssetProviderId> assetProviders,
        IEnumerable<ScriptStringHandle> scriptStrings)
    {
        if (zone.IsNone)
            throw new ArgumentOutOfRangeException(nameof(zone));
        ArgumentNullException.ThrowIfNull(assetProviders);
        ArgumentNullException.ThrowIfNull(scriptStrings);

        Zone = zone;
        AssetProviders = Array.AsReadOnly(assetProviders.Distinct().ToArray());
        ProviderContributions = Array.Empty<XAssetProviderContribution>();
        ScriptStrings = Array.AsReadOnly(scriptStrings.Distinct().ToArray());
    }

    internal DbZoneContributions(
        DbZoneHandle zone,
        IReadOnlyCollection<XAssetProviderContribution> assetProviders,
        IEnumerable<ScriptStringHandle> scriptStrings)
    {
        if (zone.IsNone)
            throw new ArgumentOutOfRangeException(nameof(zone));
        ArgumentNullException.ThrowIfNull(assetProviders);
        ArgumentNullException.ThrowIfNull(scriptStrings);

        Zone = zone;
        XAssetProviderContribution[] providers = assetProviders.ToArray();
        if (providers.Any(provider => provider is null || provider.Owner != zone) ||
            providers.Select(provider => provider.Id).Distinct().Count() != providers.Length)
        {
            throw new InvalidDataException(
                "An XZone contribution ledger contains a foreign, null, or duplicate asset provider.");
        }
        ProviderContributions = Array.AsReadOnly(providers);
        AssetProviders = Array.AsReadOnly(providers.Select(provider => provider.Id).ToArray());
        ScriptStrings = Array.AsReadOnly(scriptStrings.Distinct().ToArray());
    }

    public DbZoneHandle Zone { get; }

    public IReadOnlyList<XAssetProviderId> AssetProviders { get; }

    internal IReadOnlyList<XAssetProviderContribution> ProviderContributions { get; }

    public IReadOnlyList<ScriptStringHandle> ScriptStrings { get; }
}
