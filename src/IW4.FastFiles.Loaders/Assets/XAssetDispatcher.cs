using System.Collections.Frozen;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.ComWorld;
using IW4.FastFiles.Loaders.Assets.ColMap;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.Menu;
using IW4.FastFiles.Loaders.Assets.Font;
using IW4.FastFiles.Loaders.Assets.Fx;
using IW4.FastFiles.Loaders.Assets.FxMap;
using IW4.FastFiles.Loaders.Assets.GameMap;
using IW4.FastFiles.Loaders.Assets.GfxMap;
using IW4.FastFiles.Loaders.Assets.ImpactFx;
using IW4.FastFiles.Loaders.Assets.Image;
using IW4.FastFiles.Loaders.Assets.Leaderboard;
using IW4.FastFiles.Loaders.Assets.LightDef;
using IW4.FastFiles.Loaders.Assets.Localize;
using IW4.FastFiles.Loaders.Assets.MapEnts;
using IW4.FastFiles.Loaders.Assets.Physics;
using IW4.FastFiles.Loaders.Assets.RawFile;
using IW4.FastFiles.Loaders.Assets.Sound;
using IW4.FastFiles.Loaders.Assets.StringTable;
using IW4.FastFiles.Loaders.Assets.StructuredData;
using IW4.FastFiles.Loaders.Assets.TechniqueSet;
using IW4.FastFiles.Loaders.Assets.Tracer;
using IW4.FastFiles.Loaders.Assets.Vehicle;
using IW4.FastFiles.Loaders.Assets.Weapon;
using IW4.FastFiles.Loaders.Assets.XAnim;
using IW4.FastFiles.Loaders.Assets.XModel;
using IW4.Assets.Assets;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets;

public sealed class XAssetDispatcher
{
    private delegate BaseAsset PointerWrappedRoute(
        XAssetDispatcher dispatcher,
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadContext context);

    private readonly MenuFileLoader _menuFileLoader = new();
    private readonly MaterialLoader _materialLoader = new();
    private readonly MaterialShaderLoader _materialShaderLoader = new();
    private readonly FontLoader _fontLoader = new();
    private readonly MaterialTechniqueSetLoader _techsetLoader = new();
    private readonly GfxImageLoader _imageLoader = new();
    private readonly StringTableLoader _stringTableLoader = new();
    private readonly StructuredDataDefSetLoader _structuredDataDefSetLoader = new();
    private readonly RawFileLoader _rawFileLoader = new();
    private readonly LocalizeLoader _localizeLoader = new();
    private readonly WeaponLoader _weaponLoader = new();
    private readonly SoundAliasListLoader _soundLoader = new();
    private readonly LoadedSoundLoader _loadedSoundLoader = new();
    private readonly FxEffectDefLoader _fxLoader = new();
    private readonly FxImpactTableLoader _impactFxLoader = new();
    private readonly XAnimPartsLoader _xanimLoader = new();
    private readonly XModelLoader _xmodelLoader = new();
    private readonly PhysPresetLoader _physPresetLoader = new();
    private readonly PhysCollmapLoader _physCollmapLoader = new();
    private readonly SndCurveLoader _sndCurveLoader = new();
    private readonly VehicleDefLoader _vehicleLoader = new();
    private readonly LightDefLoader _lightDefLoader = new();
    private readonly ComWorldLoader _comWorldLoader = new();
    private readonly ClipMapLoader _clipMapLoader = new();
    private readonly MapEntsLoader _mapEntsLoader = new();
    private readonly AddonMapEntsLoader _addonMapEntsLoader = new();
    private readonly FxWorldLoader _fxWorldLoader = new();
    private readonly GfxWorldLoader _gfxWorldLoader = new();
    private readonly GameWorldSpLoader _gameWorldSpLoader = new();
    private readonly GameWorldMpLoader _gameWorldMpLoader = new();
    private readonly LeaderboardDefLoader _leaderboardDefLoader = new();
    private readonly TracerDefLoader _tracerDefLoader = new();

    private static readonly FrozenDictionary<XAssetType, PointerWrappedRoute> PointerWrappedRoutes =
        new Dictionary<XAssetType, PointerWrappedRoute>
        {
            [XAssetType.PhysPreset] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._physPresetLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.PixelShader] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._materialShaderLoader.LoadFromAssetPointer(
                    cursor,
                    pointer,
                    MaterialShaderKind.Pixel,
                    context),
            [XAssetType.VertexShader] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._materialShaderLoader.LoadFromAssetPointer(
                    cursor,
                    pointer,
                    MaterialShaderKind.Vertex,
                    context),
            [XAssetType.Techset] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._techsetLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Image] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._imageLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Material] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._materialLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.MenuFile] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._menuFileLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Menu] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._menuFileLoader.LoadMenuFromAssetPointer(cursor, pointer, context),
            [XAssetType.StringTable] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._stringTableLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.StructuredDataDef] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._structuredDataDefSetLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.RawFile] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._rawFileLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Localize] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._localizeLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Sound] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._soundLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.SndCurve] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._sndCurveLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.LoadedSound] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._loadedSoundLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Fx] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._fxLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.ImpactFx] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._impactFxLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.XAnim] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._xanimLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.XModelSurfs] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._xmodelLoader.LoadXModelSurfsFromAssetPointer(cursor, pointer, context),
            [XAssetType.XModel] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._xmodelLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.PhysCollmap] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._physCollmapLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Font] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._fontLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Vehicle] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._vehicleLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.LightDef] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._lightDefLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.ComMap] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._comWorldLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.ColMapSp] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._clipMapLoader.LoadFromAssetPointer(
                    cursor,
                    pointer,
                    context,
                    XAssetType.ColMapSp),
            [XAssetType.ColMapMp] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._clipMapLoader.LoadFromAssetPointer(
                    cursor,
                    pointer,
                    context,
                    XAssetType.ColMapMp),
            [XAssetType.MapEnts] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._mapEntsLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.AddonMapEnts] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._addonMapEntsLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.FxMap] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._fxWorldLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.GfxMap] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._gfxWorldLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.GameMapMp] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._gameWorldMpLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.GameMapSp] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._gameWorldSpLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Weapon] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._weaponLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.LeaderboardDef] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._leaderboardDefLoader.LoadFromAssetPointer(cursor, pointer, context),
            [XAssetType.Tracer] = static (dispatcher, cursor, pointer, context) =>
                dispatcher._tracerDefLoader.LoadFromAssetPointer(cursor, pointer, context)
        }.ToFrozenDictionary();

    internal static bool HasPointerWrappedRoute(XAssetType assetType) =>
        PointerWrappedRoutes.ContainsKey(assetType);

    public IReadOnlyList<XAssetLoadResult> LoadAll(
        FastFileCursor cursor,
        XAssetListSnapshot assetList,
        DbLoadContext context)
    {
        var results = new List<XAssetLoadResult>(assetList.AssetCount);

        foreach (XAssetListEntrySnapshot asset in assetList.Assets)
        {
            context.AssetProgress?.Invoke(new(
                context.CurrentFastFile.Name,
                asset.Index + 1,
                assetList.AssetCount,
                asset.Type));

            int sourceOffset = cursor.Offset;
            XAssetRowMaterializationScope materializationScope =
                context.BeginAssetRowMaterialization(asset, sourceOffset);
            try
            {
                XAssetTopLevelDispatchKind dispatchKind =
                    XAssetTopLevelDispatch.Classify(asset.Type);

                if (dispatchKind == XAssetTopLevelDispatchKind.NativeNoOp)
                {
                    if (!asset.IsOpaqueHeader)
                    {
                        throw new InvalidDataException(
                            $"XAsset[{asset.Index}] {asset.Type} must preserve its native opaque header classification.");
                    }

                    // Native no-op types have no dispatch case. The copied
                    // header remains unchanged, and no body bytes or
                    // DB_AddXAsset path are used.
                    results.Add(new XAssetLoadResult(
                        asset.Index,
                        null,
                        materializationScope.Complete(cursor.Offset)));
                    continue;
                }

                if (asset.AssetPointer.Type == PointerType.Null)
                {
                    results.Add(new XAssetLoadResult(
                        asset.Index,
                        null,
                        materializationScope.Complete(cursor.Offset)));
                    continue;
                }

                if (dispatchKind == XAssetTopLevelDispatchKind.Unsupported)
                {
                    materializationScope.Discard(cursor.Offset, unsupported: true);
                    throw new NotSupportedException(
                        $"XAsset[{asset.Index}] has unsupported type {asset.Type}; " +
                        "an incomplete XZone cannot be registered.");
                }

                BaseAsset loadedAsset = PointerWrappedRoutes[asset.Type](
                    this,
                    cursor,
                    asset.AssetPointer.Untyped,
                    context);
                results.Add(new XAssetLoadResult(
                    asset.Index,
                    loadedAsset,
                    materializationScope.Complete(cursor.Offset)));
            }
            finally
            {
                if (!materializationScope.IsClosed)
                    materializationScope.Discard(cursor.Offset);
                context.EndAssetRowMaterialization(materializationScope);
            }
        }

        return results;
    }

}
