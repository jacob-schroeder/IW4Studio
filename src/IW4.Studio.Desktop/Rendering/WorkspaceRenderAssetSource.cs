using IW4.FastFiles.Loaders.Database;
using IW4.Render.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Rendering;

internal static class WorkspaceRenderAssetSource
{
    internal static RenderAssetSource Create(
        FastFileWorkspace workspace,
        string consumerDescription)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerDescription);

        WorkspaceZone targetZone = workspace.LoadedZones.Single(zone =>
            zone.IsTarget);
        if (!targetZone.IsActive)
        {
            throw new InvalidOperationException(
                $"The target fastfile is inactive and cannot supply {consumerDescription}.");
        }

        LoadedXZone target = targetZone.LoadResult;
        return new RenderAssetSource(
            target.Context.Blocks,
            target.Context.AssetPool,
            target.Context.GfxImagesByAddress,
            target.LoadedAssets,
            target.XAssetList.Assets);
    }
}
