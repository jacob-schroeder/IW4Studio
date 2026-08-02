using System.Collections.ObjectModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Validation;

internal sealed record CompiledMapCandidateAssetExpectation(
    MapAssetKind Kind,
    XAssetType SerializedType,
    string AssetName,
    TargetZoneRowIdentity OwnerRow,
    bool IsNested,
    string SourcePath,
    string DescriptorSemanticDigest);

internal enum CompiledMapCandidateRowKind
{
    OwnedDefinition,
    ExternalReference,
    Null,
    OpaqueNativeNoOp
}

internal sealed record CompiledMapCandidateRowExpectation(
    int Index,
    XAssetType AssetType,
    int RawHeader,
    CompiledMapCandidateRowKind Kind,
    string? PayloadSemanticDigest);

/// <summary>
/// Compiled-map authored semantics expected from one isolated staging
/// snapshot. It includes both captured Studio drafts and Map Editor patches,
/// while deliberately excluding relocation-local addresses and alias tokens.
/// </summary>
internal sealed class CompiledMapCandidateExpectation
{
    private readonly IReadOnlyList<CompiledMapCandidateAssetExpectation>
        _assets;
    private readonly IReadOnlyList<CompiledMapCandidateRowExpectation>
        _rows;

    private CompiledMapCandidateExpectation(
        IEnumerable<CompiledMapCandidateAssetExpectation> assets,
        IEnumerable<CompiledMapCandidateRowExpectation> rows)
    {
        _assets =
            new ReadOnlyCollection<CompiledMapCandidateAssetExpectation>(
                assets.ToArray());
        _rows =
            new ReadOnlyCollection<CompiledMapCandidateRowExpectation>(
                rows.ToArray());
    }

    public IReadOnlyList<CompiledMapCandidateAssetExpectation> Assets =>
        _assets;

    public IReadOnlyList<CompiledMapCandidateRowExpectation> Rows =>
        _rows;

    public static CompiledMapCandidateExpectation Create(
        CompiledMapBundle baseline,
        ZoneBuildSnapshot stagingSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(stagingSnapshot);
        if (stagingSnapshot.DocumentId !=
            baseline.SourceDocumentId)
        {
            throw new InvalidDataException(
                "The staged zone snapshot belongs to a different target " +
                "document than the compiled-map bundle.");
        }

        CompiledMapCandidateRowExpectation[] rows =
            stagingSnapshot.Rows
                .Select(row => CreateRowExpectation(
                    row,
                    cancellationToken))
                .ToArray();
        IReadOnlyDictionary<int, string> topLevelPayloadDigests =
            rows
                .Where(row =>
                    row.Kind ==
                        CompiledMapCandidateRowKind.OwnedDefinition &&
                    row.PayloadSemanticDigest is not null)
                .ToDictionary(
                    row => row.Index,
                    row => row.PayloadSemanticDigest!);

        var expected =
            new List<CompiledMapCandidateAssetExpectation>(
                baseline.Assets.Count);
        foreach (CompiledMapAssetDescriptor descriptor in baseline.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IXAssetBuildData buildData = ResolveBuildData(
                descriptor,
                stagingSnapshot);
            var seed = new CompiledMapAssetDescriptorSeed(
                descriptor.Kind,
                descriptor.SerializedType,
                descriptor.AssetName,
                descriptor.OwnerRow,
                descriptor.IsNested,
                descriptor.SourcePath);
            string payloadSemanticDigest;
            if (descriptor.IsNested)
            {
                payloadSemanticDigest =
                    RelocationInvariantAssetSemanticDigest.Compute(
                        buildData,
                        cancellationToken);
            }
            else if (!topLevelPayloadDigests.TryGetValue(
                         descriptor.OwnerRow.SerializedIndex,
                         out payloadSemanticDigest!))
            {
                throw new InvalidDataException(
                    $"Staged compiled {descriptor.Kind} owner row " +
                    $"#{descriptor.OwnerRow.SerializedIndex} has no " +
                    "semantic payload digest.");
            }
            expected.Add(
                new CompiledMapCandidateAssetExpectation(
                    descriptor.Kind,
                    descriptor.SerializedType,
                    descriptor.AssetName,
                    descriptor.OwnerRow,
                    descriptor.IsNested,
                    descriptor.SourcePath,
                    CompiledMapRuntimeSemanticDigest.Compute(
                        baseline.MapIdentity,
                        seed,
                        payloadSemanticDigest,
                        cancellationToken)));
        }

        return new CompiledMapCandidateExpectation(
            expected
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.OwnerRow.SerializedIndex)
                .ThenBy(value => value.SourcePath, StringComparer.Ordinal),
            rows);
    }

    private static CompiledMapCandidateRowExpectation
        CreateRowExpectation(
            ZoneBuildRow row,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return row switch
        {
            OwnedDefinitionBuildRow owned =>
                new CompiledMapCandidateRowExpectation(
                    row.Index,
                    row.AssetType,
                    row.RawHeader,
                    CompiledMapCandidateRowKind.OwnedDefinition,
                    RelocationInvariantAssetSemanticDigest.Compute(
                        owned.BuildData,
                        cancellationToken)),
            ExternalReferenceBuildRow =>
                new CompiledMapCandidateRowExpectation(
                    row.Index,
                    row.AssetType,
                    row.RawHeader,
                    CompiledMapCandidateRowKind.ExternalReference,
                    PayloadSemanticDigest: null),
            NullBuildRow =>
                new CompiledMapCandidateRowExpectation(
                    row.Index,
                    row.AssetType,
                    row.RawHeader,
                    CompiledMapCandidateRowKind.Null,
                    PayloadSemanticDigest: null),
            OpaqueNativeNoOpBuildRow =>
                new CompiledMapCandidateRowExpectation(
                    row.Index,
                    row.AssetType,
                    row.RawHeader,
                    CompiledMapCandidateRowKind.OpaqueNativeNoOp,
                    PayloadSemanticDigest: null),
            UnsupportedBuildRow unsupported =>
                throw new InvalidDataException(
                    $"Staged target row {row.Index} is unsupported: " +
                    unsupported.Reason),
            _ => throw new InvalidDataException(
                $"Staged target row {row.Index} has unknown build " +
                $"classification '{row.GetType().Name}'.")
        };
    }

    private static IXAssetBuildData ResolveBuildData(
        CompiledMapAssetDescriptor descriptor,
        ZoneBuildSnapshot snapshot)
    {
        int ownerIndex = descriptor.OwnerRow.SerializedIndex;
        if ((uint)ownerIndex >= (uint)snapshot.Rows.Count ||
            snapshot.Rows[ownerIndex] is not
                OwnedDefinitionBuildRow owner)
        {
            throw new InvalidDataException(
                $"Staged compiled {descriptor.Kind} owner row " +
                $"#{ownerIndex} is not an owned definition.");
        }

        if (!descriptor.IsNested)
        {
            if (owner.BuildData.AssetType != descriptor.SerializedType)
            {
                throw new InvalidDataException(
                    $"Staged compiled {descriptor.Kind} owner row " +
                    $"#{ownerIndex} has type {owner.BuildData.AssetType}, " +
                    $"not {descriptor.SerializedType}.");
            }
            return owner.BuildData;
        }

        if (descriptor.Kind != MapAssetKind.MapEnts ||
            owner.BuildData is not ClipMapBuildData clipMap ||
            clipMap.References.MapEntsLink?.IncomingDefinition is not
                IXAssetBuildData nestedMapEnts ||
            nestedMapEnts.AssetType != descriptor.SerializedType)
        {
            throw new InvalidDataException(
                $"Staged nested {descriptor.Kind} descriptor at owner row " +
                $"#{ownerIndex} has no matching detached incoming definition.");
        }
        return nestedMapEnts;
    }
}
