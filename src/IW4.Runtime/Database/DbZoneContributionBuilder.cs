using IW4.FastFiles.Zone;
using IW4.FastFiles.Strings;
using IW4.Runtime.Assets;

namespace IW4.Runtime.Database;

internal sealed class DbZoneContributionBuilder
{
    private readonly List<XAssetProviderContribution> _assetProviders = [];
    private readonly HashSet<XAssetProviderId> _assetProviderSet = [];
    private readonly List<ScriptStringHandle> _scriptStrings = [];
    private readonly HashSet<ScriptStringHandle> _scriptStringSet = [];
    private bool _frozen;

    public DbZoneContributionBuilder(DbZoneHandle zone)
    {
        if (zone.IsNone)
            throw new ArgumentOutOfRangeException(nameof(zone));

        Zone = zone;
    }

    public DbZoneHandle Zone { get; }

    public void Add(XAssetProviderContribution provider)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.Owner != Zone)
        {
            throw new InvalidDataException(
                $"Provider {provider.Id} is owned by {provider.Owner}, not contribution zone {Zone}.");
        }
        if (_assetProviderSet.Add(provider.Id))
            _assetProviders.Add(provider);
    }

    public void Add(ScriptStringHandle scriptString)
    {
        EnsureMutable();
        if (!scriptString.IsNull && _scriptStringSet.Add(scriptString))
            _scriptStrings.Add(scriptString);
    }

    public DbZoneContributions Freeze()
    {
        EnsureMutable();
        _frozen = true;
        return new DbZoneContributions(Zone, _assetProviders, _scriptStrings);
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("The XZone contribution ledger has already been published.");
    }
}
