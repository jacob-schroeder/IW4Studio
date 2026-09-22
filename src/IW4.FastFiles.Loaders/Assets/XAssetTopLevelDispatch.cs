using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Assets;

/// <summary>
/// Classifies the serialized top-level XAsset header so the row reader and
/// dispatcher apply the same pointer handling.
/// </summary>
public static class XAssetTopLevelDispatch
{
    public static XAssetTopLevelDispatchKind Classify(XAssetType assetType)
    {
        // Native no-op types preserve their opaque XAssetHeader words without
        // pointer conversion, body loading, or canonical registration.
        if (XAssetTypeDispatchCatalog.IsNativeNoOp(assetType))
        {
            return XAssetTopLevelDispatchKind.NativeNoOp;
        }

        return XAssetDispatcher.HasPointerWrappedRoute(assetType)
            ? XAssetTopLevelDispatchKind.PointerWrapper
            : XAssetTopLevelDispatchKind.Unsupported;
    }

}
