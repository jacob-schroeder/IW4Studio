using IW4.Assets.Assets;
using IW4.Runtime.Database;

namespace IW4.Runtime.Database;

public sealed record XAssetLoadResult(
    int Index,
    BaseAsset? Asset,
    XAssetRowMaterialization Materialization);
