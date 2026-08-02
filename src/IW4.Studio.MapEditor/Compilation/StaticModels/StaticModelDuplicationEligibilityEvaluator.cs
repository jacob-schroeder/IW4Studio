using System.Collections.ObjectModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

public enum StaticModelDuplicationEligibilityIssueKind
{
    CorrespondenceAuthorityMismatch,
    DocumentAuthorityMismatch,
    TemplateProjectionMismatch,
    TemplateStateIneligible,
    ProjectedCorrespondenceCollision,
    DestinationPreservation,
    GfxCardinalityRebuild,
    CollisionCardinalityRebuild
}

public sealed record StaticModelDuplicationEligibilityIssue(
    StaticModelDuplicationEligibilityIssueKind Kind,
    string Detail);

/// <summary>
/// Complete semantic and compiled admission result for copying one imported
/// render/collision pair to one authored destination. The component builder
/// assessments are retained so command admission and save preparation use
/// the same typed proof boundary.
/// </summary>
public sealed class StaticModelDuplicationEligibilityAssessment
{
    internal StaticModelDuplicationEligibilityAssessment(
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin,
        StaticModelTranslationEligibilityAssessment translation,
        GfxStaticModelDuplicationAssessment? gfx,
        ClipStaticModelDuplicationAssessment? collision,
        MapDocumentId documentId,
        string bundleBaselineDigest,
        SourceBindingId? gfxTemplateRecordBinding,
        SourceBindingId? clipTemplateRecordBinding,
        IEnumerable<StaticModelDuplicationEligibilityIssue> issues)
    {
        Relationship = relationship ??
            throw new ArgumentNullException(nameof(relationship));
        if (!destinationOrigin.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(destinationOrigin));
        Translation = translation ??
            throw new ArgumentNullException(nameof(translation));
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleBaselineDigest);
        if (gfxTemplateRecordBinding is { Value: var gfxBinding } &&
            gfxBinding == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gfxTemplateRecordBinding));
        }
        if (clipTemplateRecordBinding is { Value: var clipBinding } &&
            clipBinding == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clipTemplateRecordBinding));
        }
        if (gfxTemplateRecordBinding is not null &&
            gfxTemplateRecordBinding == clipTemplateRecordBinding)
        {
            throw new ArgumentException(
                "Gfx and collision template record bindings must be " +
                "distinct.");
        }
        ArgumentNullException.ThrowIfNull(issues);

        DestinationOrigin = destinationOrigin;
        DocumentId = documentId;
        BundleBaselineDigest = bundleBaselineDigest;
        GfxTemplateRecordBinding = gfxTemplateRecordBinding;
        ClipTemplateRecordBinding = clipTemplateRecordBinding;
        Gfx = gfx;
        Collision = collision;
        Issues = new ReadOnlyCollection<
            StaticModelDuplicationEligibilityIssue>(
            issues.ToArray());
        if (Issues.Count == 0 &&
            !(translation.IsPatchEligible &&
              gfx is { IsEligible: true } &&
              collision is { IsEligible: true } &&
              gfxTemplateRecordBinding is not null &&
              clipTemplateRecordBinding is not null))
        {
            throw new ArgumentException(
                "Issue-free duplication eligibility must retain complete " +
                "translation, Gfx, and collision proof.",
                nameof(issues));
        }
    }

    public StaticModelCompilationRelationship Relationship { get; }
    public MapVector3 DestinationOrigin { get; }
    public MapDocumentId DocumentId { get; }
    public string BundleBaselineDigest { get; }
    public SourceBindingId? GfxTemplateRecordBinding { get; }
    public SourceBindingId? ClipTemplateRecordBinding { get; }
    public StaticModelTranslationEligibilityAssessment Translation { get; }
    public GfxStaticModelDuplicationAssessment? Gfx { get; }
    public ClipStaticModelDuplicationAssessment? Collision { get; }
    public IReadOnlyList<StaticModelDuplicationEligibilityIssue> Issues
    {
        get;
    }
    public bool IsPatchEligible => Issues.Count == 0;
    public string Evidence =>
        IsPatchEligible
            ? string.Join(
                " ",
                [
                    Translation.Evidence,
                    "The exact imported Gfx XModel dependency is either a " +
                    "definition-free packed alias or a retained inline " +
                    "provider; the collision dependency is a " +
                    "definition-free packed alias.",
                    $"The typed Gfx and collision builders authorize " +
                    $"projected ordinals {Gfx!.NewOrdinal} and " +
                    $"{Collision!.NewOrdinal}."
                ])
            : string.Join(" ", Issues.Select(value => value.Detail));
}

/// <summary>
/// Admits only one exact imported Gfx/Clip pair whose destination has already
/// passed the translation preservation gates. The Gfx dependency may be an
/// exact retained Inline provider or a definition-free PackedAlias; the Clip
/// dependency remains a definition-free PackedAlias.
/// </summary>
public static class StaticModelDuplicationEligibilityEvaluator
{
    public static StaticModelDuplicationEligibilityAssessment Evaluate(
        CompiledMapBundle bundle,
        EditorMapDocument document,
        StaticModelCorrespondenceCatalog catalog,
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(relationship);
        if (!destinationOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOrigin),
                "Static-model duplication coordinates must be finite.");
        }

        var issues =
            new List<StaticModelDuplicationEligibilityIssue>();
        bool correspondenceValid =
            catalog.DocumentId == document.Id &&
            document.Id == bundle.DocumentId &&
            string.Equals(
                document.MapIdentity,
                bundle.MapIdentity,
                StringComparison.Ordinal) &&
            string.Equals(
                catalog.BundleBaselineDigest,
                bundle.BaselineDigest,
                StringComparison.Ordinal) &&
            catalog.TryGetByRenderObjectId(
                relationship.RenderObjectId,
                out StaticModelCompilationRelationship? authorized) &&
            authorized == relationship;
        if (!correspondenceValid)
        {
            issues.Add(new(
                StaticModelDuplicationEligibilityIssueKind
                    .CorrespondenceAuthorityMismatch,
                "The static-model relationship is not owned by this exact " +
                "document, bundle, and correspondence catalog."));
        }
        else
        {
            StaticModelExactMatchKey projectedKey =
                StaticModelExactMatchKey.Create(
                    relationship.ModelAssetType,
                    relationship.ExactSerializedModelName,
                    destinationOrigin);
            foreach (ImportedStaticModelExactMatchKeyOwner owner in
                     FindImportedExactMatchKeyOwners(
                         bundle,
                         relationship.CollisionAssetKind,
                         projectedKey))
            {
                string authority = owner.Representation switch
                {
                    StaticModelRepresentation.Render => "Gfx",
                    StaticModelRepresentation.Collision => "collision",
                    _ => throw new ArgumentOutOfRangeException()
                };
                issues.Add(new(
                    StaticModelDuplicationEligibilityIssueKind
                        .ProjectedCorrespondenceCollision,
                    "The authored destination reuses the exact model/origin " +
                    $"match key of imported {authority} ordinal " +
                    $"{owner.SourceOrdinal}; reopening " +
                    "would make compiled correspondence ambiguous."));
            }
        }

        EditorStaticModel? render = null;
        EditorStaticModel? collision = null;
        if (!document.TryGetObject(
                relationship.RenderObjectId,
                out EditorMapObject? renderObject) ||
            renderObject is not EditorStaticModel renderModel ||
            !document.TryGetObject(
                relationship.CollisionObjectId,
                out EditorMapObject? collisionObject) ||
            collisionObject is not EditorStaticModel collisionModel)
        {
            issues.Add(new(
                StaticModelDuplicationEligibilityIssueKind
                    .DocumentAuthorityMismatch,
                "The exact render/collision template objects are not " +
                "present in the semantic document."));
        }
        else
        {
            render = renderModel;
            collision = collisionModel;
            if (!render.IsImported ||
                !collision.IsImported ||
                render.Representation !=
                    StaticModelRepresentation.Render ||
                collision.Representation !=
                    StaticModelRepresentation.Collision ||
                render.SourceOrdinal.Value !=
                    relationship.GfxSourceOrdinal ||
                collision.SourceOrdinal.Value !=
                    relationship.ClipSourceOrdinal)
            {
                issues.Add(new(
                    StaticModelDuplicationEligibilityIssueKind
                        .TemplateProjectionMismatch,
                    "Duplication templates must be the exact imported Gfx " +
                    "and collision rows named by the relationship."));
            }
            else if (render.CompiledDisposition !=
                         StaticModelCompiledDisposition.BaselinePresent ||
                     collision.CompiledDisposition !=
                         StaticModelCompiledDisposition.BaselinePresent ||
                     !render.HasTransform(render.ImportedTransform) ||
                     !collision.HasTransform(
                         collision.ImportedTransform))
            {
                issues.Add(new(
                    StaticModelDuplicationEligibilityIssueKind
                        .TemplateStateIneligible,
                    "Duplication templates must remain baseline-present and " +
                    "unmodified."));
            }
        }

        StaticModelTranslationEligibilityAssessment translation =
            StaticModelTranslationEligibilityEvaluator.Evaluate(
                bundle,
                catalog,
                relationship,
                destinationOrigin);
        issues.AddRange(translation.Issues.Select(value => new
            StaticModelDuplicationEligibilityIssue(
                StaticModelDuplicationEligibilityIssueKind
                    .DestinationPreservation,
                value.Detail)));

        GfxStaticModelDuplicationAssessment? gfxAssessment = null;
        ClipStaticModelDuplicationAssessment? clipAssessment = null;
        if (translation.GfxSpatial is { } gfxSpatial &&
            bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) &&
            gfx is not null)
        {
            gfxAssessment =
                GfxStaticModelDuplicationAssessor.Assess(
                    gfx,
                    gfxSpatial);
            issues.AddRange(gfxAssessment.Issues.Select(value => new
                StaticModelDuplicationEligibilityIssue(
                    StaticModelDuplicationEligibilityIssueKind
                        .GfxCardinalityRebuild,
                    value.Detail)));
        }
        if (translation.CollisionSpatial is { } clipSpatial &&
            bundle.TryGetBaseline(
                relationship.CollisionAssetKind,
                out ClipMapBuildData? clip) &&
            clip is not null)
        {
            clipAssessment =
                ClipStaticModelDuplicationAssessor.Assess(
                    clip,
                    clipSpatial);
            issues.AddRange(clipAssessment.Issues.Select(value => new
                StaticModelDuplicationEligibilityIssue(
                    StaticModelDuplicationEligibilityIssueKind
                        .CollisionCardinalityRebuild,
                    value.Detail)));
        }

        if (translation.IsPatchEligible &&
            gfxAssessment is null)
        {
            issues.Add(new(
                StaticModelDuplicationEligibilityIssueKind
                    .GfxCardinalityRebuild,
                "The exact Gfx duplication assessment is unavailable."));
        }
        if (translation.IsPatchEligible &&
            clipAssessment is null)
        {
            issues.Add(new(
                StaticModelDuplicationEligibilityIssueKind
                    .CollisionCardinalityRebuild,
                "The exact collision duplication assessment is unavailable."));
        }

        return new(
            relationship,
            destinationOrigin,
            translation,
            gfxAssessment,
            clipAssessment,
            document.Id,
            bundle.BaselineDigest,
            render?.SourceOrdinal.SourceBinding,
            collision?.SourceOrdinal.SourceBinding,
            issues);
    }

    private static IEnumerable<ImportedStaticModelExactMatchKeyOwner>
        FindImportedExactMatchKeyOwners(
            CompiledMapBundle bundle,
            MapAssetKind collisionAssetKind,
            StaticModelExactMatchKey projectedKey)
    {
        if (bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) &&
            gfx is not null)
        {
            int rowCount = Math.Min(
                gfx.Definition.Dpvs.SModelDrawInsts.Count,
                gfx.References.StaticModelDrawInsts.Count);
            for (int ordinal = 0; ordinal < rowCount; ordinal++)
            {
                SymbolicXAssetReference? model =
                    gfx.References.StaticModelDrawInsts[ordinal];
                IReadOnlyList<float> serializedOrigin =
                    gfx.Definition.Dpvs.SModelDrawInsts[ordinal]
                        .Placement.Origin;
                if (model is not
                    {
                        AssetType: XAssetType.XModel,
                        OriginalSerializedName.Length: > 0
                    } ||
                    serializedOrigin.Count != 3)
                {
                    continue;
                }

                var origin = new MapVector3(
                    serializedOrigin[0],
                    serializedOrigin[1],
                    serializedOrigin[2]);
                if (origin.IsFinite &&
                    StaticModelExactMatchKey.Create(
                        model.AssetType,
                        model.OriginalSerializedName,
                        origin) == projectedKey)
                {
                    yield return new(
                        StaticModelRepresentation.Render,
                        ordinal);
                }
            }
        }

        if (bundle.TryGetBaseline(
                collisionAssetKind,
                out ClipMapBuildData? clip) &&
            clip is not null)
        {
            int rowCount = Math.Min(
                clip.Definition.StaticModelList.Count,
                clip.References.StaticModels.Count);
            for (int ordinal = 0; ordinal < rowCount; ordinal++)
            {
                SymbolicXAssetReference? model =
                    clip.References.StaticModels[ordinal];
                var serializedOrigin =
                    clip.Definition.StaticModelList[ordinal].Origin;
                var origin = new MapVector3(
                    serializedOrigin.X,
                    serializedOrigin.Y,
                    serializedOrigin.Z);
                if (model is
                    {
                        AssetType: XAssetType.XModel,
                        OriginalSerializedName.Length: > 0
                    } &&
                    origin.IsFinite &&
                    StaticModelExactMatchKey.Create(
                        model.AssetType,
                        model.OriginalSerializedName,
                        origin) == projectedKey)
                {
                    yield return new(
                        StaticModelRepresentation.Collision,
                        ordinal);
                }
            }
        }
    }

    private readonly record struct ImportedStaticModelExactMatchKeyOwner(
        StaticModelRepresentation Representation,
        int SourceOrdinal);
}
