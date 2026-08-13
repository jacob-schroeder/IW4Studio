using System.Numerics;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Zone;
using IW4.Render;
using IW4.Render.Assets;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Techniques;
using IW4.Render.Textures;
using IW4.Render.UI;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Resolves a Menu material against the active canonical asset graph. The
/// proven IW4 2d/slot-4 unlit path yields an exact renderer-neutral packet;
/// unsupported graphs retain explicit execution diagnostics and fall back to
/// the deterministic texture-only planner. Studio owns provider/image-package
/// binding while IW4.Render owns material semantics.
/// </summary>
public sealed class MenuPreviewMaterialResolver : IMenuPreviewMaterialResolver
{
    private const int MaxCompletedCacheEntries = 128;
    private const long MaxCachedPayloadBytes = 64L * 1024 * 1024;

    private readonly FastFileWorkspace _workspace;
    private readonly WorkspaceGfxImagePayloadResolver _imagePayloads;
    private readonly IMaterialExecutionLookup _materialExecution;
    private readonly Dictionary<MaterialCacheKey,
        MaterialCacheEntry> _cache = [];
    private readonly LinkedList<MaterialCacheKey> _completedLru = [];
    private readonly object _revisionGate = new();
    private readonly object _renderPlanningGate = new();
    private long _cachedPoolRevision = -1;
    private long _cachedPayloadBytes;

    public MenuPreviewMaterialResolver(FastFileWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _imagePayloads = new WorkspaceGfxImagePayloadResolver(workspace);
        _materialExecution = CreateMaterialExecutionLookup(workspace);
    }

    public long Revision => _workspace.LoadedZone.Context.AssetPool.Revision;

    public async Task<MenuPreviewMaterialResolution> ResolveAsync(
        string materialName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        cancellationToken.ThrowIfCancellationRequested();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            CachedMaterialResolution cached = GetCachedResolution(
                materialName);
            MenuPreviewMaterialResolution result = await cached.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (_workspace.LoadedZone.Context.AssetPool.Revision == cached.PoolRevision)
                return result;
        }

        long currentRevision = _workspace.LoadedZone.Context.AssetPool.Revision;
        return MenuPreviewMaterialResolution.Failed(
            $"Material '{materialName}' changed providers while its preview " +
            "was being prepared; refresh the preview and try again.",
            currentRevision);
    }

    private CachedMaterialResolution GetCachedResolution(string materialName)
    {
        MaterialCacheEntry entry;
        Task<MenuPreviewMaterialResolution> task;
        bool observeCompletion = false;
        long poolRevision;
        lock (_revisionGate)
        {
            poolRevision = _workspace.LoadedZone.Context.AssetPool.Revision;
            if (_cachedPoolRevision != poolRevision)
            {
                ClearCacheNoLock();
                _cachedPoolRevision = poolRevision;
            }

            var key = new MaterialCacheKey(
                XAssetStableIdentity.NormalizeLookupName(materialName),
                poolRevision);
            if (!_cache.TryGetValue(key, out MaterialCacheEntry? cached))
            {
                var work = new Lazy<Task<MenuPreviewMaterialResolution>>(
                    () => Task.Run(
                        () => ResolveAtStableRevision(
                            materialName,
                            poolRevision),
                        CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                entry = new MaterialCacheEntry(key, work);
                _cache.Add(key, entry);
            }
            else
            {
                entry = cached;
                TouchCompletedEntryNoLock(entry);
            }

            task = entry.Work.Value;
            if (!entry.CompletionObservationStarted)
            {
                entry.CompletionObservationStarted = true;
                observeCompletion = true;
            }
        }

        if (observeCompletion)
            _ = ObserveCompletionAsync(entry, task);
        return new CachedMaterialResolution(poolRevision, task);
    }

    private async Task ObserveCompletionAsync(
        MaterialCacheEntry entry,
        Task<MenuPreviewMaterialResolution> task)
    {
        MenuPreviewMaterialResolution? result = null;
        try
        {
            result = await task.ConfigureAwait(false);
        }
        catch
        {
            // The awaiting caller retains the original task outcome. A faulted
            // single-flight operation must not occupy completed-cache capacity.
        }

        lock (_revisionGate)
        {
            if (!_cache.TryGetValue(entry.Key, out MaterialCacheEntry? current) ||
                !ReferenceEquals(current, entry))
            {
                return;
            }

            if (result is null)
            {
                RemoveCacheEntryNoLock(entry);
                return;
            }

            long retainedBytes = result.Snapshot?.RetainedByteCount ?? 0;
            if (retainedBytes > MaxCachedPayloadBytes)
            {
                RemoveCacheEntryNoLock(entry);
                return;
            }

            entry.RetainedBytes = retainedBytes;
            entry.LruNode = _completedLru.AddLast(entry.Key);
            _cachedPayloadBytes = checked(
                _cachedPayloadBytes + retainedBytes);
            TrimCompletedCacheNoLock();
        }
    }

    private void TouchCompletedEntryNoLock(MaterialCacheEntry entry)
    {
        if (entry.LruNode is not { } node)
            return;

        _completedLru.Remove(node);
        _completedLru.AddLast(node);
    }

    private void TrimCompletedCacheNoLock()
    {
        while (_completedLru.Count > MaxCompletedCacheEntries ||
               _cachedPayloadBytes > MaxCachedPayloadBytes)
        {
            LinkedListNode<MaterialCacheKey>? oldest = _completedLru.First;
            if (oldest is null)
                return;
            if (!_cache.TryGetValue(
                    oldest.Value,
                    out MaterialCacheEntry? entry))
            {
                _completedLru.RemoveFirst();
                continue;
            }

            RemoveCacheEntryNoLock(entry);
        }
    }

    private void RemoveCacheEntryNoLock(MaterialCacheEntry entry)
    {
        if (_cache.TryGetValue(entry.Key, out MaterialCacheEntry? current) &&
            ReferenceEquals(current, entry))
        {
            _cache.Remove(entry.Key);
        }
        if (entry.LruNode is { } node)
        {
            _completedLru.Remove(node);
            entry.LruNode = null;
        }

        _cachedPayloadBytes = checked(
            _cachedPayloadBytes - entry.RetainedBytes);
        entry.RetainedBytes = 0;
    }

    private void ClearCacheNoLock()
    {
        _cache.Clear();
        _completedLru.Clear();
        _cachedPayloadBytes = 0;
    }

    private MenuPreviewMaterialResolution ResolveAtStableRevision(
        string materialName,
        long poolRevision)
    {
        try
        {
            if (_workspace.LoadedZone.Context.AssetPool.Revision != poolRevision)
                return RevisionChanged(materialName, poolRevision);

            MenuPreviewMaterialResolution result = ResolveAtRevision(
                materialName,
                poolRevision);
            return _workspace.LoadedZone.Context.AssetPool.Revision == poolRevision
                ? result
                : RevisionChanged(materialName, poolRevision);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            return _workspace.LoadedZone.Context.AssetPool.Revision != poolRevision
                ? RevisionChanged(materialName, poolRevision)
                : MenuPreviewMaterialResolution.Failed(
                    $"Material '{materialName}' preview resolution failed " +
                    $"closed: {exception.Message}",
                    poolRevision);
        }
    }

    private MenuPreviewMaterialResolution ResolveAtRevision(
        string materialName,
        long poolRevision)
    {
        XAssetPool pool = _workspace.LoadedZone.Context.AssetPool;
        if (!pool.TryResolve(
                XAssetType.Material,
                materialName,
                out MaterialAsset? material) ||
            material is null)
        {
            return MenuPreviewMaterialResolution.Failed(
                $"Material '{materialName}' is not available in the active " +
                "asset pool.",
                poolRevision);
        }

        UiMaterialDrawPlan executionPlan;
        lock (_renderPlanningGate)
        {
            executionPlan = UiMaterialDrawPlanner.Plan(
                new UiMaterialDrawRequest(
                    0,
                    materialName,
                    poolRevision,
                    CreateUnitQuad()),
                _materialExecution,
                (_, row) => ResolveExactTextureResource(
                    pool,
                    row,
                    poolRevision));
        }

        UiMaterialPreviewPlan plan = UiMaterialPreviewPlanner.Plan(
            material,
            (_, row) => ResolveCanonicalImage(pool, row));
        if (!plan.CanAttemptTextureDecode ||
            plan.SelectedImage is not GfxImageAsset image)
        {
            string blockers = plan.Blockers.Count == 0
                ? "No canonical 2D image is available."
                : string.Join(" ", plan.Blockers.Select(value => value.Message));
            return MenuPreviewMaterialResolution.Failed(
                $"Material '{materialName}' cannot be previewed: {blockers}",
                poolRevision,
                plan.Diagnostics,
                executionPlan.Diagnostics);
        }

        UiMaterialCpuPreviewPlan? cpuPreviewPlan = PlanCpuPreview(
            material,
            plan,
            executionPlan);
        string payloadSource = _imagePayloads.DescribeSource(image);
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                image,
                _imagePayloads,
                out GfxImagePreviewSnapshot? preview,
                out string reason) ||
            preview is null)
        {
            return MenuPreviewMaterialResolution.Failed(
                $"Image '{image.Name ?? "unnamed image"}' for material " +
                $"'{materialName}' could not be decoded from " +
                $"{payloadSource}: {reason}",
                poolRevision,
                plan.Diagnostics,
                executionPlan.Diagnostics);
        }

        return MenuPreviewMaterialResolution.Resolved(
            new MenuPreviewMaterialSnapshot(
                plan,
                preview,
                executionPlan,
                cpuPreviewPlan),
            poolRevision);
    }

    private UiMaterialCpuPreviewPlan? PlanCpuPreview(
        MaterialAsset material,
        UiMaterialPreviewPlan previewPlan,
        UiMaterialDrawPlan executionPlan)
    {
        if (executionPlan.Packet is null &&
            !HasOnlyMaterialStateBlocker(executionPlan))
        {
            return null;
        }
        if (!previewPlan.Atlas.IsValid ||
            previewPlan.Atlas.EffectiveCellCount != 1)
        {
            return UiMaterialCpuPreviewPlan.Blocked(
                "The Menu CPU compositor requires a full-texture material; " +
                "this material's atlas frame has not been evaluated.");
        }
        if (previewPlan.SelectedSamplerState is not { } sampler ||
            sampler.MipFilter != TextureFilter.None ||
            sampler.MinFilter is not (
                TextureFilter.Point or
                TextureFilter.Linear) ||
            sampler.MagFilter is not (
                TextureFilter.Point or
                TextureFilter.Linear))
        {
            return UiMaterialCpuPreviewPlan.Blocked(
                "The Menu CPU compositor supports only decoded point or " +
                "linear base-level sampler filtering.");
        }
        if (!RenderStateDecoder.TryDecode(
                material,
                UiMaterialDrawPlanner.TechniqueSlot,
                UiMaterialDrawPlanner.PassIndex,
                _materialExecution,
                out RenderState state))
        {
            return UiMaterialCpuPreviewPlan.Blocked(
                "The selected material pass has no decodable PS3 state bits.");
        }

        return UiMaterialCpuPreviewPlan.Plan(state);
    }

    private static bool HasOnlyMaterialStateBlocker(
        UiMaterialDrawPlan plan) =>
        plan.Packet is null &&
        plan.Diagnostics.Count == 1 &&
        plan.Diagnostics[0].Code ==
            UiMaterialExecutionDiagnosticCode.UnsupportedMaterialState &&
        plan.Diagnostics[0].Severity ==
            UiDiagnosticSeverity.Blocker;

    private static IMaterialExecutionLookup CreateMaterialExecutionLookup(
        FastFileWorkspace workspace)
    {
        var target = workspace.LoadedZone;
        var source = new RenderAssetSource(
            target.Context.Blocks,
            target.Context.AssetPool,
            target.Context.GfxImagesByAddress,
            target.LoadedAssets,
            target.XAssetList.Assets);
        return new RenderAssetLookup(source);
    }

    private static UiMaterialTextureResource? ResolveExactTextureResource(
        XAssetPool pool,
        MaterialTextureDef row,
        long poolRevision)
    {
        UiMaterialPreviewImageResolution resolution =
            ResolveCanonicalImage(pool, row);
        if (resolution.Image is not { } image ||
            image.RuntimeAddress?.AssetPoolAddress is not { } address ||
            string.IsNullOrWhiteSpace(image.Name))
        {
            return null;
        }

        return new UiMaterialTextureResource(
            $"ui:{poolRevision}:{address}",
            image.Name,
            poolRevision,
            address,
            image.Width,
            image.Height,
            image.Depth,
            image.MapType,
            image.DimensionCount,
            image.MultiFaceControl,
            image.Pad0F,
            image.Pad1B);
    }

    private static UiMaterialQuad CreateUnitQuad()
    {
        var topLeft = new UiMaterialVertex(
            new Vector4(0, 0, 0, 1),
            new Vector2(0, 0),
            Vector4.One);
        var topRight = new UiMaterialVertex(
            new Vector4(1, 0, 0, 1),
            new Vector2(1, 0),
            Vector4.One);
        var bottomRight = new UiMaterialVertex(
            new Vector4(1, 1, 0, 1),
            new Vector2(1, 1),
            Vector4.One);
        var bottomLeft = new UiMaterialVertex(
            new Vector4(0, 1, 0, 1),
            new Vector2(0, 1),
            Vector4.One);
        return new UiMaterialQuad(
            topLeft,
            topRight,
            bottomRight,
            bottomLeft);
    }

    private static UiMaterialPreviewImageResolution ResolveCanonicalImage(
        XAssetPool pool,
        MaterialTextureDef row)
    {
        if (row.Image is not { } image)
        {
            return UiMaterialPreviewImageResolution.Unavailable(
                "The material texture row does not resolve to a canonical " +
                "GfxImage asset.");
        }
        if (image.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != XAssetType.Image ||
            !pool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.AssetType != XAssetType.Image ||
            slot.ActiveProvider.IsReferencePlaceholder ||
            slot.CanonicalAsset is not GfxImageAsset canonical)
        {
            return UiMaterialPreviewImageResolution.Unavailable(
                $"Image '{image.Name ?? "unnamed image"}' has no complete " +
                "active canonical provider.");
        }

        return UiMaterialPreviewImageResolution.Canonical(canonical);
    }

    private readonly record struct MaterialCacheKey(
        string NormalizedName,
        long PoolRevision);

    private readonly record struct CachedMaterialResolution(
        long PoolRevision,
        Task<MenuPreviewMaterialResolution> Task);

    private sealed class MaterialCacheEntry(
        MaterialCacheKey key,
        Lazy<Task<MenuPreviewMaterialResolution>> work)
    {
        public MaterialCacheKey Key { get; } = key;

        public Lazy<Task<MenuPreviewMaterialResolution>> Work { get; } = work;

        public bool CompletionObservationStarted { get; set; }

        public long RetainedBytes { get; set; }

        public LinkedListNode<MaterialCacheKey>? LruNode { get; set; }
    }

    private static MenuPreviewMaterialResolution RevisionChanged(
        string materialName,
        long requestedRevision) =>
        MenuPreviewMaterialResolution.Failed(
            $"Material '{materialName}' changed providers while revision " +
            $"{requestedRevision:N0} was being prepared.",
            requestedRevision);
}
