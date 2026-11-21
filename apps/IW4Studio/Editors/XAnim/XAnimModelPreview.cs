using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Render;
using IW4.Render.EditorPreview;
using IW4.Render.SceneBuilding;
using IW4.Runtime.Assets;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

internal static class XAnimModelPreview
{
    internal static bool TryResolveModel(
        FastFileWorkspace workspace,
        string name,
        out XModelAsset? resolved,
        out string? sourceDetail)
    {
        XAssetPool pool = workspace.LoadedZone.Context.AssetPool;
        resolved = null;
        sourceDetail = null;
        if (!pool.TryResolve(XAssetType.XModel, name, out XModelAsset? current) ||
            current?.RuntimeAddress?.AssetPoolAddress is not { } address ||
            !pool.TryGetSlot(address, out XAssetSlot? slot) || slot is null ||
            slot.ActiveProvider.IsReferencePlaceholder)
            return false;
        resolved = current;
        WorkspaceZone? provider = workspace.LoadedZones.FirstOrDefault(zone =>
            zone.LoadResult.Context.ZoneOwner == slot.ActiveProvider.Owner);
        sourceDetail = provider is null
            ? "Resolved workspace provider"
            : $"Provider zone: {provider.LogicalZoneName}";
        return true;
    }

    internal static (XAnimPreviewScene? PoseScene, XModelRenderScene? ModelScene, string? Reason)
        Build(
            FastFileWorkspace workspace,
            XAnimPlaybackClip clip,
            IReadOnlyList<XAnimPreviewModelComponent> components,
            IReadOnlyList<BaseAsset> stagedAssets)
    {
        XAssetPool pool = workspace.LoadedZone.Context.AssetPool;
        long revision = pool.Revision;
        const string poolChanged = "The asset pool changed while building the animation preview.";
        XAnimPreviewScene? poseScene = null;
        try
        {
            if (!XAnimPreviewScene.TryCreate(clip, components,
                    out poseScene, out string reason) ||
                poseScene is null)
                return (null, null, reason);
            XModelRenderScene modelScene = new XModelSceneBuilder().BuildComposite(
                components,
                0,
                WorkspaceRenderAssetSource.Create(workspace, "XAnim preview material assets"),
                new WorkspaceGfxImagePayloadResolver(workspace),
                stagedAssets);
            if (pool.Revision != revision)
                return (null, null, poolChanged);
            return (poseScene, modelScene, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                   InvalidDataException or ArgumentException or OverflowException)
        {
            return pool.Revision == revision
                ? (poseScene, null, exception.Message)
                : (null, null, poolChanged);
        }
    }
}
