using System.Collections.ObjectModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Bundles;

public sealed record CompiledMapAssetDescriptor(
    MapAssetKind Kind,
    XAssetType SerializedType,
    string AssetName,
    TargetZoneRowIdentity OwnerRow,
    bool IsNested,
    string SourcePath,
    string BaselineDigest);

public sealed record CompiledMapDependency(
    XAssetType OwnerAssetType,
    string OwnerAssetName,
    string OwnerPath,
    XAssetType TargetAssetType,
    string TargetAssetName,
    ZoneAssetDependencyKind Kind,
    bool IsResolved,
    WorkspaceAssetOrigin? ResolvedOrigin,
    int? TargetSourceOrdinal);

public sealed record CompiledSourceBinding
{
    public CompiledSourceBinding(
        SourceBindingId id,
        XAssetType assetType,
        string assetName,
        string fieldPath,
        TargetZoneRowIdentity ownerRow,
        int? sourceOrdinal,
        string baselineDigest,
        MapValueProvenance provenance)
    {
        if (id.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDigest);

        Id = id;
        AssetType = assetType;
        AssetName = assetName;
        FieldPath = fieldPath;
        OwnerRow = ownerRow;
        SourceOrdinal = sourceOrdinal;
        BaselineDigest = baselineDigest;
        Provenance = provenance;
    }

    public SourceBindingId Id { get; }
    public XAssetType AssetType { get; }
    public string AssetName { get; }
    public string FieldPath { get; }
    public TargetZoneRowIdentity OwnerRow { get; }
    public int? SourceOrdinal { get; }
    public string BaselineDigest { get; }
    public MapValueProvenance Provenance { get; }
}

internal sealed record CompiledMapAssetBaseline(
    CompiledMapAssetDescriptor Descriptor,
    IXAssetBuildData Source,
    IXAssetBuildData? DependencySource);

/// <summary>
/// Immutable imported authority for one compiled map. Public data is scalar
/// metadata; detached authoring objects remain private to the compilation
/// layer and are never exposed as editable document state.
/// </summary>
public sealed class CompiledMapBundle
{
    private readonly IReadOnlyList<CompiledMapAssetDescriptor> _assets;
    private readonly IReadOnlyList<CompiledMapDependency> _dependencies;
    private readonly IReadOnlyDictionary<MapAssetKind, CompiledMapAssetBaseline>
        _baselines;

    internal CompiledMapBundle(
        string mapIdentity,
        string originalMapName,
        long sourcePoolRevision,
        string baselineDigest,
        IEnumerable<CompiledMapAssetBaseline> baselines,
        IEnumerable<CompiledMapDependency> dependencies,
        long sourceEditingSessionRevision = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMapName);
        if (sourcePoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(sourcePoolRevision));
        if (sourceEditingSessionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceEditingSessionRevision));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDigest);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(dependencies);

        CompiledMapAssetBaseline[] baselineCopy = baselines.ToArray();
        if (baselineCopy.Length == 0)
            throw new ArgumentException("A compiled map bundle cannot be empty.", nameof(baselines));
        Guid sourceDocumentId =
            baselineCopy[0].Descriptor.OwnerRow.DocumentId;
        if (sourceDocumentId == Guid.Empty ||
            baselineCopy.Any(value =>
                value.Descriptor.OwnerRow.DocumentId != sourceDocumentId))
        {
            throw new InvalidDataException(
                "Every compiled-map descriptor must belong to the same " +
                "non-empty target source document.");
        }

        var byKind = new Dictionary<MapAssetKind, CompiledMapAssetBaseline>();
        foreach (CompiledMapAssetBaseline baseline in baselineCopy)
        {
            if (!byKind.TryAdd(baseline.Descriptor.Kind, baseline))
            {
                throw new InvalidDataException(
                    $"Compiled map bundle contains duplicate {baseline.Descriptor.Kind} authority.");
            }
        }

        MapIdentity = mapIdentity;
        OriginalMapName = originalMapName;
        SourcePoolRevision = sourcePoolRevision;
        SourceEditingSessionRevision = sourceEditingSessionRevision;
        SourceDocumentId = sourceDocumentId;
        BaselineDigest = baselineDigest;
        _assets = Array.AsReadOnly(
            baselineCopy.Select(value => value.Descriptor).ToArray());
        _dependencies = Array.AsReadOnly(dependencies.ToArray());
        _baselines =
            new ReadOnlyDictionary<MapAssetKind, CompiledMapAssetBaseline>(byKind);
    }

    public string MapIdentity { get; }
    public string OriginalMapName { get; }
    public long SourcePoolRevision { get; }
    public long SourceEditingSessionRevision { get; }
    public Guid SourceDocumentId { get; }
    public string BaselineDigest { get; }
    public MapDocumentId DocumentId =>
        DeterministicMapIdentity.Document(MapIdentity, BaselineDigest);
    public IReadOnlyList<CompiledMapAssetDescriptor> Assets => _assets;
    public IReadOnlyList<CompiledMapDependency> Dependencies => _dependencies;

    internal bool TryGetBaseline<T>(
        MapAssetKind kind,
        out T? source)
        where T : class
    {
        if (_baselines.TryGetValue(kind, out CompiledMapAssetBaseline? baseline) &&
            baseline.Source is T typed)
        {
            source = typed;
            return true;
        }

        source = null;
        return false;
    }

    internal CompiledMapAssetDescriptor RequireAsset(MapAssetKind kind) =>
        _baselines.TryGetValue(kind, out CompiledMapAssetBaseline? baseline)
            ? baseline.Descriptor
            : throw new KeyNotFoundException(
                $"Compiled map bundle has no {kind} authority.");

    /// <summary>
    /// Recomputes the digest from the retained detached build data instead of
    /// trusting the descriptor digests captured at import time. Persistence
    /// uses this immediately before planning and draft replacement to detect
    /// accidental mutation of the immutable compilation baseline.
    /// </summary>
    internal string ComputeCurrentBaselineDigest(
        CancellationToken cancellationToken = default)
    {
        var descriptors = new List<CompiledMapAssetDescriptor>(
            _baselines.Count);
        foreach (CompiledMapAssetBaseline baseline in _baselines.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompiledMapAssetDescriptor descriptor = baseline.Descriptor;
            var seed = new CompiledMapAssetDescriptorSeed(
                descriptor.Kind,
                descriptor.SerializedType,
                descriptor.AssetName,
                descriptor.OwnerRow,
                descriptor.IsNested,
                descriptor.SourcePath);
            descriptors.Add(descriptor with
            {
                BaselineDigest = CompiledMapBaselineDigest.ComputeAsset(
                    MapIdentity,
                    seed,
                    baseline.Source,
                    cancellationToken)
            });
        }

        return CompiledMapBaselineDigest.ComputeBundle(
            MapIdentity,
            descriptors,
            cancellationToken);
    }
}

public enum MapBundleResolutionStatus
{
    Ready,
    NotAMap,
    Ambiguous,
    Invalid
}

public sealed class MapBundleResolutionResult
{
    private readonly IReadOnlyList<string> _diagnostics;

    internal MapBundleResolutionResult(
        MapBundleResolutionStatus status,
        CompiledMapBundle? bundle,
        IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if ((status == MapBundleResolutionStatus.Ready) != (bundle is not null))
        {
            throw new ArgumentException(
                "Only a ready bundle resolution may carry a compiled bundle.",
                nameof(bundle));
        }

        Status = status;
        Bundle = bundle;
        _diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public MapBundleResolutionStatus Status { get; }
    public CompiledMapBundle? Bundle { get; }
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public bool Succeeded => Status == MapBundleResolutionStatus.Ready;
}
