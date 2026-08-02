using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Compilation.Validation;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Patching;

/// <summary>
/// One proof-gated absolute translation of an exact imported Gfx/Col
/// static-model pair. The five bindings describe only directly authored rows;
/// conservative Clip tree-envelope expansion is derived and validated by the
/// candidate builder.
/// </summary>
public sealed class StaticModelTranslationPatch
{
    private readonly IReadOnlyList<SourceBindingId> _sourceBindings;

    internal StaticModelTranslationPatch(
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin,
        IEnumerable<SourceBindingId> sourceBindings,
        GfxStaticModelTranslationSpatialAssessment gfxSpatialAssessment,
        ClipStaticModelTranslationSpatialAssessment clipSpatialAssessment,
        StaticModelLightingPreservationEligibility lightingAssessment,
        GfxStaticModelShadowMembershipAssessment shadowAssessment)
    {
        Relationship = relationship ??
            throw new ArgumentNullException(nameof(relationship));
        if (!destinationOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOrigin));
        }
        ArgumentNullException.ThrowIfNull(sourceBindings);
        ArgumentNullException.ThrowIfNull(gfxSpatialAssessment);
        ArgumentNullException.ThrowIfNull(clipSpatialAssessment);
        ArgumentNullException.ThrowIfNull(lightingAssessment);
        ArgumentNullException.ThrowIfNull(shadowAssessment);

        SourceBindingId[] bindings = sourceBindings
            .Distinct()
            .ToArray();
        if (bindings.Length != 5 ||
            bindings.Any(value => value.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "A static-model translation patch requires exactly five " +
                "distinct compiled-field bindings.",
                nameof(sourceBindings));
        }

        DestinationOrigin = destinationOrigin;
        _sourceBindings =
            new ReadOnlyCollection<SourceBindingId>(bindings);
        GfxSpatialAssessment = gfxSpatialAssessment;
        ClipSpatialAssessment = clipSpatialAssessment;
        LightingAssessment = lightingAssessment;
        ShadowAssessment = shadowAssessment;
    }

    public StaticModelCompilationRelationship Relationship { get; }
    public MapObjectId RenderObjectId => Relationship.RenderObjectId;
    public MapObjectId CollisionObjectId =>
        Relationship.CollisionObjectId;
    public int GfxSourceOrdinal => Relationship.GfxSourceOrdinal;
    public int ClipSourceOrdinal => Relationship.ClipSourceOrdinal;
    public MapAssetKind CollisionAssetKind =>
        Relationship.CollisionAssetKind;
    public MapVector3 DestinationOrigin { get; }
    public IReadOnlyList<SourceBindingId> SourceBindings =>
        _sourceBindings;
    public GfxStaticModelTranslationSpatialAssessment
        GfxSpatialAssessment { get; }
    public ClipStaticModelTranslationSpatialAssessment
        ClipSpatialAssessment { get; }
    public StaticModelLightingPreservationEligibility
        LightingAssessment { get; }
    public GfxStaticModelShadowMembershipAssessment
        ShadowAssessment { get; }

    internal StaticModelTranslationEdit GfxEdit =>
        new(
            GfxSourceOrdinal,
            DestinationOrigin.X,
            DestinationOrigin.Y,
            DestinationOrigin.Z);

    internal StaticModelTranslationEdit ClipEdit =>
        new(
            ClipSourceOrdinal,
            DestinationOrigin.X,
            DestinationOrigin.Y,
            DestinationOrigin.Z);
}

internal sealed class StaticModelTranslationPatchCandidate
{
    public StaticModelTranslationPatchCandidate(
        CompiledMapAssetDescriptor? gfxDescriptor,
        CompiledMapAssetDescriptor? clipDescriptor,
        GfxWorldBuildData? gfxBaseline,
        ClipMapBuildData? clipBaseline,
        GfxWorldBuildData? gfxBuildData,
        ClipMapBuildData? clipBuildData,
        IEnumerable<StaticModelTranslationPatch> patches,
        string? gfxBaselineSemanticDigest,
        string? clipBaselineSemanticDigest,
        MapPatchValidation validation)
    {
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentNullException.ThrowIfNull(validation);

        GfxDescriptor = gfxDescriptor;
        ClipDescriptor = clipDescriptor;
        GfxBaseline = gfxBaseline;
        ClipBaseline = clipBaseline;
        GfxBuildData = gfxBuildData;
        ClipBuildData = clipBuildData;
        Patches =
            new ReadOnlyCollection<StaticModelTranslationPatch>(
                patches.ToArray());
        GfxBaselineSemanticDigest = gfxBaselineSemanticDigest;
        ClipBaselineSemanticDigest = clipBaselineSemanticDigest;
        Validation = validation;
    }

    public CompiledMapAssetDescriptor? GfxDescriptor { get; }
    public CompiledMapAssetDescriptor? ClipDescriptor { get; }
    public GfxWorldBuildData? GfxBaseline { get; }
    public ClipMapBuildData? ClipBaseline { get; }
    public GfxWorldBuildData? GfxBuildData { get; }
    public ClipMapBuildData? ClipBuildData { get; }
    public IReadOnlyList<StaticModelTranslationPatch> Patches { get; }
    public string? GfxBaselineSemanticDigest { get; }
    public string? ClipBaselineSemanticDigest { get; }
    public MapPatchValidation Validation { get; }
}

/// <summary>
/// Builds the narrow compiled candidate for absolute translation of mutually
/// unique Gfx/Col static-model pairs. It preserves imported Gfx cell/AABB
/// membership and lighting/shadow assignments, and changes Clip spatial data
/// only through the canonical conservative leaf-to-root envelope builder.
/// </summary>
internal sealed class StaticModelTranslationPatcher
{
    private static readonly GfxWorldBodyEmitter GfxEmitter = new();

    public static MapPreservationCoverage GfxPreservationCoverage { get; } =
        new(
            MapAssetKind.GfxMap,
            "Spatially eligible exact-pair static-model translation",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "GfxWorld row identity, root scalars, counts and checksums",
                "Every unselected Gfx static-model draw/instance row",
                "Selected packed axis, scale, model link, cull distance and flags",
                "Selected lighting handle, probe/light indices, material skin and ground lighting",
                "Selected instance bounds half-size",
                "All Gfx cell/AABB topology and exact static-model memberships",
                "All DPVS topology and visibility tables",
                "All shadow-geometry rows and static-model index lists",
                "All dependencies and imported pointer source forms"
            ],
            mutableFields:
            [
                "$.definition.dpvs.sModelDrawInsts[i].placement.origin",
                "$.definition.dpvs.sModelInsts[i].bounds midpoint",
                "$.definition.dpvs.sModelInsts[i].lightingOrigin"
            ]);

    public static MapPreservationCoverage CollisionPreservationCoverage(
        MapAssetKind collisionAssetKind)
    {
        if (collisionAssetKind is not (
                MapAssetKind.ColMapMp or
                MapAssetKind.ColMapSp))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collisionAssetKind));
        }

        return new MapPreservationCoverage(
            collisionAssetKind,
            "Conservative exact-pair static-model translation",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "ColMap row identity, serialized Sp/Mp type, root scalars and counts",
                "Every unselected ClipStaticModel row",
                "Selected XModel link, pointer source form, inverse-scaled axis and bounds half-size",
                "SModelAabbNode cardinality, child ranges and index topology",
                "Every spatial node outside selected leaf-to-root ancestor paths",
                "All collision geometry, dynamic entities, stages, MapEnts and dependencies"
            ],
            mutableFields:
            [
                "$.definition.staticModelList[i].origin",
                "$.definition.staticModelList[i].absMin (decoded bounds midpoint)",
                "$.definition.smodelNodes[j].bounds on canonical owning leaf-to-root paths (derived)"
            ]);
    }

    public StaticModelTranslationPatchCandidate Prepare(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(sourceBindings);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<string>();
        if (!bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfxBaseline) ||
            gfxBaseline is null)
        {
            diagnostics.Add(
                "The compiled map bundle has no detached GfxMap baseline.");
            return InvalidCandidate(diagnostics);
        }
        if (!TryGetCollisionBaseline(
                bundle,
                out MapAssetKind collisionKind,
                out ClipMapBuildData? clipBaseline) ||
            clipBaseline is null)
        {
            diagnostics.Add(
                "The compiled map bundle must have exactly one detached " +
                "ColMapMp or ColMapSp baseline.");
            return InvalidCandidate(diagnostics);
        }

        CompiledMapAssetDescriptor gfxDescriptor =
            bundle.RequireAsset(MapAssetKind.GfxMap);
        CompiledMapAssetDescriptor clipDescriptor =
            bundle.RequireAsset(collisionKind);
        StaticModelCorrespondenceCatalog relationships =
            StaticModelCompilationRelationshipResolver.Resolve(
                bundle,
                document,
                cancellationToken);
        if (!relationships.AuthoritiesValid)
        {
            diagnostics.Add(
                "Static-model correspondence authorities are invalid: " +
                string.Join(
                    "; ",
                    relationships.Issues.Select(value => value.Evidence)));
        }

        Dictionary<SourceBindingId, CompiledSourceBinding> bindingCatalog =
            BuildBindingCatalog(sourceBindings, diagnostics);
        EditorStaticModel[] translated = document.StaticModels
            .Where(value =>
                value.IsImported &&
                !value.HasTransform(value.ImportedTransform))
            .ToArray();
        var patches = new List<StaticModelTranslationPatch>();
        var consumed = new HashSet<MapObjectId>();
        foreach (EditorStaticModel render in translated
                     .Where(value =>
                         value.Representation ==
                         StaticModelRepresentation.Render)
                     .OrderBy(value => value.SourceOrdinal.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!relationships.TryGetByRenderObjectId(
                    render.Id,
                    out StaticModelCompilationRelationship? relationship) ||
                relationship is null)
            {
                diagnostics.Add(
                    $"Translated render static model {render.Id} has no " +
                    "mutual exact-bundle collision relationship.");
                continue;
            }
            EditorStaticModel? collision = document.StaticModels
                .SingleOrDefault(value =>
                    value.IsImported &&
                    value.Id == relationship.CollisionObjectId);
            if (collision is null ||
                collision.HasTransform(collision.ImportedTransform))
            {
                diagnostics.Add(
                    $"Translated render static model {render.Id} does not " +
                    "have its exact collision counterpart translated.");
                continue;
            }
            if (relationship.CollisionAssetKind != collisionKind)
            {
                diagnostics.Add(
                    $"Static-model relationship {render.Id} targets " +
                    $"{relationship.CollisionAssetKind}, not the owned " +
                    $"{collisionKind} baseline.");
                continue;
            }
            if (render.CompiledDisposition !=
                    StaticModelCompiledDisposition.BaselinePresent ||
                collision.CompiledDisposition !=
                    StaticModelCompiledDisposition.BaselinePresent)
            {
                diagnostics.Add(
                    $"Translated static-model pair {render.Id} is suppressed " +
                    "or has inconsistent compiled disposition.");
                continue;
            }
            if (!SameVec(
                    render.Transform.Origin,
                    collision.Transform.Origin))
            {
                diagnostics.Add(
                    $"Translated static-model pair {render.Id} does not share " +
                    "one bit-exact absolute destination.");
                continue;
            }
            if (!IsTranslationOfImported(render) ||
                !IsTranslationOfImported(collision))
            {
                diagnostics.Add(
                    $"Static-model pair {render.Id} changed scale, bounds " +
                    "half-size, or another non-translation semantic field.");
                continue;
            }

            MapVector3 destination = render.Transform.Origin;
            SourceBindingId[] mutableBindings =
                GetMutableBindings(render, collision, diagnostics);
            ValidateBindings(
                bundle,
                gfxDescriptor,
                clipDescriptor,
                relationship,
                mutableBindings,
                bindingCatalog,
                diagnostics);

            if (!consumed.Add(render.Id) ||
                !consumed.Add(collision.Id))
            {
                diagnostics.Add(
                    $"Static-model relationship {render.Id} is not one-to-one " +
                    "in the translated semantic state.");
                continue;
            }
            StaticModelTranslationEligibilityAssessment eligibility =
                StaticModelTranslationEligibilityEvaluator.Evaluate(
                    bundle,
                    relationships,
                    relationship,
                    destination);
            diagnostics.AddRange(eligibility.Issues.Select(issue =>
                $"Static-model translation {render.Id} failed " +
                $"{issue.Kind}: {issue.Detail}"));
            if (mutableBindings.Length != 5 ||
                mutableBindings.Distinct().Count() != 5 ||
                !eligibility.IsPatchEligible ||
                eligibility.GfxSpatial is null ||
                eligibility.CollisionSpatial is null ||
                eligibility.Lighting is null ||
                eligibility.Shadows is null)
            {
                continue;
            }
            patches.Add(new StaticModelTranslationPatch(
                relationship,
                destination,
                mutableBindings,
                eligibility.GfxSpatial,
                eligibility.CollisionSpatial,
                eligibility.Lighting,
                eligibility.Shadows));
        }

        foreach (EditorStaticModel orphan in translated.Where(value =>
                     !consumed.Contains(value.Id)))
        {
            diagnostics.Add(
                $"Translated {orphan.Representation.ToString().ToLowerInvariant()} " +
                $"static model {orphan.Id} is not part of an authorized " +
                "atomic pair.");
        }

        ValidateLightingAndShadowEligibility(
            gfxBaseline,
            patches,
            diagnostics,
            cancellationToken);

        string gfxBaselineDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                gfxBaseline,
                cancellationToken);
        string clipBaselineDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                clipBaseline,
                cancellationToken);
        GfxWorldBuildData? gfxCandidate = null;
        ClipMapBuildData? clipCandidate = null;
        if (diagnostics.Count == 0 && patches.Count != 0)
        {
            try
            {
                gfxCandidate =
                    gfxBaseline.WithSpatiallyEligibleStaticModelTranslations(
                        patches.Select(value =>
                            value.GfxSpatialAssessment));
                clipCandidate =
                    clipBaseline.WithConservativelyTranslatedStaticModels(
                        patches.Select(value => value.ClipEdit));
                diagnostics.AddRange(
                    ValidatePreservation(
                            gfxBaseline,
                            clipBaseline,
                            gfxCandidate,
                            clipCandidate,
                            patches,
                            cancellationToken)
                        .Diagnostics);
            }
            catch (Exception exception) when (
                exception is not (
                    OutOfMemoryException or
                    OperationCanceledException))
            {
                diagnostics.Add(
                    "Could not build the static-model translation candidate: " +
                    exception.Message);
                gfxCandidate = null;
                clipCandidate = null;
            }
        }

        if (!string.Equals(
                gfxBaselineDigest,
                RelocationInvariantAssetSemanticDigest.Compute(
                    gfxBaseline,
                    cancellationToken),
                StringComparison.Ordinal) ||
            !string.Equals(
                clipBaselineDigest,
                RelocationInvariantAssetSemanticDigest.Compute(
                    clipBaseline,
                    cancellationToken),
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "Preparing static-model translation mutated an immutable " +
                "compiled baseline.");
        }

        return new StaticModelTranslationPatchCandidate(
            gfxDescriptor,
            clipDescriptor,
            gfxBaseline,
            clipBaseline,
            gfxCandidate,
            clipCandidate,
            patches,
            gfxBaselineDigest,
            clipBaselineDigest,
            new MapPatchValidation(diagnostics));
    }

    public MapPatchValidation ValidatePreservation(
        GfxWorldBuildData gfxBaseline,
        ClipMapBuildData clipBaseline,
        GfxWorldBuildData gfxCandidate,
        ClipMapBuildData clipCandidate,
        IEnumerable<StaticModelTranslationPatch> patches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gfxBaseline);
        ArgumentNullException.ThrowIfNull(clipBaseline);
        ArgumentNullException.ThrowIfNull(gfxCandidate);
        ArgumentNullException.ThrowIfNull(clipCandidate);
        ArgumentNullException.ThrowIfNull(patches);

        var diagnostics = new List<string>();
        StaticModelTranslationPatch[] patchCopy = patches.ToArray();
        if (patchCopy.Select(value => value.GfxSourceOrdinal)
                .Distinct().Count() != patchCopy.Length ||
            patchCopy.Select(value => value.ClipSourceOrdinal)
                .Distinct().Count() != patchCopy.Length)
        {
            diagnostics.Add(
                "Static-model translation patches are not one-to-one.");
        }
        if (patchCopy.Any(value =>
                value.SourceBindings.Count != 5 ||
                !value.DestinationOrigin.IsFinite))
        {
            diagnostics.Add(
                "Static-model translation patches lack the canonical five " +
                "bindings or a finite destination.");
        }
        if (gfxBaseline.Definition.Dpvs.SModelCount !=
                gfxCandidate.Definition.Dpvs.SModelCount ||
            gfxBaseline.Definition.Dpvs.SModelDrawInsts.Count !=
                gfxCandidate.Definition.Dpvs.SModelDrawInsts.Count ||
            gfxBaseline.Definition.Dpvs.SModelInsts.Count !=
                gfxCandidate.Definition.Dpvs.SModelInsts.Count)
        {
            diagnostics.Add(
                "Gfx static-model count or parallel table cardinality changed.");
        }
        if (clipBaseline.SerializedType != clipCandidate.SerializedType ||
            clipBaseline.Definition.NumStaticModels !=
                clipCandidate.Definition.NumStaticModels ||
            clipBaseline.Definition.StaticModelList.Count !=
                clipCandidate.Definition.StaticModelList.Count ||
            clipBaseline.Definition.SModelNodeCount !=
                clipCandidate.Definition.SModelNodeCount ||
            clipBaseline.Definition.SModelNodes.Count !=
                clipCandidate.Definition.SModelNodes.Count)
        {
            diagnostics.Add(
                "Collision static-model or spatial-node topology changed.");
        }

        GfxWorldBuildData? expectedGfx = null;
        ClipMapBuildData? expectedClip = null;
        try
        {
            GfxStaticModelTranslationSpatialAssessment[] assessments =
                patchCopy
                    .OrderBy(value => value.GfxSourceOrdinal)
                    .Select(value =>
                        GfxStaticModelTranslationSpatialAssessor.Assess(
                            gfxBaseline,
                            value.GfxEdit))
                    .ToArray();
            foreach (GfxStaticModelTranslationSpatialAssessment assessment in
                     assessments.Where(value => !value.IsEligible))
            {
                diagnostics.AddRange(assessment.Issues.Select(issue =>
                    $"Revalidated Gfx translation failed {issue.Kind}: " +
                    issue.Detail));
            }
            ClipStaticModelTranslationSpatialAssessment[] clipAssessments =
                patchCopy
                    .OrderBy(value => value.ClipSourceOrdinal)
                    .Select(value =>
                        clipBaseline
                            .AssessConservativeStaticModelTranslation(
                                value.ClipEdit))
                    .ToArray();
            foreach (ClipStaticModelTranslationSpatialAssessment assessment in
                     clipAssessments.Where(value => !value.IsEligible))
            {
                diagnostics.AddRange(assessment.Issues.Select(issue =>
                    $"Revalidated ColMap translation failed {issue.Kind}: " +
                    issue.Detail));
            }
            if (assessments.All(value => value.IsEligible) &&
                clipAssessments.All(value => value.IsEligible))
            {
                expectedGfx =
                    gfxBaseline.WithSpatiallyEligibleStaticModelTranslations(
                        assessments);
                expectedClip =
                    clipBaseline.WithConservativelyTranslatedStaticModels(
                        patchCopy.Select(value => value.ClipEdit));
            }
        }
        catch (Exception exception) when (
            exception is not (
                OutOfMemoryException or
                OperationCanceledException))
        {
            diagnostics.Add(
                "Could not independently rebuild the canonical translation " +
                $"candidate: {exception.Message}");
        }

        if (expectedGfx is not null)
        {
            ValidateGfxRows(
                gfxBaseline,
                gfxCandidate,
                expectedGfx,
                patchCopy,
                diagnostics);
            if (!SameSemantic(
                    expectedGfx,
                    gfxCandidate,
                    cancellationToken))
            {
                diagnostics.Add(
                    "Gfx candidate differs outside the canonical " +
                    "spatially eligible translation transformation.");
            }
        }
        if (expectedClip is not null)
        {
            ValidateClipRowsAndNodes(
                clipBaseline,
                clipCandidate,
                expectedClip,
                patchCopy,
                diagnostics);
            if (!SameSemantic(
                    expectedClip,
                    clipCandidate,
                    cancellationToken))
            {
                diagnostics.Add(
                    "ColMap candidate differs outside the canonical " +
                    "conservative translation transformation.");
            }
        }

        ValidateLightingAndShadowEligibility(
            gfxBaseline,
            patchCopy,
            diagnostics,
            cancellationToken);
        diagnostics.AddRange(
            GfxEmitter.Validate(gfxCandidate)
                .Select(value =>
                    $"GfxMap emitter validation failed at {value.Path}: " +
                    value.Message));
        var clipEmitter =
            new ClipMapBodyEmitter(clipCandidate.SerializedType);
        diagnostics.AddRange(
            clipEmitter.Validate(clipCandidate)
                .Select(value =>
                    $"ColMap emitter validation failed at {value.Path}: " +
                    value.Message));
        return new MapPatchValidation(diagnostics);
    }

    public void ApplyValidatedGfxCandidate(
        GfxWorldDraft draft,
        StaticModelTranslationPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValidCandidate(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.GfxBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged GfxMap draft no longer matches the exact " +
                "imported translation baseline.");
        }
        draft.Replace(candidate.GfxBuildData!);
    }

    public void ApplyValidatedCollisionCandidate(
        ClipMapDraft draft,
        StaticModelTranslationPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValidCandidate(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.ClipBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged ColMap draft no longer matches the exact " +
                "imported translation baseline.");
        }
        draft.Replace(candidate.ClipBuildData!);
    }

    private static void ValidateLightingAndShadowEligibility(
        GfxWorldBuildData gfxBaseline,
        IReadOnlyCollection<StaticModelTranslationPatch> patches,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Shadow geometry is a whole-world authority. Its assessor proves the
        // native primary-light-index partition; row validation below then
        // proves that translation leaves both Flags and PrimaryLightIndex
        // untouched for every selected draw.
        GfxStaticModelShadowMembershipAssessment currentShadows =
            GfxStaticModelShadowMembershipAssessor.Assess(gfxBaseline);
        foreach (GfxStaticModelShadowMembershipIssue issue in
                 currentShadows.Issues)
        {
            diagnostics.Add(
                $"Gfx shadow-membership preservation failed {issue.Kind}: " +
                issue.Detail);
        }
        foreach (StaticModelTranslationPatch patch in patches)
        {
            if (!patch.LightingAssessment.IsEligible)
            {
                foreach (StaticModelLightingPreservationIssue issue in
                         patch.LightingAssessment.Issues)
                {
                    diagnostics.Add(
                        $"Static-model lighting preservation for Gfx row " +
                        $"{patch.GfxSourceOrdinal} failed: {issue.Detail}");
                }
            }
            if (!patch.ShadowAssessment.IsValid)
            {
                foreach (GfxStaticModelShadowMembershipIssue issue in
                         patch.ShadowAssessment.Issues)
                {
                    diagnostics.Add(
                        $"Static-model shadow preservation for Gfx row " +
                        $"{patch.GfxSourceOrdinal} failed: {issue.Detail}");
                }
            }
        }
    }

    private static void ValidateGfxRows(
        GfxWorldBuildData baseline,
        GfxWorldBuildData candidate,
        GfxWorldBuildData expected,
        IReadOnlyList<StaticModelTranslationPatch> patches,
        ICollection<string> diagnostics)
    {
        HashSet<int> patched = patches
            .Select(value => value.GfxSourceOrdinal)
            .ToHashSet();
        IReadOnlyList<GfxStaticModelDrawInst> sourceDraws =
            baseline.Definition.Dpvs.SModelDrawInsts;
        IReadOnlyList<GfxStaticModelDrawInst> candidateDraws =
            candidate.Definition.Dpvs.SModelDrawInsts;
        IReadOnlyList<GfxStaticModelDrawInst> expectedDraws =
            expected.Definition.Dpvs.SModelDrawInsts;
        IReadOnlyList<GfxStaticModelInst> sourceInstances =
            baseline.Definition.Dpvs.SModelInsts;
        IReadOnlyList<GfxStaticModelInst> candidateInstances =
            candidate.Definition.Dpvs.SModelInsts;
        IReadOnlyList<GfxStaticModelInst> expectedInstances =
            expected.Definition.Dpvs.SModelInsts;
        int count = new[]
        {
            sourceDraws.Count,
            candidateDraws.Count,
            expectedDraws.Count,
            sourceInstances.Count,
            candidateInstances.Count,
            expectedInstances.Count
        }.Min();
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            if (!SameJson(
                    expectedDraws[ordinal],
                    candidateDraws[ordinal]) ||
                !SameJson(
                    expectedInstances[ordinal],
                    candidateInstances[ordinal]))
            {
                diagnostics.Add(
                    $"Gfx static-model row {ordinal} differs from the " +
                    "canonical translation builder.");
            }
            if (!patched.Contains(ordinal) &&
                (!SameJson(
                     sourceDraws[ordinal],
                     candidateDraws[ordinal]) ||
                 !SameJson(
                     sourceInstances[ordinal],
                     candidateInstances[ordinal])))
            {
                diagnostics.Add(
                    $"Unselected Gfx static-model row {ordinal} changed.");
            }
            if (patched.Contains(ordinal) &&
                (sourceDraws[ordinal].Flags !=
                    candidateDraws[ordinal].Flags ||
                 sourceDraws[ordinal].PrimaryLightIndex !=
                    candidateDraws[ordinal].PrimaryLightIndex))
            {
                diagnostics.Add(
                    $"Translated Gfx static-model row {ordinal} changed its " +
                    "shadow flags or primary-light assignment.");
            }
        }
    }

    private static void ValidateClipRowsAndNodes(
        ClipMapBuildData baseline,
        ClipMapBuildData candidate,
        ClipMapBuildData expected,
        IReadOnlyList<StaticModelTranslationPatch> patches,
        ICollection<string> diagnostics)
    {
        HashSet<int> patched = patches
            .Select(value => value.ClipSourceOrdinal)
            .ToHashSet();
        int modelCount = new[]
        {
            baseline.Definition.StaticModelList.Count,
            candidate.Definition.StaticModelList.Count,
            expected.Definition.StaticModelList.Count
        }.Min();
        for (int ordinal = 0; ordinal < modelCount; ordinal++)
        {
            ClipStaticModel source =
                baseline.Definition.StaticModelList[ordinal];
            ClipStaticModel edited =
                candidate.Definition.StaticModelList[ordinal];
            ClipStaticModel canonical =
                expected.Definition.StaticModelList[ordinal];
            if (!SameJson(canonical, edited))
            {
                diagnostics.Add(
                    $"Collision static-model row {ordinal} differs from the " +
                    "canonical translation builder.");
            }
            if (!patched.Contains(ordinal) &&
                !SameJson(source, edited))
            {
                diagnostics.Add(
                    $"Unselected collision static-model row {ordinal} changed.");
            }
        }

        int nodeCount = new[]
        {
            baseline.Definition.SModelNodes.Count,
            candidate.Definition.SModelNodes.Count,
            expected.Definition.SModelNodes.Count
        }.Min();
        for (int index = 0; index < nodeCount; index++)
        {
            SModelAabbNode source = baseline.Definition.SModelNodes[index];
            SModelAabbNode edited = candidate.Definition.SModelNodes[index];
            SModelAabbNode canonical = expected.Definition.SModelNodes[index];
            if (!SameJson(canonical, edited))
            {
                diagnostics.Add(
                    $"Collision static-model spatial node {index} differs " +
                    "from the canonical conservative builder.");
            }
            if (source.FirstChild != edited.FirstChild ||
                source.ChildCount != edited.ChildCount)
            {
                diagnostics.Add(
                    $"Collision static-model spatial node {index} changed " +
                    "its child/index topology.");
            }
        }
    }

    private static void ValidateBindings(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor gfxDescriptor,
        CompiledMapAssetDescriptor clipDescriptor,
        StaticModelCompilationRelationship relationship,
        IEnumerable<SourceBindingId> bindingIds,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding> catalog,
        ICollection<string> diagnostics)
    {
        int gfx = relationship.GfxSourceOrdinal;
        int clip = relationship.ClipSourceOrdinal;
        var expected = new Dictionary<string, CompiledMapAssetDescriptor>
        {
            [$"$.definition.dpvs.sModelDrawInsts[{gfx}].placement.origin"] =
                gfxDescriptor,
            [$"$.definition.dpvs.sModelInsts[{gfx}].bounds"] =
                gfxDescriptor,
            [$"$.definition.dpvs.sModelInsts[{gfx}].lightingOrigin"] =
                gfxDescriptor,
            [$"$.definition.staticModelList[{clip}].origin"] =
                clipDescriptor,
            [$"$.definition.staticModelList[{clip}].absMin"] =
                clipDescriptor
        };
        SourceBindingId[] ids = bindingIds.ToArray();
        CompiledSourceBinding[] bindings = ids
            .Select(id => catalog.TryGetValue(
                    id,
                    out CompiledSourceBinding? value)
                ? value
                : null)
            .Where(value => value is not null)
            .Cast<CompiledSourceBinding>()
            .ToArray();
        foreach (SourceBindingId missing in ids.Where(
                     id => !catalog.ContainsKey(id)))
        {
            diagnostics.Add(
                $"Static-model translation binding {missing} is absent " +
                "from the imported catalog.");
        }
        if (ids.Length != 5 ||
            !expected.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(bindings.Select(value => value.FieldPath)))
        {
            diagnostics.Add(
                "Static-model translation does not carry the exact five " +
                "mutable Gfx/Col compiled-field bindings.");
        }
        foreach (CompiledSourceBinding binding in bindings)
        {
            if (!expected.TryGetValue(
                    binding.FieldPath,
                    out CompiledMapAssetDescriptor? descriptor))
            {
                continue;
            }
            int ordinal = descriptor.Kind == MapAssetKind.GfxMap
                ? gfx
                : clip;
            SourceBindingId expectedId =
                DeterministicMapIdentity.Binding(
                    bundle.MapIdentity,
                    descriptor.SerializedType.ToString(),
                    descriptor.AssetName,
                    binding.FieldPath,
                    ordinal);
            if (binding.Id != expectedId ||
                binding.AssetType != descriptor.SerializedType ||
                binding.OwnerRow != descriptor.OwnerRow ||
                binding.SourceOrdinal != ordinal ||
                !string.Equals(
                    binding.AssetName,
                    descriptor.AssetName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.BaselineDigest,
                    descriptor.BaselineDigest,
                    StringComparison.Ordinal) ||
                binding.Provenance is not (
                    MapValueProvenance.ExactSerialized or
                    MapValueProvenance.ExactDecodedRuntime))
            {
                diagnostics.Add(
                    $"Static-model translation binding {binding.Id} is not " +
                    $"exact authority for '{binding.FieldPath}'.");
            }
        }
    }

    private static SourceBindingId[] GetMutableBindings(
        EditorStaticModel render,
        EditorStaticModel collision,
        ICollection<string> diagnostics)
    {
        StaticModelCompiledFieldBindings gfx =
            render.CompiledFieldBindings;
        StaticModelCompiledFieldBindings clip =
            collision.CompiledFieldBindings;
        if (!gfx.HasCompleteTranslationBindings ||
            !clip.HasCompleteTranslationBindings ||
            gfx.LightingOriginBinding is not { } lighting)
        {
            diagnostics.Add(
                $"Static-model pair {render.Id} lacks complete exact " +
                "compiled translation bindings.");
            return [];
        }

        SourceBindingId[] result =
        [
            gfx.OriginBinding,
            gfx.BoundsMidpointBinding,
            lighting,
            clip.OriginBinding,
            clip.BoundsMidpointBinding
        ];
        if (result.Distinct().Count() != result.Length)
        {
            diagnostics.Add(
                $"Static-model pair {render.Id} does not have five distinct " +
                "compiled translation bindings.");
        }
        return result;
    }

    private static bool IsTranslationOfImported(EditorStaticModel model)
    {
        EditorStaticModelTransformState imported =
            model.ImportedTransform;
        EditorStaticModelTransformState current = model.Transform;
        if (!NullableFloatBitsEqual(imported.Scale, current.Scale) ||
            imported.Bounds is not { } importedBounds ||
            current.Bounds is not { } currentBounds ||
            !SameVec(importedBounds.HalfSize, currentBounds.HalfSize))
        {
            return false;
        }

        MapVector3 delta = new(
            current.Origin.X - imported.Origin.X,
            current.Origin.Y - imported.Origin.Y,
            current.Origin.Z - imported.Origin.Z);
        return delta.IsFinite &&
            SameBits(
                currentBounds.MidPoint.X,
                importedBounds.MidPoint.X + delta.X) &&
            SameBits(
                currentBounds.MidPoint.Y,
                importedBounds.MidPoint.Y + delta.Y) &&
            SameBits(
                currentBounds.MidPoint.Z,
                importedBounds.MidPoint.Z + delta.Z);
    }

    private static void RequireValidCandidate(
        StaticModelTranslationPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!GfxPreservationCoverage.IsProven ||
            candidate.ClipDescriptor is null ||
            !CollisionPreservationCoverage(
                candidate.ClipDescriptor.Kind).IsProven ||
            !candidate.Validation.IsValid ||
            candidate.Patches.Count == 0 ||
            candidate.GfxBuildData is null ||
            candidate.ClipBuildData is null)
        {
            throw new InvalidOperationException(
                "An invalid, empty, or coverage-incomplete static-model " +
                "translation candidate cannot replace staged drafts.");
        }
    }

    private static Dictionary<SourceBindingId, CompiledSourceBinding>
        BuildBindingCatalog(
            IEnumerable<CompiledSourceBinding> sourceBindings,
            ICollection<string> diagnostics)
    {
        var result =
            new Dictionary<SourceBindingId, CompiledSourceBinding>();
        foreach (CompiledSourceBinding binding in sourceBindings)
        {
            if (binding is null || !result.TryAdd(binding.Id, binding))
            {
                diagnostics.Add(
                    "The imported compiled-binding catalog contains a null " +
                    "or duplicate entry.");
            }
        }
        return result;
    }

    private static bool TryGetCollisionBaseline(
        CompiledMapBundle bundle,
        out MapAssetKind kind,
        out ClipMapBuildData? baseline)
    {
        bool hasMp = bundle.TryGetBaseline(
            MapAssetKind.ColMapMp,
            out ClipMapBuildData? mp) &&
            mp is not null;
        bool hasSp = bundle.TryGetBaseline(
            MapAssetKind.ColMapSp,
            out ClipMapBuildData? sp) &&
            sp is not null;
        if (hasMp == hasSp)
        {
            kind = default;
            baseline = null;
            return false;
        }

        kind = hasMp
            ? MapAssetKind.ColMapMp
            : MapAssetKind.ColMapSp;
        baseline = hasMp ? mp : sp;
        return true;
    }

    private static StaticModelTranslationPatchCandidate InvalidCandidate(
        IEnumerable<string> diagnostics) =>
        new(
            gfxDescriptor: null,
            clipDescriptor: null,
            gfxBaseline: null,
            clipBaseline: null,
            gfxBuildData: null,
            clipBuildData: null,
            patches: [],
            gfxBaselineSemanticDigest: null,
            clipBaselineSemanticDigest: null,
            new MapPatchValidation(diagnostics));

    private static bool SameSemantic(
        IXAssetBuildData left,
        IXAssetBuildData right,
        CancellationToken cancellationToken) =>
        string.Equals(
            RelocationInvariantAssetSemanticDigest.Compute(
                left,
                cancellationToken),
            RelocationInvariantAssetSemanticDigest.Compute(
                right,
                cancellationToken),
            StringComparison.Ordinal);

    private static bool SameDigest(
        IXAssetBuildData value,
        string expectedDigest) =>
        string.Equals(
            RelocationInvariantAssetSemanticDigest.Compute(value),
            expectedDigest,
            StringComparison.Ordinal);

    private static bool SameJson<T>(T left, T right) =>
        System.Text.Json.JsonSerializer.Serialize(left) ==
        System.Text.Json.JsonSerializer.Serialize(right);

    private static bool SameVec(
        MapVector3 left,
        MapVector3 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z);

    private static bool NullableFloatBitsEqual(
        float? left,
        float? right) =>
        left.HasValue == right.HasValue &&
        (!left.HasValue ||
         SameBits(left.Value, right!.Value));

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}
