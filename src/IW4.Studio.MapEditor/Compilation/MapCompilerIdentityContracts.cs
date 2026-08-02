using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.Glass;
using IW4.Studio.MapEditor.Compilation.Lighting;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;
using IW4.Studio.MapEditor.Compilation.TargetAcceptance;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation;

public sealed record MapCompilerProfileIdentity
{
    public MapCompilerProfileIdentity(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ProfileId = profileId;
    }

    public string ProfileId { get; }
}

public static class MapCompilerProfiles
{
    public static MapCompilerProfileIdentity Multiplayer { get; } =
        new("iw4-multiplayer");

    /// <summary>
    /// Bounded greenfield profile for the first retail-target probe. It is
    /// intentionally distinct from the general multiplayer profile so its
    /// narrow M3-M7 compiler choices participate in content identity.
    /// </summary>
    public static MapCompilerProfileIdentity
        MinimalMultiplayerTargetProbe { get; } =
            new("iw4-multiplayer-minimal-target-probe");
}

public sealed record MapCompilerContractComponent
{
    public MapCompilerContractComponent(
        string contractId,
        int major,
        int minor,
        int patch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        if (major <= 0)
            throw new ArgumentOutOfRangeException(nameof(major));
        if (minor < 0)
            throw new ArgumentOutOfRangeException(nameof(minor));
        if (patch < 0)
            throw new ArgumentOutOfRangeException(nameof(patch));

        ContractId = contractId;
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public string ContractId { get; }
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string StableIdentity =>
        FormattableString.Invariant(
            $"{ContractId}@{Major}.{Minor}.{Patch}");

    public static MapCompilerContractComponent FromCollision(
        CollisionCompilerContractIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(
            identity.ContractId,
            identity.Version.Major,
            identity.Version.Minor,
            identity.Version.Patch);
    }
}

/// <summary>
/// Exact contract-component set required by one whole-map compiler profile.
/// Callers cannot omit a subsystem contract from content identity merely
/// because that subsystem did not change in the current edit.
/// </summary>
public sealed class MapCompilerProfileContractManifest
{
    private readonly IReadOnlyList<MapCompilerContractComponent> _components;

    internal MapCompilerProfileContractManifest(
        MapCompilerProfileIdentity profile,
        IEnumerable<MapCompilerContractComponent> components)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(components);

        MapCompilerContractComponent[] ordered = components
            .OrderBy(value => value.ContractId, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0 ||
            ordered.Any(value => value is null) ||
            ordered.Select(value => value.ContractId)
                .Distinct(StringComparer.Ordinal)
                .Count() != ordered.Length)
        {
            throw new ArgumentException(
                "A profile manifest requires one unique version of every " +
                "required compiler contract.",
                nameof(components));
        }

        Profile = profile;
        _components = Array.AsReadOnly(ordered);
    }

    public MapCompilerProfileIdentity Profile { get; }
    public IReadOnlyList<MapCompilerContractComponent> Components =>
        _components;
}

public static class MapCompilerContractManifests
{
    public static MapCompilerProfileContractManifest InitialMultiplayer
        { get; } =
        new(
            MapCompilerProfiles.Multiplayer,
            [
                new MapCompilerContractComponent(
                    "iw4-studio.map-editor.initial-mp-map-compiler-contract",
                    major: 1,
                    minor: 0,
                    patch: 0),
                MapCompilerContractComponent.FromCollision(
                    CollisionCompilerContractManifest.Current.Contract)
            ]);

    public static MapCompilerProfileContractManifest
        MinimalMultiplayerTargetProbe { get; } =
            new(
                MapCompilerProfiles.MinimalMultiplayerTargetProbe,
                [
                    new MapCompilerContractComponent(
                        "iw4-studio.map-editor.initial-mp-map-compiler-contract",
                        major: 1,
                        minor: 0,
                        patch: 0),
                    MapCompilerContractComponent.FromCollision(
                        CollisionCompilerContractManifest.Current.Contract),
                    FromStableIdentity(
                        CollisionConservativeWorldSpatialCompiler
                            .PolicyIdentity),
                    FromStableIdentity(
                        RenderWorldStructuralProfile.CompilerIdentity),
                    FromStableIdentity(
                        RenderWorldVisibilityProfile.CompilerIdentity),
                    FromStableIdentity(
                        GfxWorldTargetAcceptanceProfile.CompilerIdentity),
                    FromStableIdentity(
                        CollisionTargetAcceptanceCandidate
                            .SerializationProfileIdentity),
                    FromStableIdentity(
                        MapSpatialTargetAcceptanceAssembly
                            .AssemblyProfileIdentity),
                    FromStableIdentity(
                        GfxWorldNoBakeLightingProfile.CompilerIdentity),
                    FromStableIdentity(
                        PrimaryLightOrdinalPlan.CompilerIdentity),
                    FromStableIdentity(
                        GlassPieceIdentityAllocator.CompilerIdentity),
                    FromStableIdentity(
                        EmptyGlassDomainCompiler.CompilerIdentity),
                    FromStableIdentity(
                        MinimalMultiplayerMapTargetProbeCandidate
                            .CompilerIdentity),
                    FromStableIdentity(
                        MinimalMultiplayerMapTargetMaterialResolver
                            .CompilerIdentity),
                    FromStableIdentity(
                        MinimalMultiplayerMapRuntimeSupportCompiler
                            .CompilerIdentity)
                ]);

    public static MapCompilerProfileContractManifest GetRequired(
        MapCompilerProfileIdentity profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile == InitialMultiplayer.Profile)
            return InitialMultiplayer;
        if (profile == MinimalMultiplayerTargetProbe.Profile)
            return MinimalMultiplayerTargetProbe;

        throw new KeyNotFoundException(
            $"No compiler-contract manifest is registered for profile " +
            $"'{profile.ProfileId}'.");
    }

    private static MapCompilerContractComponent FromStableIdentity(
        string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        int separator = identity.LastIndexOf('@');
        string[] versionParts =
            separator > 0 && separator < identity.Length - 1
                ? identity[(separator + 1)..].Split('.')
                : [];
        var version = new int[3];
        if (versionParts.Length is < 1 or > 3 ||
            !TryParseCanonicalVersionParts(versionParts, version) ||
            version[0] <= 0)
        {
            throw new InvalidDataException(
                $"Compiler identity '{identity}' does not end in a valid " +
                "canonical '@major[.minor[.patch]]' version.");
        }

        return new MapCompilerContractComponent(
            identity[..separator],
            version[0],
            version[1],
            version[2]);
    }

    private static bool TryParseCanonicalVersionParts(
        IReadOnlyList<string> sources,
        int[] destination)
    {
        for (int index = 0; index < sources.Count; index++)
        {
            if (!TryParseCanonicalVersionPart(
                    sources[index],
                    out destination[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseCanonicalVersionPart(
        string source,
        out int value)
    {
        value = 0;
        if (source.Length == 0 ||
            source.Any(character =>
                character is < '0' or > '9') ||
            !int.TryParse(
                source,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        return string.Equals(
            source,
            value.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}

public sealed record MapCompilerSha256Digest
{
    public MapCompilerSha256Digest(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 ||
            value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A map compiler digest must be a SHA-256 hex value.",
                nameof(value));
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Stable whole-map content input. Document identity and revision are
/// deliberately excluded: equal source, settings, dependencies, and compiler
/// contracts produce the same content identity regardless of edit history.
/// </summary>
public sealed class MapCompilerContentIdentityInput
{
    private readonly IReadOnlyList<MapCompilerContractComponent> _contracts;

    public MapCompilerContentIdentityInput(
        string mapAssetName,
        MapCompilerProfileIdentity profile,
        IEnumerable<MapCompilerContractComponent> contracts,
        MapCompilerSha256Digest semanticSourceDigest,
        MapCompilerSha256Digest settingsDigest,
        MapCompilerSha256Digest dependencyDigest)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(semanticSourceDigest);
        ArgumentNullException.ThrowIfNull(settingsDigest);
        ArgumentNullException.ThrowIfNull(dependencyDigest);

        MapCompilerContractComponent[] contractCopy = contracts.ToArray();
        if (contractCopy.Length == 0 ||
            contractCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "A map content identity requires at least one compiler " +
                "contract.",
                nameof(contracts));
        }
        MapCompilerContractComponent[] orderedContracts = contractCopy
            .OrderBy(value => value.ContractId, StringComparer.Ordinal)
            .ThenBy(value => value.Major)
            .ThenBy(value => value.Minor)
            .ThenBy(value => value.Patch)
            .ToArray();
        if (orderedContracts
            .GroupBy(value => value.ContractId, StringComparer.Ordinal)
            .Any(value => value.Count() != 1))
        {
            throw new ArgumentException(
                "A map content identity cannot contain multiple versions " +
                "of one compiler contract.",
                nameof(contracts));
        }
        MapCompilerProfileContractManifest requiredManifest =
            MapCompilerContractManifests.GetRequired(profile);
        if (!orderedContracts
            .Select(value => value.StableIdentity)
            .SequenceEqual(
                requiredManifest.Components.Select(
                    value => value.StableIdentity),
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Profile '{profile.ProfileId}' requires the exact compiler " +
                "contract manifest [" +
                string.Join(
                    ", ",
                    requiredManifest.Components.Select(
                        value => value.StableIdentity)) +
                "].",
                nameof(contracts));
        }

        MapAssetName = NormalizeMultiplayerMapAssetName(mapAssetName);
        Profile = profile;
        _contracts = Array.AsReadOnly(orderedContracts);
        SemanticSourceDigest = semanticSourceDigest;
        SettingsDigest = settingsDigest;
        DependencyDigest = dependencyDigest;
    }

    public string MapAssetName { get; }
    public MapCompilerProfileIdentity Profile { get; }
    public IReadOnlyList<MapCompilerContractComponent> Contracts =>
        _contracts;
    public MapCompilerSha256Digest SemanticSourceDigest { get; }
    public MapCompilerSha256Digest SettingsDigest { get; }
    public MapCompilerSha256Digest DependencyDigest { get; }

    public static string NormalizeMultiplayerMapAssetName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value
            .Trim()
            .Replace('\\', '/')
            .ToLowerInvariant();
        string[] pathParts = normalized.Split('/');
        if (!normalized.StartsWith("maps/mp/", StringComparison.Ordinal) ||
            !normalized.EndsWith(".d3dbsp", StringComparison.Ordinal) ||
            normalized.Length <= "maps/mp/.d3dbsp".Length ||
            pathParts.Length != 3 ||
            pathParts[2].Length <= ".d3dbsp".Length ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            pathParts.Any(part => part is "." or "..") ||
            normalized.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "An IW4 multiplayer map identity must be a normalized " +
                "maps/mp/*.d3dbsp asset path.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed record MapCompilerContentIdentity
{
    internal MapCompilerContentIdentity(MapCompilerSha256Digest digest) =>
        Digest = digest ??
            throw new ArgumentNullException(nameof(digest));

    public MapCompilerSha256Digest Digest { get; }

    public override string ToString() => Digest.Value;
}

/// <summary>
/// Provenance remains adjacent to, but outside, the content identity so a
/// revision-only edit does not perturb reproducible compiler output.
/// </summary>
public sealed record MapCompilerBuildProvenance
{
    public MapCompilerBuildProvenance(
        MapCompilerContentIdentity contentIdentity,
        MapDocumentId documentId,
        long documentRevision)
    {
        ArgumentNullException.ThrowIfNull(contentIdentity);
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));

        ContentIdentity = contentIdentity;
        DocumentId = documentId;
        DocumentRevision = documentRevision;
    }

    public MapCompilerContentIdentity ContentIdentity { get; }
    public MapDocumentId DocumentId { get; }
    public long DocumentRevision { get; }
}

public static class MapCompilerContentIdentityCalculator
{
    private const string Domain =
        "iw4-studio.map-editor.map-content-identity/v1";

    public static MapCompilerContentIdentity Compute(
        MapCompilerContentIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendUtf8(hash, "domain", Domain);
        AppendUtf8(hash, "map-asset-name", input.MapAssetName);
        AppendUtf8(hash, "profile", input.Profile.ProfileId);
        AppendInt32(hash, "contract-count", input.Contracts.Count);
        foreach (MapCompilerContractComponent contract in input.Contracts)
        {
            AppendUtf8(hash, "contract-id", contract.ContractId);
            AppendInt32(hash, "contract-major", contract.Major);
            AppendInt32(hash, "contract-minor", contract.Minor);
            AppendInt32(hash, "contract-patch", contract.Patch);
        }
        AppendDigest(
            hash,
            "semantic-source-sha256",
            input.SemanticSourceDigest);
        AppendDigest(hash, "settings-sha256", input.SettingsDigest);
        AppendDigest(
            hash,
            "dependencies-sha256",
            input.DependencyDigest);

        return new MapCompilerContentIdentity(
            new MapCompilerSha256Digest(
                Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant()));
    }

    private static void AppendDigest(
        IncrementalHash hash,
        string tag,
        MapCompilerSha256Digest digest) =>
        Append(hash, tag, Convert.FromHexString(digest.Value));

    private static void AppendUtf8(
        IncrementalHash hash,
        string tag,
        string value) =>
        Append(hash, tag, Encoding.UTF8.GetBytes(value));

    private static void AppendInt32(
        IncrementalHash hash,
        string tag,
        int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        Append(hash, tag, bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        ReadOnlySpan<byte> value)
    {
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        AppendLength(hash, tagBytes.Length);
        hash.AppendData(tagBytes);
        AppendLength(hash, value.Length);
        hash.AppendData(value);
    }

    private static void AppendLength(
        IncrementalHash hash,
        int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}

/// <summary>
/// Primary uint32 transported by ColMap and GfxMap and published as mapcrc.
/// </summary>
public readonly record struct MapPrimaryChecksum(uint Value)
{
    public const int SerializedBitWidth = sizeof(uint) * 8;
}

public enum MapPrimaryChecksumAssignmentKind
{
    ImportedProduction = 0,
    StudioCanonicalV1 = 1
}

/// <summary>
/// Selects checksum provenance from whole-map content state, not merely from
/// collision topology. Any semantic source, setting, dependency, or compiler
/// contract change moves the candidate into <see cref="StudioAuthoredContent"/>.
/// </summary>
public enum MapPrimaryChecksumContentDisposition
{
    ImportedContentPreserved = 0,
    StudioAuthoredContent = 1
}

public enum MapPrimaryChecksumProductionFidelity
{
    ExactImportedValue = 0,
    ConsumerCompatibleProductionByteScopeUnknown = 1
}

/// <summary>
/// Checksum evidence captured from one immutable imported map bundle. The
/// asset-graph digest binds the opaque production word to the exact baseline
/// that supplied it; callers cannot preserve a bare uint without provenance.
/// </summary>
public sealed record ImportedMapPrimaryChecksumBaseline
{
    public ImportedMapPrimaryChecksumBaseline(
        string mapAssetName,
        MapCompilerSha256Digest importedAssetGraphDigest,
        MapPrimaryChecksum colMapChecksum,
        MapPrimaryChecksum gfxMapChecksum)
    {
        ArgumentNullException.ThrowIfNull(importedAssetGraphDigest);
        if (colMapChecksum != gfxMapChecksum)
        {
            throw new ArgumentException(
                "An imported primary-checksum baseline requires exact " +
                "ColMap/GfxMap equality.",
                nameof(gfxMapChecksum));
        }

        MapAssetName =
            MapCompilerContentIdentityInput.NormalizeMultiplayerMapAssetName(
                mapAssetName);
        ImportedAssetGraphDigest = importedAssetGraphDigest;
        Checksum = colMapChecksum;
    }

    public string MapAssetName { get; }
    public MapCompilerSha256Digest ImportedAssetGraphDigest { get; }
    public MapPrimaryChecksum Checksum { get; }
}

/// <summary>
/// Identity evidence independently calculated from the imported candidate
/// being validated. This is deliberately distinct from
/// <see cref="ImportedMapPrimaryChecksumBaseline"/>: preservation is legal
/// only when the candidate map identity and current asset-graph digest still
/// match the immutable baseline that supplied the production checksum.
/// </summary>
public sealed record ImportedMapPrimaryChecksumCandidateEvidence
{
    public ImportedMapPrimaryChecksumCandidateEvidence(
        string mapAssetName,
        MapCompilerSha256Digest assetGraphDigest)
    {
        ArgumentNullException.ThrowIfNull(assetGraphDigest);

        MapAssetName =
            MapCompilerContentIdentityInput.NormalizeMultiplayerMapAssetName(
                mapAssetName);
        AssetGraphDigest = assetGraphDigest;
    }

    public string MapAssetName { get; }
    public MapCompilerSha256Digest AssetGraphDigest { get; }
}

public sealed record MapPrimaryChecksumAssignment
{
    internal MapPrimaryChecksumAssignment(
        MapPrimaryChecksum checksum,
        MapPrimaryChecksumAssignmentKind kind,
        MapPrimaryChecksumProductionFidelity productionFidelity,
        MapCompilerContentIdentity? contentIdentity,
        ImportedMapPrimaryChecksumBaseline? importedBaseline)
    {
        Checksum = checksum;
        Kind = kind;
        ProductionFidelity = productionFidelity;
        ContentIdentity = contentIdentity;
        ImportedBaseline = importedBaseline;
    }

    public MapPrimaryChecksum Checksum { get; }
    public MapPrimaryChecksumAssignmentKind Kind { get; }
    public MapPrimaryChecksumProductionFidelity ProductionFidelity { get; }
    public MapCompilerContentIdentity? ContentIdentity { get; }
    public ImportedMapPrimaryChecksumBaseline? ImportedBaseline { get; }
}

/// <summary>
/// Versioned primary-checksum policy. Retail scope is preserved exactly for
/// imported maps. Greenfield output uses standard synchronous CRC-32/ISO-HDLC
/// over a domain-separated whole-map content identity. IW4 runtime consumers
/// compare only the equal Col/Gfx uint32 values, so StudioCanonicalV1 is
/// consumer-compatible without claiming production-byte fidelity.
/// </summary>
public static class MapPrimaryChecksumPolicy
{
    public const string StudioCanonicalPolicyId =
        "iw4-studio.map-primary-checksum/studio-canonical-v1";

    private const uint ReflectedPolynomial = 0xEDB88320;

    public static MapPrimaryChecksumAssignment PreserveImported(
        ImportedMapPrimaryChecksumBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return
        new(
            baseline.Checksum,
            MapPrimaryChecksumAssignmentKind.ImportedProduction,
            MapPrimaryChecksumProductionFidelity.ExactImportedValue,
            contentIdentity: null,
            baseline);
    }

    public static MapPrimaryChecksumAssignment ComputeStudioCanonical(
        MapCompilerContentIdentity contentIdentity)
    {
        ArgumentNullException.ThrowIfNull(contentIdentity);
        byte[] domain = Encoding.UTF8.GetBytes(
            StudioCanonicalPolicyId + "\0");
        byte[] digest = Convert.FromHexString(contentIdentity.Digest.Value);
        uint crc = uint.MaxValue;
        crc = Update(crc, domain);
        crc = Update(crc, digest);

        return new(
            new MapPrimaryChecksum(~crc),
            MapPrimaryChecksumAssignmentKind.StudioCanonicalV1,
            MapPrimaryChecksumProductionFidelity
                .ConsumerCompatibleProductionByteScopeUnknown,
            contentIdentity,
            importedBaseline: null);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0
                    ? crc >> 1
                    : ReflectedPolynomial ^ (crc >> 1);
            }
        }

        return crc;
    }
}

/// <summary>
/// Separate GfxMap-only vertex checksum. The audited Xbox/PS3 retail MP
/// consumers do not read it and no production byte scope is proven.
/// </summary>
public readonly record struct GfxMapVertexChecksum(uint Value)
{
    public const int SerializedBitWidth = sizeof(uint) * 8;
}

public enum GfxMapVertexChecksumAssignmentKind
{
    ImportedProduction = 0,
    StudioConstantZeroV1 = 1
}

public enum GfxMapVertexChecksumProductionFidelity
{
    ExactImportedValue = 0,
    DeterministicStudioAssignmentRetailParityUnproven = 1
}

public enum GfxMapVertexChecksumPolicyStatus
{
    ImportedPreservationAndStudioConstantZeroV1 = 0
}

public sealed record GfxMapVertexChecksumAssignment
{
    internal GfxMapVertexChecksumAssignment(
        GfxMapVertexChecksum checksum,
        GfxMapVertexChecksumAssignmentKind kind,
        GfxMapVertexChecksumProductionFidelity productionFidelity)
    {
        Checksum = checksum;
        Kind = kind;
        ProductionFidelity = productionFidelity;
    }

    public GfxMapVertexChecksum Checksum { get; }
    public GfxMapVertexChecksumAssignmentKind Kind { get; }
    public GfxMapVertexChecksumProductionFidelity ProductionFidelity
    {
        get;
    }
}

/// <summary>
/// Versioned assignment for the unconsumed GfxMap-only vertex word.
/// Imported maps preserve their exact word. Greenfield M3 geometry receives
/// constant zero by explicit Studio policy; this does not claim recovery of
/// the retail producer algorithm or grant persistence authority.
/// </summary>
public static class GfxMapVertexChecksumPolicy
{
    public const string StudioConstantZeroPolicyId =
        "iw4-studio.gfx-map-vertex-checksum/constant-zero-v1";

    public static GfxMapVertexChecksumPolicyStatus CurrentStatus =>
        GfxMapVertexChecksumPolicyStatus
            .ImportedPreservationAndStudioConstantZeroV1;

    public static GfxMapVertexChecksumAssignment PreserveImported(
        GfxMapVertexChecksum checksum) =>
        new(
            checksum,
            GfxMapVertexChecksumAssignmentKind.ImportedProduction,
            GfxMapVertexChecksumProductionFidelity.ExactImportedValue);

    public static GfxMapVertexChecksumAssignment AssignStudioConstantZero() =>
        new(
            new GfxMapVertexChecksum(0),
            GfxMapVertexChecksumAssignmentKind.StudioConstantZeroV1,
            GfxMapVertexChecksumProductionFidelity
                .DeterministicStudioAssignmentRetailParityUnproven);
}
