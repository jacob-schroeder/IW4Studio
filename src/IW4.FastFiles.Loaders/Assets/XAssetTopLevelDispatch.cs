using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Assets;

/// <summary>
/// Classifies the serialized top-level XAsset header so the row reader and
/// dispatcher apply the same pointer handling.
/// </summary>
public static class XAssetTopLevelDispatch
{
    private static readonly HashSet<XAssetType> PointerWrappedTypes =
    [
        XAssetType.PhysPreset,
        XAssetType.PixelShader,
        XAssetType.VertexShader,
        XAssetType.Techset,
        XAssetType.Image,
        XAssetType.Material,
        XAssetType.MenuFile,
        XAssetType.Menu,
        XAssetType.StringTable,
        XAssetType.StructuredDataDef,
        XAssetType.RawFile,
        XAssetType.Localize,
        XAssetType.Sound,
        XAssetType.SndCurve,
        XAssetType.LoadedSound,
        XAssetType.Fx,
        XAssetType.ImpactFx,
        XAssetType.XAnim,
        XAssetType.XModelSurfs,
        XAssetType.XModel,
        XAssetType.PhysCollmap,
        XAssetType.Font,
        XAssetType.Vehicle,
        XAssetType.LightDef,
        XAssetType.ColMapSp,
        XAssetType.ColMapMp,
        XAssetType.MapEnts,
        XAssetType.ComMap,
        XAssetType.FxMap,
        XAssetType.GfxMap,
        XAssetType.GameMapSp,
        XAssetType.GameMapMp,
        XAssetType.AddonMapEnts,
        XAssetType.Weapon,
        XAssetType.LeaderboardDef,
        XAssetType.Tracer
    ];

    public static XAssetTopLevelDispatchKind Classify(XAssetType assetType)
    {
        // Native no-op types preserve their opaque XAssetHeader words without
        // pointer conversion, body loading, or canonical registration.
        if (XAssetTypeDispatchCatalog.IsNativeNoOp(assetType))
        {
            return XAssetTopLevelDispatchKind.NativeNoOp;
        }

        return PointerWrappedTypes.Contains(assetType)
            ? XAssetTopLevelDispatchKind.PointerWrapper
            : XAssetTopLevelDispatchKind.Unsupported;
    }

}
