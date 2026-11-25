using IW4.Game.Zone;

namespace IW4.Studio.Desktop.Workbench.Tools.FastFileAssets;

/// <summary>Type and count shown in the compact category navigator.</summary>
public sealed record FastFileAssetNavigatorGroup(XAssetType AssetType, int Count)
{
    public string Name => AssetType.ToString();
}

public enum FastFileAssetContentsScope
{
    ThisFastFile,
    Dependencies,
    AssetPool
}
