using IW4.Loaders.IO;
using IW4.Loaders.Streaming.Images;
using IW4.Loaders.Streaming.Sound;
using IW4.Streaming.Images;
using IW4.Streaming.Sound;
using IW4.Streaming.Database.Streaming;
using IW4.Game.Database;
using IW4.Game.Database.Streaming;
using IW4.Game.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Runtime.Diagnostics;
using IW4.Runtime.IO;

namespace IW4.Loaders.Database;

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
    private readonly List<StreamedSoundResolver> _soundStreamResolvers = [];
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
        using SysFileHandle sysFile = _fileSystem.Sys_OpenFile(path);
        var file = new DbFile(sysFile.File, Path.GetFileNameWithoutExtension(path));
        LoadedXZone result = _loader.DB_LoadXZone(
            file,
            sysFile,
            context,
            flags,
            unknown48,
            Runtime);
        result = BindPayloadResolvers(result, path);
        return Register(result);
    }

    public LoadedXZone DB_LoadXZone(string path, XZoneInfo zoneInfo)
        => DB_LoadXZone(path, zoneInfo, captureZoneObject: true);

    internal LoadedXZone DB_LoadXZone(
        string path,
        XZoneInfo zoneInfo,
        bool captureZoneObject)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneInfo.Name);

        string sourceName = Path.GetFileName(path);
        DbLoadContext context = CreateContext(sourceName, captureZoneObject);
        using SysFileHandle sysFile = _fileSystem.Sys_OpenFile(path);
        // The physical target may be an edited override for a logical engine
        // slot. XZone/DBFile identity follows XZoneInfo.Name; diagnostics
        // retain the physical source path separately.
        var file = new DbFile(
            sysFile.File,
            Path.GetFileNameWithoutExtension(zoneInfo.Name));
        LoadedXZone result = _loader.DB_LoadXZone(file, sysFile, zoneInfo, context, Runtime);
        result = BindPayloadResolvers(result, path);
        return Register(result);
    }

    private LoadedXZone BindPayloadResolvers(LoadedXZone loaded, string path)
    {
        var imageStreams = new GfxImageStreamResolver(loaded.Header, path);
        var soundStreams = new StreamedSoundResolver(path);
        try
        {
            LoadedXZone result = loaded with
            {
                ImagePayloadResolver = new GfxImageStreamPayloadResolver(imageStreams),
                SoundPayloadResolver = new StreamedSoundPayloadResolver(soundStreams)
            };
            _imageStreamResolvers.Add(imageStreams);
            _soundStreamResolvers.Add(soundStreams);
            return result;
        }
        catch
        {
            soundStreams.Dispose();
            imageStreams.Dispose();
            throw;
        }
    }

    private DbLoadContext CreateContext(
        string sourceName,
        bool captureZoneObject = true)
    {
        DbLoadContext context = _runtime.CreateLoadContext();
        context.CaptureZoneObject = captureZoneObject;
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
            foreach (StreamedSoundResolver resolver in _soundStreamResolvers)
                resolver.Dispose();
            _soundStreamResolvers.Clear();
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
}
