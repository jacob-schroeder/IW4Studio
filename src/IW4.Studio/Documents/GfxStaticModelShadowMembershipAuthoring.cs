using IW4.Assets.Assets.GfxMap;

namespace IW4.Studio.Documents;

/// <summary>
/// Machine-readable failure categories for the serialized primary-light
/// static-model shadow partition.
/// </summary>
public enum GfxStaticModelShadowMembershipIssueKind
{
    InvalidPrimaryLightCount,
    ShadowGeometryCardinalityMismatch,
    StaticModelCardinalityMismatch,
    StaticModelCountUnrepresentable,
    ShadowListCardinalityMismatch,
    ShadowStaticModelIndexNotStrictlyAscending,
    ShadowStaticModelIndexOutOfRange,
    DuplicateStaticModelMembership,
    MissingStaticModelMembership,
    StaticModelPrimaryLightIndexOutOfRange,
    ShadowOwnerPrimaryLightMismatch
}

/// <summary>
/// One precise violation of the imported GfxWorld shadow-membership
/// invariant.
/// </summary>
public sealed record GfxStaticModelShadowMembershipIssue(
    GfxStaticModelShadowMembershipIssueKind Kind,
    string Detail,
    int? PrimaryLightIndex = null,
    int? ListElementIndex = null,
    int? StaticModelIndex = null);

/// <summary>
/// Proven shadow ownership and the two draw-row fields that must remain
/// unchanged when serialized shadow membership bytes are preserved.
/// </summary>
public sealed record GfxStaticModelShadowRowEvidence(
    int StaticModelIndex,
    int ShadowOwnerPrimaryLightIndex,
    byte DrawPrimaryLightIndex,
    byte DrawFlags);

/// <summary>
/// Read-only evidence produced only for a complete, exact static-model
/// shadow partition.
/// </summary>
public sealed class GfxStaticModelShadowMembershipEvidence
{
    internal GfxStaticModelShadowMembershipEvidence(
        int primaryLightCount,
        int staticModelCount,
        int membershipCount,
        IReadOnlyList<GfxStaticModelShadowRowEvidence> staticModels)
    {
        PrimaryLightCount = primaryLightCount;
        StaticModelCount = staticModelCount;
        MembershipCount = membershipCount;
        StaticModels = staticModels;
    }

    public int PrimaryLightCount { get; }
    public int StaticModelCount { get; }
    public int MembershipCount { get; }
    public IReadOnlyList<GfxStaticModelShadowRowEvidence> StaticModels { get; }
}

/// <summary>
/// Result of validating the serialized GfxWorld static-model shadow
/// partition.
/// </summary>
public sealed class GfxStaticModelShadowMembershipAssessment
{
    internal GfxStaticModelShadowMembershipAssessment(
        IReadOnlyList<GfxStaticModelShadowMembershipIssue> issues,
        GfxStaticModelShadowMembershipEvidence? evidence)
    {
        Issues = issues;
        Evidence = evidence;
    }

    public bool IsValid =>
        Issues.Count == 0 &&
        Evidence is not null;

    public IReadOnlyList<GfxStaticModelShadowMembershipIssue> Issues { get; }
    public GfxStaticModelShadowMembershipEvidence? Evidence { get; }
}

/// <summary>
/// Validates the native GfxWorld invariant that ShadowGeom is a complete
/// partition of static-model ordinals by draw-row PrimaryLightIndex.
/// </summary>
public static class GfxStaticModelShadowMembershipAssessor
{
    public static GfxStaticModelShadowMembershipAssessment Assess(
        GfxWorldBuildData source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Assess(source.Definition);
    }

    /// <summary>
    /// Definition-only overload for compiler graph validators that already
    /// own a detached GfxWorld and must not clone its full payload merely to
    /// assess the shadow partition.
    /// </summary>
    public static GfxStaticModelShadowMembershipAssessment Assess(
        GfxWorldAsset world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var issues =
            new List<GfxStaticModelShadowMembershipIssue>();
        if (world.PrimaryLightCount < 0)
        {
            issues.Add(Issue(
                GfxStaticModelShadowMembershipIssueKind
                    .InvalidPrimaryLightCount,
                $"GfxWorld has negative primary-light count {world.PrimaryLightCount}."));
            return Invalid(issues);
        }
        if (world.ShadowGeom.Count !=
            world.PrimaryLightCount)
        {
            issues.Add(Issue(
                GfxStaticModelShadowMembershipIssueKind
                    .ShadowGeometryCardinalityMismatch,
                $"GfxWorld materializes {world.ShadowGeom.Count} shadow-geometry rows for {world.PrimaryLightCount} primary lights."));
            return Invalid(issues);
        }

        GfxWorldDpvsStatic dpvs = world.Dpvs;
        if (dpvs.SModelCount > int.MaxValue ||
            dpvs.SModelDrawInsts.Count !=
                (int)dpvs.SModelCount)
        {
            issues.Add(Issue(
                GfxStaticModelShadowMembershipIssueKind
                    .StaticModelCardinalityMismatch,
                $"GfxWorld.dpvs declares {dpvs.SModelCount} static models but materializes {dpvs.SModelDrawInsts.Count} draw rows."));
            return Invalid(issues);
        }

        int staticModelCount = (int)dpvs.SModelCount;
        if (staticModelCount >
            (int)ushort.MaxValue + 1)
        {
            issues.Add(Issue(
                GfxStaticModelShadowMembershipIssueKind
                    .StaticModelCountUnrepresentable,
                $"The ushort shadow membership domain cannot represent all {staticModelCount} static-model ordinals."));
            return Invalid(issues);
        }

        int[] ownerByStaticModel =
            Enumerable.Repeat(-1, staticModelCount).ToArray();
        int[] occurrenceCount =
            new int[staticModelCount];
        int membershipCount = 0;
        for (int primaryLightIndex = 0;
             primaryLightIndex < world.ShadowGeom.Count;
             primaryLightIndex++)
        {
            GfxShadowGeometry row =
                world.ShadowGeom[primaryLightIndex];
            if (row.SModelCount !=
                row.SModelIndex.Count)
            {
                issues.Add(Issue(
                    GfxStaticModelShadowMembershipIssueKind
                        .ShadowListCardinalityMismatch,
                    $"Primary-light row {primaryLightIndex} declares {row.SModelCount} static-model indices but materializes {row.SModelIndex.Count}.",
                    primaryLightIndex));
            }

            ushort? previous = null;
            for (int elementIndex = 0;
                 elementIndex < row.SModelIndex.Count;
                 elementIndex++)
            {
                ushort staticModelIndex =
                    row.SModelIndex[elementIndex];
                membershipCount++;
                if (previous is not null &&
                    staticModelIndex <= previous.Value)
                {
                    issues.Add(Issue(
                        GfxStaticModelShadowMembershipIssueKind
                            .ShadowStaticModelIndexNotStrictlyAscending,
                        $"Primary-light row {primaryLightIndex} is not strictly ascending at list element {elementIndex}: {previous.Value}, {staticModelIndex}.",
                        primaryLightIndex,
                        elementIndex,
                        staticModelIndex));
                }
                previous = staticModelIndex;

                if (staticModelIndex >=
                    staticModelCount)
                {
                    issues.Add(Issue(
                        GfxStaticModelShadowMembershipIssueKind
                            .ShadowStaticModelIndexOutOfRange,
                        $"Primary-light row {primaryLightIndex}, list element {elementIndex} references static-model ordinal {staticModelIndex} outside {staticModelCount} rows.",
                        primaryLightIndex,
                        elementIndex,
                        staticModelIndex));
                    continue;
                }

                occurrenceCount[staticModelIndex]++;
                if (occurrenceCount[staticModelIndex] == 1)
                {
                    ownerByStaticModel[staticModelIndex] =
                        primaryLightIndex;
                }
                else
                {
                    issues.Add(Issue(
                        GfxStaticModelShadowMembershipIssueKind
                            .DuplicateStaticModelMembership,
                        $"Static-model ordinal {staticModelIndex} occurs more than once in the shadow partition.",
                        primaryLightIndex,
                        elementIndex,
                        staticModelIndex));
                }
            }
        }

        for (int staticModelIndex = 0;
             staticModelIndex < staticModelCount;
             staticModelIndex++)
        {
            if (occurrenceCount[staticModelIndex] == 0)
            {
                issues.Add(Issue(
                    GfxStaticModelShadowMembershipIssueKind
                        .MissingStaticModelMembership,
                    $"Static-model ordinal {staticModelIndex} is absent from the shadow partition.",
                    staticModelIndex: staticModelIndex));
                continue;
            }
            if (occurrenceCount[staticModelIndex] != 1)
                continue;

            byte drawPrimaryLightIndex =
                dpvs.SModelDrawInsts[staticModelIndex]
                    .PrimaryLightIndex;
            if (drawPrimaryLightIndex >=
                world.PrimaryLightCount)
            {
                issues.Add(Issue(
                    GfxStaticModelShadowMembershipIssueKind
                        .StaticModelPrimaryLightIndexOutOfRange,
                    $"Static-model ordinal {staticModelIndex} names primary light {drawPrimaryLightIndex} outside {world.PrimaryLightCount} rows.",
                    drawPrimaryLightIndex,
                    staticModelIndex: staticModelIndex));
                continue;
            }
            if (ownerByStaticModel[staticModelIndex] !=
                drawPrimaryLightIndex)
            {
                issues.Add(Issue(
                    GfxStaticModelShadowMembershipIssueKind
                        .ShadowOwnerPrimaryLightMismatch,
                    $"Static-model ordinal {staticModelIndex} is serialized under primary-light row {ownerByStaticModel[staticModelIndex]} but its draw row names {drawPrimaryLightIndex}.",
                    ownerByStaticModel[staticModelIndex],
                    staticModelIndex: staticModelIndex));
            }
        }

        if (issues.Count != 0)
            return Invalid(issues);

        GfxStaticModelShadowRowEvidence[] rows =
            Enumerable.Range(0, staticModelCount)
                .Select(staticModelIndex =>
                {
                    GfxStaticModelDrawInst draw =
                        dpvs.SModelDrawInsts[staticModelIndex];
                    return new GfxStaticModelShadowRowEvidence(
                        staticModelIndex,
                        ownerByStaticModel[staticModelIndex],
                        draw.PrimaryLightIndex,
                        draw.Flags);
                })
                .ToArray();
        return new(
            [],
            new GfxStaticModelShadowMembershipEvidence(
                world.PrimaryLightCount,
                staticModelCount,
                membershipCount,
                rows));
    }

    private static GfxStaticModelShadowMembershipAssessment Invalid(
        IReadOnlyList<GfxStaticModelShadowMembershipIssue> issues) =>
        new(issues.ToArray(), evidence: null);

    private static GfxStaticModelShadowMembershipIssue Issue(
        GfxStaticModelShadowMembershipIssueKind kind,
        string detail,
        int? primaryLightIndex = null,
        int? listElementIndex = null,
        int? staticModelIndex = null) =>
        new(
            kind,
            detail,
            primaryLightIndex,
            listElementIndex,
            staticModelIndex);
}
