using System.Collections.ObjectModel;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Identifies the bounded GfxWorld projection used to cross the managed
/// serializer/loader boundary before a retail target is involved.
/// </summary>
public static class GfxWorldTargetAcceptanceProfile
{
    public const string CompilerIdentity =
        "iw4-studio.gfxworld.target-acceptance.managed-unlit-zero@1";

    public const string UnlitProjectionPolicyId =
        "iw4-studio.gfxworld.unlit-zero-probe@1";

    public const string SurfaceBoundsTailPolicyId =
        "iw4-studio.gfxworld.surface-bounds-zero-tail-probe@1";

    public const int SurfaceBoundsTailByteCount = 8;
}

/// <summary>
/// This authority proves only that the detached graph passes the managed
/// GfxWorld serializer plan. It grants neither retail/emulator acceptance nor
/// FastFile persistence.
/// </summary>
public enum GfxWorldTargetAcceptanceCandidateAuthority
{
    ManagedSerializedProbeOnly = 0
}

public enum GfxWorldTargetAcceptanceDeferredMilestone
{
    M4TargetConsumerAcceptance = 4,
    M5LightingAndEnvironment = 5,
    M7AssetResolutionAndPersistence = 7
}

public enum GfxWorldTargetAcceptanceBlockerKind
{
    RetailOrEmulatorConsumerAcceptanceNotEstablished = 0,
    ProbeOnlySurfaceBoundsTailPolicyNotTargetAccepted = 1,
    SurfaceLightingClassificationNotCompiled = 2,
    LightingEnvironmentAndBakeOutputsNotCompiled = 3,
    MaterialDependenciesNotResolved = 4,
    CompleteMapAssetGraphNotAssembled = 5,
    LinkingEmissionAndPersistenceNotAuthorized = 6
}

public sealed record GfxWorldTargetAcceptanceBlocker
{
    public GfxWorldTargetAcceptanceBlocker(
        GfxWorldTargetAcceptanceDeferredMilestone milestone,
        GfxWorldTargetAcceptanceBlockerKind kind,
        string detail)
    {
        if (!Enum.IsDefined(milestone))
            throw new ArgumentOutOfRangeException(nameof(milestone));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Milestone = milestone;
        Kind = kind;
        Detail = detail;
    }

    public GfxWorldTargetAcceptanceDeferredMilestone Milestone { get; }
    public GfxWorldTargetAcceptanceBlockerKind Kind { get; }
    public string Detail { get; }
}

public enum GfxWorldTargetAcceptanceIssueKind
{
    SourceCandidateInvalid = 0,
    PrimaryChecksumAssignmentInvalid = 1,
    ProjectionIdentityMismatch = 2,
    GeometryProjectionMismatch = 3,
    VisibilityProjectionMismatch = 4,
    RuntimeStateAuthorshipViolation = 5,
    UnlitProbePolicyMismatch = 6,
    SymbolicReferenceMismatch = 7,
    SerializerValidationFailed = 8,
    SerializerPlanningFailed = 9,
    DeferredBlockerContractMismatch = 10
}

public sealed record GfxWorldTargetAcceptanceIssue(
    GfxWorldTargetAcceptanceIssueKind Kind,
    string Path,
    string Detail);

public sealed class GfxWorldTargetAcceptanceAssessment
{
    private readonly IReadOnlyList<GfxWorldTargetAcceptanceIssue> _issues;

    internal GfxWorldTargetAcceptanceAssessment(
        IEnumerable<GfxWorldTargetAcceptanceIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _issues =
            new ReadOnlyCollection<GfxWorldTargetAcceptanceIssue>(
                issues.ToArray());
    }

    public IReadOnlyList<GfxWorldTargetAcceptanceIssue> Issues => _issues;
    public bool IsValid => Issues.Count == 0;
}

public sealed class GfxWorldTargetAcceptanceCompilationException :
    Exception
{
    public GfxWorldTargetAcceptanceCompilationException(
        GfxWorldTargetAcceptanceAssessment assessment)
        : base(CreateMessage(assessment))
    {
        Assessment = assessment ??
            throw new ArgumentNullException(nameof(assessment));
    }

    public GfxWorldTargetAcceptanceAssessment Assessment { get; }

    private static string CreateMessage(
        GfxWorldTargetAcceptanceAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return
            "The bounded GfxWorld managed serialized probe failed closed: " +
            string.Join(
                "; ",
                assessment.Issues.Select(value =>
                    $"{value.Path}: {value.Detail}"));
    }
}

/// <summary>
/// Detached GfxWorld serializer candidate. The contained definition and
/// symbolic references are address-free source data, but this wrapper
/// intentionally implements no XAsset build-data interface.
/// </summary>
public sealed class GfxWorldTargetAcceptanceCandidate
{
    private readonly IReadOnlyList<GfxWorldTargetAcceptanceBlocker>
        _blockers;

    internal GfxWorldTargetAcceptanceCandidate(
        RenderWorldVisibilityCandidate visibilityCandidate,
        MapPrimaryChecksumAssignment primaryChecksumAssignment,
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references,
        IEnumerable<GfxWorldTargetAcceptanceBlocker> blockers,
        GfxWorldTargetAcceptanceAssessment validationAssessment)
    {
        VisibilityCandidate = visibilityCandidate ??
            throw new ArgumentNullException(nameof(visibilityCandidate));
        PrimaryChecksumAssignment = primaryChecksumAssignment ??
            throw new ArgumentNullException(
                nameof(primaryChecksumAssignment));
        Definition = definition ??
            throw new ArgumentNullException(nameof(definition));
        References = references ??
            throw new ArgumentNullException(nameof(references));
        ArgumentNullException.ThrowIfNull(blockers);
        ValidationAssessment = validationAssessment ??
            throw new ArgumentNullException(
                nameof(validationAssessment));

        _blockers =
            new ReadOnlyCollection<GfxWorldTargetAcceptanceBlocker>(
                blockers.ToArray());
    }

    public string CompilerIdentity =>
        GfxWorldTargetAcceptanceProfile.CompilerIdentity;
    public string UnlitProjectionPolicyId =>
        GfxWorldTargetAcceptanceProfile.UnlitProjectionPolicyId;
    public string SurfaceBoundsTailPolicyId =>
        GfxWorldTargetAcceptanceProfile.SurfaceBoundsTailPolicyId;
    public GfxWorldTargetAcceptanceCandidateAuthority Authority =>
        GfxWorldTargetAcceptanceCandidateAuthority
            .ManagedSerializedProbeOnly;
    public bool ManagedSerializerAccepted =>
        ValidationAssessment.IsValid;
    public bool TargetConsumerAccepted => false;
    public bool PersistenceAuthorized => false;
    public RenderWorldVisibilityCandidate VisibilityCandidate { get; }
    public MapPrimaryChecksumAssignment PrimaryChecksumAssignment { get; }
    public GfxWorldAsset Definition { get; }
    public GfxWorldReferenceBuildData References { get; }
    public IReadOnlyList<GfxWorldTargetAcceptanceBlocker> Blockers =>
        _blockers;
    public GfxWorldTargetAcceptanceAssessment ValidationAssessment { get; }
}

/// <summary>
/// Internal bridge used only to exercise the existing checked body emitter.
/// The public candidate remains unable to enter a linker or Save As path.
/// </summary>
internal sealed class GfxWorldTargetAcceptanceBuildData :
    IGfxWorldBuildData
{
    public GfxWorldTargetAcceptanceBuildData(
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references)
    {
        Definition = definition ??
            throw new ArgumentNullException(nameof(definition));
        References = references ??
            throw new ArgumentNullException(nameof(references));
    }

    public IW4.FastFiles.Zone.XAssetType AssetType =>
        IW4.FastFiles.Zone.XAssetType.GfxMap;
    public GfxWorldAsset Definition { get; }
    public GfxWorldReferenceBuildData References { get; }
}
