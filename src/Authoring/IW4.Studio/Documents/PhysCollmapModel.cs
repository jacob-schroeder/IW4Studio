using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

public sealed partial class AssetEditorSession
{
    public XModelAsset? FindPhysCollmapPreviewModel()
    {
        ThrowIfClosed();
        if (Definition is not PhysCollmapAsset collision) return null;
        var targets = _session.CaptureCurrentTargetAssets([XAssetType.XModel]).Definitions
            .Select(row => row.Definition).OfType<XModelAsset>();
        var otherModels = Workspace.AssetCatalog.DependencyEntries
            .Select(row => row.Definition).OfType<XModelAsset>();
        return targets.Concat(otherModels)
            .Where(model => ReferenceEquals(model.PhysCollmap, collision) ||
                (collision.RuntimeAddress?.AssetPoolAddress is { } address &&
                 model.PhysCollmap?.RuntimeAddress?.AssetPoolAddress == address))
            .OrderByDescending(model => string.Equals(model.Name, collision.Name, StringComparison.Ordinal))
            .ThenBy(model => model.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
