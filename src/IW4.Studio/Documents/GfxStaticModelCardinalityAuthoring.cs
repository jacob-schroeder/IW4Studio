using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// Machine-readable reasons why a detached GfxWorld cannot remove one or
/// more existing static-model rows without rebuilding the proven index
/// consumers.
/// </summary>
public enum GfxStaticModelRemovalIssueKind
{
    EmptyRemovalSet,
    DuplicateSourceOrdinal,
    StaticModelCardinalityMismatch,
    StaticModelOrdinalOutOfRange,
    StaticModelReferenceCardinalityMismatch,
    DependencyProviderCarryForwardUnavailable,
    VisibilityCapacityInvalid,
    AabbPointerCardinalityMismatch,
    SpatialInvariantInvalid,
    ShadowInvariantInvalid
}

public sealed record GfxStaticModelRemovalIssue(
    GfxStaticModelRemovalIssueKind Kind,
    string Detail,
    int? StaticModelOrdinal = null);

/// <summary>
/// Exact authorization to move one inline XModel definition into the
/// immediately following retained alias when its owning static-model row is
/// removed. The receiver shifts into the removed row's serialized position,
/// preserving nested-provider materialization order.
/// </summary>
public sealed record GfxStaticModelProviderCarryForward(
    int RemovedProviderOrdinal,
    int ReceiverOrdinal,
    string AliasKey,
    string DefinitionDigest);

/// <summary>
/// Exact-source authorization for a deterministic Gfx static-model removal
/// rebuild. It is intentionally tied to the detached source instance that
/// was assessed.
/// </summary>
public sealed class GfxStaticModelRemovalAssessment
{
    private readonly GfxWorldBuildData _source;

    internal GfxStaticModelRemovalAssessment(
        GfxWorldBuildData source,
        IEnumerable<int> sourceOrdinals,
        IEnumerable<GfxStaticModelRemovalIssue> issues,
        IEnumerable<GfxStaticModelProviderCarryForward>?
            providerCarryForwards = null)
    {
        _source = source;
        SourceOrdinals = Array.AsReadOnly(
            sourceOrdinals.Order().ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        ProviderCarryForwards = Array.AsReadOnly(
            providerCarryForwards?.ToArray() ?? []);
    }

    public IReadOnlyList<int> SourceOrdinals { get; }
    public IReadOnlyList<GfxStaticModelRemovalIssue> Issues { get; }
    public IReadOnlyList<GfxStaticModelProviderCarryForward>
        ProviderCarryForwards
    {
        get;
    }
    public bool IsEligible => Issues.Count == 0;

    internal bool IsFor(GfxWorldBuildData source) =>
        ReferenceEquals(_source, source);
}

/// <summary>
/// Validates every serialized Gfx static-model index consumer before a
/// cardinality-changing rebuild is allowed.
/// </summary>
public static class GfxStaticModelRemovalAssessor
{
    public static GfxStaticModelRemovalAssessment Assess(
        GfxWorldBuildData source,
        IEnumerable<int> sourceOrdinals)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceOrdinals);

        int[] requested = sourceOrdinals.ToArray();
        int[] distinct = requested.Distinct().Order().ToArray();
        var issues = new List<GfxStaticModelRemovalIssue>();
        var providerCarryForwards =
            new List<GfxStaticModelProviderCarryForward>();
        if (requested.Length == 0)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind.EmptyRemovalSet,
                "At least one existing Gfx static-model row is required."));
            return new(source, distinct, issues);
        }
        if (requested.Length != distinct.Length)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind.DuplicateSourceOrdinal,
                "A Gfx static-model source ordinal may be removed at most once."));
        }

        GfxWorldAsset world = source.Definition;
        GfxWorldDpvsStatic dpvs = world.Dpvs;
        if (dpvs.SModelCount > int.MaxValue ||
            dpvs.SModelDrawInsts.Count != (int)dpvs.SModelCount ||
            dpvs.SModelInsts.Count != (int)dpvs.SModelCount)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .StaticModelCardinalityMismatch,
                "GfxWorld.dpvs static-model count does not match both " +
                "parallel row tables."));
            return new(source, distinct, issues);
        }

        int count = (int)dpvs.SModelCount;
        foreach (int ordinal in distinct.Where(value =>
                     value < 0 || value >= count))
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .StaticModelOrdinalOutOfRange,
                $"Gfx static-model ordinal {ordinal} is outside the " +
                $"{count}-row table.",
                ordinal));
        }
        if (issues.Count != 0)
            return new(source, distinct, issues);

        GfxWorldReferenceBuildData references = source.References;
        if (references.StaticModelDrawInsts.Count != count ||
            references.StaticModelDrawInstDefinitions.Count != count ||
            references.StaticModelDrawInstLinks.Count is not (0) &&
            references.StaticModelDrawInstLinks.Count != count)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .StaticModelReferenceCardinalityMismatch,
                "Detached Gfx XModel references, definitions, and optional " +
                "link provenance must parallel the DPVS static-model table."));
            return new(source, distinct, issues);
        }

        int remaining = count - distinct.Length;
        if (dpvs.VisibilityCounts.Count != 8 ||
            dpvs.VisibilityCounts[6] >
                int.MaxValue ||
            checked((long)dpvs.VisibilityCounts[6] * 32L) < remaining)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind.VisibilityCapacityInvalid,
                "The retained runtime static-model visibility word capacity " +
                "cannot cover the rebuilt row count."));
        }

        if (references.AabbTreeSModelIndexPointers.Count is not 0 &&
            (references.AabbTreeSModelIndexPointers.Count !=
                world.CellTrees.Count ||
             references.AabbTreeSModelIndexPointers.Where(
                     (rows, cellIndex) =>
                         rows.Count !=
                         world.CellTrees[cellIndex].AabbTrees.Count)
                 .Any()))
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .AabbPointerCardinalityMismatch,
                "AABB static-model index pointer provenance does not " +
                "parallel every Gfx cell-tree row."));
        }

        HashSet<int> removed = distinct.ToHashSet();
        foreach (int ordinal in distinct)
        {
            NestedXAssetBuildLink? modelLink =
                references.StaticModelDrawInstLinks.Count == 0
                    ? null
                    : references.StaticModelDrawInstLinks[ordinal];
            IXAssetBuildData? modelDefinition =
                references.StaticModelDrawInstDefinitions[ordinal];
            if (modelDefinition is not null ||
                modelLink?.SourceForm is
                    NestedXAssetPointerSourceForm.Inline or
                    NestedXAssetPointerSourceForm.Insert)
            {
                AssessProviderCarryForward(
                    references,
                    removed,
                    ordinal,
                    modelDefinition,
                    modelLink,
                    providerCarryForwards,
                    issues);
            }

            GfxStaticModelDrawInst draw =
                dpvs.SModelDrawInsts[ordinal];
            if (draw.Placement.Origin.Count != 3 ||
                draw.Placement.Origin.Any(value =>
                    !float.IsFinite(value)))
            {
                issues.Add(new(
                    GfxStaticModelRemovalIssueKind
                        .SpatialInvariantInvalid,
                    $"Gfx static-model ordinal {ordinal} has no finite, exact " +
                    "three-component placement.",
                    ordinal));
                continue;
            }

            var noOp = new StaticModelTranslationEdit(
                ordinal,
                draw.Placement.Origin[0],
                draw.Placement.Origin[1],
                draw.Placement.Origin[2]);
            GfxStaticModelTranslationSpatialAssessment spatial =
                GfxStaticModelTranslationSpatialAssessor.Assess(
                    source,
                    noOp);
            issues.AddRange(spatial.Issues.Select(issue => new
                GfxStaticModelRemovalIssue(
                    GfxStaticModelRemovalIssueKind
                        .SpatialInvariantInvalid,
                    issue.Detail,
                    ordinal)));
        }

        GfxStaticModelShadowMembershipAssessment shadow =
            GfxStaticModelShadowMembershipAssessor.Assess(source);
        issues.AddRange(shadow.Issues.Select(issue => new
            GfxStaticModelRemovalIssue(
                GfxStaticModelRemovalIssueKind.ShadowInvariantInvalid,
                issue.Detail,
                issue.StaticModelIndex)));

        return new(
            source,
            distinct,
            issues,
            providerCarryForwards);
    }

    private static void AssessProviderCarryForward(
        GfxWorldReferenceBuildData references,
        IReadOnlySet<int> removed,
        int providerOrdinal,
        IXAssetBuildData? providerDefinition,
        NestedXAssetBuildLink? providerLink,
        ICollection<GfxStaticModelProviderCarryForward> carryForwards,
        ICollection<GfxStaticModelRemovalIssue> issues)
    {
        SymbolicXAssetReference? providerReference =
            references.StaticModelDrawInsts[providerOrdinal];
        bool validProvider =
            providerDefinition is not null &&
            providerDefinition.AssetType ==
                IW4.FastFiles.Zone.XAssetType.XModel &&
            providerLink is
            {
                SourceForm: NestedXAssetPointerSourceForm.Inline,
                IncomingDefinition: not null,
                ImportedOwnerCellRaw: { } ownerCellRaw
            } &&
            ReferenceEquals(
                providerDefinition,
                providerLink.IncomingDefinition) &&
            providerReference == providerLink.Reference &&
            providerLink.Reference.AssetType ==
                IW4.FastFiles.Zone.XAssetType.XModel &&
            IW4.FastFiles.Pointers.XPointerCodec.GetType(ownerCellRaw) ==
                IW4.FastFiles.Pointers.PointerType.Offset;
        if (!validProvider)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .DependencyProviderCarryForwardUnavailable,
                $"Gfx static-model ordinal {providerOrdinal} materializes " +
                "an XModel provider whose exact inline definition and " +
                "source owner cell cannot be transferred safely.",
                providerOrdinal));
            return;
        }
        NestedXAssetBuildLink exactProviderLink = providerLink!;

        int receiverOrdinal = providerOrdinal + 1;
        if (receiverOrdinal >=
                references.StaticModelDrawInsts.Count ||
            removed.Contains(receiverOrdinal))
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .DependencyProviderCarryForwardUnavailable,
                $"Gfx static-model ordinal {providerOrdinal} owns an inline " +
                "XModel definition, but its immediate successor is not a " +
                "retained alias receiver.",
                providerOrdinal));
            return;
        }

        NestedXAssetBuildLink? receiverLink =
            references.StaticModelDrawInstLinks[receiverOrdinal];
        SymbolicXAssetReference? receiverReference =
            references.StaticModelDrawInsts[receiverOrdinal];
        bool validReceiver =
            references.StaticModelDrawInstDefinitions[
                receiverOrdinal] is null &&
            receiverLink is
            {
                SourceForm: NestedXAssetPointerSourceForm.PackedAlias,
                IncomingDefinition: null,
                ImportedPackedRaw: { } receiverTargetRaw
            } &&
            receiverReference == providerReference &&
            receiverLink.Reference == providerReference &&
            receiverLink.AliasKey == exactProviderLink.AliasKey &&
            receiverTargetRaw ==
                exactProviderLink.ImportedOwnerCellRaw;
        if (!validReceiver)
        {
            issues.Add(new(
                GfxStaticModelRemovalIssueKind
                    .DependencyProviderCarryForwardUnavailable,
                $"Gfx static-model ordinal {providerOrdinal} owns an inline " +
                "XModel definition, but ordinal " +
                $"{receiverOrdinal} is not a definition-free alias to its " +
                "exact source owner cell.",
                providerOrdinal));
            return;
        }

        carryForwards.Add(new(
            providerOrdinal,
            receiverOrdinal,
            exactProviderLink.AliasKey,
            RelocationInvariantAssetSemanticDigest.Compute(
                providerDefinition!)));
    }
}

public sealed partial class GfxWorldBuildData
{
    /// <summary>
    /// Removes existing static-model rows and rewrites every known ordinal
    /// consumer. Bounds and topology remain conservative; all direct AABB
    /// index pointers are canonicalized because imported packed aliases no
    /// longer address a byte-identical payload layout.
    /// </summary>
    public GfxWorldBuildData WithRemovedStaticModels(
        GfxStaticModelRemovalAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsFor(this))
        {
            throw new InvalidOperationException(
                "A Gfx static-model removal assessment can authorize only " +
                "the exact detached source it inspected.");
        }
        if (!assessment.IsEligible)
        {
            throw new InvalidOperationException(
                "An ineligible Gfx static-model removal cannot be applied.");
        }

        GfxStaticModelRemovalAssessment current =
            GfxStaticModelRemovalAssessor.Assess(
                this,
                assessment.SourceOrdinals);
        if (!current.IsEligible)
        {
            throw new InvalidOperationException(
                "The Gfx static-model removal assessment became stale: " +
                current.Issues[0].Detail);
        }

        GfxWorldBuildData edited = Copy();
        HashSet<int> removed =
            assessment.SourceOrdinals.ToHashSet();
        int sourceCount =
            checked((int)edited.Definition.Dpvs.SModelCount);
        int remainingCount = sourceCount - removed.Count;
        int RewriteOrdinal(int ordinal)
        {
            int shift = assessment.SourceOrdinals.Count(value =>
                value < ordinal);
            return ordinal - shift;
        }

        GfxWorldDpvsStatic dpvs = edited.Definition.Dpvs;
        Set(
            dpvs,
            nameof(GfxWorldDpvsStatic.SModelCount),
            checked((uint)remainingCount));
        Set(
            dpvs,
            nameof(GfxWorldDpvsStatic.SModelDrawInsts),
            dpvs.SModelDrawInsts
                .Where((_, ordinal) => !removed.Contains(ordinal))
                .ToArray());
        Set(
            dpvs,
            nameof(GfxWorldDpvsStatic.SModelInsts),
            dpvs.SModelInsts
                .Where((_, ordinal) => !removed.Contains(ordinal))
                .ToArray());

        GfxCellTree[] cellTrees = edited.Definition.CellTrees
            .Select(cell => new GfxCellTree
            {
                AabbTrees = cell.AabbTrees.Select(row =>
                {
                    ushort[] indexes = row.SModelIndexes
                        .Where(index => !removed.Contains(index))
                        .Select(index =>
                            checked((ushort)RewriteOrdinal(index)))
                        .ToArray();
                    return new GfxAabbTree
                    {
                        Bounds =
                            StaticModelSpatialEnvelope.Copy(row.Bounds),
                        ChildCount = row.ChildCount,
                        SurfaceCount = row.SurfaceCount,
                        StartSurfIndex = row.StartSurfIndex,
                        SModelIndexCount =
                            checked((ushort)indexes.Length),
                        SModelIndexes = indexes,
                        ChildrenOffset = row.ChildrenOffset
                    };
                }).ToArray()
            })
            .ToArray();
        Set(
            edited.Definition,
            nameof(GfxWorldAsset.CellTrees),
            cellTrees);

        GfxShadowGeometry[] shadowGeometry =
            edited.Definition.ShadowGeom.Select(row =>
            {
                ushort[] indexes = row.SModelIndex
                    .Where(index => !removed.Contains(index))
                    .Select(index =>
                        checked((ushort)RewriteOrdinal(index)))
                    .ToArray();
                return new GfxShadowGeometry
                {
                    SurfaceCount = row.SurfaceCount,
                    SortedSurfIndex =
                        row.SortedSurfIndex.ToArray(),
                    SModelCount = checked((ushort)indexes.Length),
                    SModelIndex = indexes
                };
            }).ToArray();
        Set(
            edited.Definition,
            nameof(GfxWorldAsset.ShadowGeom),
            shadowGeometry);

        GfxWorldReferenceBuildData references =
            RewriteStaticModelRemovalReferences(
                edited.References,
                cellTrees,
                removed,
                current.ProviderCarryForwards);
        return new GfxWorldBuildData(
            edited.Definition,
            references,
            takeOwnership: true);
    }

    private static GfxWorldReferenceBuildData
        RewriteStaticModelRemovalReferences(
            GfxWorldReferenceBuildData source,
            IReadOnlyList<GfxCellTree> cellTrees,
            IReadOnlySet<int> removed,
            IReadOnlyList<GfxStaticModelProviderCarryForward>
                providerCarryForwards)
    {
        IXAssetBuildData?[] definitions =
            source.StaticModelDrawInstDefinitions.ToArray();
        NestedXAssetBuildLink?[] links =
            source.StaticModelDrawInstLinks.ToArray();
        foreach (GfxStaticModelProviderCarryForward carryForward in
                 providerCarryForwards)
        {
            IXAssetBuildData definition =
                definitions[carryForward.RemovedProviderOrdinal]
                ?? throw new InvalidOperationException(
                    "An authorized static-model provider carry-forward lost its " +
                    "incoming XModel definition.");
            if (!string.Equals(
                    RelocationInvariantAssetSemanticDigest.Compute(
                        definition),
                    carryForward.DefinitionDigest,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "An authorized static-model provider carry-forward " +
                    "definition no longer matches its assessed semantic " +
                    "digest.");
            }
            NestedXAssetBuildLink provider =
                links[carryForward.RemovedProviderOrdinal]
                ?? throw new InvalidOperationException(
                    "An authorized static-model provider carry-forward lost " +
                    "its inline source link.");
            NestedXAssetBuildLink receiver =
                links[carryForward.ReceiverOrdinal]
                ?? throw new InvalidOperationException(
                    "An authorized static-model provider carry-forward lost its " +
                    "retained alias receiver.");
            definitions[carryForward.ReceiverOrdinal] =
                definition;
            links[carryForward.ReceiverOrdinal] =
                new NestedXAssetBuildLink(
                    receiver.Reference,
                    NestedXAssetPointerSourceForm.Inline,
                    definition,
                    ImportedOwnerCellRaw:
                        provider.ImportedOwnerCellRaw);
        }

        return new GfxWorldReferenceBuildData
        {
            SkyImages = source.SkyImages,
            ReflectionProbeImages = source.ReflectionProbeImages,
            Lightmaps = source.Lightmaps,
            LightmapOverridePrimary =
                source.LightmapOverridePrimary,
            LightmapOverrideSecondary =
                source.LightmapOverrideSecondary,
            MaterialMemory = source.MaterialMemory,
            SunSpriteMaterial = source.SunSpriteMaterial,
            SunFlareMaterial = source.SunFlareMaterial,
            OutdoorImage = source.OutdoorImage,
            SurfaceMaterials = source.SurfaceMaterials,
            StaticModelDrawInsts = source.StaticModelDrawInsts
                .Where((_, ordinal) => !removed.Contains(ordinal))
                .ToArray(),
            SkyImageDefinitions = source.SkyImageDefinitions,
            ReflectionProbeImageDefinitions =
                source.ReflectionProbeImageDefinitions,
            LightmapDefinitions = source.LightmapDefinitions,
            LightmapOverridePrimaryDefinition =
                source.LightmapOverridePrimaryDefinition,
            LightmapOverrideSecondaryDefinition =
                source.LightmapOverrideSecondaryDefinition,
            MaterialMemoryDefinitions =
                source.MaterialMemoryDefinitions,
            SunSpriteMaterialDefinition =
                source.SunSpriteMaterialDefinition,
            SunFlareMaterialDefinition =
                source.SunFlareMaterialDefinition,
            OutdoorImageDefinition =
                source.OutdoorImageDefinition,
            SurfaceMaterialDefinitions =
                source.SurfaceMaterialDefinitions,
            StaticModelDrawInstDefinitions =
                definitions
                    .Where((_, ordinal) =>
                        !removed.Contains(ordinal))
                    .ToArray(),
            SkyImageLinks = source.SkyImageLinks,
            ReflectionProbeImageLinks =
                source.ReflectionProbeImageLinks,
            LightmapLinks = source.LightmapLinks,
            LightmapOverridePrimaryLink =
                source.LightmapOverridePrimaryLink,
            LightmapOverrideSecondaryLink =
                source.LightmapOverrideSecondaryLink,
            MaterialMemoryLinks = source.MaterialMemoryLinks,
            SunSpriteMaterialLink =
                source.SunSpriteMaterialLink,
            SunFlareMaterialLink =
                source.SunFlareMaterialLink,
            OutdoorImageLink =
                source.OutdoorImageLink,
            SurfaceMaterialLinks =
                source.SurfaceMaterialLinks,
            StaticModelDrawInstLinks =
                source.StaticModelDrawInstLinks.Count == 0
                    ? []
                    : links
                        .Where((_, ordinal) =>
                            !removed.Contains(ordinal))
                        .ToArray(),
            AabbTreeSModelIndexPointers = cellTrees
                .Select(cell =>
                    (IReadOnlyList<
                        GfxAabbTreeIndexPointerBuildData>)
                    cell.AabbTrees.Select(row => new
                        GfxAabbTreeIndexPointerBuildData(
                            row.SModelIndexes.Count == 0
                                ? GfxDirectPointerSourceForm.Null
                                : GfxDirectPointerSourceForm.Inline))
                        .ToArray())
                .ToArray()
        };
    }
}
