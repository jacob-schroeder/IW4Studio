using System.Collections.ObjectModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

public enum StaticModelTranslationEligibilityIssueKind
{
    CorrespondenceAuthorityMismatch,
    MissingGfxAuthority,
    MissingCollisionAuthority,
    MissingComWorldAuthority,
    GfxSpatialMembership,
    CollisionSpatialTree,
    LightingOrProbePreservation,
    ShadowMembership
}

public sealed record StaticModelTranslationEligibilityIssue(
    StaticModelTranslationEligibilityIssueKind Kind,
    string Detail);

/// <summary>
/// Complete compile-time admission result for one exact-bundle static-model
/// translation. Component assessments remain available for diagnostics; only
/// a result with no issue exposes a command authorization.
/// </summary>
public sealed class StaticModelTranslationEligibilityAssessment
{
    internal StaticModelTranslationEligibilityAssessment(
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin,
        IEnumerable<StaticModelTranslationEligibilityIssue> issues,
        GfxStaticModelTranslationSpatialAssessment? gfxSpatial,
        ClipStaticModelTranslationSpatialAssessment? collisionSpatial,
        StaticModelLightingPreservationEligibility? lighting,
        GfxStaticModelShadowMembershipAssessment? shadows,
        StaticModelTranslationAuthorization? authorization)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(issues);
        Relationship = relationship;
        DestinationOrigin = destinationOrigin;
        Issues = new ReadOnlyCollection<
            StaticModelTranslationEligibilityIssue>(
            issues.ToArray());
        GfxSpatial = gfxSpatial;
        CollisionSpatial = collisionSpatial;
        Lighting = lighting;
        Shadows = shadows;
        Authorization = authorization;
        if ((Issues.Count == 0) != (authorization is not null))
        {
            throw new ArgumentException(
                "Only an issue-free translation assessment may carry an " +
                "authorization.",
                nameof(authorization));
        }
    }

    public StaticModelCompilationRelationship Relationship { get; }
    public MapVector3 DestinationOrigin { get; }
    public IReadOnlyList<StaticModelTranslationEligibilityIssue> Issues
    {
        get;
    }
    public GfxStaticModelTranslationSpatialAssessment? GfxSpatial { get; }
    public ClipStaticModelTranslationSpatialAssessment?
        CollisionSpatial { get; }
    public StaticModelLightingPreservationEligibility? Lighting { get; }
    public GfxStaticModelShadowMembershipAssessment? Shadows { get; }
    public StaticModelTranslationAuthorization? Authorization { get; }
    public bool IsPatchEligible => Authorization is not null;
    public string Evidence =>
        IsPatchEligible
            ? Authorization!.Evidence
            : string.Join(
                " ",
                Issues.Select(value => value.Detail));
}

/// <summary>
/// Composes the exact relationship, Gfx cell/leaf, Clip tree, runtime probe,
/// static-lighting, and shadow-list preservation gates for the deliberately
/// narrow existing-instance translation capability.
/// </summary>
public static class StaticModelTranslationEligibilityEvaluator
{
    public static StaticModelTranslationEligibilityAssessment Evaluate(
        CompiledMapBundle bundle,
        StaticModelCorrespondenceCatalog catalog,
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(relationship);
        if (!destinationOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOrigin),
                "Static-model translation coordinates must be finite.");
        }

        var issues =
            new List<StaticModelTranslationEligibilityIssue>();
        if (catalog.DocumentId != bundle.DocumentId ||
            !string.Equals(
                catalog.BundleBaselineDigest,
                bundle.BaselineDigest,
                StringComparison.Ordinal) ||
            !catalog.TryGetByRenderObjectId(
                relationship.RenderObjectId,
                out StaticModelCompilationRelationship? authorized) ||
            authorized != relationship)
        {
            issues.Add(new(
                StaticModelTranslationEligibilityIssueKind
                    .CorrespondenceAuthorityMismatch,
                "The static-model relationship is not owned by this exact " +
                "imported bundle and correspondence catalog."));
            return Result();
        }

        if (!bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) ||
            gfx is null)
        {
            issues.Add(new(
                StaticModelTranslationEligibilityIssueKind
                    .MissingGfxAuthority,
                "The compiled map bundle has no detached GfxMap authority."));
            return Result();
        }
        if (!bundle.TryGetBaseline(
                relationship.CollisionAssetKind,
                out ClipMapBuildData? clip) ||
            clip is null)
        {
            issues.Add(new(
                StaticModelTranslationEligibilityIssueKind
                    .MissingCollisionAuthority,
                $"The compiled map bundle has no detached " +
                $"{relationship.CollisionAssetKind} authority."));
            return Result();
        }
        if (!bundle.TryGetBaseline(
                MapAssetKind.ComMap,
                out ComWorldBuildData? com) ||
            com is null)
        {
            issues.Add(new(
                StaticModelTranslationEligibilityIssueKind
                    .MissingComWorldAuthority,
                "Static-light preservation requires the sibling detached " +
                "ComMap authority."));
            return Result();
        }

        var gfxEdit = new StaticModelTranslationEdit(
            relationship.GfxSourceOrdinal,
            destinationOrigin.X,
            destinationOrigin.Y,
            destinationOrigin.Z);
        var clipEdit = new StaticModelTranslationEdit(
            relationship.ClipSourceOrdinal,
            destinationOrigin.X,
            destinationOrigin.Y,
            destinationOrigin.Z);
        GfxStaticModelTranslationSpatialAssessment gfxSpatial =
            GfxStaticModelTranslationSpatialAssessor.Assess(
                gfx,
                gfxEdit);
        ClipStaticModelTranslationSpatialAssessment collisionSpatial =
            clip.AssessConservativeStaticModelTranslation(
                clipEdit);
        StaticModelLightingPreservationEligibility lighting =
            StaticModelLightingPreservationEligibilityEvaluator.Evaluate(
                gfx,
                com,
                relationship.GfxSourceOrdinal,
                new Float3BuildData(
                    destinationOrigin.X,
                    destinationOrigin.Y,
                    destinationOrigin.Z));
        GfxStaticModelShadowMembershipAssessment shadows =
            GfxStaticModelShadowMembershipAssessor.Assess(gfx);

        issues.AddRange(gfxSpatial.Issues.Select(value =>
            new StaticModelTranslationEligibilityIssue(
                StaticModelTranslationEligibilityIssueKind
                    .GfxSpatialMembership,
                value.Detail)));
        issues.AddRange(collisionSpatial.Issues.Select(value =>
            new StaticModelTranslationEligibilityIssue(
                StaticModelTranslationEligibilityIssueKind
                    .CollisionSpatialTree,
                value.Detail)));
        issues.AddRange(lighting.Issues.Select(value =>
            new StaticModelTranslationEligibilityIssue(
                StaticModelTranslationEligibilityIssueKind
                    .LightingOrProbePreservation,
                value.Detail)));
        issues.AddRange(shadows.Issues.Select(value =>
            new StaticModelTranslationEligibilityIssue(
                StaticModelTranslationEligibilityIssueKind
                    .ShadowMembership,
                value.Detail)));

        StaticModelTranslationAuthorization? authorization =
            issues.Count == 0
                ? new StaticModelTranslationAuthorization(
                    relationship,
                    destinationOrigin,
                    bundle.BaselineDigest,
                    string.Join(
                        " ",
                        new string[]
                        {
                            "Exact-bundle render/collision identity is mutual and unique.",
                            "Gfx DPVS cell and AABB-leaf memberships are preserved.",
                            "Clip static-model tree envelopes can be expanded conservatively.",
                            lighting.EvidenceSummary,
                            $"The exact shadow partition contains " +
                            $"{shadows.Evidence!.MembershipCount} memberships " +
                            $"across {shadows.Evidence.PrimaryLightCount} " +
                            "primary-light rows."
                        }
                        .Where(value =>
                            !string.IsNullOrWhiteSpace(value))))
                : null;
        return Result(
            gfxSpatial,
            collisionSpatial,
            lighting,
            shadows,
            authorization);

        StaticModelTranslationEligibilityAssessment Result(
            GfxStaticModelTranslationSpatialAssessment? gfxResult = null,
            ClipStaticModelTranslationSpatialAssessment? clipResult = null,
            StaticModelLightingPreservationEligibility? lightingResult = null,
            GfxStaticModelShadowMembershipAssessment? shadowResult = null,
            StaticModelTranslationAuthorization? proof = null) =>
            new(
                relationship,
                destinationOrigin,
                issues,
                gfxResult,
                clipResult,
                lightingResult,
                shadowResult,
                proof);
    }
}
