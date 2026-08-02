using IW4.Assets.Assets.ColMap;
using IW4.Assets.Math;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

public enum ClipStaticModelRemovalIssueKind
{
    EmptyRemovalSet,
    DuplicateSourceOrdinal,
    StaticModelCardinalityMismatch,
    StaticModelOrdinalOutOfRange,
    StaticModelReferenceCardinalityMismatch,
    DependencyProviderRemovalUnsupported,
    SpatialInvariantInvalid,
    EmptyLeafWouldRemain
}

public sealed record ClipStaticModelRemovalIssue(
    ClipStaticModelRemovalIssueKind Kind,
    string Detail,
    int? StaticModelOrdinal = null,
    int? SpatialNodeOrdinal = null);

/// <summary>
/// Exact-source authorization for a Clip static-model removal rebuild.
/// Existing node rows and conservative bounds are retained; only the virtual
/// model/node child-domain offsets are reindexed.
/// </summary>
public sealed class ClipStaticModelRemovalAssessment
{
    private readonly ClipMapBuildData _source;

    internal ClipStaticModelRemovalAssessment(
        ClipMapBuildData source,
        IEnumerable<int> sourceOrdinals,
        IEnumerable<ClipStaticModelRemovalIssue> issues)
    {
        _source = source;
        SourceOrdinals = Array.AsReadOnly(
            sourceOrdinals.Order().ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public IReadOnlyList<int> SourceOrdinals { get; }
    public IReadOnlyList<ClipStaticModelRemovalIssue> Issues { get; }
    public bool IsEligible => Issues.Count == 0;

    internal bool IsFor(ClipMapBuildData source) =>
        ReferenceEquals(_source, source);
}

public static class ClipStaticModelRemovalAssessor
{
    public static ClipStaticModelRemovalAssessment Assess(
        ClipMapBuildData source,
        IEnumerable<int> sourceOrdinals)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceOrdinals);

        int[] requested = sourceOrdinals.ToArray();
        int[] distinct = requested.Distinct().Order().ToArray();
        var issues = new List<ClipStaticModelRemovalIssue>();
        if (requested.Length == 0)
        {
            issues.Add(new(
                ClipStaticModelRemovalIssueKind.EmptyRemovalSet,
                "At least one existing Clip static-model row is required."));
            return new(source, distinct, issues);
        }
        if (requested.Length != distinct.Length)
        {
            issues.Add(new(
                ClipStaticModelRemovalIssueKind.DuplicateSourceOrdinal,
                "A Clip static-model source ordinal may be removed at most once."));
        }

        ClipMapAsset definition = source.Definition;
        int count = definition.NumStaticModels;
        if (count < 0 ||
            definition.StaticModelList.Count != count)
        {
            issues.Add(new(
                ClipStaticModelRemovalIssueKind
                    .StaticModelCardinalityMismatch,
                "ColMap static-model count does not match the materialized " +
                "row table."));
            return new(source, distinct, issues);
        }
        foreach (int ordinal in distinct.Where(value =>
                     value < 0 || value >= count))
        {
            issues.Add(new(
                ClipStaticModelRemovalIssueKind
                    .StaticModelOrdinalOutOfRange,
                $"Clip static-model ordinal {ordinal} is outside the " +
                $"{count}-row table.",
                ordinal));
        }
        if (issues.Count != 0)
            return new(source, distinct, issues);

        ClipMapReferenceBuildData references = source.References;
        if (references.StaticModels.Count != count ||
            references.StaticModelLinks.Count is not (0) &&
            references.StaticModelLinks.Count != count)
        {
            issues.Add(new(
                ClipStaticModelRemovalIssueKind
                    .StaticModelReferenceCardinalityMismatch,
                "Detached Clip XModel references and optional link " +
                "provenance must parallel the static-model table."));
            return new(source, distinct, issues);
        }

        foreach (int ordinal in distinct)
        {
            NestedXAssetBuildLink? modelLink =
                references.StaticModelLinks.Count == 0
                    ? null
                    : references.StaticModelLinks[ordinal];
            if (modelLink?.SourceForm is
                    NestedXAssetPointerSourceForm.Inline or
                    NestedXAssetPointerSourceForm.Insert ||
                modelLink?.IncomingDefinition is not null)
            {
                issues.Add(new(
                    ClipStaticModelRemovalIssueKind
                        .DependencyProviderRemovalUnsupported,
                    $"Clip static-model ordinal {ordinal} materializes an " +
                    "inline/insert XModel provider. Re-owning nested XAsset " +
                    "dependencies is outside this invariant group.",
                    ordinal));
            }

            ClipStaticModel row =
                definition.StaticModelList[ordinal];
            if (!float.IsFinite(row.Origin.X) ||
                !float.IsFinite(row.Origin.Y) ||
                !float.IsFinite(row.Origin.Z))
            {
                issues.Add(new(
                    ClipStaticModelRemovalIssueKind
                        .SpatialInvariantInvalid,
                    $"Clip static-model ordinal {ordinal} has a non-finite " +
                    "source origin.",
                    ordinal));
                continue;
            }
            ClipStaticModelTranslationSpatialAssessment spatial =
                source.AssessConservativeStaticModelTranslation(
                    new StaticModelTranslationEdit(
                        ordinal,
                        row.Origin.X,
                        row.Origin.Y,
                        row.Origin.Z));
            issues.AddRange(spatial.Issues.Select(issue => new
                ClipStaticModelRemovalIssue(
                    ClipStaticModelRemovalIssueKind
                        .SpatialInvariantInvalid,
                    issue.Detail,
                    ordinal)));
        }

        if (issues.Any(value =>
                value.Kind ==
                ClipStaticModelRemovalIssueKind
                    .SpatialInvariantInvalid))
        {
            return new(source, distinct, issues);
        }

        HashSet<int> removed = distinct.ToHashSet();
        for (int nodeOrdinal = 0;
             nodeOrdinal < definition.SModelNodes.Count;
             nodeOrdinal++)
        {
            SModelAabbNode node =
                definition.SModelNodes[nodeOrdinal];
            if (node.FirstChild >= count)
                continue;

            int removedFromLeaf = Enumerable.Range(
                    node.FirstChild,
                    node.ChildCount)
                .Count(removed.Contains);
            if (removedFromLeaf == node.ChildCount)
            {
                issues.Add(new(
                    ClipStaticModelRemovalIssueKind.EmptyLeafWouldRemain,
                    $"Removing the requested rows would leave Clip spatial " +
                    $"leaf {nodeOrdinal} with an empty child range.",
                    SpatialNodeOrdinal: nodeOrdinal));
            }
        }

        return new(source, distinct, issues);
    }
}

public sealed partial class ClipMapBuildData
{
    /// <summary>
    /// Removes existing Clip static-model rows, retaining the exact spatial
    /// node topology and conservative envelopes while reindexing leaf ranges
    /// and the virtual node-domain base.
    /// </summary>
    public ClipMapBuildData WithRemovedStaticModels(
        ClipStaticModelRemovalAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsFor(this))
        {
            throw new InvalidOperationException(
                "A Clip static-model removal assessment can authorize only " +
                "the exact detached source it inspected.");
        }
        if (!assessment.IsEligible)
        {
            throw new InvalidOperationException(
                "An ineligible Clip static-model removal cannot be applied.");
        }

        ClipStaticModelRemovalAssessment current =
            ClipStaticModelRemovalAssessor.Assess(
                this,
                assessment.SourceOrdinals);
        if (!current.IsEligible)
        {
            throw new InvalidOperationException(
                "The Clip static-model removal assessment became stale: " +
                current.Issues[0].Detail);
        }

        ClipMapBuildData edited = Copy();
        ClipMapAsset definition = edited.Definition;
        HashSet<int> removed =
            assessment.SourceOrdinals.ToHashSet();
        int sourceCount = definition.NumStaticModels;
        int remainingCount = sourceCount - removed.Count;

        ClipStaticModel[] models = definition.StaticModelList
            .Where((_, ordinal) => !removed.Contains(ordinal))
            .ToArray();
        SModelAabbNode[] nodes = definition.SModelNodes
            .Select(node =>
            {
                int first = node.FirstChild;
                int childCount = node.ChildCount;
                if (first < sourceCount)
                {
                    int end = checked(first + childCount);
                    int removedBefore = assessment.SourceOrdinals
                        .Count(value => value < first);
                    int removedWithin = assessment.SourceOrdinals
                        .Count(value =>
                            value >= first && value < end);
                    return new SModelAabbNode
                    {
                        Bounds = new Bounds
                        {
                            MidPoint =
                                StaticModelSpatialEnvelope.Copy(
                                    node.Bounds.MidPoint),
                            HalfSize =
                                StaticModelSpatialEnvelope.Copy(
                                    node.Bounds.HalfSize)
                        },
                        FirstChild = checked((ushort)(
                            first - removedBefore)),
                        ChildCount = checked((ushort)(
                            childCount - removedWithin))
                    };
                }

                return new SModelAabbNode
                {
                    Bounds = new Bounds
                    {
                        MidPoint =
                            StaticModelSpatialEnvelope.Copy(
                                node.Bounds.MidPoint),
                        HalfSize =
                            StaticModelSpatialEnvelope.Copy(
                                node.Bounds.HalfSize)
                    },
                    FirstChild = checked((ushort)(
                        first - removed.Count)),
                    ChildCount = node.ChildCount
                };
            })
            .ToArray();

        Set(
            definition,
            nameof(ClipMapAsset.NumStaticModels),
            remainingCount);
        Set(
            definition,
            nameof(ClipMapAsset.StaticModelList),
            models);
        Set(
            definition,
            nameof(ClipMapAsset.SModelNodes),
            nodes);

        var references = new ClipMapReferenceBuildData(
            edited.References.StaticModels.Where(
                (_, ordinal) => !removed.Contains(ordinal)),
            edited.References.DynamicEntities,
            edited.References.MapEnts,
            edited.References.StaticModelLinks.Count == 0
                ? []
                : edited.References.StaticModelLinks.Where(
                        (_, ordinal) => !removed.Contains(ordinal)),
            edited.References.MapEntsLink);
        return new ClipMapBuildData(
            SerializedType,
            definition,
            references,
            new ClipMapLinkerProvenance(
                importedIsInUse:
                    edited.LinkerProvenance.ImportedIsInUse));
    }
}
