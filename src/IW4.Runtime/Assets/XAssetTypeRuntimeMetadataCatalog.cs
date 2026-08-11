using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Engine behavior required to load, register, and retire each serialized
/// XAsset type.
/// </summary>
public static class XAssetTypeRuntimeMetadataCatalog
{
    private static readonly XAssetTypeRuntimeMetadata[] EntryArray =
        CreateValidatedEntries();

    private static readonly IReadOnlyDictionary<XAssetType, XAssetTypeRuntimeMetadata> Entries =
        EntryArray.ToDictionary(entry => entry.SerializedType);

    public static IReadOnlyList<XAssetTypeRuntimeMetadata> All => EntryArray;

    public static XAssetTypeRuntimeMetadata Get(XAssetType assetType) =>
        Entries.TryGetValue(assetType, out XAssetTypeRuntimeMetadata? metadata)
            ? metadata
            : throw new KeyNotFoundException(
                $"No XAsset runtime behavior is registered for {assetType}.");

    public static bool TryGet(
        XAssetType assetType,
        out XAssetTypeRuntimeMetadata? metadata) =>
        Entries.TryGetValue(assetType, out metadata);

    private static XAssetTypeRuntimeMetadata[] CreateValidatedEntries()
    {
        XAssetTypeRuntimeMetadata[] entries = CreateEntries().ToArray();
        foreach (XAssetTypeRuntimeMetadata entry in entries)
        {
            bool expectedNativeNoOp =
                XAssetTypeDispatchCatalog.IsNativeNoOp(entry.SerializedType);
            if ((entry.Disposition == XAssetRuntimeDisposition.NativeNoOp) !=
                expectedNativeNoOp)
            {
                throw new InvalidDataException(
                    $"Runtime disposition for {entry.SerializedType} disagrees " +
                    "with the shared PS3 top-level dispatch catalog.");
            }
        }

        return entries;
    }

    private static IEnumerable<XAssetTypeRuntimeMetadata> CreateEntries()
    {
        yield return Canonical(XAssetType.PhysPreset, 0x2C);
        yield return Canonical(XAssetType.PhysCollmap, 0x48);
        yield return Canonical(XAssetType.XAnim, 0x58);
        yield return Canonical(XAssetType.XModelSurfs, 0x24);
        yield return Canonical(XAssetType.XModel, 0x120, hasReleaseLifecycle: true);
        yield return Canonical(XAssetType.Material, 0xA8);
        yield return Canonical(XAssetType.PixelShader, 0x18);
        yield return Canonical(XAssetType.VertexShader, 0x0C);
        yield return Canonical(XAssetType.Techset, 0x9C);
        yield return Canonical(XAssetType.Image, 0x50, hasReleaseLifecycle: true);
        yield return Canonical(XAssetType.Sound, 0x0C);
        yield return Canonical(XAssetType.SndCurve, 0x88);
        yield return Canonical(XAssetType.LoadedSound, 0x1C);
        yield return CanonicalAlias(
            XAssetType.ColMapSp,
            0x100,
            hasReleaseLifecycle: true,
            allowsFallbackPromotion: false);
        yield return Canonical(
            XAssetType.ColMapMp,
            0x100,
            hasReleaseLifecycle: true,
            allowsFallbackPromotion: false);
        yield return Canonical(
            XAssetType.ComMap,
            0x10,
            hasReleaseLifecycle: true,
            allowsFallbackPromotion: false);
        yield return Canonical(XAssetType.GameMapSp, 0x38);
        yield return Canonical(XAssetType.GameMapMp, 0x08);
        yield return Canonical(XAssetType.MapEnts, 0x2C);
        yield return Canonical(XAssetType.FxMap, 0x74);
        yield return Canonical(
            XAssetType.GfxMap,
            0x288,
            hasReleaseLifecycle: true,
            allowsFallbackPromotion: false);
        yield return Canonical(XAssetType.LightDef, 0x10);
        yield return NoOp(XAssetType.UiMap);
        yield return Canonical(XAssetType.Font, 0x18);
        yield return Canonical(XAssetType.MenuFile, 0x0C);
        yield return Canonical(XAssetType.Menu, 0x2F0);
        yield return Canonical(XAssetType.Localize, 0x08);
        yield return CanonicalWithPoolSize(XAssetType.Weapon, 0x74, 0x684);
        yield return NoOp(XAssetType.SndDriverGlobals);
        yield return Canonical(XAssetType.Fx, 0x20);
        yield return Canonical(XAssetType.ImpactFx, 0x08);
        yield return NoOp(XAssetType.AiType);
        yield return NoOp(XAssetType.MpType);
        yield return NoOp(XAssetType.Character);
        yield return NoOp(XAssetType.XModelAlias);
        yield return Canonical(XAssetType.RawFile, 0x10);
        yield return Canonical(XAssetType.StringTable, 0x10);
        yield return Canonical(XAssetType.LeaderboardDef, 0x18);
        yield return Canonical(XAssetType.StructuredDataDef, 0x0C);
        yield return Canonical(XAssetType.Tracer, 0x70);
        yield return Canonical(XAssetType.Vehicle, 0x2D0);
        yield return Canonical(XAssetType.AddonMapEnts, 0x24);
    }

    private static XAssetTypeRuntimeMetadata Canonical(
        XAssetType type,
        int rootSize,
        bool hasReleaseLifecycle = false,
        bool allowsFallbackPromotion = true) =>
        CanonicalWithPoolSize(
            type,
            rootSize,
            rootSize,
            hasReleaseLifecycle,
            allowsFallbackPromotion);

    private static XAssetTypeRuntimeMetadata CanonicalWithPoolSize(
        XAssetType type,
        int rootSize,
        int nativePoolCopySize,
        bool hasReleaseLifecycle = false,
        bool allowsFallbackPromotion = true) =>
        new(
            type,
            XAssetTypeFamilyCatalog.GetCanonicalFamily(type),
            XAssetRuntimeDisposition.Canonical,
            rootSize,
            nativePoolCopySize,
            hasReleaseLifecycle,
            allowsFallbackPromotion);

    private static XAssetTypeRuntimeMetadata CanonicalAlias(
        XAssetType serializedType,
        int rootSize,
        bool hasReleaseLifecycle = false,
        bool allowsFallbackPromotion = true) =>
        new(
            serializedType,
            XAssetTypeFamilyCatalog.GetCanonicalFamily(serializedType),
            XAssetRuntimeDisposition.CanonicalAlias,
            rootSize,
            rootSize,
            hasReleaseLifecycle,
            allowsFallbackPromotion);

    private static XAssetTypeRuntimeMetadata NoOp(XAssetType type) =>
        new(
            type,
            XAssetTypeFamilyCatalog.GetCanonicalFamily(type),
            XAssetRuntimeDisposition.NativeNoOp,
            0,
            0,
            HasReleaseLifecycle: false,
            AllowsFallbackPromotion: false);
}
