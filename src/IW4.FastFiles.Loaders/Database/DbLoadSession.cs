using IW4.FastFiles.Loaders.IO;
using IW4.FastFiles.Loaders.Streaming.Images;
using IW4.FastFiles.Streaming.Database.Streaming;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;
using IW4.Runtime.Diagnostics;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Application-facing owner for an ordered set of DB_LoadXZone calls. Each
/// XZone keeps independent XZoneMemory while DbRuntime supplies the shared
/// global XAsset identity pool used by dependency references.
/// </summary>
public sealed class DbLoadSession
{
    private readonly List<LoadedXZone> _zones = [];
    private readonly IReadOnlyList<LoadedXZone> _zoneView;
    private readonly Action<XAssetLoadProgress>? _assetProgress;
    private readonly uint _selectedLanguageMask;
    private readonly DbZoneLoader _loader;
    private readonly SysFileSystem _fileSystem = new();

    public DbLoadSession(
        Action<XAssetLoadProgress>? assetProgress = null,
        DbRuntime? runtime = null,
        uint selectedLanguageMask = 0)
    {
        _assetProgress = assetProgress;
        _selectedLanguageMask = selectedLanguageMask;
        Runtime = runtime ?? new DbRuntime();
        _loader = new DbZoneLoader(Runtime);
        _zoneView = _zones.AsReadOnly();
    }

    public DbRuntime Runtime { get; }

    public XAssetPool AssetPool => Runtime.AssetPool;

    /// <summary>
    /// Append-only successful load history. Use <see cref="ActiveZones"/> for
    /// the current registry after free-flag batches.
    /// </summary>
    public IReadOnlyList<LoadedXZone> Zones => _zoneView;

    public IReadOnlyList<LoadedXZone> LoadHistory => _zoneView;

    public IReadOnlyList<DbLoadedXZone> ActiveZones => Runtime.Zones;

    public LoadedXZone DB_LoadXZone(
        byte[] buffer,
        int length,
        XZoneInfo zoneInfo)
    {
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
        result = result with
        {
            ImagePayloadResolver = new GfxImageStreamPayloadResolver(
                result.Header,
                path)
        };
        return Register(result);
    }

    public LoadedXZone DB_LoadXZone(string path, XZoneInfo zoneInfo)
    {
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
        result = result with
        {
            ImagePayloadResolver = new GfxImageStreamPayloadResolver(
                result.Header,
                path)
        };
        return Register(result);
    }

    private DbLoadContext CreateContext(string sourceName)
    {
        DbLoadContext context = Runtime.CreateLoadContext();
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
        _zones.Add(loaded);
        return loaded;
    }

    internal int LoadHistoryCount => _zones.Count;

    internal void RollbackLoadHistory(int count)
    {
        if ((uint)count > (uint)_zones.Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == _zones.Count)
            return;

        _zones.RemoveRange(count, _zones.Count - count);
    }
}
