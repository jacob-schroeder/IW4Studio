using IW4.Assets.Assets;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Loaders.IO;
using IW4.FastFiles.Loaders.Streaming.Images;
using IW4.FastFiles.Streaming.Images;
using IW4.FastFiles.Streaming.Database.Streaming;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Runtime.Diagnostics;
using IW4.Runtime.IO;
using IW4.Linker.Contracts;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Application-facing owner for an ordered set of DB_LoadXZone calls. Each
/// XZone keeps independent XZoneMemory while DbRuntime supplies the shared
/// global XAsset identity pool used by dependency references.
/// </summary>
public sealed class DbLoadSession : IDisposable
{
    private const XZoneFlags AllZoneFlags =
        XZoneFlags.DB_ZONE_COMMON |
        XZoneFlags.DB_ZONE_UI |
        XZoneFlags.DB_ZONE_GAME |
        XZoneFlags.DB_ZONE_LOAD |
        XZoneFlags.DB_ZONE_DEV;

    private readonly List<LoadedXZone> _zones = [];
    private readonly List<GfxImageStreamResolver> _imageStreamResolvers = [];
    private readonly IReadOnlyList<LoadedXZone> _zoneView;
    private readonly Action<XAssetLoadProgress>? _assetProgress;
    private uint _selectedLanguageMask;
    private readonly DbZoneLoader _loader;
    private readonly SysFileSystem _fileSystem = new();
    private readonly DbRuntime _runtime;
    private readonly bool _ownsRuntime;
    private int _disposeState;

    public DbLoadSession(
        Action<XAssetLoadProgress>? assetProgress = null,
        DbRuntime? runtime = null,
        uint selectedLanguageMask = 0)
    {
        if (selectedLanguageMask != 0 &&
            !DbLanguageMask.IsSingleLanguage(selectedLanguageMask))
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedLanguageMask),
                "A selected language must be zero for automatic selection or contain exactly one supported PS3 IW4 language bit.");
        }

        _assetProgress = assetProgress;
        _selectedLanguageMask = selectedLanguageMask;
        _ownsRuntime = runtime is null;
        _runtime = runtime ?? new DbRuntime();
        _loader = new DbZoneLoader(_runtime);
        _zoneView = _zones.AsReadOnly();
    }

    public DbRuntime Runtime
    {
        get
        {
            ThrowIfDisposed();
            return _runtime;
        }
    }

    public XAssetPool AssetPool => Runtime.AssetPool;

    /// <summary>
    /// Append-only successful load history. Use <see cref="ActiveZones"/> for
    /// the current registry after free-flag batches.
    /// </summary>
    public IReadOnlyList<LoadedXZone> Zones
    {
        get
        {
            ThrowIfDisposed();
            return _zoneView;
        }
    }

    public IReadOnlyList<LoadedXZone> LoadHistory => Zones;

    public IReadOnlyList<DbLoadedXZone> ActiveZones => Runtime.Zones;

    /// <summary>
    /// Atomically freezes the current ordered runtime provider pool into
    /// Linker-owned immutable plans. Loader captures are consumed during
    /// construction and are not retained by the resulting pool.
    /// </summary>
    public LinkAssetPool FreezeLinkAssetPool()
    {
        ThrowIfDisposed();
        long revision = AssetPool.Revision;
        Dictionary<DbZoneHandle, LoadedXZone> loadedByOwner = _zones
            .Where(zone => !zone.Context.ZoneOwner.IsNone)
            .ToDictionary(zone => zone.Context.ZoneOwner);
        XAssetProviderContribution[] providers = AssetPool.Slots
            .SelectMany(slot => slot.Providers)
            .OrderBy(provider => provider.RegistrationSequence)
            .ToArray();
        if (AssetPool.Revision != revision)
        {
            throw new InvalidOperationException(
                "The runtime XAssetPool changed while its provider order was being captured.");
        }

        var sources = new LinkAssetProviderSource[providers.Length];
        for (int index = 0; index < providers.Length; index++)
        {
            XAssetProviderContribution provider = providers[index];
            if (provider.Owner.IsNone)
            {
                throw new NotSupportedException(
                    $"Runtime provider {provider.Id} has no zone capture. " +
                    "Pass authored BaseAsset definitions directly as LinkAssetProviderSource values.");
            }
            if (!loadedByOwner.TryGetValue(provider.Owner, out LoadedXZone? zone))
            {
                throw new InvalidOperationException(
                    $"Runtime provider {provider.Id} belongs to zone {provider.Owner}, " +
                    "but this load session has no matching captured LoadedXZone.");
            }

            IReadOnlyList<ImageFileStreamLanguageReferences> imageStreamReferences =
                FreezeImageStreamReferences(zone, provider.Asset);
            sources[index] = new LinkAssetProviderSource(
                provider.Asset,
                zone.LinkAssetImportResolver,
                imageStreamReferences);
        }

        LinkAssetPool result = new(sources);
        if (AssetPool.Revision != revision)
        {
            throw new InvalidOperationException(
                "The runtime XAssetPool changed while its providers were being frozen.");
        }

        return result;
    }

    /// <summary>Freezes providers owned by one currently loaded zone.</summary>
    public LinkAssetPool FreezeLinkAssetPool(LoadedXZone targetZone)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(targetZone);
        if (!_zones.Any(zone => ReferenceEquals(zone, targetZone)))
        {
            throw new ArgumentException(
                "The target zone was not loaded by this session.",
                nameof(targetZone));
        }

        long revision = AssetPool.Revision;
        LinkAssetPool result = FreezeProviders(
            AssetPool.Slots.SelectMany(slot => slot.Providers)
                .Where(provider => provider.Owner == targetZone.Context.ZoneOwner),
            targetZone);

        if (AssetPool.Revision != revision)
        {
            throw new InvalidOperationException(
                "The runtime XAssetPool changed while target-prioritized providers were being frozen.");
        }
        return result;
    }

    /// <summary>Freezes the effective fallback providers without one target owner.</summary>
    internal LinkAssetPool FreezeLinkAssetPoolExcluding(LoadedXZone excludedZone)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(excludedZone);
        if (!_zones.Any(zone => ReferenceEquals(zone, excludedZone)))
        {
            throw new ArgumentException(
                "The excluded zone was not loaded by this session.",
                nameof(excludedZone));
        }

        long revision = AssetPool.Revision;
        Dictionary<DbZoneHandle, LoadedXZone> loadedByOwner = _zones
            .Where(zone => !zone.Context.ZoneOwner.IsNone)
            .ToDictionary(zone => zone.Context.ZoneOwner);
        XAssetProviderContribution[] providers = AssetPool.Slots
            .Select(slot =>
            {
                XAssetProviderContribution[] remaining = slot.Providers
                    .Where(provider =>
                        provider.Owner != excludedZone.Context.ZoneOwner)
                    .ToArray();
                return remaining.FirstOrDefault(provider =>
                        !provider.IsReferencePlaceholder)
                    ?? remaining.FirstOrDefault();
            })
            .Where(provider => provider is not null)
            .Cast<XAssetProviderContribution>()
            .OrderBy(provider => provider.RegistrationSequence)
            .ToArray();
        var sources = new List<LinkAssetProviderSource>(providers.Length);
        foreach (XAssetProviderContribution provider in providers)
        {
            if (!loadedByOwner.TryGetValue(provider.Owner, out LoadedXZone? zone))
                throw new InvalidOperationException($"Fallback runtime provider {provider.Id} has no matching load-session zone.");
            sources.Add(CreateProviderSource(zone, provider));
        }
        if (AssetPool.Revision != revision)
            throw new InvalidOperationException("The runtime XAssetPool changed while fallback providers were being frozen.");
        return new LinkAssetPool(sources);
    }

    public LoadedXZone DB_LoadXZone(
        byte[] buffer,
        int length,
        XZoneInfo zoneInfo)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneInfo.Name);
        DbLoadContext context = CreateContext(zoneInfo.Name);
        LoadedXZone result = _loader.DB_LoadXZone(
            buffer,
            length,
            zoneInfo,
            context,
            Runtime);
        result = BindLinkImageStreams(result);
        return Register(result);
    }

    public LoadedXZone DB_LoadXZone(
        byte[] buffer,
        int length,
        string sourceName = "<memory>",
        XZoneFlags flags = XZoneFlags.None,
        uint unknown48 = 0)
    {
        ThrowIfDisposed();
        DbLoadContext context = CreateContext(sourceName);
        LoadedXZone result = _loader.DB_LoadXZone(
            buffer,
            length,
            context,
            sourceName,
            flags,
            unknown48,
            Runtime);
        result = BindLinkImageStreams(result);
        return Register(result);
    }

    public LoadedXZone DB_LoadXZone(
        string path,
        XZoneFlags flags = XZoneFlags.None,
        uint unknown48 = 0)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string sourceName = Path.GetFileName(path);
        DbLoadContext context = CreateContext(sourceName);
        using SysFile sysFile = _fileSystem.Sys_OpenFile(path);
        var file = new DbFile(sysFile, Path.GetFileNameWithoutExtension(path));
        LoadedXZone result = _loader.DB_LoadXZone(
            file,
            context,
            flags,
            unknown48,
            Runtime);
        result = BindImageStreams(result, path);
        return Register(result);
    }

    public LoadedXZone DB_LoadXZone(string path, XZoneInfo zoneInfo)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneInfo.Name);

        string sourceName = Path.GetFileName(path);
        DbLoadContext context = CreateContext(sourceName);
        using SysFile sysFile = _fileSystem.Sys_OpenFile(path);
        // The physical target may be an edited override for a logical engine
        // slot. XZone/DBFile identity follows XZoneInfo.Name; diagnostics
        // retain the physical source path separately.
        var file = new DbFile(
            sysFile,
            Path.GetFileNameWithoutExtension(zoneInfo.Name));
        LoadedXZone result = _loader.DB_LoadXZone(file, zoneInfo, context, Runtime);
        result = BindImageStreams(result, path);
        return Register(result);
    }

    private LoadedXZone BindImageStreams(LoadedXZone loaded, string path)
    {
        var streams = new GfxImageStreamResolver(loaded.Header, path);
        try
        {
            LoadedXZone result = loaded with
            {
                ImagePayloadResolver = new GfxImageStreamPayloadResolver(streams),
                LinkImageStreams = new LinkGfxImageStreamSource(loaded.Header)
            };
            _imageStreamResolvers.Add(streams);
            return result;
        }
        catch
        {
            streams.Dispose();
            throw;
        }
    }

    private static LoadedXZone BindLinkImageStreams(LoadedXZone loaded) =>
        loaded with
        {
            LinkImageStreams = new LinkGfxImageStreamSource(loaded.Header)
        };

    private static IReadOnlyList<ImageFileStreamLanguageReferences>
        FreezeImageStreamReferences(LoadedXZone zone, BaseAsset asset)
    {
        if (asset is not GfxImageAsset image ||
            !image.StreamData.Any(entry => entry.HasStreamingData))
        {
            return Array.Empty<ImageFileStreamLanguageReferences>();
        }

        LinkGfxImageStreamSource source = zone.LinkImageStreams ??
            throw new InvalidOperationException(
                $"Streamed GfxImage '{image.Name}' has no source DB-header imagefile binding.");
        return source.Freeze(image);
    }

    private DbLoadContext CreateContext(string sourceName)
    {
        DbLoadContext context = _runtime.CreateLoadContext();
        context.SelectedLanguageMask = _selectedLanguageMask;
        context.CurrentFastFile = new StreamFileRef(
            0,
            sourceName,
            StreamFileKind.CurrentFastFile);
        context.AssetProgress = _assetProgress;
        return context;
    }

    private LoadedXZone Register(LoadedXZone loaded)
    {
        if (_selectedLanguageMask == 0)
            _selectedLanguageMask = loaded.Context.SelectedLanguageMask;

        _zones.Add(loaded);
        return loaded;
    }

    internal int LoadHistoryCount
    {
        get
        {
            ThrowIfDisposed();
            return _zones.Count;
        }
    }

    internal void RollbackLoadHistory(int count)
    {
        ThrowIfDisposed();
        if ((uint)count > (uint)_zones.Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == _zones.Count)
            return;

        _zones.RemoveRange(count, _zones.Count - count);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            if (_ownsRuntime)
                _runtime.DB_FreeXZones(AllZoneFlags);
        }
        finally
        {
            foreach (GfxImageStreamResolver resolver in _imageStreamResolvers)
                resolver.Dispose();
            _imageStreamResolvers.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
            throw new ObjectDisposedException(nameof(DbLoadSession));
    }

    private LinkAssetPool FreezeProviders(
        IEnumerable<XAssetProviderContribution> providers,
        LoadedXZone zone) => new(providers
        .OrderBy(provider => provider.RegistrationSequence)
        .Select(provider => CreateProviderSource(zone, provider)));

    private static LinkAssetProviderSource CreateProviderSource(
        LoadedXZone zone,
        XAssetProviderContribution provider) => new(
        provider.Asset,
        zone.LinkAssetImportResolver,
        FreezeImageStreamReferences(zone, provider.Asset));
}
