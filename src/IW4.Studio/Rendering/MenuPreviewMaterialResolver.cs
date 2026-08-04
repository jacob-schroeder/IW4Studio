using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Zone;
using IW4.Render.Textures;
using IW4.Render.UI;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Documents;

namespace IW4.Studio.Rendering;

/// <summary>
/// Resolves a Menu shader-window material to one deterministic editor image.
/// Renderer-owned planning selects the canonical texture approximation;
/// Studio binds that plan to the active workspace provider and its matching
/// image-package resolver. It does not execute the PS3 material technique.
/// </summary>
public sealed class MenuPreviewMaterialResolver : IMenuPreviewMaterialResolver
{
    private readonly FastFileWorkspace _workspace;
    private readonly Dictionary<MaterialCacheKey,
        Lazy<Task<MenuPreviewMaterialResolution>>> _cache = [];
    private readonly object _revisionGate = new();
    private long _cachedPoolRevision = -1;

    public MenuPreviewMaterialResolver(FastFileWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

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
            if (_workspace.Runtime.AssetPool.Revision == cached.PoolRevision)
                return result;
        }

        long currentRevision = _workspace.Runtime.AssetPool.Revision;
        return MenuPreviewMaterialResolution.Failed(
            $"Material '{materialName}' changed providers while its preview " +
            "was being prepared; refresh the preview and try again.",
            currentRevision);
    }

    private CachedMaterialResolution GetCachedResolution(string materialName)
    {
        lock (_revisionGate)
        {
            long poolRevision = _workspace.Runtime.AssetPool.Revision;
            if (_cachedPoolRevision != poolRevision)
            {
                _cache.Clear();
                _cachedPoolRevision = poolRevision;
            }

            var key = new MaterialCacheKey(
                XAssetStableIdentity.NormalizeLookupName(materialName),
                poolRevision);
            if (!_cache.TryGetValue(
                    key,
                    out Lazy<Task<MenuPreviewMaterialResolution>>? lazy))
            {
                lazy = new Lazy<Task<MenuPreviewMaterialResolution>>(
                    () => Task.Run(
                        () => ResolveAtStableRevision(
                            materialName,
                            poolRevision),
                        CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                _cache.Add(key, lazy);
            }

            return new CachedMaterialResolution(poolRevision, lazy.Value);
        }
    }

    private MenuPreviewMaterialResolution ResolveAtStableRevision(
        string materialName,
        long poolRevision)
    {
        if (_workspace.Runtime.AssetPool.Revision != poolRevision)
            return RevisionChanged(materialName, poolRevision);

        MenuPreviewMaterialResolution result = ResolveAtRevision(
            materialName,
            poolRevision);
        return _workspace.Runtime.AssetPool.Revision == poolRevision
            ? result
            : RevisionChanged(materialName, poolRevision);
    }

    private MenuPreviewMaterialResolution ResolveAtRevision(
        string materialName,
        long poolRevision)
    {
        XAssetPool pool = _workspace.Runtime.AssetPool;
        if (!pool.TryResolve(
                XAssetType.Material,
                materialName,
                out MaterialAsset? material) ||
            material is null)
        {
            return MenuPreviewMaterialResolution.Failed(
                $"Material '{materialName}' is not available in the active " +
                "asset pool. Open the fastfile with its dependencies loaded.",
                poolRevision);
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
                plan.Diagnostics);
        }

        IGfxImagePayloadResolver payloadResolver =
            ResolvePayloadResolver(pool, image, out string payloadSource);
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                image,
                payloadResolver,
                out GfxImagePreviewSnapshot? preview,
                out string reason) ||
            preview is null)
        {
            return MenuPreviewMaterialResolution.Failed(
                $"Image '{image.Name ?? "unnamed image"}' for material " +
                $"'{materialName}' could not be decoded from " +
                $"{payloadSource}: {reason}",
                poolRevision,
                plan.Diagnostics);
        }

        return MenuPreviewMaterialResolution.Resolved(
            new MenuPreviewMaterialSnapshot(plan, preview),
            poolRevision);
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

    private IGfxImagePayloadResolver ResolvePayloadResolver(
        XAssetPool pool,
        GfxImageAsset image,
        out string source)
    {
        if (image.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !pool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.ActiveProvider.Owner.IsNone)
        {
            source = "the embedded image payload";
            return UnavailableGfxImagePayloadResolver.Instance;
        }

        WorkspaceZone[] owners = _workspace.LoadedZones
            .Where(zone =>
                zone.RuntimeZoneHandle == slot.ActiveProvider.Owner)
            .ToArray();
        if (owners.Length != 1)
        {
            source = owners.Length == 0
                ? "an unavailable provider-zone image package"
                : "an ambiguous provider-zone image package";
            return UnavailableGfxImagePayloadResolver.Instance;
        }

        source = $"image package for zone '{owners[0].LogicalZoneName}'";
        return owners[0].LoadResult.ImagePayloadResolver;
    }

    private readonly record struct MaterialCacheKey(
        string NormalizedName,
        long PoolRevision);

    private readonly record struct CachedMaterialResolution(
        long PoolRevision,
        Task<MenuPreviewMaterialResolution> Task);

    private static MenuPreviewMaterialResolution RevisionChanged(
        string materialName,
        long requestedRevision) =>
        MenuPreviewMaterialResolution.Failed(
            $"Material '{materialName}' changed providers while revision " +
            $"{requestedRevision:N0} was being prepared.",
            requestedRevision);
}
