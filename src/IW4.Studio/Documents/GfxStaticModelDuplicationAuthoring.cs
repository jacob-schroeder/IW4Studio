using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Math;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Machine-readable reasons why one proven Gfx static-model translation
/// cannot be materialized as a new compiled row.
/// </summary>
public enum GfxStaticModelDuplicationIssueKind
{
    SpatialAssessmentSourceMismatch,
    SpatialAssessmentIneligible,
    StaticModelCardinalityMismatch,
    StaticModelCountUnrepresentable,
    VisibilityCapacityInvalid,
    StaticModelReferenceCardinalityMismatch,
    SourceModelAliasUnavailable,
    AabbPointerCardinalityMismatch,
    AabbMembershipCountUnrepresentable,
    ShadowInvariantInvalid,
    ShadowMembershipCountUnrepresentable
}

public sealed record GfxStaticModelDuplicationIssue(
    GfxStaticModelDuplicationIssueKind Kind,
    string Detail,
    int? StaticModelOrdinal = null,
    int? CellOrdinal = null,
    int? TreeOrdinal = null,
    int? PrimaryLightOrdinal = null);

/// <summary>
/// Exact-source authorization for appending one translated Gfx static-model
/// row and rebuilding every compiled ordinal consumer owned by GfxWorld.
/// </summary>
public sealed class GfxStaticModelDuplicationAssessment
{
    private readonly GfxWorldBuildData _source;

    internal GfxStaticModelDuplicationAssessment(
        GfxWorldBuildData source,
        GfxStaticModelTranslationSpatialAssessment spatialAssessment,
        int newOrdinal,
        GfxStaticModelShadowMembershipAssessment shadowAssessment,
        IEnumerable<GfxStaticModelDuplicationIssue> issues)
    {
        _source = source;
        SpatialAssessment = spatialAssessment;
        NewOrdinal = newOrdinal;
        ShadowAssessment = shadowAssessment;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public StaticModelTranslationEdit Edit => SpatialAssessment.Edit;
    public int SourceOrdinal => Edit.SourceOrdinal;
    public int NewOrdinal { get; }
    public GfxStaticModelTranslationSpatialAssessment SpatialAssessment
    {
        get;
    }
    public GfxStaticModelShadowMembershipAssessment ShadowAssessment
    {
        get;
    }
    public IReadOnlyList<GfxStaticModelDuplicationIssue> Issues { get; }
    public bool IsEligible => Issues.Count == 0;

    internal bool IsFor(GfxWorldBuildData source) =>
        ReferenceEquals(_source, source);
}

/// <summary>
/// Validates the typed, constrained Gfx duplication boundary. The source
/// placement must already have passed the exact DPVS/cell-tree translation
/// proof. Its XModel dependency must be either a definition-free packed
/// alias or an exact retained inline provider.
/// </summary>
public static class GfxStaticModelDuplicationAssessor
{
    public static GfxStaticModelDuplicationAssessment Assess(
        GfxWorldBuildData source,
        GfxStaticModelTranslationSpatialAssessment spatialAssessment)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(spatialAssessment);

        var issues = new List<GfxStaticModelDuplicationIssue>();
        if (!spatialAssessment.IsFor(source))
        {
            issues.Add(new(
                GfxStaticModelDuplicationIssueKind
                    .SpatialAssessmentSourceMismatch,
                "The Gfx translation proof belongs to a different detached world.",
                spatialAssessment.Edit.SourceOrdinal));
        }
        if (!spatialAssessment.IsEligible)
        {
            issues.AddRange(spatialAssessment.Issues.Select(issue => new
                GfxStaticModelDuplicationIssue(
                    GfxStaticModelDuplicationIssueKind
                        .SpatialAssessmentIneligible,
                    issue.Detail,
                    issue.StaticModelIndex,
                    issue.CellIndex,
                    issue.TreeIndex)));
        }

        GfxWorldAsset world = source.Definition;
        GfxWorldDpvsStatic dpvs = world.Dpvs;
        int count = -1;
        if (dpvs.SModelCount > int.MaxValue ||
            dpvs.SModelDrawInsts.Count != (int)dpvs.SModelCount ||
            dpvs.SModelInsts.Count != (int)dpvs.SModelCount)
        {
            issues.Add(new(
                GfxStaticModelDuplicationIssueKind
                    .StaticModelCardinalityMismatch,
                "GfxWorld.dpvs static-model count does not match both parallel row tables."));
        }
        else
        {
            count = (int)dpvs.SModelCount;
        }

        int newOrdinal = count;
        if (count < 0 ||
            count >= (int)ushort.MaxValue + 1)
        {
            issues.Add(new(
                GfxStaticModelDuplicationIssueKind
                    .StaticModelCountUnrepresentable,
                "Appending the Gfx static model would exceed the ushort ordinal domain.",
                spatialAssessment.Edit.SourceOrdinal));
        }

        if (count >= 0 &&
            (dpvs.VisibilityCounts.Count != 8 ||
             checked((long)dpvs.VisibilityCounts[6] * 32L) <
                checked((long)count + 1L)))
        {
            issues.Add(new(
                GfxStaticModelDuplicationIssueKind.VisibilityCapacityInvalid,
                "The imported runtime static-model visibility word capacity cannot cover the appended row.",
                spatialAssessment.Edit.SourceOrdinal));
        }

        GfxWorldReferenceBuildData references = source.References;
        if (count >= 0 &&
            (references.StaticModelDrawInsts.Count != count ||
             references.StaticModelDrawInstDefinitions.Count != count ||
             references.StaticModelDrawInstLinks.Count != count))
        {
            issues.Add(new(
                GfxStaticModelDuplicationIssueKind
                    .StaticModelReferenceCardinalityMismatch,
                "Detached Gfx XModel identities, definitions, and pointer links must exactly parallel the static-model rows.",
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
                GfxStaticModelDuplicationIssueKind
                    .AabbPointerCardinalityMismatch,
                "AABB static-model index pointer provenance does not parallel every Gfx cell-tree row."));
        }

        if (count >= 0 &&
            (uint)spatialAssessment.Edit.SourceOrdinal < (uint)count)
        {
            int sourceOrdinal =
                spatialAssessment.Edit.SourceOrdinal;
            for (int cellIndex = 0;
                 cellIndex < world.CellTrees.Count;
                 cellIndex++)
            {
                IReadOnlyList<GfxAabbTree> rows =
                    world.CellTrees[cellIndex].AabbTrees;
                for (int treeIndex = 0;
                     treeIndex < rows.Count;
                     treeIndex++)
                {
                    GfxAabbTree row = rows[treeIndex];
                    if (row.SModelIndexes.Contains(
                            checked((ushort)sourceOrdinal)) &&
                        row.SModelIndexes.Count >= ushort.MaxValue)
                    {
                        issues.Add(new(
                            GfxStaticModelDuplicationIssueKind
                                .AabbMembershipCountUnrepresentable,
                            $"Cell {cellIndex}, AABB row {treeIndex} cannot represent one additional static-model membership.",
                            sourceOrdinal,
                            cellIndex,
                            treeIndex));
                    }
                }
            }
        }

        GfxStaticModelShadowMembershipAssessment shadow =
            GfxStaticModelShadowMembershipAssessor.Assess(source);
        issues.AddRange(shadow.Issues.Select(issue => new
            GfxStaticModelDuplicationIssue(
                GfxStaticModelDuplicationIssueKind.ShadowInvariantInvalid,
                issue.Detail,
                issue.StaticModelIndex,
                PrimaryLightOrdinal: issue.PrimaryLightIndex)));
        if (shadow.Evidence is { } evidence &&
            count >= 0 &&
            (uint)spatialAssessment.Edit.SourceOrdinal < (uint)count)
        {
            int sourceOrdinal = spatialAssessment.Edit.SourceOrdinal;
            int owner = evidence.StaticModels[sourceOrdinal]
                .ShadowOwnerPrimaryLightIndex;
            GfxShadowGeometry row = world.ShadowGeom[owner];
            if (row.SModelIndex.Count >= ushort.MaxValue)
            {
                issues.Add(new(
                    GfxStaticModelDuplicationIssueKind
                        .ShadowMembershipCountUnrepresentable,
                    $"Primary-light row {owner} cannot represent one additional static-model membership.",
                    sourceOrdinal,
                    PrimaryLightOrdinal: owner));
            }
        }

        return new(
            source,
            spatialAssessment,
            newOrdinal,
            shadow,
            issues);
    }

    private static void AssessSourceAlias(
        GfxWorldReferenceBuildData references,
        int sourceOrdinal,
        ICollection<GfxStaticModelDuplicationIssue> issues)
    {
        SymbolicXAssetReference? reference =
            references.StaticModelDrawInsts[sourceOrdinal];
        NestedXAssetBuildLink? link =
            references.StaticModelDrawInstLinks[sourceOrdinal];
        IXAssetBuildData? definition =
            references.StaticModelDrawInstDefinitions[sourceOrdinal];
        bool validReference =
            reference is
            {
                AssetType: XAssetType.XModel,
                IsExternalReference: true
            };
        bool validPackedAlias =
            validReference &&
            definition is null &&
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
        bool validInlineProvider =
            validReference &&
            definition is not null &&
            HasSameXModelIdentity(definition, reference!) &&
            link is
            {
                SourceForm: NestedXAssetPointerSourceForm.Inline,
                IncomingDefinition: not null,
                ImportedPackedRaw: null,
                ImportedOwnerCellRaw: { } providerOwnerCellRaw
            } &&
            ReferenceEquals(definition, link.IncomingDefinition) &&
            link.Reference == reference &&
            XPointerCodec.GetType(providerOwnerCellRaw) ==
                PointerType.Offset;
        if (!(validPackedAlias || validInlineProvider))
        {
            issues.Add(new(
                GfxStaticModelDuplicationIssueKind
                    .SourceModelAliasUnavailable,
                $"Gfx static-model ordinal {sourceOrdinal} must retain either a definition-free packed XModel alias or one exact inline XModel provider before it can be duplicated.",
                sourceOrdinal));
        }
    }

    private static bool HasSameXModelIdentity(
        IXAssetBuildData definition,
        SymbolicXAssetReference reference)
    {
        if (definition is not IXModelBuildData
            {
                Name: { Length: > 0 } name
            })
        {
            return false;
        }

        try
        {
            return new ZoneAssetKey(XAssetType.XModel, name) ==
                ZoneAssetKey.FromWireName(
                    XAssetType.XModel,
                    reference.OriginalSerializedName);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed partial class GfxWorldBuildData
{
    /// <summary>
    /// Appends one translated static-model row and rewrites every known Gfx
    /// ordinal consumer. The source row remains byte-semantically unchanged.
    /// </summary>
    public GfxWorldBuildData WithDuplicatedStaticModel(
        GfxStaticModelDuplicationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsFor(this))
        {
            throw new InvalidOperationException(
                "A Gfx static-model duplication assessment can authorize only the exact detached source it inspected.");
        }
        if (!assessment.IsEligible)
        {
            throw new InvalidOperationException(
                "An ineligible Gfx static-model duplication cannot be applied.");
        }

        GfxStaticModelTranslationSpatialAssessment spatial =
            GfxStaticModelTranslationSpatialAssessor.Assess(
                this,
                assessment.Edit);
        GfxStaticModelDuplicationAssessment current =
            GfxStaticModelDuplicationAssessor.Assess(
                this,
                spatial);
        if (!current.IsEligible)
        {
            throw new InvalidOperationException(
                "The Gfx static-model duplication assessment became stale: " +
                current.Issues[0].Detail);
        }

        GfxWorldBuildData translated =
            WithSpatiallyEligibleStaticModelTranslation(spatial);
        GfxWorldBuildData edited = Copy();
        GfxWorldDpvsStatic dpvs = edited.Definition.Dpvs;
        GfxWorldDpvsStatic translatedDpvs =
            translated.Definition.Dpvs;
        int sourceOrdinal = current.SourceOrdinal;
        int newOrdinal = current.NewOrdinal;
        GfxStaticModelDrawInst[] draws =
        [
            .. dpvs.SModelDrawInsts,
            CloneDraw(
                translatedDpvs.SModelDrawInsts[sourceOrdinal])
        ];
        GfxStaticModelInst[] instances =
        [
            .. dpvs.SModelInsts,
            CloneInstance(
                translatedDpvs.SModelInsts[sourceOrdinal])
        ];
        Set(
            dpvs,
            nameof(GfxWorldDpvsStatic.SModelCount),
            checked((uint)draws.Length));
        edited.ReplaceStaticModelTables(draws, instances);

        GfxCellTree[] cellTrees = edited.Definition.CellTrees
            .Select(cell => RebuildCellTree(
                cell,
                checked((ushort)sourceOrdinal),
                checked((ushort)newOrdinal)))
            .ToArray();
        Set(
            edited.Definition,
            nameof(GfxWorldAsset.CellTrees),
            cellTrees);

        int shadowOwner = current.ShadowAssessment.Evidence!
            .StaticModels[sourceOrdinal]
            .ShadowOwnerPrimaryLightIndex;
        GfxShadowGeometry[] shadowGeometry =
            edited.Definition.ShadowGeom
                .Select((row, primaryLightOrdinal) =>
                    CloneShadowGeometry(
                        row,
                        primaryLightOrdinal == shadowOwner
                            ? checked((ushort?)newOrdinal)
                            : null))
                .ToArray();
        Set(
            edited.Definition,
            nameof(GfxWorldAsset.ShadowGeom),
            shadowGeometry);

        GfxWorldReferenceBuildData references =
            AppendStaticModelReference(
                edited.References,
                cellTrees,
                sourceOrdinal);
        var result = new GfxWorldBuildData(
            edited.Definition,
            references,
            takeOwnership: true);
        ValidateDuplicatedGraph(result, newOrdinal);
        return result;
    }

    private static GfxCellTree RebuildCellTree(
        GfxCellTree cell,
        ushort sourceOrdinal,
        ushort newOrdinal)
    {
        GfxAabbTree[] source = cell.AabbTrees.ToArray();
        var rebuilt = new GfxAabbTree[source.Length];
        for (int treeIndex = source.Length - 1;
             treeIndex >= 0;
             treeIndex--)
        {
            GfxAabbTree row = source[treeIndex];
            ushort[] indexes;
            if (row.ChildCount == 0)
            {
                indexes = row.SModelIndexes.Contains(sourceOrdinal)
                    ? [.. row.SModelIndexes, newOrdinal]
                    : row.SModelIndexes.ToArray();
            }
            else
            {
                int firstChild = checked(
                    treeIndex +
                    row.ChildrenOffset /
                    GfxAabbTree.SerializedSize);
                indexes = Enumerable
                    .Range(firstChild, row.ChildCount)
                    .SelectMany(child =>
                        rebuilt[child].SModelIndexes)
                    .ToArray();
            }
            rebuilt[treeIndex] = CloneAabbTree(row, indexes);
        }
        return new GfxCellTree
        {
            AabbTrees = rebuilt
        };
    }

    private static GfxAabbTree CloneAabbTree(
        GfxAabbTree source,
        IReadOnlyList<ushort> indexes) =>
        new()
        {
            Bounds = new Bounds
            {
                MidPoint =
                    StaticModelSpatialEnvelope.Copy(
                        source.Bounds.MidPoint),
                HalfSize =
                    StaticModelSpatialEnvelope.Copy(
                        source.Bounds.HalfSize)
            },
            ChildCount = source.ChildCount,
            SurfaceCount = source.SurfaceCount,
            StartSurfIndex = source.StartSurfIndex,
            SModelIndexCount =
                checked((ushort)indexes.Count),
            SModelIndexes = indexes.ToArray(),
            ChildrenOffset = source.ChildrenOffset
        };

    private static GfxShadowGeometry CloneShadowGeometry(
        GfxShadowGeometry source,
        ushort? appendedStaticModel)
    {
        ushort[] indexes = appendedStaticModel is { } value
            ? [.. source.SModelIndex, value]
            : source.SModelIndex.ToArray();
        return new GfxShadowGeometry
        {
            SurfaceCount = source.SurfaceCount,
            SortedSurfIndex =
                source.SortedSurfIndex.ToArray(),
            SModelCount = checked((ushort)indexes.Length),
            SModelIndex = indexes
        };
    }

    private static GfxStaticModelDrawInst CloneDraw(
        GfxStaticModelDrawInst source) =>
        new()
        {
            Placement = new GfxPackedPlacement
            {
                Origin = source.Placement.Origin.ToArray(),
                PackedAxis =
                    source.Placement.PackedAxis.ToArray(),
                Scale = source.Placement.Scale
            },
            ModelPointer = source.ModelPointer,
            Model = source.Model,
            ModelIncomingDefinition =
                source.ModelIncomingDefinition,
            CullDist = source.CullDist,
            LightingHandle = source.LightingHandle,
            ReflectionProbeIndex =
                source.ReflectionProbeIndex,
            PrimaryLightIndex = source.PrimaryLightIndex,
            Flags = source.Flags,
            FirstMaterialSkinIndex =
                source.FirstMaterialSkinIndex,
            GroundLighting = source.GroundLighting
        };

    private static GfxStaticModelInst CloneInstance(
        GfxStaticModelInst source) =>
        new()
        {
            Bounds = new Bounds
            {
                MidPoint =
                    StaticModelSpatialEnvelope.Copy(
                        source.Bounds.MidPoint),
                HalfSize =
                    StaticModelSpatialEnvelope.Copy(
                        source.Bounds.HalfSize)
            },
            LightingOrigin =
                StaticModelSpatialEnvelope.Copy(
                    source.LightingOrigin)
        };

    private static GfxWorldReferenceBuildData
        AppendStaticModelReference(
            GfxWorldReferenceBuildData source,
            IReadOnlyList<GfxCellTree> cellTrees,
            int sourceOrdinal)
    {
        NestedXAssetBuildLink sourceLink =
            source.StaticModelDrawInstLinks[sourceOrdinal]
            ?? throw new InvalidOperationException(
                "The authorized source XModel alias disappeared.");
        var duplicateLink = new NestedXAssetBuildLink(
            sourceLink.Reference,
            NestedXAssetPointerSourceForm.PackedAlias,
            IncomingDefinition: null,
            ImportedPackedRaw:
                sourceLink.SourceForm ==
                    NestedXAssetPointerSourceForm.PackedAlias
                        ? sourceLink.ImportedPackedRaw
                        : null,
            ImportedOwnerCellRaw: null);
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
            StaticModelDrawInsts =
            [
                .. source.StaticModelDrawInsts,
                source.StaticModelDrawInsts[sourceOrdinal]
            ],
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
            [
                .. source.StaticModelDrawInstDefinitions,
                null
            ],
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
            OutdoorImageLink = source.OutdoorImageLink,
            SurfaceMaterialLinks =
                source.SurfaceMaterialLinks,
            StaticModelDrawInstLinks =
            [
                .. source.StaticModelDrawInstLinks,
                duplicateLink
            ],
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

    private static void ValidateDuplicatedGraph(
        GfxWorldBuildData result,
        int newOrdinal)
    {
        GfxStaticModelDrawInst draw =
            result.Definition.Dpvs.SModelDrawInsts[newOrdinal];
        var noOp = new StaticModelTranslationEdit(
            newOrdinal,
            draw.Placement.Origin[0],
            draw.Placement.Origin[1],
            draw.Placement.Origin[2]);
        GfxStaticModelTranslationSpatialAssessment spatial =
            GfxStaticModelTranslationSpatialAssessor.Assess(
                result,
                noOp);
        if (!spatial.IsEligible)
        {
            throw new InvalidOperationException(
                "The rebuilt Gfx static-model graph failed spatial validation: " +
                spatial.Issues[0].Detail);
        }

        GfxStaticModelShadowMembershipAssessment shadow =
            GfxStaticModelShadowMembershipAssessor.Assess(result);
        if (!shadow.IsValid)
        {
            throw new InvalidOperationException(
                "The rebuilt Gfx static-model graph failed shadow validation: " +
                shadow.Issues[0].Detail);
        }

        IReadOnlyList<IW4.FastFiles.Emitters.Emission.EmissionError>
            diagnostics =
                new GfxWorldBodyEmitter().Validate(result);
        if (diagnostics.Count != 0)
        {
            throw new InvalidOperationException(
                "The rebuilt Gfx static-model graph failed emission validation: " +
                diagnostics[0]);
        }
    }
}
