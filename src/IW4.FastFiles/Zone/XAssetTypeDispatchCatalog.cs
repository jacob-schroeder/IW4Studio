namespace IW4.FastFiles.Zone;

/// <summary>
/// Native PS3 top-level dispatch facts shared by loading and canonical
/// linking. Native no-op rows preserve their opaque XAssetHeader word and do
/// not materialize or register a provider body.
/// </summary>
public static class XAssetTypeDispatchCatalog
{
    public static bool IsNativeNoOp(XAssetType serializedType)
    {
        if (!Enum.IsDefined(serializedType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedType),
                serializedType,
                "XAsset type must be defined.");
        }

        return serializedType is
            XAssetType.UiMap or
            XAssetType.SndDriverGlobals or
            XAssetType.AiType or
            XAssetType.MpType or
            XAssetType.Character or
            XAssetType.XModelAlias;
    }
}
