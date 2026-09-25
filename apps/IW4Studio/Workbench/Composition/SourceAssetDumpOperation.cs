using IW4.Formats.RawFile;
using IW4.Formats.SourceFormat.Fx;
using IW4.Formats.SourceFormat.Font;
using IW4.Formats.SourceFormat.Image;
using IW4.Formats.SourceFormat.Leaderboard;
using IW4.Formats.SourceFormat.LightDef;
using IW4.Formats.SourceFormat.Localize;
using IW4.Formats.SourceFormat.MapEnts;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.Menu;
using IW4.Formats.SourceFormat.PhysCollmap;
using IW4.Formats.SourceFormat.PhysPreset;
using IW4.Formats.SourceFormat.RawFile;
using IW4.Formats.SourceFormat.Shader;
using IW4.Formats.SourceFormat.Sound;
using IW4.Formats.SourceFormat.StringTable;
using IW4.Formats.SourceFormat.StructuredData;
using IW4.Formats.SourceFormat.Technique;
using IW4.Formats.SourceFormat.Techset;
using IW4.Formats.SourceFormat.Tracer;
using IW4.Formats.SourceFormat.Vehicle;
using IW4.Formats.SourceFormat.Weapon;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Formats.SourceFormat.XModel;
using IW4.Game.Assets;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Font;
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
using IW4.Formats.XModel;
using IW4.Game.Zone;
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
            XAssetType.Fx,
            XAssetType.Sound,
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
        IReadOnlyList<BaseAsset> targetProviders,
        int supportedRowCount,
        int unsupportedRowCount,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(targetProviders);
        ArgumentOutOfRangeException.ThrowIfNegative(supportedRowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(unsupportedRowCount);

        AppliedAssetDefinition[] definitions = capture.Definitions
            .Where(value => SupportedAssetTypes.Contains(
                value.Definition.SerializedAssetType))
            .ToArray();
        BaseAsset[] assets = definitions
            .OrderBy(value => value.RowIdentity.SerializedIndex)
            .Select(value => value.Definition)
            .Concat(targetProviders.Where(asset => SupportedAssetTypes.Contains(
                asset.SerializedAssetType)))
            .ToArray();
        MenuFileAsset[] menuFiles = assets
            .OfType<MenuFileAsset>()
            .ToArray();
        LocalizeAsset[] localizeEntries = assets
            .OfType<LocalizeAsset>()
            .ToArray();
        MaterialTechniqueSetAsset[] techniqueSets = assets
            .OfType<MaterialTechniqueSetAsset>()
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

        IEnumerable<BaseAsset> orderedAssets = assets
            .Where(asset => asset is not LocalizeAsset)
            .OrderBy(asset => asset is MenuFileAsset ? 0 : 1);
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
                    FxEffectDefAsset effect => new FxExchange().Unlink(
                        sourceDirectory,
                        effect),
                    SoundAliasListAsset sound => DumpSoundAlias(
                        sourceDirectory,
                        sound,
                        workspace),
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

        var seenTechniques = new HashSet<MaterialTechniqueAsset>(
            ReferenceEqualityComparer.Instance);
        var techniqueSourceNames = new Dictionary<string, MaterialTechniqueAsset>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        TechniqueExchange? techniqueExchange = null;
        foreach (MaterialTechniqueAsset technique in techniqueSets
            .SelectMany(techniqueSet => techniqueSet.TechniqueSlots)
            .Select(slot => slot.Technique)
            .Where(technique => technique is not null)
            .Cast<MaterialTechniqueAsset>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenTechniques.Add(technique))
                continue;

            string sourceName = (technique.Name ?? string.Empty)
                .Replace('\\', '/');
            if (techniqueSourceNames.TryGetValue(
                    sourceName,
                    out MaterialTechniqueAsset? existing))
            {
                failures.Add(new SourceAssetDumpFailure(
                    XAssetType.Techset,
                    technique.Name ?? "<unnamed technique>",
                    $"Technique source name collides with the distinct definition at 0x{existing.Offset:X}."));
                continue;
            }
            techniqueSourceNames.Add(sourceName, technique);

            try
            {
                techniqueExchange ??= new TechniqueExchange();
                IReadOnlyList<string> writtenFiles = techniqueExchange.Unlink(
                    sourceDirectory,
                    technique);
                dumpedFileCount = checked(
                    dumpedFileCount + writtenFiles.Count);
            }
            catch (Exception exception)
            {
                failures.Add(new SourceAssetDumpFailure(
                    XAssetType.Techset,
                    technique.Name ?? "<unnamed technique>",
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
        IReadOnlyList<string> nativeFiles = NativeImageSourceExport.Unlink(sourceDirectory, image, imagePayloads);
        try
        {
            IReadOnlyList<ImageSourceMipLevel> mipLevels =
                SourceImageDumpDecoder.DecodeForExport(image, imagePayloads);
            IReadOnlyList<string> previewFiles = new ImageExchange().UnlinkPreview(
                sourceDirectory,
                image,
                mipLevels);
            return nativeFiles.Concat(previewFiles).ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or
                                          NotSupportedException or
                                          OverflowException)
        {
            // Native pixels remain usable even when the decoded preview format
            // cannot represent this image (for example floating-point water).
            return nativeFiles;
        }
    }

    private static IReadOnlyList<string> DumpSoundAlias(
        string sourceDirectory,
        SoundAliasListAsset sound,
        FastFileWorkspace workspace)
    {
        // Source dumps capture current target rows, which may be detached from
        // the runtime provider objects used by workspace preview resolution.
        var resolver = workspace.LoadedZones
            .First(zone => zone.IsTarget)
            .LoadResult.SoundPayloadResolver;
        return new SoundAliasListExchange().Unlink(
            sourceDirectory,
            sound,
            streamed => resolver.TryResolvePayload(
                streamed,
                out byte[] payload,
                out _) ? payload : null);
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
        IReadOnlyList<string> nativeFiles = new XModelNativeExchange().Unlink(
            sourceDirectory,
            model);
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
                    out _) ||
                document is null)
            {
                return nativeFiles;
            }

            documents.Add(lodIndex, document);
        }

        try
        {
            IReadOnlyList<string> previewFiles = new XModelExchange().Unlink(
                sourceDirectory,
                model,
                documents);
            return nativeFiles.Concat(previewFiles).ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return nativeFiles;
        }
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
