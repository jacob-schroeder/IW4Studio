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
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Documents;

/// <summary>
/// Creates the smallest detached, emitter-valid owned definition for every
/// provider-backed PS3 XAsset type.
/// </summary>
internal static class NewAssetDefinitionFactory
{
    private static readonly IReadOnlyList<XAssetType> AddableTypes =
        Array.AsReadOnly(
        [
            XAssetType.PhysPreset,
            XAssetType.PhysCollmap,
            XAssetType.XAnim,
            XAssetType.XModelSurfs,
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

    internal static IReadOnlyList<XAssetType> SupportedAssetTypes => AddableTypes;

    internal static BaseAsset Create(XAssetType assetType, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!AddableTypes.Contains(assetType))
        {
            throw new NotSupportedException(
                $"Serialized asset type '{assetType}' has no new-definition factory.");
        }

        return assetType switch
        {
            XAssetType.PhysPreset => new PhysPresetAsset { Name = name },
            XAssetType.PhysCollmap => new PhysCollmapAsset { Name = name },
            XAssetType.XAnim => new XAnimPartsAsset
            {
                Name = name,
                BoneCounts = new byte[10]
            },
            XAssetType.XModelSurfs => new XModelSurfsAsset
            {
                Name = name,
                PartBits = new uint[6]
            },
            XAssetType.XModel => CreateXModel(name),
            XAssetType.Material => new MaterialAsset
            {
                Info = new MaterialInfo { Name = name },
                StateBitsEntries = Enumerable.Range(
                        0,
                        MaterialAsset.TechniqueSlotCount)
                    .Select(_ => new MaterialStateBitsEntry(0))
                    .ToArray()
            },
            XAssetType.PixelShader => new MaterialShaderAsset
            {
                Kind = MaterialShaderKind.Pixel,
                Name = name,
                ProgramBytes = new byte[
                    MaterialShaderAsset.GetProgramByteCount(
                        MaterialShaderKind.Pixel)]
            },
            XAssetType.VertexShader => new MaterialShaderAsset
            {
                Kind = MaterialShaderKind.Vertex,
                Name = name
            },
            XAssetType.Techset => new MaterialTechniqueSetAsset
            {
                Name = name,
                TechniqueSlots = Enumerable.Range(
                        0,
                        (int)MaterialTechniqueType.Count)
                    .Select(index => new MaterialTechniqueSlot(
                        (MaterialTechniqueType)index,
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
            XAssetType.MenuFile => new MenuFileAsset
            {
                Name = name,
                MenuCount = 0,
                Menus = []
            },
            XAssetType.Menu => MenuAuthoringDefaults.CreateMenu(name),
            XAssetType.Localize => new LocalizeAsset
            {
                Name = name,
                Value = string.Empty
            },
            XAssetType.Weapon => CreateWeapon(name),
            XAssetType.Fx => new FxEffectDefAsset { Name = name },
            XAssetType.ImpactFx => CreateImpactFx(name),
            XAssetType.RawFile => new RawFileAsset
            {
                Name = name,
                Buffer = [0],
                Len = 0
            },
            XAssetType.StringTable => new StringTableAsset
            {
                Name = name,
                RowCount = 0,
                ColumnCount = 0,
                Cells = []
            },
            XAssetType.LeaderboardDef => new LeaderboardDefAsset { Name = name },
            XAssetType.StructuredDataDef => new StructuredDataDefSetAsset
            {
                Name = name
            },
            XAssetType.Tracer => new TracerDefAsset
            {
                Name = name,
                Colors = new TracerColor[TracerDefAsset.ColorCount]
            },
            XAssetType.Vehicle => new VehicleDefAsset
            {
                Name = name,
                TrophyTags = CreateScriptStrings(
                    VehicleDefAsset.ScriptStringCount),
                SurfaceSoundFields = Enumerable.Range(
                        0,
                        VehicleDefAsset.SurfaceSoundCount)
                    .Select(_ => VehicleSoundAliasField.Empty)
                    .ToArray()
            },
            XAssetType.AddonMapEnts => new AddonMapEntsAsset { Name = name },
            _ => throw new NotSupportedException(
                $"Serialized asset type '{assetType}' has no new-definition factory.")
        };
    }

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

    private static WeaponAsset CreateWeapon(string name)
    {
        var definition = new WeaponDef
        {
            InternalName = name,
            Turret = new WeaponTurretFields
            {
                BarrelSpinUpSounds = Enumerable.Range(
                        0,
                        (int)WeaponTurretBarrelSpinSoundSlot.Count)
                    .Select(_ => new WeaponSoundAliasField())
                    .ToArray(),
                BarrelSpinDownSounds = Enumerable.Range(
                        0,
                        (int)WeaponTurretBarrelSpinSoundSlot.Count)
                    .Select(_ => new WeaponSoundAliasField())
                    .ToArray()
            }
        };
        return new WeaponAsset
        {
            Variant = new WeaponVariantDef
            {
                InternalName = name,
                Definition = definition
            }
        };
    }

    private static ScriptStringReference[] CreateScriptStrings(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new ScriptStringReference(
                0,
                null,
                default,
                default))
            .ToArray();

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
