using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Loaders.Database;
using IW4.Linker.Capture;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

namespace IW4.Studio.Documents;

public static partial class LoadedZoneLinkExtensions
{
    /// <summary>Freezes all currently registered providers in load order.</summary>
    public static LinkAssetPool FreezeLinkAssetPool(this DbLoadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        long revision = session.AssetPool.Revision;
        Dictionary<DbZoneHandle, LoadedXZone> loadedByOwner = session.LoadHistory
            .Where(zone => !zone.Context.ZoneOwner.IsNone)
            .ToDictionary(zone => zone.Context.ZoneOwner);
        XAssetProviderContribution[] providers = session.AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .OrderBy(provider => provider.RegistrationSequence)
            .ToArray();
        if (session.AssetPool.Revision != revision)
            throw new InvalidOperationException("The runtime XAssetPool changed while its provider order was being captured.");

        var sources = new LinkAssetProviderSource[providers.Length];
        for (int index = 0; index < providers.Length; index++)
        {
            XAssetProviderContribution provider = providers[index];
            if (provider.Owner.IsNone)
                throw new NotSupportedException($"Runtime provider {provider.Id} has no zone capture. Pass authored BaseAsset definitions directly as LinkAssetProviderSource values.");
            if (!loadedByOwner.TryGetValue(provider.Owner, out LoadedXZone? zone))
                throw new InvalidOperationException($"Runtime provider {provider.Id} belongs to zone {provider.Owner}, but this load session has no matching captured LoadedXZone.");
            sources[index] = CreateProviderSource(zone, provider);
        }

        LinkAssetPool result = new(sources);
        if (session.AssetPool.Revision != revision)
            throw new InvalidOperationException("The runtime XAssetPool changed while its providers were being frozen.");
        return result;
    }

    /// <summary>Freezes providers owned by one currently loaded zone.</summary>
    public static LinkAssetPool FreezeLinkAssetPool(this DbLoadSession session, LoadedXZone targetZone)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(targetZone);
        if (!session.LoadHistory.Any(zone => ReferenceEquals(zone, targetZone)))
            throw new ArgumentException("The target zone was not loaded by this session.", nameof(targetZone));

        long revision = session.AssetPool.Revision;
        DbLoadedXZone registeredTarget = session.Runtime.Zones.SingleOrDefault(zone =>
                zone.Handle == targetZone.Context.ZoneOwner &&
                ReferenceEquals(zone.Context, targetZone.Context))
            ?? throw new InvalidOperationException($"Target zone '{targetZone.SourceName}' is not active in the runtime registry.");
        LinkAssetPool result = new(
            session.AssetPool.Slots
                .SelectMany(slot => slot.Providers)
                .Where(provider => provider.Owner == registeredTarget.Handle)
                .OrderBy(provider => provider.RegistrationSequence)
                .Select(provider => CreateProviderSource(targetZone, provider)),
            targetZone.Context.Diagnostics.Warn);

        if (session.AssetPool.Revision != revision)
            throw new InvalidOperationException("The runtime XAssetPool changed while target-prioritized providers were being frozen.");
        return result;
    }

    private static LinkAssetProviderSource CreateProviderSource(LoadedXZone zone, XAssetProviderContribution provider)
    {
        string serializedName = DbLoadExecutionContext.NormalizeLoadedAssetName(
            provider.IsReferencePlaceholder ? "," + provider.Name : provider.Name);
        bool nameWasNormalized = !string.Equals(serializedName,
            provider.Asset.SerializedAssetName, StringComparison.Ordinal);
        ZoneObjectImport import = ZoneObjectObservationTranslator.Translate(zone.Observation ??
            throw new InvalidOperationException($"Zone '{zone.SourceName}' was loaded without canonical linker capture."));
        return new LinkAssetProviderSource(
            provider.Asset,
            import.Resolver,
            FreezeImageStreamReferences(zone, provider.Asset),
            nameWasNormalized
                ? LinkAssetProviderSourceDisposition.AuthoredDetached
                : LinkAssetProviderSourceDisposition.PreserveImportedIdentity,
            serializedName: serializedName);
    }

    private static IReadOnlyList<ImageFileStreamLanguageReferences> FreezeImageStreamReferences(
        LoadedXZone zone, BaseAsset asset)
    {
        if (asset is not GfxImageAsset image ||
            !image.StreamData.Any(entry => entry.HasStreamingData))
            return Array.Empty<ImageFileStreamLanguageReferences>();
        return new LinkGfxImageStreamSource(zone.Header).Freeze(image);
    }
}
