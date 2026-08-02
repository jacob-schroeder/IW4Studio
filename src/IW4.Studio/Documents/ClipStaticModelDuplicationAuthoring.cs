using IW4.Assets.Assets.ColMap;
using IW4.Assets.Math;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Machine-readable reasons why one conservatively translated Clip
/// static-model row cannot be inserted into the compiled collision graph.
/// </summary>
public enum ClipStaticModelDuplicationIssueKind
{
    SpatialAssessmentIneligible,
    StaticModelCardinalityMismatch,
    StaticModelCountUnrepresentable,
    StaticModelReferenceCardinalityMismatch,
    SourceModelAliasUnavailable,
    SpatialTreeInvalid,
    SpatialChildRangeUnrepresentable
}

public sealed record ClipStaticModelDuplicationIssue(
    ClipStaticModelDuplicationIssueKind Kind,
    string Detail,
    int? StaticModelOrdinal = null,
    int? SpatialNodeOrdinal = null);

/// <summary>
/// Exact-source authorization for inserting one translated Clip static-model
/// immediately after its immutable template row.
/// </summary>
public sealed class ClipStaticModelDuplicationAssessment
{
    private readonly ClipMapBuildData _source;

    internal ClipStaticModelDuplicationAssessment(
        ClipMapBuildData source,
        ClipStaticModelTranslationSpatialAssessment spatialAssessment,
        int newOrdinal,
        IEnumerable<ClipStaticModelDuplicationIssue> issues)
    {
        _source = source;
        SpatialAssessment = spatialAssessment;
        NewOrdinal = newOrdinal;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public ClipStaticModelTranslationSpatialAssessment SpatialAssessment
    {
        get;
    }
    public StaticModelTranslationEdit Edit => SpatialAssessment.Edit;
    public int SourceOrdinal => Edit.SourceOrdinal;
    public int NewOrdinal { get; }
    public IReadOnlyList<ClipStaticModelDuplicationIssue> Issues { get; }
    public bool IsEligible => Issues.Count == 0;

    internal bool IsFor(ClipMapBuildData source) =>
        ReferenceEquals(_source, source);
}

/// <summary>
/// Validates collision-row cardinality, topology, virtual child offsets, and
/// packed XModel alias provenance before a Clip duplication is authorized.
/// </summary>
public static class ClipStaticModelDuplicationAssessor
{
    public static ClipStaticModelDuplicationAssessment Assess(
        ClipMapBuildData source,
        ClipStaticModelTranslationSpatialAssessment spatialAssessment)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(spatialAssessment);

        var issues = new List<ClipStaticModelDuplicationIssue>();
        if (!spatialAssessment.IsEligible)
        {
            issues.AddRange(spatialAssessment.Issues.Select(issue => new
                ClipStaticModelDuplicationIssue(
                    ClipStaticModelDuplicationIssueKind
                        .SpatialAssessmentIneligible,
                    issue.Detail,
                    spatialAssessment.Edit.SourceOrdinal)));
        }

        ClipMapAsset definition = source.Definition;
        int count = definition.NumStaticModels;
        if (count < 0 ||
            definition.StaticModelList.Count != count ||
            definition.SModelNodeCount !=
                definition.SModelNodes.Count)
        {
            issues.Add(new(
                ClipStaticModelDuplicationIssueKind
                    .StaticModelCardinalityMismatch,
                "ColMap static-model or spatial-node count does not match its materialized table.",
                spatialAssessment.Edit.SourceOrdinal));
        }

        int newOrdinal =
            count >= 0 &&
            (uint)spatialAssessment.Edit.SourceOrdinal <
                (uint)count
                ? spatialAssessment.Edit.SourceOrdinal + 1
                : -1;
        if (count < 0 ||
            count >= (int)ushort.MaxValue + 1)
        {
            issues.Add(new(
                ClipStaticModelDuplicationIssueKind
                    .StaticModelCountUnrepresentable,
                "Inserting the Clip static model would exceed the ushort virtual-child domain.",
                spatialAssessment.Edit.SourceOrdinal));
        }

        ClipMapReferenceBuildData references = source.References;
        if (count >= 0 &&
            (references.StaticModels.Count != count ||
             references.StaticModelLinks.Count != count))
        {
            issues.Add(new(
                ClipStaticModelDuplicationIssueKind
                    .StaticModelReferenceCardinalityMismatch,
                "Detached Clip XModel identities and pointer links must exactly parallel the static-model rows.",
                spatialAssessment.Edit.SourceOrdinal));
        }
        else if (count >= 0 &&
                 (uint)spatialAssessment.Edit.SourceOrdinal <
                    (uint)count)
        {
            AssessSourceAlias(
                references,
                spatialAssessment.Edit.SourceOrdinal,
                issues);
        }

        ClipStaticModelTranslationSpatialAssessment currentSpatial =
            source.AssessConservativeStaticModelTranslation(
                spatialAssessment.Edit);
        if (!currentSpatial.IsEligible)
        {
            issues.AddRange(currentSpatial.Issues.Select(issue => new
                ClipStaticModelDuplicationIssue(
                    ClipStaticModelDuplicationIssueKind
                        .SpatialTreeInvalid,
                    issue.Detail,
                    spatialAssessment.Edit.SourceOrdinal)));
        }
        else if (count >= 0 &&
                 count < (int)ushort.MaxValue + 1)
        {
            AssessShiftedNodeRanges(
                definition,
                spatialAssessment.Edit.SourceOrdinal,
                newOrdinal,
                issues);
        }

        return new(
            source,
            spatialAssessment,
            newOrdinal,
            issues);
    }

    private static void AssessSourceAlias(
        ClipMapReferenceBuildData references,
        int sourceOrdinal,
        ICollection<ClipStaticModelDuplicationIssue> issues)
    {
        SymbolicXAssetReference? reference =
            references.StaticModels[sourceOrdinal];
        NestedXAssetBuildLink? link =
            references.StaticModelLinks[sourceOrdinal];
        bool valid =
            reference is
            {
                AssetType: XAssetType.XModel,
                IsExternalReference: true
            } &&
            link is
            {
                SourceForm: NestedXAssetPointerSourceForm.PackedAlias,
                IncomingDefinition: null
            } &&
            link.Reference == reference &&
            (link.ImportedPackedRaw is not { } packedRaw ||
             XPointerCodec.GetType(packedRaw) == PointerType.Offset) &&
            (link.ImportedOwnerCellRaw is not { } ownerCellRaw ||
             XPointerCodec.GetType(ownerCellRaw) == PointerType.Offset);
        if (!valid)
        {
            issues.Add(new(
                ClipStaticModelDuplicationIssueKind
                    .SourceModelAliasUnavailable,
                $"Clip static-model ordinal {sourceOrdinal} must retain a definition-free packed XModel alias before it can be duplicated.",
                sourceOrdinal));
        }
    }

    private static void AssessShiftedNodeRanges(
        ClipMapAsset definition,
        int sourceOrdinal,
        int insertionOrdinal,
        ICollection<ClipStaticModelDuplicationIssue> issues)
    {
        int count = definition.NumStaticModels;
        int sourceLeaf = FindOwningLeaf(
            definition.SModelNodes,
            count,
            sourceOrdinal);
        for (int nodeOrdinal = 0;
             nodeOrdinal < definition.SModelNodes.Count;
             nodeOrdinal++)
        {
            SModelAabbNode node =
                definition.SModelNodes[nodeOrdinal];
            bool childCountIncreases =
                nodeOrdinal == sourceLeaf;
            bool firstChildIncreases =
                node.FirstChild >= count ||
                nodeOrdinal != sourceLeaf &&
                node.FirstChild >= insertionOrdinal;
            if (childCountIncreases &&
                node.ChildCount == ushort.MaxValue ||
                firstChildIncreases &&
                node.FirstChild == ushort.MaxValue)
            {
                issues.Add(new(
                    ClipStaticModelDuplicationIssueKind
                        .SpatialChildRangeUnrepresentable,
                    $"Collision static-model node {nodeOrdinal} cannot represent its rebuilt child range.",
                    sourceOrdinal,
                    nodeOrdinal));
            }
        }
    }

    internal static int FindOwningLeaf(
        IReadOnlyList<SModelAabbNode> nodes,
        int modelCount,
        int sourceOrdinal)
    {
        for (int nodeOrdinal = 0;
             nodeOrdinal < nodes.Count;
             nodeOrdinal++)
        {
            SModelAabbNode node = nodes[nodeOrdinal];
            int first = node.FirstChild;
            int end = first + node.ChildCount;
            if (first < modelCount &&
                sourceOrdinal >= first &&
                sourceOrdinal < end)
            {
                return nodeOrdinal;
            }
        }
        throw new InvalidDataException(
            $"Collision static-model ordinal {sourceOrdinal} has no owning leaf.");
    }
}

public sealed partial class ClipMapBuildData
{
    /// <summary>
    /// Inserts one translated collision static model after its immutable
    /// template and reindexes the leaf and virtual-node child domains.
    /// </summary>
    public ClipMapBuildData WithDuplicatedStaticModel(
        ClipStaticModelDuplicationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsFor(this))
        {
            throw new InvalidOperationException(
                "A Clip static-model duplication assessment can authorize only the exact detached source it inspected.");
        }
        if (!assessment.IsEligible)
        {
            throw new InvalidOperationException(
                "An ineligible Clip static-model duplication cannot be applied.");
        }

        ClipStaticModelTranslationSpatialAssessment spatial =
            AssessConservativeStaticModelTranslation(
                assessment.Edit);
        ClipStaticModelDuplicationAssessment current =
            ClipStaticModelDuplicationAssessor.Assess(
                this,
                spatial);
        if (!current.IsEligible)
        {
            throw new InvalidOperationException(
                "The Clip static-model duplication assessment became stale: " +
                current.Issues[0].Detail);
        }

        ClipMapBuildData translated =
            WithConservativelyTranslatedStaticModels(
                [assessment.Edit]);
        ClipMapBuildData edited = Copy();
        ClipMapAsset definition = edited.Definition;
        int sourceCount = definition.NumStaticModels;
        int sourceOrdinal = current.SourceOrdinal;
        int insertionOrdinal = current.NewOrdinal;

        ClipStaticModel candidate =
            CloneStaticModel(
                translated.Definition.StaticModelList[
                    sourceOrdinal]);
        ClipStaticModel[] models = InsertAt(
            definition.StaticModelList,
            insertionOrdinal,
            candidate);

        int sourceLeaf =
            ClipStaticModelDuplicationAssessor.FindOwningLeaf(
                definition.SModelNodes,
                sourceCount,
                sourceOrdinal);
        SModelAabbNode[] nodes = definition.SModelNodes
            .Select((node, nodeOrdinal) =>
            {
                SModelAabbNode translatedNode =
                    translated.Definition.SModelNodes[nodeOrdinal];
                int first = node.FirstChild;
                int childCount = node.ChildCount;
                if (nodeOrdinal == sourceLeaf)
                {
                    childCount = checked(childCount + 1);
                }
                else if (first < sourceCount &&
                         first >= insertionOrdinal)
                {
                    first = checked(first + 1);
                }
                if (node.FirstChild >= sourceCount)
                {
                    first = checked(node.FirstChild + 1);
                }
                return new SModelAabbNode
                {
                    Bounds = new Bounds
                    {
                        MidPoint =
                            StaticModelSpatialEnvelope.Copy(
                                translatedNode.Bounds.MidPoint),
                        HalfSize =
                            StaticModelSpatialEnvelope.Copy(
                                translatedNode.Bounds.HalfSize)
                    },
                    FirstChild = checked((ushort)first),
                    ChildCount =
                        checked((ushort)childCount)
                };
            })
            .ToArray();

        Set(
            definition,
            nameof(ClipMapAsset.NumStaticModels),
            models.Length);
        Set(
            definition,
            nameof(ClipMapAsset.StaticModelList),
            models);
        Set(
            definition,
            nameof(ClipMapAsset.SModelNodes),
            nodes);

        ClipMapReferenceBuildData references =
            InsertStaticModelReference(
                edited.References,
                sourceOrdinal,
                insertionOrdinal);
        var result = new ClipMapBuildData(
            SerializedType,
            definition,
            references,
            new ClipMapLinkerProvenance(
                importedIsInUse:
                    edited.LinkerProvenance.ImportedIsInUse));
        ValidateDuplicatedGraph(result, insertionOrdinal);
        return result;
    }

    private static ClipStaticModel CloneStaticModel(
        ClipStaticModel source) =>
        new()
        {
            XModelPointer = source.XModelPointer,
            XModel = source.XModel,
            XModelIncomingDefinition =
                source.XModelIncomingDefinition,
            Origin =
                StaticModelSpatialEnvelope.Copy(
                    source.Origin),
            InvScaledAxis = source.InvScaledAxis
                .Select(StaticModelSpatialEnvelope.Copy)
                .ToArray(),
            AbsMin =
                StaticModelSpatialEnvelope.Copy(
                    source.AbsMin),
            AbsMax =
                StaticModelSpatialEnvelope.Copy(
                    source.AbsMax)
        };

    private static T[] InsertAt<T>(
        IReadOnlyList<T> source,
        int ordinal,
        T value)
    {
        var result = new T[source.Count + 1];
        for (int index = 0; index < ordinal; index++)
            result[index] = source[index];
        result[ordinal] = value;
        for (int index = ordinal;
             index < source.Count;
             index++)
        {
            result[index + 1] = source[index];
        }
        return result;
    }

    private static ClipMapReferenceBuildData
        InsertStaticModelReference(
            ClipMapReferenceBuildData source,
            int sourceOrdinal,
            int insertionOrdinal)
    {
        NestedXAssetBuildLink sourceLink =
            source.StaticModelLinks[sourceOrdinal]
            ?? throw new InvalidOperationException(
                "The authorized source XModel alias disappeared.");
        var duplicateLink = new NestedXAssetBuildLink(
            sourceLink.Reference,
            NestedXAssetPointerSourceForm.PackedAlias,
            IncomingDefinition: null,
            ImportedPackedRaw:
                sourceLink.ImportedPackedRaw,
            ImportedOwnerCellRaw: null);
        return new ClipMapReferenceBuildData(
            InsertAt(
                source.StaticModels,
                insertionOrdinal,
                source.StaticModels[sourceOrdinal]),
            source.DynamicEntities,
            source.MapEnts,
            InsertAt(
                source.StaticModelLinks,
                insertionOrdinal,
                duplicateLink),
            source.MapEntsLink);
    }

    private static void ValidateDuplicatedGraph(
        ClipMapBuildData result,
        int newOrdinal)
    {
        ClipStaticModel row =
            result.Definition.StaticModelList[newOrdinal];
        ClipStaticModelTranslationSpatialAssessment spatial =
            result.AssessConservativeStaticModelTranslation(
                new StaticModelTranslationEdit(
                    newOrdinal,
                    row.Origin.X,
                    row.Origin.Y,
                    row.Origin.Z));
        if (!spatial.IsEligible)
        {
            throw new InvalidOperationException(
                "The rebuilt Clip static-model graph failed spatial validation: " +
                spatial.Issues[0].Detail);
        }

        IReadOnlyList<IW4.FastFiles.Emitters.Emission.EmissionError>
            diagnostics =
                new ClipMapBodyEmitter(
                    result.SerializedType).Validate(result);
        if (diagnostics.Count != 0)
        {
            throw new InvalidOperationException(
                "The rebuilt Clip static-model graph failed emission validation: " +
                diagnostics[0]);
        }
    }
}
