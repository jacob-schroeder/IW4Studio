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
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.XAnim;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Creates the smallest detached, emitter-valid definition supported by the
/// production authoring registry. Defaults deliberately include every fixed
/// native table; callers can save a newly added row before opening its editor.
/// </summary>
public static class NewAssetDefinitionFactory
{
    private static readonly IReadOnlyList<XAssetType> AddableTypes =
        Array.AsReadOnly(
        [
            XAssetType.PhysPreset,
            XAssetType.PhysCollmap,
            XAssetType.XAnim,
            XAssetType.XModel,
            XAssetType.Material,
            XAssetType.PixelShader,
            XAssetType.VertexShader,
            XAssetType.Techset,
            XAssetType.Image,
            XAssetType.Sound,
            XAssetType.SndCurve,
            XAssetType.LoadedSound,
            XAssetType.ColMapSp,
            XAssetType.ColMapMp,
            XAssetType.ComMap,
            XAssetType.GameMapSp,
            XAssetType.GameMapMp,
            XAssetType.MapEnts,
            XAssetType.FxMap,
            XAssetType.GfxMap,
            XAssetType.LightDef,
            XAssetType.Font,
            XAssetType.MenuFile,
            XAssetType.Menu,
            XAssetType.Localize,
            XAssetType.Weapon,
            XAssetType.Fx,
            XAssetType.ImpactFx,
            XAssetType.RawFile,
            XAssetType.StringTable,
            XAssetType.LeaderboardDef,
            XAssetType.StructuredDataDef,
            XAssetType.Tracer,
            XAssetType.Vehicle,
            XAssetType.AddonMapEnts
        ]);

    public static IReadOnlyList<XAssetType> SupportedAssetTypes =>
        AddableTypes;

    public static ITargetZoneDetachedSemanticSnapshot Create(
        XAssetType assetType,
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!AddableTypes.Contains(assetType))
        {
            throw new NotSupportedException(
                $"Serialized asset type '{assetType}' has no new-definition factory.");
        }

        BaseAsset asset = CreateAsset(assetType, name);
        return DetachedAssetSemanticSnapshotFactory.Capture(
                   assetType,
                   asset,
                   new DetachedAssetSemanticGraphClone())
               ?? throw new InvalidDataException(
                   $"New {assetType} definition did not produce detached semantic data.");
    }

    private static BaseAsset CreateAsset(
        XAssetType assetType,
        string name) =>
        assetType switch
        {
            XAssetType.PhysPreset => new PhysPresetAsset { Name = name },
            XAssetType.PhysCollmap =>
                new IW4.Assets.Assets.Physics.PhysCollmapAsset { Name = name },
            XAssetType.XAnim => new XAnimPartsAsset
            {
                Name = name,
                BoneCounts = new byte[10]
            },
            XAssetType.XModel => CreateXModel(name),
            XAssetType.Material => new MaterialAsset
            {
                Info = new MaterialInfo { Name = name },
                StateBitsEntries = Enumerable.Range(
                        0,
                        MaterialAsset.TechniqueSlotCount)
                    .Select(index => new MaterialStateBitsEntry(index, 0))
                    .ToArray()
            },
            XAssetType.PixelShader => new MaterialShaderAsset
            {
                Kind = MaterialShaderKind.Pixel,
                Name = name,
                ProgramBytes = new byte[12]
            },
            XAssetType.VertexShader => new MaterialShaderAsset
            {
                Kind = MaterialShaderKind.Vertex,
                Name = name
            },
            XAssetType.Techset => new MaterialTechniqueSetAsset
            {
                Name = name,
                TechniqueSlots = Enumerable.Range(0, 37)
                    .Select(index => new MaterialTechniqueSlot(
                        index,
                        default,
                        null))
                    .ToArray()
            },
            XAssetType.Image => new GfxImageAsset
            {
                Name = name,
                StreamData = Enumerable.Range(
                        0,
                        GfxImageStreamData.EntryCount)
                    .Select(_ => new GfxImageStreamData(0, 0, 0))
                    .ToArray()
            },
            XAssetType.Sound => new SoundAliasListAsset { AliasName = name },
            XAssetType.SndCurve => new SndCurve
            {
                Filename = name,
                Knots = new SndCurveKnot[SndCurve.MaxKnotCount]
            },
            XAssetType.LoadedSound => new LoadedSound { Name = name },
            XAssetType.ColMapSp => CreateClipMap(XAssetType.ColMapSp, name),
            XAssetType.ColMapMp => CreateClipMap(XAssetType.ColMapMp, name),
            XAssetType.ComMap => new ComWorldAsset { Name = name },
            XAssetType.GameMapSp => new GameWorldSpAsset { Name = name },
            XAssetType.GameMapMp => new GameWorldMpAsset
            {
                Name = name,
                GlassData = new GGlassData
                {
                    Pad14To7F = new byte[0x6c]
                }
            },
            XAssetType.MapEnts => new MapEntsAsset
            {
                Name = name,
                Pad29To2B = new byte[3]
            },
            XAssetType.FxMap => new FxWorldAsset { Name = name },
            XAssetType.GfxMap => CreateGfxWorld(name),
            XAssetType.LightDef => new LightDefAsset
            {
                Name = name,
                Pad09To0B = new byte[3]
            },
            XAssetType.Font => new FontAsset { Name = name },
            XAssetType.MenuFile => new MenuFileAsset { Name = name },
            XAssetType.Menu => CreateMenu(name),
            XAssetType.Localize => new LocalizeAsset { Name = name },
            XAssetType.Weapon => CreateWeapon(name),
            XAssetType.Fx => new FxEffectDefAsset { Name = name },
            XAssetType.ImpactFx => CreateImpactFx(name),
            XAssetType.RawFile => new RawFileAsset { Name = name },
            XAssetType.StringTable => new StringTableAsset { Name = name },
            XAssetType.LeaderboardDef => new LeaderboardDefAsset { Name = name },
            XAssetType.StructuredDataDef => new StructuredDataDefSetAsset { Name = name },
            XAssetType.Tracer => new TracerDefAsset
            {
                Name = name,
                Colors = new TracerColor[TracerDefAsset.ColorCount]
            },
            XAssetType.Vehicle => new VehicleDefAsset
            {
                Name = name,
                TrophyTags = new ushort[VehicleDefAsset.ScriptStringCount],
                SurfaceSoundAliases = new string?[VehicleDefAsset.SurfaceSoundCount]
            },
            XAssetType.AddonMapEnts => new AddonMapEntsAsset { Name = name },
            _ => throw new NotSupportedException(
                $"Serialized asset type '{assetType}' has no new-definition factory.")
        };

    private static XModelAsset CreateXModel(string name) =>
        new()
        {
            Name = name,
            Scale = 1.0f,
            NoScalePartBits = new uint[6],
            Lods = Enumerable.Range(0, 4)
                .Select(_ => new XModelLodInfo
                {
                    PartBits = new uint[6]
                })
                .ToArray(),
            MaxLoadedLod = byte.MaxValue,
            CollLod = byte.MaxValue
        };

    private static ClipMapAsset CreateClipMap(
        XAssetType assetType,
        string name) =>
        new()
        {
            SerializedType = assetType,
            Name = name,
            DynEntCount = new ushort[2],
            DynEntDefList = TwoEmptyRows<DynEntityDef>(),
            DynEntPoseList = TwoEmptyRows<DynEntityPose>(),
            DynEntClientList = TwoEmptyRows<DynEntityClient>(),
            DynEntCollList = TwoEmptyRows<DynEntityColl>(),
            PadD0ToFF = new byte[0x30]
        };

    private static GfxWorldAsset CreateGfxWorld(string name) =>
        new()
        {
            Name = name,
            BaseName = name,
            Mins = new float[3],
            Maxs = new float[3],
            OutdoorLookupMatrix = new float[16],
            Sun = new Sunflare
            {
                SunFxPosition = new float[3]
            },
            LightGrid = new GfxLightGrid
            {
                Mins = new ushort[3],
                Maxs = new ushort[3],
                RowDataStart = new ushort[1]
            },
            Dpvs = new GfxWorldDpvsStatic
            {
                VisibilityCounts = new uint[8]
            },
            DpvsDyn = new GfxWorldDpvsDynamic
            {
                DynEntClientWordCount = new uint[2],
                DynEntClientCount = new uint[2]
            }
        };

    private static MenuDefAsset CreateMenu(string name)
    {
        MenuTransition[] Transitions() => Enumerable.Range(0, 4)
            .Select(_ => new MenuTransition())
            .ToArray();

        return new MenuDefAsset
        {
            Window = new WindowDef
            {
                Name = name,
                DynamicFlags = new WindowDynamicFlags[4]
            },
            CursorItems = new int[4],
            ScaleTransitions = Transitions(),
            AlphaTransitions = Transitions(),
            XTransitions = Transitions(),
            YTransitions = Transitions()
        };
    }

    private static WeaponAsset CreateWeapon(string name)
    {
        static ScriptStringReference[] ScriptStrings(int count) =>
            Enumerable.Range(0, count)
                .Select(_ => new ScriptStringReference(
                    0,
                    null,
                    default,
                    default))
                .ToArray();

        var definition = new WeaponDef
        {
            InternalName = name,
            RightHandAnimationNames = new string?[WeaponDef.WeaponAnimCount],
            LeftHandAnimationNames = new string?[WeaponDef.WeaponAnimCount],
            NoteTrackMaps = new WeaponNoteTrackMaps
            {
                SoundMapKeys = ScriptStrings(WeaponDef.NoteTrackMapCount),
                SoundMapValues = ScriptStrings(WeaponDef.NoteTrackMapCount),
                RumbleMapKeys = ScriptStrings(WeaponDef.NoteTrackMapCount),
                RumbleMapValues = ScriptStrings(WeaponDef.NoteTrackMapCount)
            },
            SoundAliasNames = new string?[WeaponDef.WeaponSoundAliasCount],
            Projectile = new WeaponProjectileFields
            {
                ParallelBounce = new float[WeaponDef.SurfaceCount],
                PerpendicularBounce = new float[WeaponDef.SurfaceCount]
            },
            LocationDamageMultipliers = new float[WeaponDef.HitLocationCount],
            Turret = new WeaponTurretFields
            {
                BarrelSpinUpSoundNames =
                    new string?[WeaponDef.TurretBarrelSpinSoundCount],
                BarrelSpinDownSoundNames =
                    new string?[WeaponDef.TurretBarrelSpinSoundCount]
            }
        };
        return new WeaponAsset
        {
            Variant = new WeaponVariantDef
            {
                InternalName = name,
                Definition = definition,
                HideTags = ScriptStrings(WeaponVariantDef.HideTagCount),
                AnimationNames = new string?[WeaponVariantDef.WeaponAnimCount]
            }
        };
    }

    private static FxImpactTableAsset CreateImpactFx(string name) =>
        new()
        {
            Name = name,
            Entries = Enumerable.Range(0, FxImpactTableAsset.EntryCount)
                .Select(_ => new FxImpactEntry
                {
                    SurfaceEffectPointers =
                        new XPointer<FxEffectDefAsset>[FxImpactEntry.SurfaceEffectCount],
                    SurfaceEffects =
                        new FxEffectDefAsset?[FxImpactEntry.SurfaceEffectCount],
                    FleshEffectPointers =
                        new XPointer<FxEffectDefAsset>[FxImpactEntry.FleshEffectCount],
                    FleshEffects =
                        new FxEffectDefAsset?[FxImpactEntry.FleshEffectCount]
                })
                .ToArray()
        };

    private static IReadOnlyList<IReadOnlyList<T>> TwoEmptyRows<T>() =>
        new IReadOnlyList<T>[]
        {
            Array.Empty<T>(),
            Array.Empty<T>()
        };
}
