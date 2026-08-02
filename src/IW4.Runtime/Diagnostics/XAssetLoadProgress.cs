using IW4.FastFiles.Zone;

namespace IW4.Runtime.Diagnostics;

public readonly record struct XAssetLoadProgress(
    string SourceName,
    int AssetNumber,
    int AssetCount,
    XAssetType AssetType);
