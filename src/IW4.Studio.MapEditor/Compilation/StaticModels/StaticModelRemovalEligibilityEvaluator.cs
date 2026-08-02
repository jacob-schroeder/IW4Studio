using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

public enum StaticModelRemovalEligibilityIssueKind
{
    CatalogAuthorityInvalid,
    BundleIdentityMismatch,
    RelationshipNotInCatalog,
    MissingGfxWorldAuthority,
    MissingCollisionAuthority,
    CollisionAuthorityMismatch,
    GfxRemovalInvariantInvalid,
    CollisionRemovalInvariantInvalid
}

public sealed record StaticModelRemovalEligibilityIssue(
    StaticModelRemovalEligibilityIssueKind Kind,
    string Detail);

/// <summary>
/// Stable evidence for the IW4 PS3 consumers whose index contracts are
/// rebuilt by the Phase 6 static-model removal slice.
/// </summary>
public static class StaticModelRemovalConsumerEvidence
{
    public const string ExecutableSha256 =
        "a2e79a8498dd63bebbf899eb04cc5928574fd229df52c082a8f01184906237a6";

    public const string Evidence =
        "IW4 PS3 Gfx AABB static-model consumer 0x00350928; " +
        "static-model shadow consumer 0x00343470; Clip static-model " +
        "traversal 0x001D8320/0x001D8540.";
}

/// <summary>
/// Cross-asset authorization for removing one exact-bundle Gfx/Col pair.
/// </summary>
public sealed class StaticModelRemovalEligibilityAssessment
{
    internal StaticModelRemovalEligibilityAssessment(
        StaticModelCompilationRelationship relationship,
        GfxStaticModelRemovalAssessment? gfx,
        ClipStaticModelRemovalAssessment? collision,
        IEnumerable<StaticModelRemovalEligibilityIssue> issues)
    {
        Relationship = relationship;
        Gfx = gfx;
        Collision = collision;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public StaticModelCompilationRelationship Relationship { get; }
    public GfxStaticModelRemovalAssessment? Gfx { get; }
    public ClipStaticModelRemovalAssessment? Collision { get; }
    public IReadOnlyList<StaticModelRemovalEligibilityIssue> Issues { get; }
    public bool IsPatchEligible =>
        Issues.Count == 0 &&
        Gfx?.IsEligible == true &&
        Collision?.IsEligible == true;
    public string Evidence =>
        IsPatchEligible
            ? StaticModelRemovalConsumerEvidence.Evidence
            : string.Join("; ", Issues.Select(value => value.Detail));
}

public static class StaticModelRemovalEligibilityEvaluator
{
    public static StaticModelRemovalEligibilityAssessment Evaluate(
        CompiledMapBundle bundle,
        StaticModelCorrespondenceCatalog catalog,
        StaticModelCompilationRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(relationship);

        var issues = new List<StaticModelRemovalEligibilityIssue>();
        if (!catalog.AuthoritiesValid)
        {
            issues.Add(new(
                StaticModelRemovalEligibilityIssueKind
                    .CatalogAuthorityInvalid,
                "The exact-bundle static-model correspondence authorities " +
                "are invalid."));
        }
        if (catalog.DocumentId != bundle.DocumentId ||
            !string.Equals(
                catalog.MapIdentity,
                bundle.MapIdentity,
                StringComparison.Ordinal) ||
            !string.Equals(
                catalog.BundleBaselineDigest,
                bundle.BaselineDigest,
                StringComparison.Ordinal))
        {
            issues.Add(new(
                StaticModelRemovalEligibilityIssueKind
                    .BundleIdentityMismatch,
                "The correspondence catalog does not belong to this exact " +
                "compiled-map baseline."));
        }
        if (!catalog.TryGetByRenderObjectId(
                relationship.RenderObjectId,
                out StaticModelCompilationRelationship? catalogEntry) ||
            catalogEntry != relationship)
        {
            issues.Add(new(
                StaticModelRemovalEligibilityIssueKind
                    .RelationshipNotInCatalog,
                "The requested pair is not an ExactBundleUnique relationship " +
                "in the supplied catalog."));
        }

        GfxWorldBuildData? gfx = null;
        if (!bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out gfx) ||
            gfx is null)
        {
            issues.Add(new(
                StaticModelRemovalEligibilityIssueKind
                    .MissingGfxWorldAuthority,
                "The bundle has no detached GfxMap authority."));
        }

        ClipMapBuildData? clip = null;
        if (!bundle.TryGetBaseline(
                relationship.CollisionAssetKind,
                out clip) ||
            clip is null)
        {
            issues.Add(new(
                StaticModelRemovalEligibilityIssueKind
                    .MissingCollisionAuthority,
                "The bundle has no detached collision authority for the " +
                "exact relationship."));
        }
        if (relationship.CollisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            issues.Add(new(
                StaticModelRemovalEligibilityIssueKind
                    .CollisionAuthorityMismatch,
                "Static-model removal requires a ColMapMp or ColMapSp " +
                "relationship owner."));
        }

        GfxStaticModelRemovalAssessment? gfxAssessment =
            gfx is null
                ? null
                : GfxStaticModelRemovalAssessor.Assess(
                    gfx,
                    [relationship.GfxSourceOrdinal]);
        ClipStaticModelRemovalAssessment? clipAssessment =
            clip is null
                ? null
                : ClipStaticModelRemovalAssessor.Assess(
                    clip,
                    [relationship.ClipSourceOrdinal]);
        if (gfxAssessment is { IsEligible: false })
        {
            issues.AddRange(gfxAssessment.Issues.Select(issue => new
                StaticModelRemovalEligibilityIssue(
                    StaticModelRemovalEligibilityIssueKind
                        .GfxRemovalInvariantInvalid,
                    issue.Detail)));
        }
        if (clipAssessment is { IsEligible: false })
        {
            issues.AddRange(clipAssessment.Issues.Select(issue => new
                StaticModelRemovalEligibilityIssue(
                    StaticModelRemovalEligibilityIssueKind
                        .CollisionRemovalInvariantInvalid,
                    issue.Detail)));
        }

        return new(
            relationship,
            gfxAssessment,
            clipAssessment,
            issues);
    }
}
