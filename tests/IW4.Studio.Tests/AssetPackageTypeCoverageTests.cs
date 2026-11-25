using System.Buffers.Binary;
using IW4.Game.Assets;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.GameMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.ImpactFx;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Leaderboard;
using IW4.Game.Assets.LightDef;
using IW4.Game.Assets.Localize;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Menu;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.Sound;
using IW4.Game.Assets.StringTable;
using IW4.Game.Assets.StructuredData;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.Tracer;
using IW4.Game.Assets.Vehicle;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XAnim;
using IW4.Game.Assets.XModel;
using IW4.Game.Assets.ComWorld;
using IW4.Game.Assets.Font;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class AssetPackageTypeCoverageTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("IW4.AssetPackage.Types.");

    [Fact]
    public void Named_asset_families_export_import_save_reopen_and_reimport()
    {
        BaseAsset[] definitions = CreateDefinitions();
        Assert.All(definitions, definition => Assert.False(string.IsNullOrWhiteSpace(definition.SerializedAssetName),
            $"{definition.GetType().Name} ({definition.SerializedAssetType}) has no fixture name."));
        string sourcePath = Path.Combine(_directory.FullName, "types.ff");
        File.WriteAllBytes(sourcePath, AssetPackageFixture.FastFile(definitions, definitions));

        using var source = Open(sourcePath);
        AssetPackageExportPreview exported = source.PreviewAssetPackageExport(source.Document.Rows);
        Assert.Equal(definitions.Length, exported.Rows.Count);
        Assert.All(exported.Rows, row => Assert.Equal(AssetPackageStatus.Included, row.Status));
        string packagePath = Path.Combine(_directory.FullName, "types.iw4pkg");
        exported.WriteTo(packagePath);

        string baselinePath = Path.Combine(_directory.FullName, "baseline.ff");
        File.WriteAllBytes(baselinePath, AssetPackageFixture.FastFile([], []));
        using var target = Open(baselinePath);
        using (var input = File.OpenRead(packagePath))
        using (AssetPackageImportPreview preview = target.PreviewAssetPackageImport(input))
        {
            Assert.All(preview.Rows, row => Assert.Equal(AssetPackageStatus.Add, row.Status));
            Assert.Equal(definitions.Length, target.ImportAssetPackage(preview, new Dictionary<AssetKey, AssetPackageConflictChoice>()));
        }

        string savedPath = Path.Combine(_directory.FullName, "types-reopened.ff");
        SaveAsResult saved = new TransactionalSaveAsService().SaveAs(target, new(savedPath, false));
        Assert.True(saved.Succeeded, string.Join(Environment.NewLine, saved.Diagnostics));
        using var reopened = Open(savedPath);
        Assert.Equal(definitions.Length, reopened.Document.Rows.Count);
        foreach (BaseAsset definition in definitions)
        {
            WorkspaceAssetCatalogEntry row = Assert.Single(reopened.Document.Rows,
                item => item.AssetType == definition.SerializedAssetType &&
                    string.Equals(item.OriginalName, definition.SerializedAssetName, StringComparison.Ordinal));
            Assert.Contains(reopened.Workspace.LoadedZone.LoadedAssets,
                loaded => loaded.Asset?.SerializedAssetType == definition.SerializedAssetType &&
                    string.Equals(loaded.Asset.SerializedAssetName, definition.SerializedAssetName, StringComparison.Ordinal));
        }
        using var repeated = File.OpenRead(packagePath);
        using AssetPackageImportPreview repeat = reopened.PreviewAssetPackageImport(repeated);
        Assert.All(repeat.Rows, row => Assert.Equal(AssetPackageStatus.Reuse, row.Status));
        Assert.Equal(0, reopened.ImportAssetPackage(repeat, new Dictionary<AssetKey, AssetPackageConflictChoice>()));
    }

    private static BaseAsset[] CreateDefinitions()
    {
        string[] names = Enumerable.Range(0, 36).Select(index => $"package/type_{index:D2}").ToArray();
        var techniqueSet = new MaterialTechniqueSetAsset
        {
            Name = names[10],
            TechniqueSlots = Enumerable.Range(0, (int)MaterialTechniqueType.Count)
                .Select(index => new MaterialTechniqueSlot((MaterialTechniqueType)index, default, null)).ToArray()
        };
        BaseAsset[] definitions =
        [
            new RawFileAsset { Name = names[0], Len = 1, CompressedLen = 0, Buffer = [0x41, 0] },
            new LightDefAsset { Name = names[1] },
            new GfxImageAsset { Name = names[2] },
            new LocalizeAsset { Name = names[3], Value = "text/type" },
            new StringTableAsset
            {
                Name = names[4], ColumnCount = 1, RowCount = 1,
                Cells = [new StringTableCell { String = "value/type", Hash = StringTableCell.CalculateHash("value/type") }]
            },
            new LoadedSound { Name = names[5] },
            new SoundAliasListAsset { AliasName = names[6] },
            new SndCurve
            {
                Filename = names[7], KnotCount = 16,
                Knots = Enumerable.Range(0, 16).Select(index => new SndCurveKnot(index, index + 0.5f)).ToArray()
            },
            new MaterialShaderAsset
            {
                Name = names[8], Kind = MaterialShaderKind.Pixel, DataSize = 60,
                Data = CreatePixelShaderBytes(), LoadDefTail = new byte[12]
            },
            new MaterialShaderAsset { Name = names[9], Kind = MaterialShaderKind.Vertex },
            techniqueSet,
            new MaterialAsset
            {
                Info = new MaterialInfo { Name = names[11] },
                TechniqueSet = techniqueSet,
                StateBitsEntries = Enumerable.Repeat(new MaterialStateBitsEntry(255),
                    MaterialAsset.TechniqueSlotCount).ToArray()
            },
            new TracerDefAsset
            {
                Name = names[12], Speed = 3,
                Colors = Enumerable.Range(0, TracerDefAsset.ColorCount)
                    .Select(index => new TracerColor(index, index + 1, index + 2, 1)).ToArray()
            },
            new PhysPresetAsset { Name = names[13] },
            new PhysCollmapAsset { Name = names[14] },
            CreateClipMap(XAssetType.ColMapSp, names[15]),
            CreateClipMap(XAssetType.ColMapMp, names[16]),
            new XAnimPartsAsset { Name = names[17], NumFrames = 1, BoneCounts = new byte[10] },
            new XModelSurfsAsset { Name = names[18], PartBits = new uint[6] },
            new XModelAsset
            {
                Name = names[19], NoScalePartBits = new uint[6],
                Lods = Enumerable.Range(0, 4).Select(_ => new XModelLodInfo { PartBits = new uint[6] }).ToArray()
            },
            new FxEffectDefAsset { Name = names[20] },
            new FxImpactTableAsset { Name = names[21] },
            new FxWorldAsset { Name = names[22] },
            new ComWorldAsset { Name = names[23] },
            new GameWorldMpAsset { Name = names[24], GlassData = new GGlassData() },
            new GameWorldSpAsset { Name = names[25] },
            new MapEntsAsset
            {
                Name = names[26], EntityString = "// map type", EntityStringBytes = [.. "// map type"u8, 0],
                NumEntityChars = "// map type".Length + 1
            },
            new AddonMapEntsAsset
            {
                Name = names[27], EntityString = "// addon type", EntityStringBytes = [.. "// addon type"u8, 0],
                NumEntityChars = "// addon type".Length + 1
            },
            new FontAsset { Name = names[28] },
            new MenuFileAsset { Name = names[29] },
            new MenuDefAsset
            {
                Window = new WindowDef { Name = names[30], DynamicFlags = new WindowDynamicFlags[4] },
                CursorItems = new int[4], ScaleTransitions = EmptyTransitions(), AlphaTransitions = EmptyTransitions(),
                XTransitions = EmptyTransitions(), YTransitions = EmptyTransitions()
            },
            new GfxWorldAsset
            {
                Name = names[31],
                LightGrid = new GfxLightGrid
                {
                    Mins = [0, 0, 0], Maxs = [0, 0, 0],
                    RowAxis = GfxLightGridHorizontalAxis.X, ColAxis = GfxLightGridHorizontalAxis.Y,
                    RowDataStart = [0]
                }
            },
            new VehicleDefAsset
            {
                Name = names[32],
                TrophyTags = Enumerable.Range(0, VehicleDefAsset.ScriptStringCount)
                    .Select(_ => new ScriptStringReference(0, null, default, default)).ToArray(),
                SurfaceSoundFields = Enumerable.Repeat(VehicleSoundAliasField.Empty,
                    VehicleDefAsset.SurfaceSoundCount).ToArray()
            },
            new WeaponAsset { Variant = new WeaponVariantDef { InternalName = names[33], DisplayName = "Test Weapon" } },
            new LeaderboardDefAsset { Name = names[34] },
            new StructuredDataDefSetAsset { Name = names[35] }
        ];
        XAssetType[] actualTypes = definitions.Select(definition => definition.SerializedAssetType).ToArray();
        XAssetType[] expectedTypes = Enum.GetValues<XAssetType>()
            .Where(type => !XAssetTypeDispatchCatalog.IsNativeNoOp(type)).ToArray();
        Assert.Equal(expectedTypes.Order(), actualTypes.Order());
        return definitions;
    }

    private static ClipMapAsset CreateClipMap(XAssetType type, string name) => new()
    {
        Name = name,
        SerializedType = type,
        DynEntCount = [0, 0],
        DynEntDefListPointers = [default, default],
        DynEntDefList = [Array.Empty<DynEntityDef>(), Array.Empty<DynEntityDef>()],
        DynEntPoseListPointers = [default, default],
        DynEntPoseList = [Array.Empty<DynEntityPose>(), Array.Empty<DynEntityPose>()],
        DynEntClientListPointers = [default, default],
        DynEntClientList = [Array.Empty<DynEntityClient>(), Array.Empty<DynEntityClient>()],
        DynEntCollListPointers = [default, default],
        DynEntCollList = [Array.Empty<DynEntityColl>(), Array.Empty<DynEntityColl>()]
    };

    private static byte[] CreatePixelShaderBytes()
    {
        byte[] bytes = new byte[60];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x14), 0x20);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x18), 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x1c), 0x38);
        bytes[0x38] = 0x63;
        return bytes;
    }

    private static IReadOnlyList<MenuTransition> EmptyTransitions() =>
        Enumerable.Range(0, 4).Select(_ => new MenuTransition()).ToArray();

    private static FastFileEditingSession Open(string path) =>
        new(new FastFileDocumentService().Open(new(path, Isolated.Instance)));

    public void Dispose() => _directory.Delete(recursive: true);
}
