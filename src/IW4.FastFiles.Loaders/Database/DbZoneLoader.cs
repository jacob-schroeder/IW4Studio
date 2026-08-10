using IW4.FastFiles.Loaders.IO;
using IW4.FastFiles.Loaders.Assets;
using IW4.FastFiles.Streaming.Database.Streaming;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using IW4.Runtime.Strings;
using IW4.Linker.Model;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Coordinates the DB file-to-zone path while leaving byte interpretation in
/// readers and allocated state in Runtime. This type exposes the single-zone
/// DB_LoadXZone core. The outer, free-before-load DB_LoadXAssets batch
/// lifecycle is implemented by DbLoadPlanExecutor and DbRuntime so one row
/// cannot accidentally apply free flags out of order.
/// </summary>
public sealed class DbZoneLoader
{
    private readonly DbHeaderReader _dbHeaderReader = new();
    private readonly DbPackedStreamReader _packedStreamReader = new();
    private readonly XFileHeaderReader _xfileHeaderReader = new();
    private readonly XAssetListReader _xassetListReader = new();
    private readonly XAssetDispatcher _xassetDispatcher = new();
    private readonly SysFileSystem _fileSystem = new();
    private DbRuntime? _runtime;

    public DbZoneLoader(DbRuntime? runtime = null)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// Global managed DB state used by direct calls through this loader.
    /// DbLoadSession exposes the same state while also retaining each context.
    /// </summary>
    public DbRuntime Runtime => _runtime ??= new DbRuntime();

    /// <summary>
    /// Managed single-zone adapter for an XZoneInfo request.
    /// FreeFlags belongs to the outer DB_LoadXAssets batch and is rejected at
    /// this inner boundary rather than being applied in the wrong order.
    /// </summary>
    public LoadedXZone DB_LoadXZone(
        byte[] buffer,
        int length,
        XZoneInfo zoneInfo,
        DbLoadContext? context = null,
        DbRuntime? runtime = null)
    {
        string requestName = ValidateSupportedRequest(zoneInfo);
        return DB_LoadXZone(
            buffer,
            length,
            context,
            requestName,
            zoneInfo.AllocFlags,
            runtime: runtime);
    }

    public LoadedXZone DB_LoadXZone(
        byte[] buffer,
        int length,
        DbLoadContext? context = null,
        string sourceName = "<memory>",
        XZoneFlags flags = XZoneFlags.None,
        uint unknown48 = 0,
        DbRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)length > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        using var stream = new MemoryStream(buffer, 0, length, writable: false, publiclyVisible: true);
        using SysFile sysFile = _fileSystem.Sys_OpenFile(stream);
        var file = new DbFile(sysFile, Path.GetFileNameWithoutExtension(sourceName));
        DbRuntime activeRuntime = ResolveRuntime(context, runtime);
        DbLoadContext activeContext = context ?? activeRuntime.CreateLoadContext();
        activeContext.CurrentFastFile = new StreamFileRef(
            0,
            sourceName,
            StreamFileKind.CurrentFastFile);
        return DB_LoadXZone(
            file,
            activeContext,
            flags,
            unknown48,
            activeRuntime);
    }

    // The caller owns file.SysFile for this overload. The byte[] overload above
    // creates and closes its temporary SysFile after the load completes.
    public LoadedXZone DB_LoadXZone(
        DbFile file,
        XZoneInfo zoneInfo,
        DbLoadContext context,
        DbRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        string requestedZoneName = ValidateSupportedRequest(zoneInfo);
        string requestName = Path.GetFileNameWithoutExtension(requestedZoneName);
        if (!string.Equals(file.Name, requestName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"XZoneInfo name '{zoneInfo.Name}' does not match DBFile '{file.Name}'.");
        }

        return DB_LoadXZone(
            file,
            context,
            flags: zoneInfo.AllocFlags,
            runtime: runtime);
    }

    public LoadedXZone DB_LoadXZone(
        DbFile file,
        DbLoadContext context,
        XZoneFlags flags = XZoneFlags.None,
        uint unknown48 = 0,
        DbRuntime? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(context);

        DbRuntime activeRuntime = ResolveRuntime(context, runtime);
        activeRuntime.ThrowIfFaulted();
        if (context.Blocks.ZoneMemory is not null)
        {
            throw new InvalidOperationException(
                "A DbLoadContext is bound to one XZone. Create a new context for each DB_LoadXZone call.");
        }

        // The enclosing DB_LoadXAssets batch already owns a complete runtime
        // rollback snapshot. Taking the same growing pool/string/material
        // snapshots for every zone in that batch is redundant and turns a
        // dependency plan into repeated O(total-runtime-state) copying.
        // Standalone DB_LoadXZone calls retain their original per-zone
        // transaction boundary.
        bool ownsZoneTransactions = !activeRuntime.HasActiveBatchTransaction;
        using XAssetPoolTransaction? assetTransaction = ownsZoneTransactions
            ? activeRuntime.AssetPool.BeginTransaction()
            : null;
        using ScriptStringTableTransaction? scriptStringTransaction = ownsZoneTransactions
            ? activeRuntime.ScriptStrings.BeginTransaction()
            : null;
        using MaterialTechniqueStateCacheTransaction? materialStateTransaction = ownsZoneTransactions
            ? activeRuntime.MaterialTechniqueStateCache.BeginTransaction()
            : null;
        using XAssetRuntimeLifecycleTransaction? lifecycleTransaction =
            activeRuntime.BeginZoneLifecycleTransaction();

        XFileLoadState loadState = DB_InitLoadXFile(file, context);
        XZoneMemory memory = activeRuntime.ZoneMemoryAllocator.DB_AllocXZoneMemory(
            loadState.XFile,
            file.Name);
        context.Blocks.DB_InitStreams(memory);

        var zone = new XZone(
            file,
            unknown48,
            flags,
            DB_GetZoneAllocType(flags),
            memory);
        activeRuntime.BeginXZoneLoad(zone, context);
        LoadedXZone result = DB_LoadXFileCore(memory, file, loadState, context, zone);

        activeRuntime.StageXZone(zone, context);
        activeRuntime.DB_PostLoadXZone();

        // DB_PostLoadXZone has now published the zone. Commit global identity
        // state only after that publication succeeds.
        lifecycleTransaction?.Commit();
        materialStateTransaction?.Commit();
        scriptStringTransaction?.Commit();
        assetTransaction?.Commit();

        return result;
    }

    /// <summary>
    /// COMMON, LOAD, and DEV use allocator 0; every other exact flag value
    /// uses allocator 1. Combined masks therefore use allocator 1.
    /// </summary>
    public static int DB_GetZoneAllocType(XZoneFlags zoneFlags)
    {
        return zoneFlags is XZoneFlags.DB_ZONE_COMMON
            or XZoneFlags.DB_ZONE_LOAD
            or XZoneFlags.DB_ZONE_DEV
            ? 0
            : 1;
    }

    private DbRuntime ResolveRuntime(DbLoadContext? context, DbRuntime? requestedRuntime)
    {
        DbRuntime activeRuntime;
        if (_runtime is null)
        {
            activeRuntime = requestedRuntime ?? new DbRuntime(
                context?.AssetPool,
                context?.ScriptStrings,
                context?.MaterialTechniqueStateCache,
                context?.GfxImageRuntimeRegistrationHooks,
                context?.AssetRuntimeLifecycle);
            _runtime = activeRuntime;
        }
        else
        {
            activeRuntime = requestedRuntime ?? _runtime;
            if (!ReferenceEquals(activeRuntime, _runtime))
            {
                throw new InvalidOperationException(
                    "A DbZoneLoader is bound to one DbRuntime. Create another loader for a different runtime.");
            }
        }

        if (context is not null && !ReferenceEquals(context.AssetPool, activeRuntime.AssetPool))
        {
            throw new InvalidOperationException(
                "DB_LoadXZone requires the load context and DbRuntime to share one global XAssetPool.");
        }

        if (context is not null && !ReferenceEquals(context.ScriptStrings, activeRuntime.ScriptStrings))
        {
            throw new InvalidOperationException(
                "DB_LoadXZone requires the load context and DbRuntime to share one global script-string table.");
        }

        if (context is not null &&
            !ReferenceEquals(
                context.MaterialTechniqueStateCache,
                activeRuntime.MaterialTechniqueStateCache))
        {
            throw new InvalidOperationException(
                "DB_LoadXZone requires the load context and DbRuntime to share one process-global material technique-state cache.");
        }

        if (context is not null &&
            !ReferenceEquals(
                context.GfxImageRuntimeRegistrationHooks,
                activeRuntime.GfxImageRuntimeRegistrationHooks))
        {
            throw new InvalidOperationException(
                "DB_LoadXZone requires the load context and DbRuntime to share the same GfxImage runtime registration hooks.");
        }

        if (context is not null &&
            !ReferenceEquals(context.AssetRuntimeLifecycle, activeRuntime.AssetRuntimeLifecycle))
        {
            throw new InvalidOperationException(
                "DB_LoadXZone requires the load context and DbRuntime to share one XAsset runtime lifecycle.");
        }

        return activeRuntime;
    }

    private static string ValidateSupportedRequest(XZoneInfo zoneInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneInfo.Name);
        if (zoneInfo.FreeFlags != XZoneFlags.None)
        {
            throw new NotSupportedException(
                $"XZoneInfo.FreeFlags 0x{unchecked((uint)zoneInfo.FreeFlags):X8} must be executed by the " +
                "outer DB_LoadXAssets plan; the single-zone loader will not reorder or ignore it.");
        }

        return zoneInfo.Name;
    }

    public XFileLoadState DB_InitLoadXFile(DbFile file, DbLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(context);

        byte[] buffer = _fileSystem.Sys_ReadToEnd(file.SysFile);
        var cursor = new FastFileCursor(buffer);
        DbHeader header = _dbHeaderReader.Read(cursor, context);
        if (header.FileSize < cursor.Offset || header.FileSize > buffer.Length)
        {
            throw new InvalidDataException(
                $"DBFile '{file.Name}' declares packed end 0x{header.FileSize:X}, " +
                $"outside source range 0x{cursor.Offset:X}..0x{buffer.Length:X}.");
        }

        byte[] zoneBytes = _packedStreamReader.ReadZone(cursor, header.FileSize);
        context.DecodedZoneBytes = zoneBytes;

        var zoneCursor = new FastFileCursor(zoneBytes, decodedTapeBaseOffset: 0);
        XFile xfile = _xfileHeaderReader.Read(zoneCursor);
        context.BeginZoneObjectCapture(zoneBytes, xfile);

        return new XFileLoadState(header, xfile, zoneBytes, zoneCursor.Offset);
    }

    // The explicit state, context, and zone arguments replace engine globals;
    // the first two arguments preserve the DB_LoadXFile semantic boundary.
    public LoadedXZone DB_LoadXFile(
        XZoneMemory zoneMemory,
        DbFile file,
        XFileLoadState loadState,
        DbLoadContext context,
        XZone zone)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Public phase-level callers do not inherit DB_LoadXZone's outer
        // transaction. Give them the same failure isolation for loader-time
        // string and asset publication, while the outer path calls the core so
        // its transaction can remain open through DB_PostLoadXZone.
        using XAssetPoolTransaction assetTransaction = context.AssetPool.BeginTransaction();
        using ScriptStringTableTransaction scriptStringTransaction = context.ScriptStrings.BeginTransaction();
        using MaterialTechniqueStateCacheTransaction materialStateTransaction =
            context.MaterialTechniqueStateCache.BeginTransaction();
        using XAssetRuntimeLifecycleTransaction lifecycleTransaction =
            context.AssetRuntimeLifecycle.Dispatcher.BeginTransaction();
        LoadedXZone loaded = DB_LoadXFileCore(zoneMemory, file, loadState, context, zone);
        lifecycleTransaction.Commit();
        materialStateTransaction.Commit();
        scriptStringTransaction.Commit();
        assetTransaction.Commit();
        return loaded;
    }

    private LoadedXZone DB_LoadXFileCore(
        XZoneMemory zoneMemory,
        DbFile file,
        XFileLoadState loadState,
        DbLoadContext context,
        XZone zone)
    {
        ArgumentNullException.ThrowIfNull(zoneMemory);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(loadState);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(zone);

        if (!ReferenceEquals(context.Blocks.ZoneMemory, zoneMemory))
            throw new InvalidOperationException("DB_InitStreams was not called for the supplied XZoneMemory.");
        if (!ReferenceEquals(zone.Memory, zoneMemory) || !ReferenceEquals(zone.File, file))
            throw new InvalidOperationException("DB_LoadXFile received mismatched XZone, XZoneMemory, and DBFile state.");
        if (context.Header != loadState.Header ||
            !ReferenceEquals(context.DecodedZoneBytes, loadState.ZoneBytes))
        {
            throw new InvalidOperationException(
                "DB_LoadXFile received state from a different DB_InitLoadXFile operation.");
        }

        ValidateAllocation(loadState.XFile, zoneMemory);

        var zoneCursor = new FastFileCursor(loadState.ZoneBytes, decodedTapeBaseOffset: 0);
        zoneCursor.Skip(loadState.XFileDataOffset);
        XAssetListSnapshot xassetList =
            _xassetListReader.Read(zoneCursor, context);


        IReadOnlyList<XAssetLoadResult> loadedAssets =
            _xassetDispatcher.LoadAll(zoneCursor, xassetList, context);
        if (loadedAssets.Count != xassetList.AssetCount)
        {
            throw new InvalidDataException(
                $"DB_LoadXFile materialized {loadedAssets.Count} of {xassetList.AssetCount} XAssets; " +
                "an incomplete XZone cannot be registered.");
        }

        int declaredMeaningfulEnd = checked((int)loadState.XFile.Size + XFile.SerializedSize);
        if (zoneCursor.Offset != declaredMeaningfulEnd)
        {
            throw new InvalidDataException(
                $"Decoded zone semantic traversal ended at 0x{zoneCursor.Offset:X}, but XFile.Size " +
                $"declares the meaningful end at 0x{declaredMeaningfulEnd:X}.");
        }
        RecordZoneTailPadding(loadState.ZoneBytes, zoneCursor.Offset, context);
        ZoneObjectFile objectFile = context.FreezeZoneObjectFile();

        var loaded = new LoadedXZone(
            SourceName: context.CurrentFastFile.Name,
            Zone: zone,
            Context: context,
            Header: loadState.Header,
            XFile: loadState.XFile,
            XAssetList: xassetList,
            LoadedAssets: Array.AsReadOnly(loadedAssets.ToArray()),
            ZoneBytes: loadState.ZoneBytes,
            Warnings: Array.AsReadOnly(context.Diagnostics.Warnings.ToArray()),
            ZoneObjectFile: objectFile);

        return loaded;
    }

    private static void ValidateAllocation(XFile xfile, XZoneMemory memory)
    {
        for (int index = 0; index < XZoneMemory.BlockCount; index++)
        {
            uint expected = xfile.BlockSizes[index];
            uint actual = memory.Blocks[index].Size;
            if (actual != expected)
            {
                throw new InvalidDataException(
                    $"XZoneMemory block {(XFileBlockType)index} has size 0x{actual:X}; " +
                    $"XFile declares 0x{expected:X}.");
            }
        }
    }

    private static void RecordZoneTailPadding(
        byte[] zone,
        int sourceOffset,
        DbLoadContext context)
    {
        if (sourceOffset == zone.Length)
            return;

        if (sourceOffset > zone.Length)
        {
            throw new InvalidDataException(
                $"Zone cursor ended at 0x{sourceOffset:X}, beyond decoded zone length 0x{zone.Length:X}.");
        }

        ReadOnlySpan<byte> tail = zone.AsSpan(sourceOffset);
        if (tail.IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException(
                $"Decoded zone has non-zero unparsed tail bytes at 0x{sourceOffset:X}..0x{zone.Length:X}; " +
                "an incomplete XZone cannot be registered.");
        }

    }
}
