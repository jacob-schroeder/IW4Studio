using IW4.Assets.Assets;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.GfxMap;
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
using IW4.Assets.Assets.XAnim;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Linker.Plans;
using WeaponAssetDefinition = IW4.Assets.Assets.Weapon.WeaponAsset;

namespace IW4.Linker.Contracts;

/// <summary>
/// One immutable provider input. Construction freezes the supported semantic
/// asset data and deliberately discards loader and runtime pointer state.
/// </summary>
public sealed class LinkAssetProvider
{
    internal LinkAssetProvider(
        LinkAssetProviderSource source,
        LinkAssetFreezeContext freezeContext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(freezeContext);
        BaseAsset definition = source.Definition;
        ArgumentNullException.ThrowIfNull(definition);
        LinkAssetFreezeScope freeze = freezeContext.Bind(
            source.ImportedDefinition,
            source.ImportResolver,
            source.Disposition);
        XAssetType serializedType = definition.SerializedAssetType;
        if (!Enum.IsDefined(serializedType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                serializedType,
                "Provider serialized type must be a defined XAssetType.");
        }

        AssetKey key = AssetKey.FromDefinition(definition);
        string serializedName = definition.SerializedAssetName ??
            throw new ArgumentException(
                "Provider definition has no serialized name.",
                nameof(definition));
        if (serializedType != XAssetType.Image && source.ImageStreamReferences.Count != 0)
        {
            throw new ArgumentException(
                $"{serializedType} providers cannot carry GfxImage stream references.",
                nameof(source));
        }

        Plan = (serializedType, definition) switch
        {
            (XAssetType.RawFile, RawFileAsset rawFile) =>
                RawFileLinkPlan.Freeze(key, serializedName, rawFile, freeze),
            (XAssetType.LightDef, LightDefAsset lightDef) =>
                LightDefLinkPlan.Freeze(key, serializedName, lightDef, freeze),
            (XAssetType.Image, GfxImageAsset image) =>
                GfxImageLinkPlan.Freeze(
                    key,
                    serializedName,
                    image,
                    source.ImageStreamReferences,
                    freeze),
            (XAssetType.Localize, LocalizeAsset localize) =>
                LocalizeLinkPlan.Freeze(key, serializedName, localize, freeze),
            (XAssetType.StringTable, StringTableAsset stringTable) =>
                StringTableLinkPlan.Freeze(key, serializedName, stringTable, freeze),
            (XAssetType.LoadedSound, LoadedSound loadedSound) =>
                LoadedSoundLinkPlan.Freeze(key, serializedName, loadedSound, freeze),
            (XAssetType.Sound, SoundAliasListAsset sound) =>
                SoundLinkPlan.Freeze(key, serializedName, sound, freeze),
            (XAssetType.SndCurve, SndCurve sndCurve) =>
                SndCurveLinkPlan.Freeze(key, serializedName, sndCurve, freeze),
            (XAssetType.PixelShader, MaterialShaderAsset pixelShader) =>
                MaterialShaderLinkPlan.Freeze(key, serializedName, pixelShader, freeze),
            (XAssetType.VertexShader, MaterialShaderAsset vertexShader) =>
                MaterialShaderLinkPlan.Freeze(key, serializedName, vertexShader, freeze),
            (XAssetType.Techset, MaterialTechniqueSetAsset techniqueSet) =>
                MaterialTechniqueSetLinkPlan.Freeze(
                    key,
                    serializedName,
                    techniqueSet,
                    freeze),
            (XAssetType.Material, MaterialAsset material) =>
                MaterialLinkPlan.Freeze(key, serializedName, material, freeze),
            (XAssetType.Tracer, TracerDefAsset tracer) =>
                TracerLinkPlan.Freeze(key, serializedName, tracer, freeze),
            (XAssetType.PhysPreset, PhysPresetAsset physPreset) =>
                PhysPresetLinkPlan.Freeze(key, serializedName, physPreset, freeze),
            (XAssetType.PhysCollmap, PhysCollmapAsset physCollmap) =>
                PhysCollmapLinkPlan.Freeze(key, serializedName, physCollmap, freeze),
            (XAssetType.ColMapSp, ClipMapAsset clipMapSp) =>
                ClipMapLinkPlan.Freeze(key, serializedName, clipMapSp, freeze),
            (XAssetType.ColMapMp, ClipMapAsset clipMapMp) =>
                ClipMapLinkPlan.Freeze(key, serializedName, clipMapMp, freeze),
            (XAssetType.XAnim, XAnimPartsAsset xanim) =>
                XAnimLinkPlan.Freeze(key, serializedName, xanim, freeze),
            (XAssetType.XModelSurfs, XModelSurfsAsset modelSurfs) =>
                XModelSurfsLinkPlan.Freeze(key, serializedName, modelSurfs, freeze),
            (XAssetType.XModel, XModelAsset model) =>
                XModelLinkPlan.Freeze(key, serializedName, model, freeze),
            (XAssetType.Fx, FxEffectDefAsset effect) =>
                FxEffectDefLinkPlan.Freeze(key, serializedName, effect, freeze),
            (XAssetType.ImpactFx, FxImpactTableAsset impactFx) =>
                FxImpactTableLinkPlan.Freeze(key, serializedName, impactFx, freeze),
            (XAssetType.FxMap, FxWorldAsset fxWorld) =>
                FxWorldLinkPlan.Freeze(key, serializedName, fxWorld, freeze),
            (XAssetType.ComMap, ComWorldAsset comWorld) =>
                ComWorldLinkPlan.Freeze(key, serializedName, comWorld, freeze),
            (XAssetType.GameMapMp, GameWorldMpAsset gameWorldMp) =>
                GameWorldMpLinkPlan.Freeze(key, serializedName, gameWorldMp, freeze),
            (XAssetType.GameMapSp, GameWorldSpAsset gameWorldSp) =>
                GameWorldSpLinkPlan.Freeze(key, serializedName, gameWorldSp, freeze),
            (XAssetType.MapEnts, MapEntsAsset mapEnts) =>
                MapEntsLinkPlan.Freeze(key, serializedName, mapEnts, freeze),
            (XAssetType.AddonMapEnts, AddonMapEntsAsset addonMapEnts) =>
                AddonMapEntsLinkPlan.Freeze(key, serializedName, addonMapEnts, freeze),
            (XAssetType.Font, FontAsset font) =>
                FontLinkPlan.Freeze(key, serializedName, font, freeze),
            (XAssetType.MenuFile, MenuFileAsset menuFile) =>
                MenuFileLinkPlan.Freeze(key, serializedName, menuFile, freeze),
            (XAssetType.Menu, MenuDefAsset menu) =>
                MenuLinkPlan.Freeze(key, serializedName, menu, freeze),
            (XAssetType.GfxMap, GfxWorldAsset gfxWorld) =>
                GfxWorldLinkPlan.Freeze(key, serializedName, gfxWorld, freeze),
            (XAssetType.Vehicle, VehicleDefAsset vehicle) =>
                VehicleLinkPlan.Freeze(key, serializedName, vehicle, freeze),
            (XAssetType.Weapon, WeaponAssetDefinition weapon) =>
                WeaponLinkPlan.Freeze(key, serializedName, weapon, freeze),
            (XAssetType.LeaderboardDef, LeaderboardDefAsset leaderboard) =>
                LeaderboardLinkPlan.Freeze(key, serializedName, leaderboard, freeze),
            (XAssetType.StructuredDataDef, StructuredDataDefSetAsset structuredData) =>
                StructuredDataLinkPlan.Freeze(key, serializedName, structuredData, freeze),
            (XAssetType.RawFile, _) => throw new ArgumentException(
                "A RawFile provider requires a RawFileAsset definition.",
                nameof(definition)),
            (XAssetType.LightDef, _) => throw new ArgumentException(
                "A LightDef provider requires a LightDefAsset definition.",
                nameof(definition)),
            (XAssetType.Image, _) => throw new ArgumentException(
                "An Image provider requires a GfxImageAsset definition.",
                nameof(definition)),
            (XAssetType.Localize, _) => throw new ArgumentException(
                "A Localize provider requires a LocalizeAsset definition.",
                nameof(definition)),
            (XAssetType.StringTable, _) => throw new ArgumentException(
                "A StringTable provider requires a StringTableAsset definition.",
                nameof(definition)),
            (XAssetType.LoadedSound, _) => throw new ArgumentException(
                "A LoadedSound provider requires a LoadedSound definition.",
                nameof(definition)),
            (XAssetType.Sound, _) => throw new ArgumentException(
                "A Sound provider requires a SoundAliasListAsset definition.",
                nameof(definition)),
            (XAssetType.SndCurve, _) => throw new ArgumentException(
                "A SndCurve provider requires a SndCurve definition.",
                nameof(definition)),
            (XAssetType.PixelShader, _) => throw new ArgumentException(
                "A PixelShader provider requires a MaterialShaderAsset definition.",
                nameof(definition)),
            (XAssetType.VertexShader, _) => throw new ArgumentException(
                "A VertexShader provider requires a MaterialShaderAsset definition.",
                nameof(definition)),
            (XAssetType.Techset, _) => throw new ArgumentException(
                "A Techset provider requires a MaterialTechniqueSetAsset definition.",
                nameof(definition)),
            (XAssetType.Material, _) => throw new ArgumentException(
                "A Material provider requires a MaterialAsset definition.",
                nameof(definition)),
            (XAssetType.Tracer, _) => throw new ArgumentException(
                "A Tracer provider requires a TracerDefAsset definition.",
                nameof(definition)),
            (XAssetType.PhysPreset, _) => throw new ArgumentException(
                "A PhysPreset provider requires a PhysPresetAsset definition.",
                nameof(definition)),
            (XAssetType.PhysCollmap, _) => throw new ArgumentException(
                "A PhysCollmap provider requires a PhysCollmapAsset definition.",
                nameof(definition)),
            (XAssetType.ColMapSp, _) => throw new ArgumentException(
                "A ColMapSp provider requires a ClipMapAsset definition.",
                nameof(definition)),
            (XAssetType.ColMapMp, _) => throw new ArgumentException(
                "A ColMapMp provider requires a ClipMapAsset definition.",
                nameof(definition)),
            (XAssetType.XAnim, _) => throw new ArgumentException(
                "An XAnim provider requires an XAnimPartsAsset definition.",
                nameof(definition)),
            (XAssetType.XModelSurfs, _) => throw new ArgumentException(
                "An XModelSurfs provider requires an XModelSurfsAsset definition.",
                nameof(definition)),
            (XAssetType.XModel, _) => throw new ArgumentException(
                "An XModel provider requires an XModelAsset definition.",
                nameof(definition)),
            (XAssetType.Fx, _) => throw new ArgumentException(
                "An Fx provider requires an FxEffectDefAsset definition.",
                nameof(definition)),
            (XAssetType.ImpactFx, _) => throw new ArgumentException(
                "An ImpactFx provider requires an FxImpactTableAsset definition.",
                nameof(definition)),
            (XAssetType.FxMap, _) => throw new ArgumentException(
                "An FxMap provider requires an FxWorldAsset definition.",
                nameof(definition)),
            (XAssetType.ComMap, _) => throw new ArgumentException(
                "A ComMap provider requires a ComWorldAsset definition.",
                nameof(definition)),
            (XAssetType.GameMapMp, _) => throw new ArgumentException(
                "A GameMapMp provider requires a GameWorldMpAsset definition.",
                nameof(definition)),
            (XAssetType.GameMapSp, _) => throw new ArgumentException(
                "A GameMapSp provider requires a GameWorldSpAsset definition.",
                nameof(definition)),
            (XAssetType.MapEnts, _) => throw new ArgumentException(
                "A MapEnts provider requires a MapEntsAsset definition.",
                nameof(definition)),
            (XAssetType.AddonMapEnts, _) => throw new ArgumentException(
                "An AddonMapEnts provider requires an AddonMapEntsAsset definition.",
                nameof(definition)),
            (XAssetType.Font, _) => throw new ArgumentException(
                "A Font provider requires a FontAsset definition.",
                nameof(definition)),
            (XAssetType.MenuFile, _) => throw new ArgumentException(
                "A MenuFile provider requires a MenuFileAsset definition.",
                nameof(definition)),
            (XAssetType.Menu, _) => throw new ArgumentException(
                "A Menu provider requires a MenuDefAsset definition.",
                nameof(definition)),
            (XAssetType.GfxMap, _) => throw new ArgumentException(
                "A GfxMap provider requires a GfxWorldAsset definition.",
                nameof(definition)),
            (XAssetType.Vehicle, _) => throw new ArgumentException(
                "A Vehicle provider requires a VehicleDefAsset definition.",
                nameof(definition)),
            (XAssetType.Weapon, _) => throw new ArgumentException(
                "A Weapon provider requires a WeaponAsset definition.",
                nameof(definition)),
            (XAssetType.LeaderboardDef, _) => throw new ArgumentException(
                "A LeaderboardDef provider requires a LeaderboardDefAsset definition.",
                nameof(definition)),
            (XAssetType.StructuredDataDef, _) => throw new ArgumentException(
                "A StructuredDataDef provider requires a StructuredDataDefSetAsset definition.",
                nameof(definition)),
            _ => throw new NotSupportedException(
                $"Canonical linking does not yet support {serializedType} providers.")
        };

        Key = key;
        SerializedType = serializedType;
        OriginalSerializedName = Plan.OriginalSerializedName;
        IsReferencePlaceholder = Plan.IsReferencePlaceholder;
    }

    public AssetKey Key { get; }
    public XAssetType SerializedType { get; }
    public string OriginalSerializedName { get; }
    public bool IsReferencePlaceholder { get; }

    internal AssetLinkPlan Plan { get; }
}

/// <summary>
/// Immutable provider occurrences in highest-precedence-first order.
/// Duplicate logical keys remain distinct provider inputs.
/// </summary>
public sealed class LinkAssetPool
{
    private readonly IReadOnlyList<LinkAssetProvider> _providers;
    private readonly LinkAssetFrozenIdentityCatalog _identityCatalog;

    public LinkAssetPool(IEnumerable<LinkAssetProviderSource> providers)
    {
        (LinkAssetProvider[] frozen, LinkAssetFrozenIdentityCatalog catalog) =
            FreezeProviders(
                providers,
                new LinkAssetFrozenIdentityCatalog());
        _providers = Array.AsReadOnly(frozen);
        _identityCatalog = catalog;
    }

    private LinkAssetPool(
        IEnumerable<LinkAssetProvider> providers,
        LinkAssetFrozenIdentityCatalog identityCatalog)
    {
        _providers = Array.AsReadOnly(providers.ToArray());
        _identityCatalog = identityCatalog ?? throw new ArgumentNullException(
            nameof(identityCatalog));
    }

    public IReadOnlyList<LinkAssetProvider> Providers => _providers;

    /// <summary>
    /// Places an already-frozen provider pool before this pool without
    /// changing either pool's provider or storage identities.
    /// </summary>
    public LinkAssetPool WithHighestPrecedencePool(LinkAssetPool providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return new LinkAssetPool(
            providers.Providers.Concat(_providers),
            _identityCatalog.Merge(providers._identityCatalog));
    }

    /// <summary>
    /// Freezes a new provider batch against this pool's immutable imported
    /// identity catalog, then places the batch at highest precedence. Mutable
    /// definitions and Loader resolvers remain transient to this call.
    /// </summary>
    public LinkAssetPool WithHighestPrecedenceProviders(
        IEnumerable<LinkAssetProviderSource> providers)
    {
        (LinkAssetProvider[] frozen, LinkAssetFrozenIdentityCatalog catalog) =
            FreezeProviders(providers, _identityCatalog.Clone());
        return new LinkAssetPool(frozen.Concat(_providers), catalog);
    }

    /// <summary>
    /// Returns a pool without any provider occurrence for the supplied
    /// logical asset keys.
    /// </summary>
    public LinkAssetPool WithoutProviders(IEnumerable<AssetKey> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var removed = new HashSet<AssetKey>();
        foreach (AssetKey asset in assets)
        {
            if (!asset.IsValid)
            {
                throw new ArgumentException(
                    "Removed provider keys must be constructed and valid.",
                    nameof(assets));
            }

            removed.Add(asset);
        }

        return new LinkAssetPool(
            _providers.Where(provider => !removed.Contains(provider.Key)),
            _identityCatalog);
    }

    private static (
        LinkAssetProvider[] Providers,
        LinkAssetFrozenIdentityCatalog Catalog) FreezeProviders(
        IEnumerable<LinkAssetProviderSource> providers,
        LinkAssetFrozenIdentityCatalog identityCatalog)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(identityCatalog);
        LinkAssetProviderSource[] sources = providers
            .Select(source => source ?? throw new ArgumentException(
                "Link asset provider sources cannot contain null.",
                nameof(providers)))
            .ToArray();

        var freezeContext = new LinkAssetFreezeContext(identityCatalog);
        var frozen = new LinkAssetProvider[sources.Length];
        for (int index = 0; index < sources.Length; index++)
            frozen[index] = new LinkAssetProvider(sources[index], freezeContext);
        freezeContext.Complete();
        return (frozen, freezeContext.Catalog);
    }
}
