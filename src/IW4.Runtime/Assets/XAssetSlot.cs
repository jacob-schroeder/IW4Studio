using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Runtime.Assets;

/// <summary>
/// Stable canonical identity for one (type, normalized name) pair. Provider
/// definitions may come and go, but the slot address does not change while at
/// least one provider remains.
/// </summary>
public sealed class XAssetSlot
{
    private readonly List<XAssetProviderContribution> _providers;
    private readonly IReadOnlyList<XAssetProviderContribution> _providerView;
    private XAssetCanonicalProjection _projection;

    internal XAssetSlot(
        XAssetPoolAddress address,
        XAssetType assetType,
        string name,
        IEnumerable<XAssetProviderContribution> providers,
        XAssetCanonicalProjection? projection = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Address = address;
        AssetType = assetType;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _providers = providers.ToList();
        if (_providers.Count == 0)
            throw new ArgumentException("An XAsset slot requires at least one provider.", nameof(providers));

        _providerView = _providers.AsReadOnly();
        _projection = projection ?? XAssetCanonicalProjection.FromProvider(ActiveProvider);
    }

    public XAssetPoolAddress Address { get; }

    public XAssetType AssetType { get; }

    public string Name { get; }

    public IReadOnlyList<XAssetProviderContribution> Providers => _providerView;

    public XAssetProviderContribution ActiveProvider =>
        SelectActiveProvider()
        ?? throw new InvalidOperationException($"XAsset slot {Address} has no providers.");

    public BaseAsset CanonicalAsset => _projection.Asset;

    internal void AddProvider(XAssetProviderContribution provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.AssetType != AssetType ||
            !string.Equals(
                XAssetStableIdentity.NormalizeLookupName(provider.Name),
                XAssetStableIdentity.NormalizeLookupName(Name),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Provider {provider.Id} identity {provider.AssetType} '{provider.Name}' does not match slot {AssetType} '{Name}'.");
        }

        XAssetProviderId previousActive = ActiveProvider.Id;
        _providers.Add(provider);
        if (ActiveProvider.Id != previousActive)
            _projection = XAssetCanonicalProjection.FromProvider(ActiveProvider);
    }

    internal IReadOnlyList<XAssetProviderContribution> RemoveProviders(DbZoneHandle owner)
    {
        if (owner.IsNone)
            throw new ArgumentOutOfRangeException(nameof(owner));

        XAssetProviderContribution[] removed = _providers
            .Where(provider => provider.Owner == owner)
            .ToArray();
        if (removed.Length == 0)
            return [];

        _providers.RemoveAll(provider => provider.Owner == owner);
        return Array.AsReadOnly(removed);
    }

    internal XAssetPoolEntry ToActiveEntry()
    {
        XAssetProviderContribution provider = ActiveProvider;
        return new XAssetPoolEntry(
            Address,
            AssetType,
            Name,
            _projection.Asset,
            provider.StagingAddress,
            _projection.HeaderBytes,
            provider.IsReferencePlaceholder,
            _projection.SourceBlocks)
        {
            NativePoolCopyBytes = _projection.NativePoolCopyBytes,
            NativePoolCopyCapturedLength = _projection.NativePoolCopyCapturedLength
        };
    }

    internal XAssetSlot Clone() =>
        new(Address, AssetType, Name, _providers, _projection.Clone());

    internal void CopyActiveProviderToProjection()
    {
        _projection = XAssetCanonicalProjection.FromProvider(ActiveProvider);
    }

    internal void KeepImageDestinationWithSourceName()
    {
        _projection = _projection.KeepImageDestinationWithSourceName(
            ActiveProvider,
            Address);
    }

    private XAssetProviderContribution? SelectActiveProvider()
    {
        return _providers.FirstOrDefault(provider => !provider.IsReferencePlaceholder)
            ?? _providers.FirstOrDefault();
    }
}
