using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Version of a serialized collision-compiler contract. A major increment
/// denotes an incompatible input, ordering, or emitted-index contract.
/// </summary>
public sealed record CollisionCompilerContractVersion
{
    public CollisionCompilerContractVersion(
        int major,
        int minor,
        int patch)
    {
        if (major <= 0)
            throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0)
            throw new ArgumentOutOfRangeException(nameof(minor));
        if (patch < 0)
            throw new ArgumentOutOfRangeException(nameof(patch));

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public override string ToString() =>
        FormattableString.Invariant($"{Major}.{Minor}.{Patch}");
}

/// <summary>
/// Stable identity written into future compile evidence. It is intentionally
/// independent from the Studio application or assembly version.
/// </summary>
public sealed record CollisionCompilerContractIdentity
{
    public CollisionCompilerContractIdentity(
        string contractId,
        CollisionCompilerContractVersion version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(version);

        ContractId = contractId;
        Version = version;
    }

    public string ContractId { get; }
    public CollisionCompilerContractVersion Version { get; }

    public override string ToString() => $"{ContractId}@{Version}";
}

/// <summary>
/// Typed SHA-256 input digest. This is an already-computed identity value;
/// the M0 contract intentionally does not prescribe hashing implementation.
/// </summary>
public sealed record CollisionCompilerSha256Digest
{
    public CollisionCompilerSha256Digest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A collision compiler input digest must be a SHA-256 hex " +
                "value.",
                nameof(value));
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Complete deterministic identity input for a future collision build. It
/// contains identities only and performs no hashing or compilation.
/// </summary>
public sealed record CollisionCompilerBuildIdentityInput
{
    public CollisionCompilerBuildIdentityInput(
        CollisionCompilerContractIdentity contract,
        MapDocumentId documentId,
        long documentRevision,
        CollisionCompilerSha256Digest semanticSourceDigest,
        CollisionCompilerSha256Digest settingsDigest,
        CollisionCompilerSha256Digest dependencyDigest)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        ArgumentNullException.ThrowIfNull(semanticSourceDigest);
        ArgumentNullException.ThrowIfNull(settingsDigest);
        ArgumentNullException.ThrowIfNull(dependencyDigest);

        Contract = contract;
        DocumentId = documentId;
        DocumentRevision = documentRevision;
        SemanticSourceDigest = semanticSourceDigest;
        SettingsDigest = settingsDigest;
        DependencyDigest = dependencyDigest;
    }

    public CollisionCompilerContractIdentity Contract { get; }
    public MapDocumentId DocumentId { get; }
    public long DocumentRevision { get; }
    public CollisionCompilerSha256Digest SemanticSourceDigest { get; }
    public CollisionCompilerSha256Digest SettingsDigest { get; }
    public CollisionCompilerSha256Digest DependencyDigest { get; }
}

public enum CollisionOrderingKey
{
    SourceProvenanceNumeric = 0,
    ImportedSourceOrdinalNumeric = 1,
    GeometryKindNumeric = 2,
    OwnershipCategoryNumeric = 3,
    StableObjectIdGuidNOrdinal = 4,
    IndexDomainNumeric = 5,
    SourceRankNumeric = 6
}

public enum CollisionOrderingScope
{
    AllSources = 0,
    ImportedSourcesOnly = 1,
    IndexContributions = 2
}

public readonly record struct CollisionOrderingClause
{
    public CollisionOrderingClause(
        CollisionOrderingKey key,
        CollisionOrderingScope scope)
    {
        if (!Enum.IsDefined(key))
            throw new ArgumentOutOfRangeException(nameof(key));
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        Key = key;
        Scope = scope;
    }

    public CollisionOrderingKey Key { get; }
    public CollisionOrderingScope Scope { get; }
}

/// <summary>
/// Versioned identity and complete ascending-key specification for the
/// deterministic ordering already enforced by
/// <see cref="CollisionSourceIndexPlan"/>.
/// </summary>
public sealed class CollisionDeterministicOrderingPolicyIdentity
{
    internal CollisionDeterministicOrderingPolicyIdentity(
        string policyId,
        CollisionCompilerContractVersion version,
        IEnumerable<CollisionOrderingClause> sourceOrdering,
        IEnumerable<CollisionOrderingClause> indexAllocationOrdering)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(sourceOrdering);
        ArgumentNullException.ThrowIfNull(indexAllocationOrdering);

        CollisionOrderingClause[] sourceCopy = sourceOrdering.ToArray();
        CollisionOrderingClause[] allocationCopy =
            indexAllocationOrdering.ToArray();
        if (sourceCopy.Length == 0)
        {
            throw new ArgumentException(
                "Source ordering requires at least one key.",
                nameof(sourceOrdering));
        }
        if (allocationCopy.Length == 0)
        {
            throw new ArgumentException(
                "Index allocation ordering requires at least one key.",
                nameof(indexAllocationOrdering));
        }

        PolicyId = policyId;
        Version = version;
        SourceOrdering = Array.AsReadOnly(sourceCopy);
        IndexAllocationOrdering = Array.AsReadOnly(allocationCopy);
    }

    public string PolicyId { get; }
    public CollisionCompilerContractVersion Version { get; }
    public IReadOnlyList<CollisionOrderingClause> SourceOrdering { get; }
    public IReadOnlyList<CollisionOrderingClause> IndexAllocationOrdering
    {
        get;
    }

    public string StableIdentity => $"{PolicyId}@{Version}";
}

/// <summary>
/// Exact official fastfile evidence used by the collision compiler corpus.
/// <see cref="RelativeFastFilePath"/> is relative to the separately configured
/// official-fastfile root; no developer-machine path is part of the contract.
/// </summary>
public sealed class CollisionOfficialFastFileCorpusCase
{
    internal CollisionOfficialFastFileCorpusCase(
        string caseId,
        string relativeFastFilePath,
        string mapAssetName,
        long byteLength,
        string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFastFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapAssetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (relativeFastFilePath.IndexOfAny(['/', '\\']) >= 0 ||
            Path.GetFileName(relativeFastFilePath) != relativeFastFilePath ||
            !relativeFastFilePath.EndsWith(
                ".ff",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Official corpus paths must be .ff filenames relative to the " +
                "official-fastfile root.",
                nameof(relativeFastFilePath));
        }
        if (byteLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        if (sha256.Length != 64 || sha256.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new ArgumentException(
                "The official corpus fingerprint must be a SHA-256 hex value.",
                nameof(sha256));
        }

        CaseId = caseId;
        RelativeFastFilePath = relativeFastFilePath;
        MapAssetName = mapAssetName;
        ByteLength = byteLength;
        Sha256 = sha256.ToLowerInvariant();
    }

    public string CaseId { get; }
    public string RelativeFastFilePath { get; }
    public string MapAssetName { get; }
    public long ByteLength { get; }
    public string Sha256 { get; }
}

/// <summary>
/// Topological facts for one authored source in the tiny deterministic corpus
/// scene. These are fixture metadata, not emitted ColMap records.
/// </summary>
public sealed class CollisionAuthoredCorpusSource
{
    internal CollisionAuthoredCorpusSource(
        string sourceId,
        CollisionGeometryKind geometryKind,
        CollisionOwnershipCategory ownership,
        int planeCount,
        int vertexCount,
        int triangleCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!Enum.IsDefined(geometryKind))
            throw new ArgumentOutOfRangeException(nameof(geometryKind));
        if (!Enum.IsDefined(ownership))
            throw new ArgumentOutOfRangeException(nameof(ownership));
        if (planeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(planeCount));
        if (vertexCount < 0)
            throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (triangleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(triangleCount));

        bool validTopology = geometryKind switch
        {
            CollisionGeometryKind.ConvexBrush =>
                planeCount >= 4 &&
                vertexCount == 0 &&
                triangleCount == 0,
            CollisionGeometryKind.TriangleMesh =>
                planeCount == 0 &&
                vertexCount >= 3 &&
                triangleCount >= 1,
            CollisionGeometryKind.StaticModelHull =>
                planeCount == 0 &&
                vertexCount >= 3 &&
                triangleCount >= 1,
            _ => false
        };
        if (!validTopology)
        {
            throw new ArgumentException(
                $"Topology cardinality is invalid for {geometryKind}.",
                nameof(geometryKind));
        }
        if (ownership == CollisionOwnershipCategory.PairedStaticModel &&
            geometryKind != CollisionGeometryKind.StaticModelHull)
        {
            throw new ArgumentException(
                "Paired static-model corpus topology must be a static-model " +
                "hull.",
                nameof(ownership));
        }
        if (ownership == CollisionOwnershipCategory.BrushModelEntity &&
            geometryKind != CollisionGeometryKind.ConvexBrush)
        {
            throw new ArgumentException(
                "Brush-model corpus topology must be a convex brush.",
                nameof(ownership));
        }

        SourceId = sourceId;
        GeometryKind = geometryKind;
        Ownership = ownership;
        PlaneCount = planeCount;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
    }

    public string SourceId { get; }
    public CollisionGeometryKind GeometryKind { get; }
    public CollisionOwnershipCategory Ownership { get; }
    public CollisionSourceProvenance Provenance =>
        CollisionSourceProvenance.Authored;
    public int PlaneCount { get; }
    public int VertexCount { get; }
    public int TriangleCount { get; }
}

public sealed class CollisionAuthoredSceneCorpusCase
{
    internal CollisionAuthoredSceneCorpusCase(
        string caseId,
        string sceneId,
        IEnumerable<CollisionAuthoredCorpusSource> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);
        ArgumentNullException.ThrowIfNull(sources);

        CollisionAuthoredCorpusSource[] sourceCopy = sources.ToArray();
        if (sourceCopy.Length == 0 || sourceCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "An authored corpus scene requires at least one source.",
                nameof(sources));
        }
        if (sourceCopy
            .GroupBy(value => value.SourceId, StringComparer.Ordinal)
            .Any(value => value.Count() > 1))
        {
            throw new ArgumentException(
                "Authored corpus source identities must be unique.",
                nameof(sources));
        }

        CaseId = caseId;
        SceneId = sceneId;
        Sources = Array.AsReadOnly(sourceCopy);
    }

    public string CaseId { get; }
    public string SceneId { get; }
    public IReadOnlyList<CollisionAuthoredCorpusSource> Sources { get; }
}

public sealed class CollisionCompilerCorpusManifest
{
    internal CollisionCompilerCorpusManifest(
        IEnumerable<CollisionOfficialFastFileCorpusCase> officialFastFiles,
        IEnumerable<CollisionAuthoredSceneCorpusCase> authoredScenes)
    {
        ArgumentNullException.ThrowIfNull(officialFastFiles);
        ArgumentNullException.ThrowIfNull(authoredScenes);

        CollisionOfficialFastFileCorpusCase[] officialCopy =
            officialFastFiles.ToArray();
        CollisionAuthoredSceneCorpusCase[] authoredCopy =
            authoredScenes.ToArray();
        if (officialCopy.Length == 0 ||
            officialCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "The corpus requires official fastfile evidence.",
                nameof(officialFastFiles));
        }
        if (authoredCopy.Length == 0 ||
            authoredCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "The corpus requires authored collision evidence.",
                nameof(authoredScenes));
        }

        string[] caseIds = officialCopy
            .Select(value => value.CaseId)
            .Concat(authoredCopy.Select(value => value.CaseId))
            .ToArray();
        if (caseIds
            .GroupBy(value => value, StringComparer.Ordinal)
            .Any(value => value.Count() > 1))
        {
            throw new ArgumentException(
                "Collision corpus case identities must be unique.");
        }

        OfficialFastFiles = Array.AsReadOnly(officialCopy);
        AuthoredScenes = Array.AsReadOnly(authoredCopy);
    }

    public IReadOnlyList<CollisionOfficialFastFileCorpusCase>
        OfficialFastFiles { get; }
    public IReadOnlyList<CollisionAuthoredSceneCorpusCase> AuthoredScenes
    {
        get;
    }
}

/// <summary>
/// M0 collision compiler contract manifest. This type specifies stable
/// identities and corpus evidence only; it performs no compilation.
/// </summary>
public sealed class CollisionCompilerContractManifest
{
    private CollisionCompilerContractManifest(
        CollisionCompilerContractIdentity contract,
        CollisionDeterministicOrderingPolicyIdentity orderingPolicy,
        CollisionCompilerCorpusManifest corpus)
    {
        Contract = contract;
        OrderingPolicy = orderingPolicy;
        Corpus = corpus;
    }

    public static CollisionCompilerContractManifest Current { get; } =
        CreateCurrent();

    public CollisionCompilerContractIdentity Contract { get; }
    public CollisionDeterministicOrderingPolicyIdentity OrderingPolicy
    {
        get;
    }
    public CollisionCompilerCorpusManifest Corpus { get; }

    private static CollisionCompilerContractManifest CreateCurrent()
    {
        // 2.0 removes the incorrect global UInt16 triangle-vertex target
        // contract. IW4 indices are UInt16 values relative to each
        // CollisionPartition.FirstVertSegment window.
        var version = new CollisionCompilerContractVersion(2, 0, 0);
        var contract = new CollisionCompilerContractIdentity(
            "iw4-studio.map-editor.colmap-compiler-contract",
            version);
        var orderingPolicy =
            new CollisionDeterministicOrderingPolicyIdentity(
                "iw4-studio.map-editor.colmap-deterministic-order",
                new CollisionCompilerContractVersion(1, 0, 0),
                [
                    new CollisionOrderingClause(
                        CollisionOrderingKey.SourceProvenanceNumeric,
                        CollisionOrderingScope.AllSources),
                    new CollisionOrderingClause(
                        CollisionOrderingKey.ImportedSourceOrdinalNumeric,
                        CollisionOrderingScope.ImportedSourcesOnly),
                    new CollisionOrderingClause(
                        CollisionOrderingKey.GeometryKindNumeric,
                        CollisionOrderingScope.AllSources),
                    new CollisionOrderingClause(
                        CollisionOrderingKey.OwnershipCategoryNumeric,
                        CollisionOrderingScope.AllSources),
                    new CollisionOrderingClause(
                        CollisionOrderingKey.StableObjectIdGuidNOrdinal,
                        CollisionOrderingScope.AllSources)
                ],
                [
                    new CollisionOrderingClause(
                        CollisionOrderingKey.IndexDomainNumeric,
                        CollisionOrderingScope.IndexContributions),
                    new CollisionOrderingClause(
                        CollisionOrderingKey.SourceRankNumeric,
                        CollisionOrderingScope.IndexContributions)
                ]);

        var corpus = new CollisionCompilerCorpusManifest(
            [
                new CollisionOfficialFastFileCorpusCase(
                    "official-mp-rust",
                    "mp_rust.ff",
                    "maps/mp/mp_rust.d3dbsp",
                    33_397_616,
                    "d53b7e6a264b39a30ba80af58ee46b0fd727087a8fd2694a970b8b9a97c3823b"),
                new CollisionOfficialFastFileCorpusCase(
                    "official-mp-terminal",
                    "mp_terminal.ff",
                    "maps/mp/mp_terminal.d3dbsp",
                    41_836_843,
                    "a5c1af63685ac3bcbdec37c4c9d88fca58f5cd0c960d7d7f246edf09bbd052f0"),
                new CollisionOfficialFastFileCorpusCase(
                    "official-mp-boneyard",
                    "mp_boneyard.ff",
                    "maps/mp/mp_boneyard.d3dbsp",
                    33_523_838,
                    "c115295efd0bcedb69fca06b6f612a3edda3f1f740e56a5a3b2ac7581bdd0fb2")
            ],
            [
                new CollisionAuthoredSceneCorpusCase(
                    "authored-tiny-standalone-world",
                    "collision://m0/tiny-standalone-world/v1",
                    [
                        new CollisionAuthoredCorpusSource(
                            "invisible-wall",
                            CollisionGeometryKind.ConvexBrush,
                            CollisionOwnershipCategory.StandaloneWorld,
                            planeCount: 6,
                            vertexCount: 0,
                            triangleCount: 0),
                        new CollisionAuthoredCorpusSource(
                            "walkable-pad",
                            CollisionGeometryKind.TriangleMesh,
                            CollisionOwnershipCategory.StandaloneWorld,
                            planeCount: 0,
                            vertexCount: 4,
                            triangleCount: 2)
                    ])
            ]);

        return new CollisionCompilerContractManifest(
            contract,
            orderingPolicy,
            corpus);
    }
}
