using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Database;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.Database;

namespace IW4.Studio.Documents;

/// <summary>Stable identity for one serialized target-zone row.</summary>
public readonly record struct TargetZoneRowIdentity
{
    public TargetZoneRowIdentity(Guid documentId, int serializedIndex)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (serializedIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(serializedIndex));

        DocumentId = documentId;
        SerializedIndex = serializedIndex;
    }

    public Guid DocumentId { get; }
    public int SerializedIndex { get; }
}

public enum TargetZoneRowSourceState
{
    Definition,
    ResolvedReference,
    UnresolvedReference,
    OffsetAlias,
    Null,
    OpaqueNativeNoOp,
    Unsupported
}

public sealed record TargetZoneScriptStringSource(int Index, string? Value);

/// <summary>Container values needed when linking the next decoded zone.</summary>
public sealed record TargetZoneDecodedZoneMetadata(
    IReadOnlyList<uint>? BlockSizeFloors = null,
    uint ExternalSize = 0);

public sealed record TargetZoneExternalReferenceIdentity(
    XAssetStableIdentity Identity,
    string OriginalSerializedName);

/// <summary>
/// One detached Material definition captured while consuming a top-level
/// source row. The within-row ordinal is the deterministic depth-first capture
/// order. Together with the container row it provides source ordering without
/// retaining runtime addresses or loader-owned assets.
/// </summary>
public sealed class TargetZoneMaterialDefinitionSource
{
    internal TargetZoneMaterialDefinitionSource(
        TargetZoneRowIdentity containerRow,
        int definitionOrdinal,
        MaterialBuildData buildData)
    {
        if (definitionOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionOrdinal));
        }
        ArgumentNullException.ThrowIfNull(buildData);
        string name = buildData.Name ??
            throw new InvalidDataException(
                "A captured Material definition has no identity.");
        ContainerRow = containerRow;
        DefinitionOrdinal = definitionOrdinal;
        Key = ZoneAssetKey.FromWireName(
            XAssetType.Material,
            name);
        BuildData = buildData;
    }

    public TargetZoneRowIdentity ContainerRow { get; }

    public int ContainerSerializedIndex =>
        ContainerRow.SerializedIndex;

    public int DefinitionOrdinal { get; }

    public ZoneAssetKey Key { get; }

    public MaterialBuildData BuildData { get; }
}

/// <summary>
/// Detached editable data. Implementations contain values only and never
/// retain runtime assets, pool addresses, or loader state.
/// </summary>
public interface ITargetZoneDetachedSemanticSnapshot
{
    XAssetType AssetType { get; }
}

/// <summary>Detached editable definition for one target row.</summary>
public sealed class TargetZoneAuthoredDefinitionSource
{
    internal TargetZoneAuthoredDefinitionSource(
        ITargetZoneDetachedSemanticSnapshot? semanticSnapshot)
    {
        if (semanticSnapshot is not null && !Enum.IsDefined(semanticSnapshot.AssetType))
        {
            throw new InvalidDataException(
                "Detached semantic data has an invalid serialized asset type.");
        }

        SemanticSnapshot = semanticSnapshot;
    }

    public ITargetZoneDetachedSemanticSnapshot? SemanticSnapshot { get; }
}

/// <summary>
/// Immutable source identity and detached editable data for one target row.
/// </summary>
public sealed class TargetZoneRowSource
{
    internal TargetZoneRowSource(
        TargetZoneRowIdentity identity,
        XAssetType serializedType,
        int rawHeader,
        XAssetHeaderKind headerKind,
        string? originalSerializedName,
        XAssetStableIdentity? stableIdentity,
        TargetZoneExternalReferenceIdentity? externalReference,
        TargetZoneAuthoredDefinitionSource? authoredDefinition,
        TargetZoneRowSourceState state)
    {
        Identity = identity;
        SerializedType = serializedType;
        RawHeader = rawHeader;
        HeaderKind = headerKind;
        OriginalSerializedName = originalSerializedName;
        StableIdentity = stableIdentity;
        ExternalReference = externalReference;
        AuthoredDefinition = authoredDefinition;
        if (authoredDefinition?.SemanticSnapshot is { } semantic &&
            semantic.AssetType != serializedType)
        {
            throw new InvalidDataException(
                "Detached semantic data does not match the serialized target-row type.");
        }

        State = state;
    }

    public TargetZoneRowIdentity Identity { get; }
    public int SerializedIndex => Identity.SerializedIndex;
    public XAssetType SerializedType { get; }
    public int RawHeader { get; }
    public XAssetHeaderKind HeaderKind { get; }
    public string? OriginalSerializedName { get; }
    public XAssetStableIdentity? StableIdentity { get; }
    public string? NormalizedKey => StableIdentity?.NormalizedName;
    public TargetZoneExternalReferenceIdentity? ExternalReference { get; }
    public TargetZoneAuthoredDefinitionSource? AuthoredDefinition { get; }
    public TargetZoneRowSourceState State { get; }
}

/// <summary>
/// Detached authoring state for the selected target zone. Runtime objects
/// remain available through the workspace, while this object owns only the
/// values needed for editing and saving.
/// </summary>
public sealed class TargetZoneSourceSnapshot
{
    private TargetZoneSourceSnapshot(
        Guid documentId,
        string physicalPath,
        string logicalZoneName,
        DbHeader containerEnvelope,
        TargetZoneDecodedZoneMetadata decodedMetadata,
        IEnumerable<TargetZoneScriptStringSource> scriptStrings,
        IEnumerable<TargetZoneRowSource> rows,
        IEnumerable<TargetZoneMaterialDefinitionSource>
            materialDefinitions)
    {
        ArgumentNullException.ThrowIfNull(containerEnvelope);
        ArgumentNullException.ThrowIfNull(scriptStrings);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(materialDefinitions);

        DocumentId = documentId;
        PhysicalPath = Path.GetFullPath(physicalPath);
        LogicalZoneName = logicalZoneName;
        ContainerEnvelope = containerEnvelope;
        DecodedMetadata = decodedMetadata;
        ScriptStrings = Array.AsReadOnly(scriptStrings.ToArray());
        Rows = Array.AsReadOnly(rows.ToArray());
        MaterialDefinitions =
            Array.AsReadOnly(materialDefinitions.ToArray());
        if (MaterialDefinitions.Any(value =>
                value.ContainerRow.DocumentId != DocumentId ||
                value.ContainerSerializedIndex >= Rows.Count))
        {
            throw new InvalidDataException(
                "A captured Material definition has invalid container-row " +
                "provenance.");
        }
        if (MaterialDefinitions
            .Select(value => (
                value.ContainerSerializedIndex,
                value.DefinitionOrdinal))
            .Zip(
                MaterialDefinitions
                    .Select(value => (
                        value.ContainerSerializedIndex,
                        value.DefinitionOrdinal))
                    .Skip(1),
                (left, right) =>
                    left.ContainerSerializedIndex <
                        right.ContainerSerializedIndex ||
                    left.ContainerSerializedIndex ==
                        right.ContainerSerializedIndex &&
                    left.DefinitionOrdinal <
                        right.DefinitionOrdinal)
            .Any(inSourceOrder => !inSourceOrder))
        {
            throw new InvalidDataException(
                "Captured Material definitions are not in strict source " +
                "ownership order.");
        }
    }

    public Guid DocumentId { get; }
    public string PhysicalPath { get; }
    public string LogicalZoneName { get; }
    public DbHeader ContainerEnvelope { get; }
    public TargetZoneDecodedZoneMetadata DecodedMetadata { get; }
    public IReadOnlyList<TargetZoneScriptStringSource> ScriptStrings { get; }
    public IReadOnlyList<TargetZoneRowSource> Rows { get; }
    public IReadOnlyList<TargetZoneMaterialDefinitionSource>
        MaterialDefinitions { get; }

    public static TargetZoneSourceSnapshot Capture(
        LoadedXZone target,
        string physicalPath)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        XAssetListSnapshot assetList = target.XAssetList;
        if (assetList.Assets.Count != target.LoadedAssets.Count)
        {
            throw new InvalidDataException(
                $"Target '{target.Zone.Name}' has contradictory serialized and loaded asset counts.");
        }

        Guid documentId = Guid.NewGuid();
        var semanticGraph = new DetachedAssetSemanticGraphClone();
        TargetZoneRowSource[] rows = assetList.Assets
            .Select((serializedRow, index) => CaptureRow(
                new TargetZoneRowIdentity(documentId, index),
                serializedRow,
                target.LoadedAssets[index],
                semanticGraph))
            .ToArray();
        TargetZoneMaterialDefinitionSource[] materialDefinitions =
            semanticGraph.MaterialDefinitionCaptures
                .Select(value =>
                    new TargetZoneMaterialDefinitionSource(
                        new TargetZoneRowIdentity(
                            documentId,
                            value.ContainerSerializedIndex),
                        value.DefinitionOrdinal,
                        value.BuildData))
                .ToArray();
        TargetZoneScriptStringSource[] scriptStrings = assetList.ScriptStrings
            .Select((entry, index) => new TargetZoneScriptStringSource(index, entry.Value))
            .ToArray();
        var metadata = new TargetZoneDecodedZoneMetadata(
            Array.AsReadOnly(target.XFile.BlockSizes.ToArray()),
            target.XFile.ExternalSize);

        return new TargetZoneSourceSnapshot(
            documentId,
            physicalPath,
            target.Zone.Name,
            CopyHeader(target.Header),
            metadata,
            scriptStrings,
            rows,
            materialDefinitions);
    }

    private static TargetZoneRowSource CaptureRow(
        TargetZoneRowIdentity identity,
        XAssetListEntrySnapshot serializedRow,
        XAssetLoadResult loadedRow,
        DetachedAssetSemanticGraphClone semanticGraph)
    {
        semanticGraph.BeginTopLevelRow(identity.SerializedIndex);
        XAssetRowMaterialization materialization = loadedRow.Materialization
            ?? throw new InvalidDataException(
                $"Target XAsset row {identity.SerializedIndex} has no materialized result.");
        XAssetProviderMaterialization? rootProvider = materialization.RootProvider;
        XAssetStableIdentity? stableIdentity = rootProvider?.Identity;
        string? originalName = rootProvider?.OriginalName;

        TargetZoneRowSourceState state;
        TargetZoneExternalReferenceIdentity? externalReference = null;
        TargetZoneAuthoredDefinitionSource? authoredDefinition = null;
        switch (materialization.Disposition)
        {
            case XAssetMaterializationDisposition.FullDefinition:
                if (rootProvider?.Disposition !=
                    XAssetProviderRegistrationDisposition.FullDefinition)
                {
                    throw new InvalidDataException(
                        $"Target XAsset row {identity.SerializedIndex} has no full-definition root asset.");
                }

                state = TargetZoneRowSourceState.Definition;
                authoredDefinition = new TargetZoneAuthoredDefinitionSource(
                    CaptureIncomingDefinition(
                        identity,
                        serializedRow.Type,
                        rootProvider,
                        semanticGraph));
                break;

            case XAssetMaterializationDisposition.ResolvedReference:
            case XAssetMaterializationDisposition.UnresolvedReference:
                if (rootProvider?.Disposition !=
                        XAssetProviderRegistrationDisposition.ReferencePlaceholder ||
                    !XAssetStableIdentity.IsReferenceName(rootProvider.OriginalName) ||
                    materialization.ActiveProviderId is null)
                {
                    throw new InvalidDataException(
                        $"Target XAsset row {identity.SerializedIndex} has no reference provider.");
                }

                state = materialization.Disposition ==
                        XAssetMaterializationDisposition.ResolvedReference
                    ? TargetZoneRowSourceState.ResolvedReference
                    : TargetZoneRowSourceState.UnresolvedReference;
                externalReference = new TargetZoneExternalReferenceIdentity(
                    rootProvider.Identity,
                    rootProvider.OriginalName);
                break;

            case XAssetMaterializationDisposition.OffsetAlias:
                RequireNoRootProvider(identity, materialization);
                state = TargetZoneRowSourceState.OffsetAlias;
                break;

            case XAssetMaterializationDisposition.Null:
                RequireNoRootProvider(identity, materialization);
                state = TargetZoneRowSourceState.Null;
                break;

            case XAssetMaterializationDisposition.OpaqueNativeNoOp:
                RequireNoRootProvider(identity, materialization);
                state = TargetZoneRowSourceState.OpaqueNativeNoOp;
                break;

            case XAssetMaterializationDisposition.Unsupported:
                state = TargetZoneRowSourceState.Unsupported;
                break;

            case XAssetMaterializationDisposition.FailedRolledBack:
            default:
                throw new InvalidDataException(
                    $"Target XAsset row {identity.SerializedIndex} did not finish loading.");
        }

        return new TargetZoneRowSource(
            identity,
            serializedRow.Type,
            serializedRow.RawHeader,
            serializedRow.HeaderKind,
            originalName,
            stableIdentity,
            externalReference,
            authoredDefinition,
            state);
    }

    private static ITargetZoneDetachedSemanticSnapshot? CaptureIncomingDefinition(
        TargetZoneRowIdentity rowIdentity,
        XAssetType serializedType,
        XAssetProviderMaterialization rootProvider,
        DetachedAssetSemanticGraphClone semanticGraph)
    {
        ITargetZoneDetachedSemanticSnapshot? snapshot =
            DetachedAssetSemanticSnapshotFactory.Capture(
                serializedType,
                rootProvider.Asset,
                semanticGraph);
        if (snapshot is not null && snapshot.AssetType != serializedType)
        {
            throw new InvalidDataException(
                $"Target XAsset row {rowIdentity.SerializedIndex} produced data for {snapshot.AssetType}, not {serializedType}.");
        }

        return snapshot;
    }

    private static void RequireNoRootProvider(
        TargetZoneRowIdentity identity,
        XAssetRowMaterialization materialization)
    {
        if (materialization.RootProvider is not null ||
            materialization.ActiveProviderId is not null)
        {
            throw new InvalidDataException(
                $"Target XAsset row {identity.SerializedIndex} unexpectedly registered a root provider.");
        }
    }

    private static DbHeader CopyHeader(DbHeader source)
    {
        DbHeaderImageStreamLanguageTable[] tables = source.LanguageTables
            .Select(table => new DbHeaderImageStreamLanguageTable(
                table.SerializedIndex,
                table.LanguageMask,
                table.ImageStreamEntries))
            .ToArray();
        return new DbHeader(
            source.Magic,
            source.Version,
            source.AllowOnlineUpdate,
            source.FileCreationTimeRaw,
            source.LanguageMask,
            source.SelectedLanguageMask,
            source.LanguageCount,
            source.SelectedLanguageIndex,
            source.EntryCount,
            tables,
            source.FileSize,
            source.MaxFileSize,
            source.SerializedHeaderOffset,
            source.SerializedHeaderBytes.ToArray(),
            source.PackedStreamOffset,
            source.SourceFileLength,
            source.MetadataDispositions);
    }
}
