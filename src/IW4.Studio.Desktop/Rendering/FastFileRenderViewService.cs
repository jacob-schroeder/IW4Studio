using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Render;
using IW4.Render.Assets;
using IW4.Render.Resources;
using IW4.Render.SceneBuilding;
using IW4.Studio.Documents;
using System.Runtime.CompilerServices;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Backend-neutral Studio workflow for preparing map render content and for
/// attaching render-view intent to an immutable document workspace. Graphics
/// API selection, handles, surfaces, and execution remain Desktop concerns.
/// </summary>
public sealed class FastFileRenderViewService : IDisposable
{
    private const string NoRenderableMapAssetsReason =
        "The target fastfile contains neither a GfxWorld nor a ClipMap asset.";
    private const string InactiveTargetReason =
        "The target fastfile was retired by its dependency lifecycle and " +
        "cannot supply a live render scene.";
    private const int MaxRuntimeStabilityBuildAttempts = 3;

    private readonly IMapRenderSceneBuilder _sceneBuilder;
    private readonly Lock _sceneBuilderLock = new();
    private readonly Lock _sceneBuildCacheLock = new();
    private readonly Dictionary<SceneBuildCacheKey, SceneBuildCacheEntry>
        _sceneBuilds = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _disposed;

    public FastFileRenderViewService()
        : this(new MapSceneBuilder())
    {
    }

    private FastFileRenderViewService(IMapRenderSceneBuilder sceneBuilder)
    {
        ArgumentNullException.ThrowIfNull(sceneBuilder);
        _sceneBuilder = sceneBuilder;
    }

    internal static bool CanRenderTargetMap(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        WorkspaceZone targetZone = workspace.LoadedZones.Single(zone => zone.IsTarget);
        return targetZone.IsActive &&
               HasRenderableMapAssets(targetZone.LoadResult);
    }

    private static RenderSceneSnapshot CreateInteractiveSnapshot(
        MapRenderScene scene,
        long revision) =>
        OperatingSystem.IsMacOS()
            ? RenderSceneSnapshotBuilder.CreateInteractiveMetal(
                scene,
                revision)
            : RenderSceneSnapshotBuilder.CreateInteractiveOpenGl(
                scene,
                revision);

    private RenderViewSceneBuildResult BuildSceneCore(
        FastFileWorkspace workspace,
        long snapshotRevision,
        Action<string>? progress,
        CancellationToken buildCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (snapshotRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotRevision));
        buildCancellationToken.ThrowIfCancellationRequested();
        Action<string>? buildProgress = progress;
        if (buildCancellationToken.CanBeCanceled)
        {
            buildProgress = message =>
            {
                buildCancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(message);
            };
        }

        WorkspaceZone targetZone = workspace.LoadedZones.Single(zone => zone.IsTarget);
        LoadedXZone loadedZone = targetZone.LoadResult;
        if (!targetZone.IsActive)
        {
            return RenderViewSceneBuildResult.NoRenderableMapAssets(
                workspace.Document.DocumentId,
                loadedZone,
                InactiveTargetReason);
        }
        if (!HasRenderableMapAssets(loadedZone))
        {
            return RenderViewSceneBuildResult.NoRenderableMapAssets(
                workspace.Document.DocumentId,
                loadedZone,
                NoRenderableMapAssetsReason);
        }

        GfxWorldAsset? gfxWorld = loadedZone.LoadedAssets
            .Select(asset => asset.Asset)
            .OfType<GfxWorldAsset>()
            .SingleOrDefault();
        ClipMapAsset? clipMap = loadedZone.LoadedAssets
            .Select(asset => asset.Asset)
            .OfType<ClipMapAsset>()
            .SingleOrDefault();

        var assetSource = new RenderAssetSource(
            loadedZone.Context.Blocks,
            loadedZone.Context.AssetPool,
            loadedZone.Context.GfxImagesByAddress,
            loadedZone.LoadedAssets,
            loadedZone.XAssetList.Assets);
        var input = new MapRenderInput(
            assetSource,
            loadedZone.Context.AssetRuntimeLifecycle.GfxWorld,
            workspace.Document.Request.Path,
            gfxWorld,
            clipMap,
            buildProgress)
        {
            ImagePayloadResolver = loadedZone.ImagePayloadResolver,
            BuildProfile = MapRenderSceneBuildProfile.InteractiveNative
        };

        MapRenderScene scene;
        lock (_sceneBuilderLock)
        {
            buildCancellationToken.ThrowIfCancellationRequested();
            scene = _sceneBuilder.Build(input)
                ?? throw new InvalidOperationException(
                    "The map render scene builder returned no scene.");
        }
        buildCancellationToken.ThrowIfCancellationRequested();
        buildProgress?.Invoke("freezing scene resources for the renderer");
        RenderSceneSnapshot sceneSnapshot = CreateInteractiveSnapshot(
            scene,
            snapshotRevision);
        buildCancellationToken.ThrowIfCancellationRequested();
        RenderViewSceneBuildResult result =
            RenderViewSceneBuildResult.Renderable(
                workspace.Document.DocumentId,
                loadedZone,
                gfxWorld,
                clipMap,
                scene,
                sceneSnapshot);

        RenderBuildMemoryReclaimer.ReclaimCompletedBuildWorkspace();
        buildCancellationToken.ThrowIfCancellationRequested();
        buildProgress?.Invoke(
            "scene resources are ready for native renderer initialization");
        return result;
    }

    private static bool HasRenderableMapAssets(LoadedXZone loadedZone) =>
        loadedZone.LoadedAssets.Any(asset =>
            asset.Asset is GfxWorldAsset or ClipMapAsset);

    /// <summary>
    /// Coalesces concurrent Live Preview callers for one immutable
    /// workspace/runtime revision onto one background scene build so they
    /// reuse the exact scene and snapshot instead of repeating asset
    /// resolution, texture decode, geometry construction, and snapshot
    /// preparation.
    /// </summary>
    public async Task<RenderViewSceneBuildResult> BuildSceneAsync(
        FastFileWorkspace workspace,
        long snapshotRevision = 0,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (snapshotRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotRevision));
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        for (int attempt = 1;
             attempt <= MaxRuntimeStabilityBuildAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SceneBuildCacheKey key = SceneBuildCacheKey.Capture(
                workspace);
            long currentWorldTextureRevision =
                CaptureWorldTextureRevision(workspace);
            SceneBuildCacheEntry entry;
            lock (_sceneBuildCacheLock)
            {
                ThrowIfDisposed();
                RemoveTerminalSceneBuildsExcept(key);
                if (!_sceneBuilds.TryGetValue(key, out entry!) ||
                    !entry.CanServe(currentWorldTextureRevision))
                {
                    entry = new SceneBuildCacheEntry(
                        key,
                        report => BuildSceneCore(
                            workspace,
                            snapshotRevision: 0,
                            report,
                            _lifetimeCancellation.Token),
                        _lifetimeCancellation.Token);
                    _sceneBuilds[key] = entry;
                }
            }

            RenderViewSceneBuildResult result =
                await entry.JoinAsync(progress, cancellationToken)
                    .ConfigureAwait(false);
            SceneBuildCacheKey completedKey = SceneBuildCacheKey.Capture(
                workspace);
            long completedWorldTextureRevision =
                CaptureWorldTextureRevision(workspace);
            if (entry.IsStableFor(
                    completedKey,
                    completedWorldTextureRevision))
            {
                return entry.GetResultForSnapshotRevision(
                    result,
                    snapshotRevision);
            }

            ReportAdvisoryProgress(
                progress,
                "runtime assets changed while the render scene was building; refreshing");
            lock (_sceneBuildCacheLock)
            {
                if (_sceneBuilds.TryGetValue(key, out var cached) &&
                    ReferenceEquals(cached, entry))
                {
                    _sceneBuilds.Remove(key);
                }
            }
        }

        throw new InvalidOperationException(
            "Runtime map assets changed during three consecutive render-scene builds. Wait for the current asset refresh to finish, then try Render Map again.");
    }

    public void Dispose()
    {
        Task<RenderViewSceneBuildResult>[] activeBuilds;
        lock (_sceneBuildCacheLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            activeBuilds = _sceneBuilds.Values
                .Select(entry => entry.BuildTask)
                .ToArray();
            _sceneBuilds.Clear();
        }

        // Scene construction observes this token through its existing
        // progress checkpoints, including the long world/static loops.
        _lifetimeCancellation.Cancel();
        try
        {
            Task.WhenAll(activeBuilds).GetAwaiter().GetResult();
        }
        catch
        {
            // Build failures are reported to their waiting render windows.
            // Disposal only needs to ensure that every build has stopped.
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private void RemoveTerminalSceneBuildsExcept(SceneBuildCacheKey keep)
    {
        foreach (var pair in _sceneBuilds.ToArray())
        {
            if (pair.Key == keep ||
                !pair.Value.BuildTask.IsCompleted)
            {
                continue;
            }

            _sceneBuilds.Remove(pair.Key);
        }
    }

    private static void ReportAdvisoryProgress(
        Action<string>? progress,
        string message)
    {
        try
        {
            progress?.Invoke(message);
        }
        catch
        {
            // Progress cannot affect scene construction or cache stability.
        }
    }

    private static long CaptureWorldTextureRevision(
        FastFileWorkspace workspace) =>
        workspace.LoadedZone.Context.AssetRuntimeLifecycle.GfxWorld
            .TextureState?.Revision ?? -1;

    private readonly struct SceneBuildCacheKey :
        IEquatable<SceneBuildCacheKey>
    {
        internal SceneBuildCacheKey(
            FastFileWorkspace workspace,
            long assetPoolRevision)
        {
            Workspace = workspace;
            AssetPoolRevision = assetPoolRevision;
        }

        internal FastFileWorkspace Workspace { get; }
        internal long AssetPoolRevision { get; }

        public bool Equals(SceneBuildCacheKey other) =>
            ReferenceEquals(Workspace, other.Workspace) &&
            AssetPoolRevision == other.AssetPoolRevision;

        public override bool Equals(object? obj) =>
            obj is SceneBuildCacheKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                RuntimeHelpers.GetHashCode(Workspace),
                AssetPoolRevision);

        public static bool operator ==(
            SceneBuildCacheKey left,
            SceneBuildCacheKey right) =>
            left.Equals(right);

        public static bool operator !=(
            SceneBuildCacheKey left,
            SceneBuildCacheKey right) =>
            !left.Equals(right);

        internal static SceneBuildCacheKey Capture(
            FastFileWorkspace workspace) =>
            new(
                workspace,
                workspace.LoadedZone.Context.AssetPool.Revision);
    }

    private sealed class SceneBuildCacheEntry
    {
        private readonly Lock _progressLock = new();
        private readonly Lock _snapshotResultLock = new();
        private readonly List<Action<string>> _progressObservers = [];
        private readonly Dictionary<long, RenderViewSceneBuildResult>
            _snapshotResults = [];
        private string? _latestProgress;
        private long _completedWorldTextureRevision;
        private int _tracksWorldTextureRevision;
        private int _buildCompleted;

        internal SceneBuildCacheEntry(
            SceneBuildCacheKey key,
            Func<Action<string>, RenderViewSceneBuildResult> build,
            CancellationToken buildCancellationToken)
        {
            Key = key;
            ArgumentNullException.ThrowIfNull(build);
            BuildTask = Task.Run(() =>
            {
                RenderViewSceneBuildResult result = build(ReportProgress);
                bool tracksWorldTextureRevision =
                    result.GfxWorld is not null &&
                    result.Scene is
                    {
                        WorldTextureRevisionAtConstruction: >= 0
                    };
                Volatile.Write(
                    ref _completedWorldTextureRevision,
                    result.Scene?.WorldTextureRevisionAtConstruction ?? -1);
                Volatile.Write(
                    ref _tracksWorldTextureRevision,
                    tracksWorldTextureRevision ? 1 : 0);
                Volatile.Write(ref _buildCompleted, 1);
                return result;
            }, buildCancellationToken);
        }

        internal SceneBuildCacheKey Key { get; }

        internal Task<RenderViewSceneBuildResult> BuildTask { get; }

        internal bool CanServe(long currentWorldTextureRevision) =>
            !BuildTask.IsFaulted &&
            !BuildTask.IsCanceled &&
            (Volatile.Read(ref _buildCompleted) == 0 ||
             Volatile.Read(ref _tracksWorldTextureRevision) == 0 ||
             Volatile.Read(ref _completedWorldTextureRevision) ==
                currentWorldTextureRevision);

        internal bool IsStableFor(
            SceneBuildCacheKey current,
            long currentWorldTextureRevision) =>
            Key == current &&
            Volatile.Read(ref _buildCompleted) != 0 &&
            (Volatile.Read(ref _tracksWorldTextureRevision) == 0 ||
             Volatile.Read(ref _completedWorldTextureRevision) ==
                currentWorldTextureRevision);

        internal RenderViewSceneBuildResult GetResultForSnapshotRevision(
            RenderViewSceneBuildResult result,
            long snapshotRevision)
        {
            if (!result.IsRenderable ||
                result.Scene is not { } scene ||
                result.SceneSnapshot is not { } snapshot ||
                snapshot.Revision == snapshotRevision)
            {
                return result;
            }

            lock (_snapshotResultLock)
            {
                if (_snapshotResults.TryGetValue(
                        snapshotRevision,
                        out RenderViewSceneBuildResult? cached))
                {
                    return cached;
                }

                var revised = RenderViewSceneBuildResult.Renderable(
                    result.SourceDocumentId,
                    result.SourceZone,
                    result.GfxWorld,
                    result.ClipMap,
                    scene,
                    CreateInteractiveSnapshot(scene, snapshotRevision));
                _snapshotResults.Add(snapshotRevision, revised);
                return revised;
            }
        }

        internal void ReportProgress(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Action<string>[] observers;
            lock (_progressLock)
            {
                _latestProgress = message;
                observers = _progressObservers.ToArray();
            }

            foreach (Action<string> observer in observers)
            {
                try
                {
                    observer(message);
                }
                catch
                {
                    // Progress is advisory and must never fault the shared
                    // build for other windows.
                }
            }
        }

        internal async Task<RenderViewSceneBuildResult> JoinAsync(
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            string? latest = null;
            if (progress is not null)
            {
                lock (_progressLock)
                {
                    _progressObservers.Add(progress);
                    latest = _latestProgress;
                }
            }

            try
            {
                if (latest is not null)
                {
                    try
                    {
                        progress!(latest);
                    }
                    catch
                    {
                        // Replaying the cached status is advisory too. A late
                        // observer must not fault its join or remain retained
                        // merely because its progress sink threw.
                    }
                }

                return await BuildTask.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (progress is not null)
                {
                    lock (_progressLock)
                        _progressObservers.Remove(progress);
                }
            }
        }
    }
}
