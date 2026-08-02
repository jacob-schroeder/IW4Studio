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

                BaseAsset loadedAsset;

                PatchInlineAssetPointer(asset, context);

                if (asset.Type == XAssetType.PhysPreset)
                {
                    loadedAsset = _physPresetLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.PixelShader)
                {
                    loadedAsset = _materialShaderLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        MaterialShaderKind.Pixel,
                        context);
                }
                else if (asset.Type == XAssetType.VertexShader)
                {
                    loadedAsset = _materialShaderLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        MaterialShaderKind.Vertex,
                        context);
                }
                else if (asset.Type == XAssetType.Techset)
                {
                    loadedAsset = _techsetLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Image)
                {
                    loadedAsset = _imageLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Material)
                {
                    loadedAsset = _materialLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.MenuFile)
                {
                    loadedAsset = _menuFileLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        context);
                }
                else if (asset.Type == XAssetType.Menu)
                {
                    loadedAsset = _menuFileLoader.LoadMenuFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        context);
                }
                else if (asset.Type == XAssetType.StringTable)
                {
                    loadedAsset = _stringTableLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.StructuredDataDef)
                {
                    loadedAsset = _structuredDataDefSetLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.RawFile)
                {
                    loadedAsset = _rawFileLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Localize)
                {
                    loadedAsset = _localizeLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Sound)
                {
                    loadedAsset = _soundLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.SndCurve)
                {
                    loadedAsset = _sndCurveLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.LoadedSound)
                {
                    loadedAsset = _loadedSoundLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        context);
                }
                else if (asset.Type == XAssetType.Fx)
                {
                    loadedAsset = _fxLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.ImpactFx)
                {
                    loadedAsset = _impactFxLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.XAnim)
                {
                    loadedAsset = _xanimLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.XModel)
                {
                    loadedAsset = _xmodelLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.PhysCollmap)
                {
                    loadedAsset = _physCollmapLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Font)
                {
                    loadedAsset = _fontLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Vehicle)
                {
                    loadedAsset = _vehicleLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.LightDef)
                {
                    loadedAsset = _lightDefLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.ComMap)
                {
                    loadedAsset = _comWorldLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type is XAssetType.ColMapSp or XAssetType.ColMapMp)
                {
                    loadedAsset = _clipMapLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        context,
                        asset.Type);
                }
                else if (asset.Type == XAssetType.MapEnts)
                {
                    loadedAsset = _mapEntsLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        context);
                }
                else if (asset.Type == XAssetType.AddonMapEnts)
                {
                    loadedAsset = _addonMapEntsLoader.LoadFromAssetPointer(
                        cursor,
                        asset.AssetPointer.Untyped,
                        context);
                }
                else if (asset.Type == XAssetType.FxMap)
                {
                    loadedAsset = _fxWorldLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.GfxMap)
                {
                    loadedAsset = _gfxWorldLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.GameMapMp)
                {
                    loadedAsset = _gameWorldMpLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.GameMapSp)
                {
                    loadedAsset = _gameWorldSpLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Weapon)
                {
                    loadedAsset = _weaponLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.LeaderboardDef)
                {
                    loadedAsset = _leaderboardDefLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else if (asset.Type == XAssetType.Tracer)
                {
                    loadedAsset = _tracerDefLoader.LoadFromAssetPointer(cursor, asset.AssetPointer.Untyped, context);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"XAsset type {asset.Type} is marked pointer-wrapped but has no dispatcher route.");
                }
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

    private static void PatchInlineAssetPointer(
        XAssetListEntrySnapshot asset,
        DbLoadContext context)
    {
        if (asset.AssetPointer.Type is not (PointerType.Inline or PointerType.Insert))
            return;

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(
            asset.AssetPointerCellAddress,
            asset.AssetPointer.Raw,
            alignment: 4);
        int runtimePointer = XPointerCodec.Encode(targetAddress);
    }
}
