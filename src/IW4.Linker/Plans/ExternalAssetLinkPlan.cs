using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.ImpactFx;
using IW4.Assets.Assets.Leaderboard;
using IW4.Assets.Assets.LightDef;
using IW4.Assets.Assets.Localize;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.StructuredData;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.Tracer;
using IW4.Assets.Assets.Vehicle;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.XAnim;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Stock PS3 comma-reference body. Provider-backed external rows are zeroed
/// TEMP roots with one inline name XString in their schema-proven cell.
/// </summary>
internal sealed class ExternalAssetLinkPlan : AssetLinkPlan
{
    private ExternalAssetLinkPlan(
        AssetKey key,
        string originalSerializedName,
        int rootSize,
        int namePointerOffset,
        LinkStorageSymbol nameStorage)
        : base(
            key,
            originalSerializedName,
            nameStorage,
            requireReferencePlaceholder: true)
    {
        if (rootSize < sizeof(int) ||
            namePointerOffset < 0 ||
            namePointerOffset > rootSize - sizeof(int) ||
            namePointerOffset % sizeof(int) != 0)
        {
            throw new InvalidDataException(
                "External XAsset reference shape has an invalid name cell.");
        }

        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            new byte[rootSize],
            alignment: 4,
            root => [NameOperation(root, namePointerOffset)]);
    }

    public static ExternalAssetLinkPlan Create(
        AssetKey key,
        XAssetType serializedType,
        string originalSerializedName,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(freeze);
        (int rootSize, int namePointerOffset) = GetShape(serializedType);
        return new ExternalAssetLinkPlan(
            key,
            originalSerializedName,
            rootSize,
            namePointerOffset,
            freeze.FreezeProviderName(
                originalSerializedName,
                namePointerOffset,
                "Asset.Name"));
    }

    public static ExternalAssetLinkPlan CreateSynthetic(
        AssetKey key,
        XAssetType serializedType,
        string originalSerializedName)
    {
        (int rootSize, int namePointerOffset) = GetShape(serializedType);
        return new ExternalAssetLinkPlan(
            key,
            originalSerializedName,
            rootSize,
            namePointerOffset,
            LinkStorageSymbol.CString(originalSerializedName, "Asset.Name"));
    }

    internal override LinkStorageSymbol Root { get; }

    private static (int RootSize, int NamePointerOffset) GetShape(
        XAssetType serializedType) =>
        serializedType switch
        {
            XAssetType.PhysPreset => (PhysPresetAsset.SerializedSize, 0),
            XAssetType.PhysCollmap =>
                (IW4.Assets.Assets.Physics.PhysCollmapAsset.SerializedSize, 0),
            XAssetType.XAnim => (XAnimPartsAsset.SerializedSize, 0),
            XAssetType.XModelSurfs => (XModelSurfsAsset.SerializedSize, 0),
            XAssetType.XModel => (XModelAsset.SerializedSize, 0),
            XAssetType.Material => (MaterialAsset.SerializedSize, 0),
            XAssetType.PixelShader => (MaterialShaderAsset.PixelShaderSerializedSize, 0),
            XAssetType.VertexShader => (MaterialShaderAsset.VertexShaderSerializedSize, 0),
            XAssetType.Techset => (MaterialTechniqueSetAsset.SerializedSize, 0),
            XAssetType.Image => (GfxImageAsset.SerializedSize, 0x4c),
            XAssetType.Sound => (SoundAliasListAsset.SerializedSize, 0),
            XAssetType.SndCurve => (SndCurve.SerializedSize, 0),
            XAssetType.LoadedSound => (LoadedSound.SerializedSize, 0),
            XAssetType.ColMapSp or XAssetType.ColMapMp =>
                (ClipMapAsset.SerializedSize, 0),
            XAssetType.ComMap => (ComWorldAsset.SerializedSize, 0),
            XAssetType.GameMapSp => (GameWorldSpAsset.SerializedSize, 0),
            XAssetType.GameMapMp => (GameWorldMpAsset.SerializedSize, 0),
            XAssetType.MapEnts => (MapEntsAsset.SerializedSize, 0),
            XAssetType.FxMap =>
                (IW4.Assets.Assets.FxMap.FxWorldAsset.SerializedSize, 0),
            XAssetType.GfxMap =>
                (IW4.Assets.Assets.GfxMap.GfxWorldAsset.SerializedSize, 0),
            XAssetType.LightDef => (LightDefAsset.SerializedSize, 0),
            XAssetType.Font => (FontAsset.SerializedSize, 0),
            XAssetType.MenuFile => (MenuFileAsset.SerializedSize, 0),
            XAssetType.Menu => (MenuDefAsset.SerializedSize, 0),
            XAssetType.Localize => (LocalizeAsset.SerializedSize, sizeof(int)),
            XAssetType.Weapon => (WeaponAsset.SerializedSize, 0),
            XAssetType.Fx => (FxEffectDefAsset.SerializedSize, 0),
            XAssetType.ImpactFx => (FxImpactTableAsset.SerializedSize, 0),
            XAssetType.RawFile => (RawFileAsset.SerializedSize, 0),
            XAssetType.StringTable => (StringTableAsset.SerializedSize, 0),
            XAssetType.LeaderboardDef => (LeaderboardDefAsset.SerializedSize, 0),
            XAssetType.StructuredDataDef =>
                (StructuredDataDefSetAsset.SerializedSize, 0),
            XAssetType.Tracer => (TracerDefAsset.SerializedSize, 0),
            XAssetType.Vehicle => (VehicleDefAsset.SerializedSize, 0),
            XAssetType.AddonMapEnts => (AddonMapEntsAsset.SerializedSize, 0),
            _ => throw new InvalidDataException(
                $"{serializedType} has no provider-backed stock external-reference shape.")
        };
}
