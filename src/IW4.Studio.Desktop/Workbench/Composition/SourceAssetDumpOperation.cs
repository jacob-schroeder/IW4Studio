using IW4.AssetExchange.RawFile;
using IW4.AssetExchange.SourceFormat.Font;
using IW4.AssetExchange.SourceFormat.Image;
using IW4.AssetExchange.SourceFormat.Leaderboard;
using IW4.AssetExchange.SourceFormat.LightDef;
using IW4.AssetExchange.SourceFormat.Localize;
using IW4.AssetExchange.SourceFormat.MapEnts;
using IW4.AssetExchange.SourceFormat.Material;
using IW4.AssetExchange.SourceFormat.Menu;
using IW4.AssetExchange.SourceFormat.PhysCollmap;
using IW4.AssetExchange.SourceFormat.PhysPreset;
using IW4.AssetExchange.SourceFormat.RawFile;
using IW4.AssetExchange.SourceFormat.Shader;
using IW4.AssetExchange.SourceFormat.Sound;
using IW4.AssetExchange.SourceFormat.StringTable;
using IW4.AssetExchange.SourceFormat.StructuredData;
using IW4.AssetExchange.SourceFormat.Techset;
using IW4.AssetExchange.SourceFormat.Tracer;
using IW4.AssetExchange.SourceFormat.Vehicle;
using IW4.AssetExchange.SourceFormat.Weapon;
using IW4.AssetExchange.SourceFormat.XAnim;
using IW4.AssetExchange.SourceFormat.XModel;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Image;
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
using IW4.AssetExchange.XModel;
using IW4.FastFiles.Zone;
using IW4.Render.Textures;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Composition;

internal static class SourceAssetDumpOperation
{
    internal static IReadOnlySet<XAssetType> SupportedAssetTypes { get; } =
        new HashSet<XAssetType>
        {
            XAssetType.PhysPreset,
            XAssetType.PhysCollmap,
            XAssetType.XAnim,
            XAssetType.XModel,
            XAssetType.Material,
            XAssetType.Techset,
            XAssetType.PixelShader,
            XAssetType.VertexShader,
            XAssetType.Image,
            XAssetType.SndCurve,
            XAssetType.MapEnts,
            XAssetType.LightDef,
            XAssetType.Font,
            XAssetType.MenuFile,
            XAssetType.Menu,
            XAssetType.Localize,
            XAssetType.Weapon,
            XAssetType.RawFile,
            XAssetType.StringTable,
            XAssetType.LeaderboardDef,
            XAssetType.StructuredDataDef,
            XAssetType.Tracer,
            XAssetType.Vehicle,
            XAssetType.AddonMapEnts
        };

    internal static SourceAssetDumpResult Execute(
        string sourceDirectory,
        FastFileWorkspace workspace,
        AppliedAssetDefinitionsCapture capture,
        IReadOnlyList<MaterialShaderAsset> targetShaderProviders,
        int supportedRowCount,
        int unsupportedRowCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(targetShaderProviders);
        ArgumentOutOfRangeException.ThrowIfNegative(supportedRowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unsupportedRowCount);

        AppliedAssetDefinition[] definitions = capture.Definitions
            .Where(value => SupportedAssetTypes.Contains(
                value.Definition.SerializedAssetType))
            .ToArray();
        MenuFileAsset[] menuFiles = definitions
            .Select(value => value.Definition)
            .OfType<MenuFileAsset>()
            .ToArray();
        LocalizeAsset[] localizeEntries = definitions
            .Select(value => value.Definition)
            .OfType<LocalizeAsset>()
            .ToArray();

        MenuExchange? menuExchange = null;
        Exception? menuContextFailure = null;
        try
        {
            menuExchange = new MenuExchange(sourceDirectory, menuFiles);
        }
        catch (Exception exception)
        {
            menuContextFailure = exception;
        }

        var imagePayloads = new WorkspaceGfxImagePayloadResolver(workspace);
        var weaponExchange = new WeaponExchange();
        var failures = new List<SourceAssetDumpFailure>();
        int dumpedAssetCount = 0;
        int dumpedFileCount = 0;
        if (localizeEntries.Length != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (workspace.IsBlank)
                {
                    throw new InvalidOperationException(
                        "A blank workspace has no fastfile name for its localized-string source file.");
                }

                IReadOnlyList<string> writtenFiles = new LocalizeExchange().Unlink(
                    sourceDirectory,
                    workspace.LoadedZone.Zone.Name,
                    workspace.LoadedZone.Header.SelectedLanguageMask,
                    localizeEntries);
                dumpedAssetCount = checked(
                    dumpedAssetCount + localizeEntries.Length);
                dumpedFileCount = checked(
                    dumpedFileCount + writtenFiles.Count);
            }
            catch (Exception exception)
            {
                failures.Add(new SourceAssetDumpFailure(
                    XAssetType.Localize,
                    workspace.IsBlank
                        ? "<localized strings>"
                        : workspace.LoadedZone.Zone.Name,
                    exception.Message));
            }
        }

        IEnumerable<BaseAsset> orderedAssets = definitions
            .Where(value => value.Definition is not LocalizeAsset)
            .OrderBy(value => value.Definition is MenuFileAsset ? 0 : 1)
            .ThenBy(value => value.RowIdentity.SerializedIndex)
            .Select(value => value.Definition)
            .Concat(targetShaderProviders);
        foreach (BaseAsset asset in orderedAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IReadOnlyList<string> writtenFiles = asset switch
                {
                    PhysPresetAsset physPreset =>
                        new PhysPresetExchange().Unlink(
                            sourceDirectory,
                            physPreset),
                    PhysCollmapAsset physCollmap =>
                        new PhysCollmapExchange().Unlink(
                            sourceDirectory,
                            physCollmap),
                    XAnimPartsAsset animation =>
                        new XAnimExchange().Unlink(
                            sourceDirectory,
                            animation),
                    XModelAsset model => DumpXModel(
                        sourceDirectory,
                        model),
                    MaterialAsset material =>
                        new MaterialExchange().Unlink(
                            sourceDirectory,
                            material),
                    MaterialTechniqueSetAsset techniqueSet =>
                        new TechsetExchange().Unlink(
                            sourceDirectory,
                            techniqueSet),
                    MaterialShaderAsset shader =>
                        new ShaderExchange().Unlink(
                            sourceDirectory,
                            shader),
                    GfxImageAsset image => DumpImage(
                        sourceDirectory,
                        image,
                        imagePayloads),
                    SndCurve curve => new SndCurveExchange().Unlink(
                        sourceDirectory,
                        curve),
                    MapEntsAsset mapEnts => new MapEntsExchange().Unlink(
                        sourceDirectory,
                        mapEnts),
                    AddonMapEntsAsset addonMapEnts =>
                        new MapEntsExchange().Unlink(
                            sourceDirectory,
                            addonMapEnts),
                    LightDefAsset lightDef => new LightDefExchange().Unlink(
                        sourceDirectory,
                        lightDef),
                    FontAsset font => new FontExchange().Unlink(
                        sourceDirectory,
                        font),
                    MenuFileAsset menuFile when menuExchange is not null =>
                        menuExchange.Unlink(menuFile),
                    MenuDefAsset menu when menuExchange is not null =>
                        menuExchange.Unlink(menu),
                    MenuFileAsset or MenuDefAsset => throw new InvalidDataException(
                        $"Menu source context could not be created: {menuContextFailure?.Message}"),
                    RawFileAsset rawFile => DumpRawFile(
                        sourceDirectory,
                        rawFile),
                    StringTableAsset stringTable =>
                        new StringTableExchange().Unlink(
                            sourceDirectory,
                            stringTable),
                    LeaderboardDefAsset leaderboard =>
                        new LeaderboardExchange().Unlink(
                            sourceDirectory,
                            leaderboard),
                    StructuredDataDefSetAsset structuredData =>
                        new StructuredDataExchange().Unlink(
                            sourceDirectory,
                            structuredData),
                    TracerDefAsset tracer => new TracerExchange().Unlink(
                        sourceDirectory,
                        tracer),
                    VehicleDefAsset vehicle => new VehicleExchange().Unlink(
                        sourceDirectory,
                        vehicle),
                    WeaponAsset weapon => weaponExchange.Unlink(
                        sourceDirectory,
                        weapon),
                    _ => throw new NotSupportedException(
                        $"Source dumping is not implemented for {asset.SerializedAssetType}.")
                };
                dumpedAssetCount++;
                dumpedFileCount = checked(dumpedFileCount + writtenFiles.Count);
            }
            catch (Exception exception)
            {
                failures.Add(new SourceAssetDumpFailure(
                    asset.SerializedAssetType,
                    asset.SerializedAssetName ?? "<unnamed>",
                    exception.Message));
            }
        }

        return new SourceAssetDumpResult(
            capture.Revision,
            dumpedAssetCount,
            dumpedFileCount,
            Math.Max(0, supportedRowCount - definitions.Length),
            unsupportedRowCount,
            Array.AsReadOnly(failures.ToArray()));
    }

    private static IReadOnlyList<string> DumpImage(
        string sourceDirectory,
        GfxImageAsset image,
        WorkspaceGfxImagePayloadResolver imagePayloads)
    {
        if (!GfxImagePreviewDecoder.TryDecodeBestAvailable(
                image,
                imagePayloads,
                out GfxImagePreviewSnapshot? preview,
                out string reason) ||
            preview is null)
        {
            throw new InvalidDataException(
                $"Image '{image.Name ?? "<unnamed>"}' cannot be converted to DDS: {reason}");
        }

        return new ImageExchange().Unlink(
            sourceDirectory,
            image,
            preview.Width,
            preview.Height,
            preview.GetRgbaBytesCopy());
    }

    private static IReadOnlyList<string> DumpRawFile(
        string sourceDirectory,
        RawFileAsset rawFile)
    {
        byte[] logicalContent = RawFileContentCodec.DecodeStrictSerializedContent(
            rawFile.Name ?? "<unnamed>",
            rawFile);
        return new RawFileExchange().Unlink(
            sourceDirectory,
            rawFile,
            logicalContent);
    }

    private static IReadOnlyList<string> DumpXModel(
        string sourceDirectory,
        XModelAsset model)
    {
        int lodCount = model.NumLods == 0
            ? model.Lods.Count
            : model.NumLods;
        var documents = new SortedDictionary<int, XModelExportDocument>();
        for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
        {
            if (!XModelExportProjector.TryProjectMaterializedLod(
                    model,
                    lodIndex,
                    out XModelExportDocument? document,
                    out IReadOnlyList<string> blockers) ||
                document is null)
            {
                string detail = string.Join(" ", blockers);
                throw new InvalidDataException(
                    $"LOD {lodIndex} cannot be converted to XMODEL_EXPORT. {detail}");
            }

            documents.Add(lodIndex, document);
        }

        return new XModelExchange().Unlink(
            sourceDirectory,
            model,
            documents);
    }
}

internal sealed record SourceAssetDumpFailure(
    XAssetType AssetType,
    string AssetName,
    string Message);

internal sealed record SourceAssetDumpResult(
    long Revision,
    int DumpedAssetCount,
    int DumpedFileCount,
    int UnavailableSupportedAssetCount,
    int UnsupportedAssetCount,
    IReadOnlyList<SourceAssetDumpFailure> Failures);
