using System.Collections.ObjectModel;
using System.Security.Cryptography;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Authority granted by a bounded import from an explicitly selected official
/// target zone. It does not authorize general retail-asset copying or Map
/// Editor persistence.
/// </summary>
public enum MapGameplayModelSupportAuthority
{
    OfficialTargetZoneDefinitionImport = 0
}

public enum MapGameplayModelDependencyDisposition
{
    ReportedDefinitionsOnly = 0,
    ImportedNestedDependencyClosure = 1
}

/// <summary>
/// Serialized-row provenance retained for one imported gameplay XModel.
/// Runtime addresses and loader-owned objects are deliberately excluded.
/// </summary>
public sealed record MapGameplayModelSupportSourceProvenance
{
    internal MapGameplayModelSupportSourceProvenance(
        int serializedIndex,
        XAssetHeaderKind headerKind,
        int rawHeader,
        string originalSerializedName)
    {
        if (serializedIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serializedIndex));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            originalSerializedName);
        SerializedIndex = serializedIndex;
        HeaderKind = headerKind;
        RawHeader = rawHeader;
        OriginalSerializedName = originalSerializedName;
    }

    public int SerializedIndex { get; }

    public XAssetHeaderKind HeaderKind { get; }

    public int RawHeader { get; }

    public string OriginalSerializedName { get; }
}

/// <summary>
/// One detached, target-owned gameplay-model definition. Entries remain in
/// their official source asset-table order.
/// </summary>
public sealed record MapGameplayModelSupportOwnedAsset
{
    internal MapGameplayModelSupportOwnedAsset(
        ZoneAssetKey key,
        IXModelBuildData buildData,
        MapGameplayModelSupportSourceProvenance source)
    {
        if (key.Type != XAssetType.XModel)
        {
            throw new ArgumentException(
                "Gameplay-model support may own only XModel definitions.",
                nameof(key));
        }

        ArgumentNullException.ThrowIfNull(buildData);
        ArgumentNullException.ThrowIfNull(source);
        if (buildData.Name is not { } buildName ||
            key != ZoneAssetKey.FromWireName(
                XAssetType.XModel,
                buildName))
        {
            throw new ArgumentException(
                "Gameplay-model support key and detached build data " +
                "must identify the same XModel.",
                nameof(buildData));
        }

        Key = key;
        BuildData = buildData;
        Source = source;
    }

    public ZoneAssetKey Key { get; }

    public IXModelBuildData BuildData { get; }

    public MapGameplayModelSupportSourceProvenance Source { get; }
}

/// <summary>
/// Preserves one reported XModel definition while leaving its Material and
/// physics providers external. Nested XModelSurfs remain part of the XModel
/// definition itself.
/// </summary>
internal sealed class ReportedGameplayXModelBuildData :
    IXModelBuildData
{
    private readonly IXModelBuildData _source;

    internal ReportedGameplayXModelBuildData(
        IXModelBuildData source) =>
        _source = source ??
            throw new ArgumentNullException(nameof(source));

    public XAssetType AssetType => XAssetType.XModel;
    public string? Name => _source.Name;
    public byte NumBones => _source.NumBones;
    public byte NumRootBones => _source.NumRootBones;
    public byte NumSurfs => _source.NumSurfs;
    public byte Pad07 => _source.Pad07;
    public float Scale => _source.Scale;
    public IReadOnlyList<uint> NoScalePartBits =>
        _source.NoScalePartBits;
    public IReadOnlyList<ushort> BoneNames => _source.BoneNames;
    public IReadOnlyList<byte> ParentList => _source.ParentList;
    public IReadOnlyList<short> Quats => _source.Quats;
    public IReadOnlyList<float> Trans => _source.Trans;
    public IReadOnlyList<byte> PartClassification =>
        _source.PartClassification;
    public IReadOnlyList<XModelDObjAnimMatBuildData> BaseMat =>
        _source.BaseMat;
    public IReadOnlyList<SymbolicXAssetReference?>
        MaterialReferences => _source.MaterialReferences;
    public IReadOnlyList<NestedXAssetBuildLink?> MaterialLinks => [];
    public IReadOnlyList<XModelLodBuildData> Lods => _source.Lods;
    public byte MaxLoadedLod => _source.MaxLoadedLod;
    public byte NumLods => _source.NumLods;
    public byte CollLod => _source.CollLod;
    public byte Flags => _source.Flags;
    public IReadOnlyList<XModelCollSurfBuildData> CollSurfs =>
        _source.CollSurfs;
    public int Contents => _source.Contents;
    public IReadOnlyList<XModelBoneInfoBuildData> BoneInfo =>
        _source.BoneInfo;
    public float Radius => _source.Radius;
    public Float3BuildData BoundsMidpoint => _source.BoundsMidpoint;
    public Float3BuildData BoundsHalfSize => _source.BoundsHalfSize;
    public IReadOnlyList<ushort> InvHighMipRadius =>
        _source.InvHighMipRadius;
    public int MemUsage => _source.MemUsage;
    public SymbolicXAssetReference? PhysPresetReference =>
        _source.PhysPresetReference;
    public SymbolicXAssetReference? PhysCollmapReference =>
        _source.PhysCollmapReference;
    public XModelLinkerProvenance LinkerProvenance =>
        _source.LinkerProvenance;
    public NestedXAssetBuildLink? PhysPresetLink => null;
    public NestedXAssetBuildLink? PhysCollmapLink => null;
}

/// <summary>
/// Provenance for a Material definition owned inline by an earlier official
/// top-level asset. The container row and within-row definition ordinal
/// identify its exact source position without pretending the Material had its
/// own retail XAsset row.
/// </summary>
public sealed record MapGameplayMaterialStateOwnerSourceProvenance
{
    internal MapGameplayMaterialStateOwnerSourceProvenance(
        int containerSerializedIndex,
        int definitionOrdinal,
        XAssetHeaderKind containerHeaderKind,
        int containerRawHeader,
        string containerOriginalSerializedName)
    {
        if (containerSerializedIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(containerSerializedIndex));
        }
        if (definitionOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionOrdinal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            containerOriginalSerializedName);
        ContainerSerializedIndex = containerSerializedIndex;
        DefinitionOrdinal = definitionOrdinal;
        ContainerHeaderKind = containerHeaderKind;
        ContainerRawHeader = containerRawHeader;
        ContainerOriginalSerializedName =
            containerOriginalSerializedName;
    }

    public int ContainerSerializedIndex { get; }

    public int DefinitionOrdinal { get; }

    public XAssetHeaderKind ContainerHeaderKind { get; }

    public int ContainerRawHeader { get; }

    public string ContainerOriginalSerializedName { get; }
}

/// <summary>
/// An exact retail Material definition promoted from an earlier nested owner
/// into a target-owned row. It establishes persistent GfxStateBits owner cells
/// before gameplay XModels consume their captured alias tokens.
/// </summary>
public sealed record MapGameplayMaterialStateOwnerAsset
{
    internal MapGameplayMaterialStateOwnerAsset(
        ZoneAssetKey key,
        MaterialBuildData buildData,
        MapGameplayMaterialStateOwnerSourceProvenance source)
    {
        if (key.Type != XAssetType.Material)
        {
            throw new ArgumentException(
                "A gameplay Material state owner must be a Material.",
                nameof(key));
        }

        ArgumentNullException.ThrowIfNull(buildData);
        ArgumentNullException.ThrowIfNull(source);
        if (buildData.Name is not { } buildName ||
            key != ZoneAssetKey.FromWireName(
                XAssetType.Material,
                buildName))
        {
            throw new ArgumentException(
                "Gameplay Material owner key and detached build data must " +
                "identify the same Material.",
                nameof(buildData));
        }

        Key = key;
        BuildData = buildData;
        Source = source;
    }

    public ZoneAssetKey Key { get; }

    public MaterialBuildData BuildData { get; }

    public MapGameplayMaterialStateOwnerSourceProvenance Source { get; }
}

/// <summary>
/// Full game-mode constant-configstring definition imported from the same
/// official target source as the gameplay models.
/// </summary>
public sealed record MapGameplayConstantConfigStringTableAsset
{
    internal MapGameplayConstantConfigStringTableAsset(
        ZoneAssetKey key,
        StringTableBuildData buildData,
        MapGameplayModelSupportSourceProvenance source)
    {
        if (key.Type != XAssetType.StringTable)
        {
            throw new ArgumentException(
                "Constant-configstring support must be a StringTable.",
                nameof(key));
        }

        ArgumentNullException.ThrowIfNull(buildData);
        ArgumentNullException.ThrowIfNull(source);
        if (buildData.Name is not { } buildName ||
            key != ZoneAssetKey.FromWireName(
                XAssetType.StringTable,
                buildName))
        {
            throw new ArgumentException(
                "Constant-configstring support key and detached build data " +
                "must identify the same StringTable.",
                nameof(buildData));
        }

        if (buildData.RowCount == 0 ||
            buildData.ColumnCount != 2)
        {
            throw new ArgumentException(
                "The retail constant-configstring definition must retain " +
                "its nonempty two-column table shape.",
                nameof(buildData));
        }

        Key = key;
        BuildData = buildData;
        Source = source;
    }

    public ZoneAssetKey Key { get; }

    public StringTableBuildData BuildData { get; }

    public MapGameplayModelSupportSourceProvenance Source { get; }
}

/// <summary>
/// Detached gameplay-model support imported from one explicit official
/// mp_terminal zone. The collection order is the source asset-table order,
/// which also preserves cross-model nested-link provenance captured by the
/// shared Studio semantic graph.
/// </summary>
public sealed class MapGameplayModelSupportCompilation
{
    private readonly IReadOnlyList<
        MapGameplayModelSupportOwnedAsset> _ownedAssets;
    private readonly IReadOnlyList<
        MapGameplayMaterialStateOwnerAsset> _stateOwnerMaterials;
    private readonly IReadOnlyList<string?> _scriptStrings;
    private readonly IReadOnlyList<ZoneAssetKey> _ownedAssetKeys;
    private readonly IReadOnlyList<IXAssetBuildData> _ownedBuildData;
    private readonly IReadOnlyList<int> _ownedImportedOrders;
    private readonly IReadOnlyList<ZoneAssetKey> _runtimeDefinitionKeys;
    private readonly IReadOnlyList<IXAssetBuildData>
        _runtimeDefinitionBuildData;

    internal MapGameplayModelSupportCompilation(
        string sourceFastFilePath,
        string sourceSha256,
        string sourceZoneName,
        IEnumerable<MapGameplayMaterialStateOwnerAsset>
            stateOwnerMaterials,
        IEnumerable<MapGameplayModelSupportOwnedAsset> ownedAssets,
        MapGameplayConstantConfigStringTableAsset
            constantConfigStringTable,
        IEnumerable<TargetZoneScriptStringSource> scriptStrings,
        MapGameplayModelDependencyDisposition dependencyDisposition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFastFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZoneName);
        ArgumentNullException.ThrowIfNull(stateOwnerMaterials);
        ArgumentNullException.ThrowIfNull(ownedAssets);
        ArgumentNullException.ThrowIfNull(constantConfigStringTable);
        ArgumentNullException.ThrowIfNull(scriptStrings);

        MapGameplayMaterialStateOwnerAsset[] copiedStateOwners =
            stateOwnerMaterials.ToArray();
        MapGameplayModelSupportOwnedAsset[] copied =
            ownedAssets.ToArray();
        bool definitionsOnly =
            dependencyDisposition ==
                MapGameplayModelDependencyDisposition
                    .ReportedDefinitionsOnly;
        if (definitionsOnly
                ? copiedStateOwners.Length != 0
                : copiedStateOwners.Length == 0)
        {
            throw new ArgumentException(
                definitionsOnly
                    ? "Reported XModel definitions cannot own historical " +
                      "Material state-owner rows."
                    : "Imported nested dependency closure requires its " +
                      "earlier Material state-owner definitions.",
                nameof(stateOwnerMaterials));
        }
        if (copied.Length == 0)
        {
            throw new ArgumentException(
                "Gameplay-model support cannot be empty.",
                nameof(ownedAssets));
        }

        if (copiedStateOwners.Any(value => value is null))
        {
            throw new ArgumentException(
                "Gameplay Material state owners cannot contain a null asset.",
                nameof(stateOwnerMaterials));
        }
        if (copied.Any(value => value is null))
        {
            throw new ArgumentException(
                "Gameplay-model support cannot contain a null asset.",
                nameof(ownedAssets));
        }
        if (definitionsOnly &&
            copied.Any(value =>
                value.BuildData.MaterialLinks.Count != 0 ||
                value.BuildData.PhysPresetLink is not null ||
                value.BuildData.PhysCollmapLink is not null))
        {
            throw new ArgumentException(
                "Reported XModel definitions must leave Material and " +
                "physics providers external.",
                nameof(ownedAssets));
        }

        if (copiedStateOwners
            .Select(value => value.Key)
            .Distinct()
            .Count() != copiedStateOwners.Length)
        {
            throw new ArgumentException(
                "Gameplay Material state owners cannot contain duplicate " +
                "identities.",
                nameof(stateOwnerMaterials));
        }
        if (copied
            .Select(value => value.Key)
            .Distinct()
            .Count() != copied.Length)
        {
            throw new ArgumentException(
                "Gameplay-model support cannot contain duplicate XModel " +
                "identities.",
                nameof(ownedAssets));
        }

        (int Row, int Ordinal)[] stateOwnerSourceOrder =
            copiedStateOwners
            .Select(value => (
                Row: value.Source.ContainerSerializedIndex,
                Ordinal: value.Source.DefinitionOrdinal))
            .ToArray();
        if (stateOwnerSourceOrder
                .Zip(
                    stateOwnerSourceOrder.Skip(1),
                    (left, right) =>
                        left.Row < right.Row ||
                        left.Row == right.Row &&
                        left.Ordinal < right.Ordinal)
                .Any(inSourceOrder => !inSourceOrder) ||
            stateOwnerSourceOrder.Length != 0 &&
                stateOwnerSourceOrder[^1].Row >=
                    copied[0].Source.SerializedIndex)
        {
            throw new ArgumentException(
                "Gameplay Material state owners must retain strict earlier " +
                "container-row order before the gameplay XModels.",
                nameof(stateOwnerMaterials));
        }
        if (copied
            .Select(value => value.Source.SerializedIndex)
            .Zip(
                copied
                    .Select(value => value.Source.SerializedIndex)
                    .Skip(1),
                (left, right) => left < right)
            .Any(inSourceOrder => !inSourceOrder))
        {
            throw new ArgumentException(
                "Gameplay-model support must retain strict serialized " +
                "source order.",
                nameof(ownedAssets));
        }

        SourceFastFilePath = Path.GetFullPath(sourceFastFilePath);
        SourceSha256 = sourceSha256;
        SourceZoneName = sourceZoneName;
        DependencyDisposition = dependencyDisposition;
        ConstantConfigStringTable = constantConfigStringTable;
        _stateOwnerMaterials =
            new ReadOnlyCollection<MapGameplayMaterialStateOwnerAsset>(
                copiedStateOwners);
        _ownedAssets =
            new ReadOnlyCollection<MapGameplayModelSupportOwnedAsset>(
                copied);
        TargetZoneScriptStringSource[] scriptStringSlots =
            scriptStrings.ToArray();
        if (!scriptStringSlots
            .Select(value => value.Index)
            .SequenceEqual(
                Enumerable.Range(0, scriptStringSlots.Length)))
        {
            throw new ArgumentException(
                "Imported script-string slots must remain contiguous and " +
                "in exact source order.",
                nameof(scriptStrings));
        }

        _scriptStrings = new ReadOnlyCollection<string?>(
            scriptStringSlots
                .Select(value => value.Value)
                .ToArray());
        (ZoneAssetKey Key, IXAssetBuildData BuildData, int SourceIndex,
            int DefinitionOrdinal)[]
            runtimeDefinitions =
            copiedStateOwners
                .Select(value => (
                    Key: value.Key,
                    BuildData: (IXAssetBuildData)value.BuildData,
                    SourceIndex:
                        value.Source.ContainerSerializedIndex,
                    DefinitionOrdinal:
                        value.Source.DefinitionOrdinal))
                .Concat(copied
                .Select(value => (
                    Key: value.Key,
                    BuildData: (IXAssetBuildData)value.BuildData,
                    SourceIndex: value.Source.SerializedIndex,
                    DefinitionOrdinal: -1)))
                .OrderBy(value => value.SourceIndex)
                .ThenBy(value => value.DefinitionOrdinal)
                .ToArray();
        (ZoneAssetKey Key, IXAssetBuildData BuildData, int SourceIndex,
            int DefinitionOrdinal)[]
            allOwnedAssets =
            runtimeDefinitions
                .Append((
                    Key: constantConfigStringTable.Key,
                    BuildData: (IXAssetBuildData)
                        constantConfigStringTable.BuildData,
                    SourceIndex:
                        constantConfigStringTable.Source.SerializedIndex,
                    DefinitionOrdinal: -1))
                .OrderBy(value => value.SourceIndex)
                .ThenBy(value => value.DefinitionOrdinal)
                .ToArray();
        if (allOwnedAssets
            .Select(value => value.Key)
            .Distinct()
            .Count() != allOwnedAssets.Length)
        {
            throw new ArgumentException(
                "Gameplay support cannot contain duplicate package asset " +
                "identities.");
        }
        _ownedAssetKeys =
            new ReadOnlyCollection<ZoneAssetKey>(
                allOwnedAssets
                    .Select(value => value.Key)
                    .ToArray());
        _ownedBuildData =
            new ReadOnlyCollection<IXAssetBuildData>(
                allOwnedAssets
                    .Select(value => value.BuildData)
                    .ToArray());
        _ownedImportedOrders =
            new ReadOnlyCollection<int>(
                Enumerable.Range(0, allOwnedAssets.Length)
                    .ToArray());
        _runtimeDefinitionKeys =
            new ReadOnlyCollection<ZoneAssetKey>(
                runtimeDefinitions
                    .Select(value => value.Key)
                    .ToArray());
        _runtimeDefinitionBuildData =
            new ReadOnlyCollection<IXAssetBuildData>(
                runtimeDefinitions
                    .Select(value => value.BuildData)
                    .ToArray());
    }

    public MapGameplayModelSupportAuthority Authority =>
        MapGameplayModelSupportAuthority
            .OfficialTargetZoneDefinitionImport;

    public string SourceFastFilePath { get; }

    public string SourceSha256 { get; }

    public string SourceZoneName { get; }

    public MapGameplayModelDependencyDisposition
        DependencyDisposition { get; }

    public bool IncludesNestedDependencyDefinitions =>
        DependencyDisposition ==
            MapGameplayModelDependencyDisposition
                .ImportedNestedDependencyClosure;

    /// <summary>
    /// The 53 proven gameplay XModel definitions in official serialized
    /// source order.
    /// </summary>
    public IReadOnlyList<MapGameplayModelSupportOwnedAsset>
        OwnedAssets => _ownedAssets;

    /// <summary>
    /// The exact earlier retail Material definitions whose persistent
    /// GfxStateBits cells are referenced by the gameplay XModel closure.
    /// </summary>
    public IReadOnlyList<MapGameplayMaterialStateOwnerAsset>
        StateOwnerMaterials => _stateOwnerMaterials;

    public MapGameplayConstantConfigStringTableAsset
        ConstantConfigStringTable { get; }

    /// <summary>
    /// Exact source XAssetList script-string slots. Slot positions are part
    /// of the XModel contract because bone-name values are opaque local
    /// scr_string_t indices.
    /// </summary>
    public IReadOnlyList<string?> ScriptStrings => _scriptStrings;

    public bool PreserveImportedScriptStringOrderRequired => true;

    /// <summary>
    /// All source-owned package assets (the six Material state owners, the
    /// game-mode table, and 53 XModels) in official serialized source order.
    /// </summary>
    public IReadOnlyList<ZoneAssetKey> OwnedAssetKeys =>
        _ownedAssetKeys;

    public IReadOnlyList<IXAssetBuildData> OwnedBuildData =>
        _ownedBuildData;

    internal IReadOnlyList<int> OwnedImportedOrders =>
        _ownedImportedOrders;

    /// <summary>
    /// Source-ordered runtime definitions excluding the constant-configstring
    /// row, which is retargeted separately for the generated map checksum.
    /// </summary>
    internal IReadOnlyList<ZoneAssetKey> RuntimeDefinitionKeys =>
        _runtimeDefinitionKeys;

    internal IReadOnlyList<IXAssetBuildData>
        RuntimeDefinitionBuildData => _runtimeDefinitionBuildData;

    internal MapGameplayModelSupportCompilation
        ToReportedDefinitionsOnly()
    {
        if (!IncludesNestedDependencyDefinitions)
        {
            return this;
        }

        return new MapGameplayModelSupportCompilation(
            SourceFastFilePath,
            SourceSha256,
            SourceZoneName,
            [],
            OwnedAssets.Select(value =>
                new MapGameplayModelSupportOwnedAsset(
                    value.Key,
                    new ReportedGameplayXModelBuildData(
                        value.BuildData),
                    value.Source)),
            ConstantConfigStringTable,
            ScriptStrings.Select((value, index) =>
                new TargetZoneScriptStringSource(index, value)),
            MapGameplayModelDependencyDisposition
                .ReportedDefinitionsOnly);
    }
}

/// <summary>
/// Fail-closed target-acceptance proof that binds every imported streamed
/// image identity to its four selected-language DB-header wire records.
/// Source container offsets are deliberately excluded from comparison.
/// </summary>
internal static class MapGameplayImageStreamIntegrityVerifier
{
    private const int EntriesPerImage =
        GfxImageStreamData.EntryCount;

    internal static void RequireLinkedManifest(
        MapGameplayModelSupportCompilation gameplay,
        ZoneLinkRequest request,
        ZoneLinkResult link)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(link);

        ImageBinding[] sourceAuthority =
            CollectSourceAuthority(gameplay);
        Dictionary<ZoneAssetKey, ImageBinding> authorityByKey =
            sourceAuthority.ToDictionary(value => value.ImageKey);
        ImageBinding[] expected =
            CollectExpectedEmissionOrder(
                gameplay,
                request,
                authorityByKey);
        LinkedImageBinding[] actual = link
            .StreamedGfxImageContributions
            .Select(value =>
                CreateLinkedBinding(
                    value.ImageKey,
                    value.SelectedLanguageStreamEntries,
                    "linker contribution"))
            .ToArray();

        RequireExactLinkedManifest(
            expected,
            actual,
            "linker streamed-image manifest");

        ImageStreamWireRecord[] expectedFlat = expected
            .SelectMany(value => value.Entries)
            .ToArray();
        ImageStreamWireRecord[] actualFlat = link
            .SelectedLanguageImageStreamEntries
            .Select(ImageStreamWireRecord.From)
            .ToArray();
        if (!actualFlat.SequenceEqual(expectedFlat) ||
            actualFlat.Length !=
                checked(expected.Length * EntriesPerImage) ||
            link.StreamedGfxImageCount != expected.Length)
        {
            throw new InvalidDataException(
                "The linker flattened selected-language image-stream table " +
                "does not exactly match its ordered identity manifest. " +
                DescribeCounts(expected.Length, actualFlat.Length));
        }
    }

    internal static void RequireReopenedManifest(
        MapGameplayModelSupportCompilation gameplay,
        DbLoadSession loadSession,
        LoadedXZone loaded)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(loadSession);
        ArgumentNullException.ThrowIfNull(loaded);

        ImageBinding[] expected = CollectSourceAuthority(gameplay);
        ImageStreamWireRecord[] expectedFlat = expected
            .SelectMany(value => value.Entries)
            .ToArray();
        ImageStreamWireRecord[] reopenedHeader = loaded.Header
            .ImageStreamEntries
            .Select(ImageStreamWireRecord.From)
            .ToArray();
        if (reopenedHeader.Length !=
                MapGameplayModelSupportImporter
                    .RequiredImageStreamRecordCount ||
            !reopenedHeader.SequenceEqual(expectedFlat))
        {
            throw new InvalidDataException(
                "The reopened target DB header does not retain the exact " +
                "ordered selected-language image-stream table. " +
                DescribeCounts(expected.Length, reopenedHeader.Length));
        }

        (int Index, ImageBinding Binding)[] reopened = loadSession
            .AssetPool
            .Slots
            .Where(slot => slot.AssetType == XAssetType.Image)
            .SelectMany(slot => slot.Providers)
            .Where(provider =>
                provider.Owner == loaded.Context.ZoneOwner &&
                !provider.IsReferencePlaceholder &&
                provider.Asset is GfxImageAsset
                {
                    StreamImageIndex: not null
                })
            .Select(provider =>
            {
                var image = (GfxImageAsset)provider.Asset;
                string name = image.Name ??
                    throw new InvalidDataException(
                        "A reopened streamed GfxImage has no identity.");
                return (
                    Index: image.StreamImageIndex!.Value,
                    Binding: CreateBinding(
                        new ZoneAssetKey(
                            XAssetType.Image,
                            name),
                        image.StreamData.Select(
                            ImageStreamInlineRecord.From),
                        image.StreamEntries,
                        "reopened streamed GfxImage"));
            })
            .OrderBy(value => value.Index)
            .ToArray();
        if (reopened.Length !=
                MapGameplayModelSupportImporter
                    .RequiredStreamedImageCount ||
            !reopened
                .Select(value => value.Index)
                .SequenceEqual(
                    Enumerable.Range(0, expected.Length)))
        {
            throw new InvalidDataException(
                "Reopened streamed GfxImage indices are not the exact " +
                "contiguous candidate emission order.");
        }

        RequireExactManifest(
            expected,
            reopened.Select(value => value.Binding).ToArray(),
            "reopened streamed GfxImage bindings");
    }

    private static ImageBinding[] CollectSourceAuthority(
        MapGameplayModelSupportCompilation gameplay)
    {
        ImageBinding[] bindings = gameplay.StateOwnerMaterials
            .SelectMany(asset =>
                CollectMaterialDefinitions(asset.BuildData))
            .Concat(gameplay.OwnedAssets
                .SelectMany(asset =>
                    CollectModelDefinitions(asset.BuildData)))
            .ToArray();
        if (bindings.Length == 0)
        {
            throw new InvalidDataException(
                "Imported gameplay-model support contains no streamed " +
                "GfxImage definitions.");
        }

        ZoneAssetKey[] duplicates = bindings
            .GroupBy(value => value.ImageKey)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new InvalidDataException(
                "Imported gameplay-model support must bind each streamed " +
                "GfxImage identity exactly once; duplicate identities: " +
                string.Join(", ", duplicates));
        }
        RequireOfficialClosureCardinality(
            gameplay,
            bindings);
        return bindings;
    }

    private static ImageBinding[] CollectExpectedEmissionOrder(
        MapGameplayModelSupportCompilation gameplay,
        ZoneLinkRequest request,
        IReadOnlyDictionary<ZoneAssetKey, ImageBinding> authorityByKey)
    {
        ZoneAssetKey[] expectedDefinitionOrder =
            gameplay.RuntimeDefinitionKeys
            .ToArray();
        HashSet<ZoneAssetKey> expectedDefinitions =
            expectedDefinitionOrder.ToHashSet();
        ZoneAssetEntry[] orderedDefinitionEntries = request
            .GetDeterministicLinkOrder()
            .Where(value => expectedDefinitions.Contains(value.Key))
            .ToArray();
        if (!orderedDefinitionEntries
                .Select(value => value.Key)
                .SequenceEqual(expectedDefinitionOrder))
        {
            throw new InvalidDataException(
                "Official gameplay Material owners and XModels are not " +
                "linked in their exact source order.");
        }

        var expected = new List<ImageBinding>();
        foreach (ZoneAssetEntry entry in orderedDefinitionEntries)
        {
            if (entry.Intent != ZoneAssetReferenceIntent.Owned)
            {
                throw new InvalidDataException(
                    $"Official gameplay support row '{entry.Key}' is not an " +
                    "owned definition.");
            }

            IEnumerable<ImageBinding> emittedDefinitions =
                entry.BuildData switch
                {
                    IMaterialBuildData material =>
                        CollectMaterialDefinitions(material),
                    IXModelBuildData model =>
                        CollectModelDefinitions(model),
                    _ => throw new InvalidDataException(
                        $"Official gameplay support row '{entry.Key}' is " +
                        "neither a Material owner nor an XModel.")
                };
            foreach (ImageBinding emitted in emittedDefinitions)
            {
                if (!authorityByKey.TryGetValue(
                        emitted.ImageKey,
                        out ImageBinding? source) ||
                    !emitted.StreamData.SequenceEqual(
                        source.StreamData) ||
                    !emitted.Entries.SequenceEqual(source.Entries))
                {
                    throw new InvalidDataException(
                        $"Streamed GfxImage '{emitted.ImageKey}' no longer " +
                        "matches its imported source authority.");
                }
                expected.Add(source);
            }
        }

        if (expected.Count != authorityByKey.Count ||
            expected.Select(value => value.ImageKey).Distinct().Count() !=
                authorityByKey.Count ||
            !expected.Select(value => value.ImageKey)
                .ToHashSet()
                .SetEquals(authorityByKey.Keys))
        {
            throw new InvalidDataException(
                "The ordered gameplay Material/XModel graph does not emit " +
                "every imported streamed GfxImage definition exactly once.");
        }
        return expected.ToArray();
    }

    private static IEnumerable<ImageBinding> CollectModelDefinitions(
        IXModelBuildData model)
    {
        foreach (NestedXAssetBuildLink? materialLink in
                 model.MaterialLinks)
        {
            if (!IsDefinition(materialLink))
                continue;
            if (materialLink!.IncomingDefinition is not
                IMaterialBuildData material)
            {
                throw new InvalidDataException(
                    $"Inline/insert Material '{materialLink.Reference}' " +
                    "does not carry Material build data.");
            }

            foreach (ImageBinding binding in
                     CollectMaterialDefinitions(material))
                yield return binding;
        }
    }

    private static IEnumerable<ImageBinding> CollectMaterialDefinitions(
        IMaterialBuildData material)
    {
        foreach (MaterialTextureBuildData texture in
                 material.Textures)
        {
            NestedXAssetBuildLink? imageLink =
                texture.Semantic == 0x0b
                    ? texture.Water?.ImageLink
                    : texture.ImageLink;
            if (!IsDefinition(imageLink))
                continue;
            if (imageLink!.IncomingDefinition is not
                IGfxImageBuildData image)
            {
                throw new InvalidDataException(
                    $"Inline/insert Image '{imageLink.Reference}' does " +
                    "not carry GfxImage build data.");
            }
            if (!image.StreamData.Any(value =>
                    value.HasStreamingData))
            {
                continue;
            }

            ZoneAssetKey referenceKey =
                new ZoneAssetKey(
                    XAssetType.Image,
                    imageLink.Reference.OriginalSerializedName);
            string imageName = image.Name ??
                throw new InvalidDataException(
                    "A streamed GfxImage definition has no name.");
            ZoneAssetKey definitionKey =
                new(
                    XAssetType.Image,
                    imageName);
            if (referenceKey != definitionKey)
            {
                throw new InvalidDataException(
                    "A streamed GfxImage definition does not match its " +
                    $"nested identity: {referenceKey} != " +
                    $"{definitionKey}.");
            }

            IReadOnlyList<EmissionError> diagnostics =
                new GfxImageBodyEmitter().Validate(image);
            if (diagnostics.Count != 0)
            {
                throw new InvalidDataException(
                    $"Streamed GfxImage '{definitionKey}' is invalid: " +
                    string.Join("; ", diagnostics));
            }
            yield return CreateBinding(
                definitionKey,
                image.StreamData.Select(
                    ImageStreamInlineRecord.From),
                image.SelectedLanguageStreamEntries,
                "imported streamed GfxImage");
        }
    }

    private static bool IsDefinition(
        NestedXAssetBuildLink? link) =>
        link is
        {
            SourceForm:
                NestedXAssetPointerSourceForm.Inline or
                NestedXAssetPointerSourceForm.Insert
        };

    private static ImageBinding CreateBinding(
        ZoneAssetKey imageKey,
        IEnumerable<ImageStreamInlineRecord> streamData,
        IEnumerable<DbHeaderImageStreamEntry> entries,
        string source)
    {
        if (imageKey.Type != XAssetType.Image)
        {
            throw new InvalidDataException(
                $"{source} key '{imageKey}' is not an Image.");
        }
        ArgumentNullException.ThrowIfNull(streamData);
        ImageStreamInlineRecord[] normalizedStreamData =
            streamData.ToArray();
        if (normalizedStreamData.Length != EntriesPerImage)
        {
            throw new InvalidDataException(
                $"{source} '{imageKey}' must carry exactly " +
                $"{EntriesPerImage} inline GfxImage StreamData records; " +
                $"observed {normalizedStreamData.Length}.");
        }
        ImageStreamWireRecord[] normalized = entries
            .Select(ImageStreamWireRecord.From)
            .ToArray();
        if (normalized.Length != EntriesPerImage)
        {
            throw new InvalidDataException(
                $"{source} '{imageKey}' must bind exactly " +
                $"{EntriesPerImage} DB-header records; observed " +
                $"{normalized.Length}.");
        }
        return new ImageBinding(
            imageKey,
            Array.AsReadOnly(normalizedStreamData),
            Array.AsReadOnly(normalized));
    }

    private static LinkedImageBinding CreateLinkedBinding(
        ZoneAssetKey imageKey,
        IEnumerable<DbHeaderImageStreamEntry> entries,
        string source)
    {
        if (imageKey.Type != XAssetType.Image)
        {
            throw new InvalidDataException(
                $"{source} key '{imageKey}' is not an Image.");
        }
        ImageStreamWireRecord[] normalized = entries
            .Select(ImageStreamWireRecord.From)
            .ToArray();
        if (normalized.Length != EntriesPerImage)
        {
            throw new InvalidDataException(
                $"{source} '{imageKey}' must bind exactly " +
                $"{EntriesPerImage} DB-header records; observed " +
                $"{normalized.Length}.");
        }
        return new LinkedImageBinding(
            imageKey,
            Array.AsReadOnly(normalized));
    }

    private static void RequireExactLinkedManifest(
        IReadOnlyList<ImageBinding> expected,
        IReadOnlyList<LinkedImageBinding> actual,
        string subject)
    {
        int shared = Math.Min(expected.Count, actual.Count);
        int mismatch = -1;
        for (int index = 0; index < shared; index++)
        {
            if (expected[index].ImageKey != actual[index].ImageKey ||
                !expected[index].Entries.SequenceEqual(
                    actual[index].Entries))
            {
                mismatch = index;
                break;
            }
        }
        if (mismatch < 0 && expected.Count != actual.Count)
            mismatch = shared;
        if (mismatch < 0)
            return;

        string expectedValue = mismatch < expected.Count
            ? expected[mismatch].ImageKey.ToString()
            : "<end>";
        string actualValue = mismatch < actual.Count
            ? actual[mismatch].ImageKey.ToString()
            : "<end>";
        throw new InvalidDataException(
            $"{subject} differs at image index {mismatch}: expected " +
            $"'{expectedValue}', observed '{actualValue}'. " +
            DescribeCounts(
                expected.Count,
                checked(actual.Count * EntriesPerImage)));
    }

    private static void RequireExactManifest(
        IReadOnlyList<ImageBinding> expected,
        IReadOnlyList<ImageBinding> actual,
        string subject)
    {
        int mismatch = FirstMismatch(expected, actual);
        if (mismatch < 0)
            return;

        string expectedValue = mismatch < expected.Count
            ? expected[mismatch].ImageKey.ToString()
            : "<end>";
        string actualValue = mismatch < actual.Count
            ? actual[mismatch].ImageKey.ToString()
            : "<end>";
        throw new InvalidDataException(
            $"{subject} differs at image index {mismatch}: expected " +
            $"'{expectedValue}', observed '{actualValue}'. " +
            DescribeCounts(
                expected.Count,
                checked(actual.Count * EntriesPerImage)));
    }

    private static int FirstMismatch(
        IReadOnlyList<ImageBinding> expected,
        IReadOnlyList<ImageBinding> actual)
    {
        int shared = Math.Min(expected.Count, actual.Count);
        for (int index = 0; index < shared; index++)
        {
            if (expected[index].ImageKey != actual[index].ImageKey ||
                !expected[index].StreamData.SequenceEqual(
                    actual[index].StreamData) ||
                !expected[index].Entries.SequenceEqual(
                    actual[index].Entries))
            {
                return index;
            }
        }
        return expected.Count == actual.Count ? -1 : shared;
    }

    private static void RequireOfficialClosureCardinality(
        MapGameplayModelSupportCompilation gameplay,
        IReadOnlyCollection<ImageBinding> bindings)
    {
        if (!string.Equals(
                gameplay.SourceSha256,
                MapGameplayModelSupportImporter.OfficialSourceSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Streamed-image closure cardinality is authoritative only " +
                "for the exact SHA-gated official mp_terminal source.");
        }

        int recordCount = bindings.Sum(value => value.Entries.Count);
        if (bindings.Count !=
                MapGameplayModelSupportImporter
                    .RequiredStreamedImageCount ||
            recordCount !=
                MapGameplayModelSupportImporter
                    .RequiredImageStreamRecordCount)
        {
            throw new InvalidDataException(
                "The exact official mp_terminal streamed-image closure must " +
                $"contain {MapGameplayModelSupportImporter.RequiredStreamedImageCount} " +
                "unique image identities and " +
                $"{MapGameplayModelSupportImporter.RequiredImageStreamRecordCount} " +
                $"selected-language records; observed {bindings.Count} " +
                $"and {recordCount}.");
        }
    }

    private static string DescribeCounts(
        int imageCount,
        int entryCount) =>
        $"Derived expectation: {imageCount} image(s), " +
        $"{checked(imageCount * EntriesPerImage)} record(s); observed " +
        $"{entryCount} record(s).";

    private sealed record ImageBinding(
        ZoneAssetKey ImageKey,
        IReadOnlyList<ImageStreamInlineRecord> StreamData,
        IReadOnlyList<ImageStreamWireRecord> Entries);

    private sealed record LinkedImageBinding(
        ZoneAssetKey ImageKey,
        IReadOnlyList<ImageStreamWireRecord> Entries);

    private readonly record struct ImageStreamInlineRecord(
        ushort Width,
        ushort Height,
        uint LevelSizeAndOffset)
    {
        internal static ImageStreamInlineRecord From(
            GfxImageStreamBuildData value) =>
            new(
                value.Width,
                value.Height,
                value.LevelSizeAndOffset);

        internal static ImageStreamInlineRecord From(
            GfxImageStreamData value) =>
            new(
                value.Width,
                value.Height,
                value.LevelSizeAndOffset);
    }

    private readonly record struct ImageStreamWireRecord(
        uint FileIndex,
        uint SourceStart,
        uint SourceEnd,
        uint BlockOffset,
        uint StreamOffset)
    {
        internal static ImageStreamWireRecord From(
            DbHeaderImageStreamEntry value) =>
            new(
                value.FileIndex,
                value.SourceStart,
                value.SourceEnd,
                value.BlockOffset,
                value.StreamOffset);
    }
}

/// <summary>
/// Bounded source importer for the exact mp_terminal gameplay XModels and
/// their complete earlier Material state-owner closure proven necessary by
/// retail startup diagnostics.
/// </summary>
public static class MapGameplayModelSupportImporter
{
    public const string ImporterIdentity =
        "iw4-studio.target-acceptance.map-gameplay-model-support." +
        "mp-terminal-official-source@1";

    public const int RequiredModelCount = 53;

    public const int RequiredStateOwnerMaterialCount = 6;

    /// <summary>
    /// Exact closure observed only in the SHA-gated official mp_terminal.
    /// These are acceptance constants, not general IW4 format limits.
    /// </summary>
    public const int RequiredStreamedImageCount = 206;

    public const int RequiredImageStreamRecordCount =
        RequiredStreamedImageCount *
        GfxImageStreamData.EntryCount;

    private const string SourceZoneName = "mp_terminal";

    public const string OfficialSourceSha256 =
        "a5c1af63685ac3bcbdec37c4c9d88fca58f5cd0c960d7d7f246edf09bbd052f0";

    public const string ConstantConfigStringTableName =
        "mp/configstrings/configstrings_ps3_mp_terminal_dm.csv";

    private static readonly IReadOnlyList<StateOwnerManifestEntry>
        RequiredStateOwnerManifest =
            Array.AsReadOnly<StateOwnerManifestEntry>(
            [
                new(
                    67,
                    0,
                    "props/plant_large_ground_splat",
                    "mc/gfx_dirt"),
                new(
                    70,
                    3,
                    "props/plant_large_destroy",
                    "m/mtl_flower_pot_large"),
                new(
                    70,
                    4,
                    "props/plant_large_destroy",
                    "m/mtl_potted_plant01"),
                new(
                    80,
                    0,
                    "props/clothes_sweater_natural_landed",
                    "m/mtl_hanging_clothes_palepink"),
                new(
                    88,
                    1,
                    "props/luggage_contents",
                    "m/mtl_trash_bottles_plastic_outside"),
                new(
                    88,
                    3,
                    "props/luggage_contents",
                    "m/mtl_trash_bottles_plastic_inside")
            ]);

    private static readonly IReadOnlyList<string> RequiredModelNames =
        Array.AsReadOnly(
        [
            "mp_body_us_army_assault_a",
            "head_us_army_a",
            "head_us_army_b",
            "head_us_army_c",
            "head_us_army_d",
            "head_us_army_f",
            "viewhands_us_army",
            "mp_body_us_army_assault_b",
            "mp_body_us_army_assault_c",
            "mp_body_us_army_lmg",
            "mp_body_us_army_lmg_b",
            "mp_body_us_army_lmg_c",
            "mp_body_us_army_shotgun",
            "mp_body_us_army_shotgun_b",
            "mp_body_us_army_shotgun_c",
            "mp_body_us_army_smg",
            "mp_body_us_army_smg_b",
            "mp_body_us_army_smg_c",
            "mp_body_army_sniper",
            "head_allies_us_army_sniper",
            "viewhands_sniper_us_army",
            "mp_body_us_army_riot",
            "head_us_army_e",
            "com_plasticcase_rangers",
            "mp_body_airborne_assault_a",
            "head_airborne_a",
            "head_airborne_b",
            "head_airborne_c",
            "head_airborne_d",
            "head_airborne_e",
            "viewhands_russian_airborne",
            "mp_body_airborne_assault_b",
            "mp_body_airborne_assault_c",
            "mp_body_airborne_lmg",
            "mp_body_airborne_lmg_b",
            "mp_body_airborne_lmg_c",
            "mp_body_airborne_shotgun",
            "mp_body_airborne_shotgun_b",
            "mp_body_airborne_shotgun_c",
            "mp_body_airborne_smg",
            "mp_body_airborne_smg_b",
            "mp_body_airborne_smg_c",
            "mp_body_op_airborne_sniper",
            "head_op_airborne_sniper",
            "viewhands_sniper_op_airborne",
            "mp_body_riot_op_airborne",
            "head_riot_op_airborne",
            "com_plasticcase_ussr",
            "mp_body_ally_sniper_ghillie_urban",
            "head_allies_sniper_ghillie_urban",
            "viewhands_ghillie_urban",
            "mp_body_op_sniper_ghillie_urban",
            "head_op_sniper_ghillie_urban"
        ]);

    /// <summary>
    /// Opens only the caller-selected official mp_terminal fastfile and
    /// imports the exact observed gameplay XModel definition set. No default
    /// path is inferred and no dependency zone may satisfy a missing model.
    /// </summary>
    public static MapGameplayModelSupportCompilation ImportMpTerminal(
        string officialMpTerminalFastFilePath)
    {
        (string sourcePath, string sourceSha256) =
            RequireOfficialSourcePath(
            officialMpTerminalFastFilePath);
        FastFileWorkspace workspace =
            new FastFileDocumentService().Open(
                new FastFileDocumentOpenRequest(
                    sourcePath,
                    Isolated.Instance));
        TargetZoneSourceSnapshot source = workspace.TargetSource;
        if (!string.Equals(
                source.LogicalZoneName,
                SourceZoneName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Gameplay-model support requires logical zone " +
                $"'{SourceZoneName}', but '{source.LogicalZoneName}' was " +
                "opened.");
        }

        HashSet<ZoneAssetKey> requiredKeys = RequiredModelNames
            .Select(name => new ZoneAssetKey(XAssetType.XModel, name))
            .ToHashSet();
        (TargetZoneRowSource Source, ZoneAssetKey Key)[] selected =
            source.Rows
                .Where(row =>
                    row.SerializedType == XAssetType.XModel &&
                    row.NormalizedKey is { } normalizedName &&
                    requiredKeys.Contains(
                        new ZoneAssetKey(
                            XAssetType.XModel,
                            normalizedName)))
                .Select(row => (
                    Source: row,
                    Key: new ZoneAssetKey(
                        XAssetType.XModel,
                        row.NormalizedKey!)))
                .ToArray();

        RequireExactDefinitionSet(selected, requiredKeys);

        var adapter = new XModelAuthoringAdapter();
        MapGameplayModelSupportOwnedAsset[] ownedAssets = selected
            .Select(value =>
            {
                TargetZoneRowSource row = value.Source;
                XModelAuthoredSnapshot authored =
                    adapter.ImportAuthoredSnapshot(row);
                XModelDraft draft = adapter.CreateDraft(authored);
                XModelBuildData buildData =
                    adapter.ExportBuildData(draft);
                string originalName =
                    row.OriginalSerializedName ??
                    throw new InvalidDataException(
                        $"Official XModel row {row.SerializedIndex} has " +
                        "no original serialized name.");
                return new MapGameplayModelSupportOwnedAsset(
                    value.Key,
                    buildData,
                    new MapGameplayModelSupportSourceProvenance(
                        row.SerializedIndex,
                        row.HeaderKind,
                        row.RawHeader,
                        originalName));
            })
            .ToArray();
        MapGameplayMaterialStateOwnerAsset[] stateOwnerMaterials =
            ImportStateOwnerMaterials(
                source,
                ownedAssets);
        RequireCompleteMaterialLoadBitsAliasClosure(
            stateOwnerMaterials,
            ownedAssets);
        MapGameplayConstantConfigStringTableAsset
            constantConfigStringTable =
                ImportConstantConfigStringTable(source);

        return new MapGameplayModelSupportCompilation(
            sourcePath,
            sourceSha256,
            source.LogicalZoneName,
            stateOwnerMaterials,
            ownedAssets,
            constantConfigStringTable,
            source.ScriptStrings,
            MapGameplayModelDependencyDisposition
                .ImportedNestedDependencyClosure);
    }

    /// <summary>
    /// Imports the exact SHA-gated XModel definition set reported by the
    /// baseline target probe while leaving Materials, physics assets, and
    /// their transitive graphs external. The minimal generated map's own
    /// two-row config-string table remains unchanged.
    /// </summary>
    public static MapGameplayModelSupportCompilation
        ImportReportedDefinitionsOnly(
            string officialMpTerminalFastFilePath) =>
        ImportMpTerminal(officialMpTerminalFastFilePath)
            .ToReportedDefinitionsOnly();

    private static MapGameplayMaterialStateOwnerAsset[]
        ImportStateOwnerMaterials(
            TargetZoneSourceSnapshot source,
            IReadOnlyList<MapGameplayModelSupportOwnedAsset> models)
    {
        Dictionary<int, MaterialOwnerCell> ownerCells =
            IndexMaterialOwnerCells(source.MaterialDefinitions);
        HashSet<int> modelOwnerTokens =
            CollectModelOwnerTokens(models);
        var requiredDefinitions =
            new HashSet<TargetZoneMaterialDefinitionSource>(
                ReferenceEqualityComparer.Instance);
        var activeDefinitions =
            new HashSet<TargetZoneMaterialDefinitionSource>(
                ReferenceEqualityComparer.Instance);

        foreach ((ZoneAssetKey modelKey, IMaterialBuildData material) in
                 EnumerateModelMaterialDefinitions(models))
        {
            for (int stateIndex = 0;
                 stateIndex < material.StateBits.Count;
                 stateIndex++)
            {
                MaterialStateBitsBuildData state =
                    material.StateBits[stateIndex];
                if (state.LoadBits.Count == 0)
                    continue;
                MaterialLoadBitsLinkerProvenance provenance =
                    RequireStateProvenance(
                        modelKey,
                        material,
                        stateIndex,
                        state);
                if (provenance.SourceForm ==
                        MaterialLoadBitsPointerSourceForm.PackedAlias)
                {
                    RequireExternalOwner(
                        provenance.TargetAlias!.Value.Value);
                }
            }
        }

        TargetZoneMaterialDefinitionSource[] ordered =
            requiredDefinitions
                .OrderBy(value => value.ContainerSerializedIndex)
                .ThenBy(value => value.DefinitionOrdinal)
                .ToArray();
        RequireExactStateOwnerManifest(source, ordered);
        return ordered
            .Select(value =>
                CreateStateOwnerMaterial(source, value))
            .ToArray();

        void RequireExternalOwner(int targetToken)
        {
            if (modelOwnerTokens.Contains(targetToken))
                return;
            if (!ownerCells.TryGetValue(
                    targetToken,
                    out MaterialOwnerCell? owner))
            {
                throw new InvalidDataException(
                    $"Gameplay XModel state targets unknown Material " +
                    $"loadBits owner token 0x{targetToken:X8}.");
            }
            VisitDefinition(owner.Definition);
        }

        void VisitDefinition(
            TargetZoneMaterialDefinitionSource definition)
        {
            if (requiredDefinitions.Contains(definition))
                return;
            if (!activeDefinitions.Add(definition))
            {
                throw new InvalidDataException(
                    $"Gameplay Material owner closure contains a cycle at " +
                    $"'{definition.Key}'.");
            }

            for (int stateIndex = 0;
                 stateIndex < definition.BuildData.StateBits.Count;
                 stateIndex++)
            {
                MaterialStateBitsBuildData state =
                    definition.BuildData.StateBits[stateIndex];
                if (state.LoadBits.Count == 0)
                    continue;
                MaterialLoadBitsLinkerProvenance provenance =
                    RequireStateProvenance(
                        definition.Key,
                        definition.BuildData,
                        stateIndex,
                        state);
                if (provenance.SourceForm !=
                        MaterialLoadBitsPointerSourceForm.PackedAlias)
                {
                    continue;
                }

                int targetToken =
                    provenance.TargetAlias!.Value.Value;
                if (!ownerCells.TryGetValue(
                        targetToken,
                        out MaterialOwnerCell? targetOwner))
                {
                    throw new InvalidDataException(
                        $"Gameplay Material owner '{definition.Key}' " +
                        $"stateBits[{stateIndex}] targets unknown loadBits " +
                        $"owner token 0x{targetToken:X8}.");
                }
                if (ReferenceEquals(
                        targetOwner.Definition,
                        definition))
                {
                    if (targetOwner.StateIndex >= stateIndex)
                    {
                        throw new InvalidDataException(
                            $"Gameplay Material owner '{definition.Key}' " +
                            $"stateBits[{stateIndex}] targets a non-earlier " +
                            "state in the same definition.");
                    }
                    continue;
                }
                if (modelOwnerTokens.Contains(targetToken))
                {
                    throw new InvalidDataException(
                        $"Earlier gameplay Material owner " +
                        $"'{definition.Key}' depends on a state cell owned " +
                        "inside the later selected XModel closure.");
                }
                VisitDefinition(targetOwner.Definition);
            }

            activeDefinitions.Remove(definition);
            requiredDefinitions.Add(definition);
        }
    }

    private static Dictionary<int, MaterialOwnerCell>
        IndexMaterialOwnerCells(
            IReadOnlyList<TargetZoneMaterialDefinitionSource>
                definitions)
    {
        var result = new Dictionary<int, MaterialOwnerCell>();
        foreach (TargetZoneMaterialDefinitionSource definition in
                 definitions)
        {
            for (int stateIndex = 0;
                 stateIndex < definition.BuildData.StateBits.Count;
                 stateIndex++)
            {
                MaterialStateBitsBuildData state =
                    definition.BuildData.StateBits[stateIndex];
                if (state.LoadBits.Count == 0)
                    continue;
                MaterialLoadBitsLinkerProvenance provenance =
                    RequireStateProvenance(
                        definition.Key,
                        definition.BuildData,
                        stateIndex,
                        state);
                int ownerToken =
                    provenance.OwnerAlias!.Value.Value;
                var indexed = new MaterialOwnerCell(
                    definition,
                    stateIndex);
                if (result.TryGetValue(
                        ownerToken,
                        out MaterialOwnerCell? existing))
                {
                    if (!ReferenceEquals(
                            existing.Definition,
                            definition) ||
                        existing.StateIndex != stateIndex)
                    {
                        throw new InvalidDataException(
                            $"Material loadBits owner token " +
                            $"0x{ownerToken:X8} is claimed by multiple " +
                            "detached source definitions.");
                    }
                    continue;
                }
                result.Add(ownerToken, indexed);
            }
        }
        return result;
    }

    private static HashSet<int> CollectModelOwnerTokens(
        IReadOnlyList<MapGameplayModelSupportOwnedAsset> models)
    {
        var result = new HashSet<int>();
        foreach ((ZoneAssetKey modelKey, IMaterialBuildData material) in
                 EnumerateModelMaterialDefinitions(models))
        {
            for (int stateIndex = 0;
                 stateIndex < material.StateBits.Count;
                 stateIndex++)
            {
                MaterialStateBitsBuildData state =
                    material.StateBits[stateIndex];
                if (state.LoadBits.Count == 0)
                    continue;
                MaterialLoadBitsLinkerProvenance provenance =
                    RequireStateProvenance(
                        modelKey,
                        material,
                        stateIndex,
                        state);
                result.Add(provenance.OwnerAlias!.Value.Value);
            }
        }
        return result;
    }

    private static IEnumerable<(
        ZoneAssetKey ModelKey,
        IMaterialBuildData Material)>
        EnumerateModelMaterialDefinitions(
            IEnumerable<MapGameplayModelSupportOwnedAsset> models)
    {
        foreach (MapGameplayModelSupportOwnedAsset model in models)
        {
            for (int materialIndex = 0;
                 materialIndex <
                    model.BuildData.MaterialLinks.Count;
                 materialIndex++)
            {
                NestedXAssetBuildLink? link =
                    model.BuildData.MaterialLinks[materialIndex];
                if (link is not
                    {
                        SourceForm:
                            NestedXAssetPointerSourceForm.Inline or
                            NestedXAssetPointerSourceForm.Insert
                    })
                {
                    continue;
                }
                if (link.IncomingDefinition is not
                    IMaterialBuildData material)
                {
                    throw new InvalidDataException(
                        $"Gameplay XModel '{model.Key}' material " +
                        $"definition {materialIndex} has no detached " +
                        "Material build data.");
                }
                yield return (model.Key, material);
            }
        }
    }

    private static MaterialLoadBitsLinkerProvenance
        RequireStateProvenance(
            ZoneAssetKey ownerKey,
            IMaterialBuildData material,
            int stateIndex,
            MaterialStateBitsBuildData state)
    {
        MaterialLoadBitsLinkerProvenance provenance =
            state.LinkerProvenance ??
            MaterialLoadBitsLinkerProvenance.Empty;
        if (provenance.TargetAlias is null ||
            provenance.OwnerAlias is null)
        {
            throw new InvalidDataException(
                $"Gameplay support '{ownerKey}' Material " +
                $"'{material.Name}' stateBits[{stateIndex}] has incomplete " +
                "detached loadBits ownership provenance.");
        }
        return provenance;
    }

    private static void RequireExactStateOwnerManifest(
        TargetZoneSourceSnapshot source,
        IReadOnlyList<TargetZoneMaterialDefinitionSource> definitions)
    {
        if (definitions.Count !=
                RequiredStateOwnerMaterialCount ||
            definitions.Count !=
                RequiredStateOwnerManifest.Count)
        {
            throw new InvalidDataException(
                "The exact official gameplay Material state-owner manifest " +
                $"must contain {RequiredStateOwnerMaterialCount} entries; " +
                $"observed {definitions.Count}.");
        }

        for (int index = 0; index < definitions.Count; index++)
        {
            TargetZoneMaterialDefinitionSource definition =
                definitions[index];
            StateOwnerManifestEntry expected =
                RequiredStateOwnerManifest[index];
            TargetZoneRowSource container =
                source.Rows[definition.ContainerSerializedIndex];
            ZoneAssetKey expectedKey = new(
                XAssetType.Material,
                expected.MaterialName);
            if (definition.ContainerSerializedIndex !=
                    expected.ContainerSerializedIndex ||
                definition.DefinitionOrdinal !=
                    expected.DefinitionOrdinal ||
                definition.Key != expectedKey ||
                container.SerializedType != XAssetType.Fx ||
                container.State !=
                    TargetZoneRowSourceState.Definition ||
                container.AuthoredDefinition?.SemanticSnapshot is not
                    FxAuthoredSnapshot ||
                !string.Equals(
                    container.NormalizedKey,
                    expected.ContainerAssetName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Official gameplay Material state-owner manifest " +
                    $"entry {index} no longer matches " +
                    $"row {expected.ContainerSerializedIndex}, ordinal " +
                    $"{expected.DefinitionOrdinal}, Material " +
                    $"'{expected.MaterialName}'.");
            }
        }
    }

    private static MapGameplayMaterialStateOwnerAsset
        CreateStateOwnerMaterial(
            TargetZoneSourceSnapshot source,
            TargetZoneMaterialDefinitionSource definition)
    {
        TargetZoneRowSource container =
            source.Rows[definition.ContainerSerializedIndex];
        string containerName =
            container.OriginalSerializedName ??
            throw new InvalidDataException(
                $"Official source row {container.SerializedIndex} has no " +
                "original serialized name.");
        return new MapGameplayMaterialStateOwnerAsset(
            definition.Key,
            definition.BuildData,
            new MapGameplayMaterialStateOwnerSourceProvenance(
                definition.ContainerSerializedIndex,
                definition.DefinitionOrdinal,
                container.HeaderKind,
                container.RawHeader,
                containerName));
    }

    private static void RequireCompleteMaterialLoadBitsAliasClosure(
        IReadOnlyList<MapGameplayMaterialStateOwnerAsset> stateOwners,
        IReadOnlyList<MapGameplayModelSupportOwnedAsset> models)
    {
        var availableOwnerCells = new HashSet<int>();
        foreach (MapGameplayMaterialStateOwnerAsset owner in stateOwners)
        {
            AdvanceMaterialLoadBitsAliases(
                owner.Key,
                owner.BuildData,
                availableOwnerCells);
        }

        if (models.Count == 0)
        {
            throw new InvalidDataException(
                "Gameplay XModel support is empty.");
        }

        foreach ((ZoneAssetKey modelKey, IMaterialBuildData material) in
                 EnumerateModelMaterialDefinitions(models))
        {
            AdvanceMaterialLoadBitsAliases(
                modelKey,
                material,
                availableOwnerCells);
        }
    }

    private static void AdvanceMaterialLoadBitsAliases(
        ZoneAssetKey ownerKey,
        IMaterialBuildData material,
        ISet<int> availableOwnerCells)
    {
        for (int stateIndex = 0;
             stateIndex < material.StateBits.Count;
             stateIndex++)
        {
            MaterialStateBitsBuildData state =
                material.StateBits[stateIndex];
            if (state.LoadBits.Count == 0)
                continue;

            MaterialLoadBitsLinkerProvenance provenance =
                RequireStateProvenance(
                    ownerKey,
                    material,
                    stateIndex,
                    state);
            if (provenance.SourceForm ==
                    MaterialLoadBitsPointerSourceForm.PackedAlias &&
                (provenance.TargetAlias is not { } target ||
                 !availableOwnerCells.Contains(target.Value)))
            {
                throw new InvalidDataException(
                    $"Gameplay support '{ownerKey}' Material " +
                    $"'{material.Name}' stateBits[{stateIndex}] targets a " +
                    "GfxStateBits alias before its persistent owner cell is " +
                    "emitted.");
            }

            if (provenance.SourceForm is
                    MaterialLoadBitsPointerSourceForm.Inline or
                    MaterialLoadBitsPointerSourceForm.Insert)
            {
                availableOwnerCells.Add(
                    provenance.TargetAlias!.Value.Value);
            }
            availableOwnerCells.Add(
                provenance.OwnerAlias!.Value.Value);
        }
    }

    private sealed record MaterialOwnerCell(
        TargetZoneMaterialDefinitionSource Definition,
        int StateIndex);

    private readonly record struct StateOwnerManifestEntry(
        int ContainerSerializedIndex,
        int DefinitionOrdinal,
        string ContainerAssetName,
        string MaterialName);

    private static (string Path, string Sha256)
        RequireOfficialSourcePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string fullPath = Path.GetFullPath(value);
        if (!string.Equals(
                Path.GetFileName(fullPath),
                $"{SourceZoneName}.ff",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Gameplay-model support requires an explicit " +
                $"'{SourceZoneName}.ff' source path.",
                nameof(value));
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The official gameplay-model support source was not found.",
                fullPath);
        }

        using FileStream source = File.OpenRead(fullPath);
        string sha256 = Convert.ToHexString(
                SHA256.HashData(source))
            .ToLowerInvariant();
        if (!string.Equals(
                sha256,
                OfficialSourceSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Gameplay-model support source does not match the exact " +
                "official mp_terminal fastfile evidence. Expected SHA-256 " +
                $"'{OfficialSourceSha256}', but observed '{sha256}'.");
        }

        return (fullPath, sha256);
    }

    private static MapGameplayConstantConfigStringTableAsset
        ImportConstantConfigStringTable(
            TargetZoneSourceSnapshot source)
    {
        ZoneAssetKey expectedKey = new(
            XAssetType.StringTable,
            ConstantConfigStringTableName);
        TargetZoneRowSource[] matchingRows = source.Rows
            .Where(row =>
                row.SerializedType == XAssetType.StringTable &&
                row.NormalizedKey is { } normalizedName &&
                new ZoneAssetKey(
                    XAssetType.StringTable,
                    normalizedName) == expectedKey)
            .ToArray();
        if (matchingRows.Length != 1)
        {
            throw new InvalidDataException(
                "Official mp_terminal must contain exactly one full " +
                $"'{ConstantConfigStringTableName}' definition; observed " +
                $"{matchingRows.Length}.");
        }

        TargetZoneRowSource row = matchingRows[0];
        if (row.State != TargetZoneRowSourceState.Definition ||
            row.AuthoredDefinition?.SemanticSnapshot is not
                StringTableAuthoredSnapshot)
        {
            throw new InvalidDataException(
                "The mp_terminal free-for-all constant-configstring table " +
                "must be an owned detached definition.");
        }

        var adapter = new StringTableAuthoringAdapter();
        StringTableAuthoredSnapshot authored =
            adapter.ImportAuthoredSnapshot(row);
        StringTableDraft draft = adapter.CreateDraft(authored);
        StringTableBuildData buildData =
            adapter.ExportBuildData(draft);
        string originalName =
            row.OriginalSerializedName ??
            throw new InvalidDataException(
                "The mp_terminal free-for-all constant-configstring table " +
                "has no original serialized name.");
        return new MapGameplayConstantConfigStringTableAsset(
            expectedKey,
            buildData,
            new MapGameplayModelSupportSourceProvenance(
                row.SerializedIndex,
                row.HeaderKind,
                row.RawHeader,
                originalName));
    }

    private static void RequireExactDefinitionSet(
        IReadOnlyList<(
            TargetZoneRowSource Source,
            ZoneAssetKey Key)> selected,
        IReadOnlySet<ZoneAssetKey> requiredKeys)
    {
        ZoneAssetKey[] duplicateKeys = selected
            .GroupBy(value => value.Key)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
            .ToArray();
        if (duplicateKeys.Length != 0)
        {
            throw new InvalidDataException(
                "Official mp_terminal contains duplicate required XModel " +
                $"definitions: {string.Join(", ", duplicateKeys)}.");
        }

        ZoneAssetKey[] selectedKeys =
            selected.Select(value => value.Key).ToArray();
        ZoneAssetKey[] missingKeys = requiredKeys
            .Except(selectedKeys)
            .OrderBy(value => value.LogicalName, StringComparer.Ordinal)
            .ToArray();
        if (missingKeys.Length != 0 ||
            selected.Count != RequiredModelCount)
        {
            throw new InvalidDataException(
                "Official mp_terminal is missing required gameplay XModel " +
                $"definitions: {string.Join(", ", missingKeys)}.");
        }

        TargetZoneRowSource[] invalidRows = selected
            .Select(value => value.Source)
            .Where(row =>
                row.State != TargetZoneRowSourceState.Definition ||
                row.AuthoredDefinition?.SemanticSnapshot is not
                    XModelAuthoredSnapshot)
            .ToArray();
        if (invalidRows.Length != 0)
        {
            throw new InvalidDataException(
                "Gameplay-model support requires owned detached XModel " +
                "definitions; invalid serialized rows: " +
                string.Join(
                    ", ",
                    invalidRows.Select(row => row.SerializedIndex)));
        }
    }
}
