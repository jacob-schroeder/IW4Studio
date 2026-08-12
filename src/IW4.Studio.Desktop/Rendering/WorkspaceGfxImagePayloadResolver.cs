using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Routes streamed image reads through the package resolver owned by the
/// image's active canonical provider zone.
/// </summary>
internal sealed class WorkspaceGfxImagePayloadResolver(
    FastFileWorkspace workspace) : IGfxImagePayloadResolver
{
    private readonly FastFileWorkspace _workspace =
        workspace ?? throw new ArgumentNullException(nameof(workspace));

    public bool TryResolveBestPayload(
        GfxImageAsset image,
        out GfxImagePayload payload,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!TryResolveProvider(
                image,
                out IGfxImagePayloadResolver resolver,
                out _,
                out reason))
        {
            payload = default;
            return false;
        }

        return resolver.TryResolveBestPayload(image, out payload, out reason);
    }

    public bool TryResolveMipPayloads(
        GfxImageAsset image,
        out IReadOnlyList<GfxImagePayload> mips,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!TryResolveProvider(
                image,
                out IGfxImagePayloadResolver resolver,
                out _,
                out reason))
        {
            mips = [];
            return false;
        }

        return resolver.TryResolveMipPayloads(image, out mips, out reason);
    }

    internal string DescribeSource(GfxImageAsset image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _ = TryResolveProvider(image, out _, out string source, out _);
        return source;
    }

    private bool TryResolveProvider(
        GfxImageAsset image,
        out IGfxImagePayloadResolver resolver,
        out string source,
        out string reason)
    {
        XAssetPool pool = _workspace.LoadedZone.Context.AssetPool;
        if (image.RuntimeAddress?.AssetPoolAddress is not { } address ||
            address.AssetType != XAssetType.Image ||
            !pool.TryGetSlot(address, out XAssetSlot? slot) ||
            slot is null ||
            slot.AssetType != XAssetType.Image ||
            slot.ActiveProvider.IsReferencePlaceholder ||
            slot.ActiveProvider.Owner.IsNone)
        {
            resolver = UnavailableGfxImagePayloadResolver.Instance;
            source = "the embedded image payload";
            reason =
                $"image '{image.Name ?? "unnamed image"}' has no complete " +
                "active canonical provider";
            return false;
        }

        WorkspaceZone? ownerZone = _workspace.LoadedZones.FirstOrDefault(zone =>
            zone.LoadResult.Context.ZoneOwner == slot.ActiveProvider.Owner);
        if (ownerZone is null)
        {
            resolver = UnavailableGfxImagePayloadResolver.Instance;
            source = "an unavailable provider-zone image package";
            reason =
                $"image '{image.Name ?? "unnamed image"}' has no available " +
                "provider-zone image package";
            return false;
        }

        resolver = ownerZone.LoadResult.ImagePayloadResolver;
        source = $"image package for zone '{ownerZone.LogicalZoneName}'";
        reason = string.Empty;
        return true;
    }
}
