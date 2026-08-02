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

public sealed class StaticModelRemovalPatch
{
    internal StaticModelRemovalPatch(
        StaticModelRemovalEligibilityAssessment eligibility,
        IEnumerable<StaticModelRemovalSourceBindingAuthority>
            bindingAuthorities)
    {
        Eligibility = eligibility ??
            throw new ArgumentNullException(nameof(eligibility));
        if (!eligibility.IsPatchEligible)
        {
            throw new ArgumentException(
                "A static-model removal patch requires complete eligibility.",
                nameof(eligibility));
        }
        ArgumentNullException.ThrowIfNull(bindingAuthorities);
        StaticModelRemovalSourceBindingAuthority[] authorities =
            bindingAuthorities.ToArray();
        int expectedCount = 2 +
            eligibility.Gfx!.ProviderCarryForwards.Count;
        if (authorities.Length != expectedCount ||
            authorities.Select(value => value.Role)
                .Distinct().Count() != authorities.Length ||
            authorities.Select(value => value.SourceBinding)
                .Distinct().Count() != authorities.Length ||
            authorities.Any(value =>
                value.SourceBinding.Value == Guid.Empty) ||
            authorities.Count(value =>
                value.Role ==
                StaticModelRemovalSourceBindingRole.RemovedGfxRow) != 1 ||
            authorities.Count(value =>
                value.Role ==
                StaticModelRemovalSourceBindingRole
                    .RemovedCollisionRow) != 1 ||
            authorities.Count(value =>
                value.Role ==
                StaticModelRemovalSourceBindingRole
                    .GfxProviderReceiverRow) !=
                eligibility.Gfx.ProviderCarryForwards.Count)
        {
            throw new ArgumentException(
                "A static-model removal patch requires exact, distinct " +
                "removed-Gfx, removed-collision, and optional provider-" +
                "receiver binding roles.",
                nameof(bindingAuthorities));
        }
        if (authorities.Single(value =>
                    value.Role ==
                    StaticModelRemovalSourceBindingRole.RemovedGfxRow)
                .SourceOrdinal !=
                eligibility.Relationship.GfxSourceOrdinal ||
            authorities.Single(value =>
                    value.Role ==
                    StaticModelRemovalSourceBindingRole
                        .RemovedCollisionRow)
                .SourceOrdinal !=
                eligibility.Relationship.ClipSourceOrdinal ||
            eligibility.Gfx.ProviderCarryForwards.Any(carryForward =>
                authorities.Single(value =>
                        value.Role ==
                        StaticModelRemovalSourceBindingRole
                            .GfxProviderReceiverRow)
                    .SourceOrdinal !=
                    carryForward.ReceiverOrdinal))
        {
            throw new ArgumentException(
                "Static-model removal binding roles do not match their " +
                "assessed source ordinals.",
                nameof(bindingAuthorities));
        }

        BindingAuthorities = new ReadOnlyCollection<
            StaticModelRemovalSourceBindingAuthority>(authorities);
        SourceBindings =
            new ReadOnlyCollection<SourceBindingId>(
                authorities.Select(value => value.SourceBinding)
                    .ToArray());
    }

    public StaticModelRemovalEligibilityAssessment Eligibility { get; }
    public StaticModelCompilationRelationship Relationship =>
        Eligibility.Relationship;
    public int GfxSourceOrdinal =>
        Relationship.GfxSourceOrdinal;
    public int ClipSourceOrdinal =>
        Relationship.ClipSourceOrdinal;
    public IReadOnlyList<StaticModelRemovalSourceBindingAuthority>
        BindingAuthorities { get; }
    public IReadOnlyList<SourceBindingId> SourceBindings { get; }
}

internal sealed class StaticModelRemovalPatchCandidate
{
    public StaticModelRemovalPatchCandidate(
        CompiledMapAssetDescriptor? gfxDescriptor,
        CompiledMapAssetDescriptor? clipDescriptor,
        GfxWorldBuildData? gfxBaseline,
        ClipMapBuildData? clipBaseline,
        GfxWorldBuildData? gfxBuildData,
        ClipMapBuildData? clipBuildData,
        IEnumerable<StaticModelRemovalPatch> patches,
        string? gfxBaselineSemanticDigest,
        string? clipBaselineSemanticDigest,
        MapPatchValidation validation)
    {
        GfxDescriptor = gfxDescriptor;
        ClipDescriptor = clipDescriptor;
        GfxBaseline = gfxBaseline;
        ClipBaseline = clipBaseline;
        GfxBuildData = gfxBuildData;
        ClipBuildData = clipBuildData;
        Patches = new ReadOnlyCollection<StaticModelRemovalPatch>(
            patches.ToArray());
        GfxBaselineSemanticDigest = gfxBaselineSemanticDigest;
        ClipBaselineSemanticDigest = clipBaselineSemanticDigest;
        Validation = validation ??
            throw new ArgumentNullException(nameof(validation));
    }

    public CompiledMapAssetDescriptor? GfxDescriptor { get; }
    public CompiledMapAssetDescriptor? ClipDescriptor { get; }
    public GfxWorldBuildData? GfxBaseline { get; }
    public ClipMapBuildData? ClipBaseline { get; }
    public GfxWorldBuildData? GfxBuildData { get; }
    public ClipMapBuildData? ClipBuildData { get; }
    public IReadOnlyList<StaticModelRemovalPatch> Patches { get; }
    public string? GfxBaselineSemanticDigest { get; }
    public string? ClipBaselineSemanticDigest { get; }
    public MapPatchValidation Validation { get; }
}

/// <summary>
/// Rebuilds all known ordinal consumers while removing ExactBundleUnique
/// Gfx/Col static-model pairs as one atomic compiled candidate.
/// </summary>
internal sealed class StaticModelRemovalPatcher
{
    private static readonly GfxWorldBodyEmitter GfxEmitter = new();

    public static MapPreservationCoverage GfxPreservationCoverage { get; } =
        new(
            MapAssetKind.GfxMap,
            "Exact-pair static-model removal and ordinal rebuild",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "GfxWorld owner identity, root scalars, surfaces, cells, portals, DPVS planes and checksums",
                "Every retained static-model draw/instance row in original relative order",
                "Nested XModel provider sequence, with an exact adjacent inline carry-forward when the removed row owns the provider",
                "Every retained AABB membership with source ordinals deterministically compacted",
                "AABB row topology and conservative bounds",
                "Every retained shadow membership with source ordinals deterministically compacted",
                "Primary-light, probe, lighting, material-skin and ground-lighting assignments of retained rows",
                "Runtime visibility capacity and all unrelated dependencies"
            ],
            mutableFields:
            [
                "$.definition.dpvs.sModelCount and parallel row tables",
                "$.definition.cellTrees[*].aabbTrees[*].sModelIndexes",
                "$.definition.shadowGeom[*].sModelIndex",
                "$.references.staticModelDrawInsts and parallel definitions/links, including an authorized adjacent provider receiver",
                "$.references.aabbTreeSModelIndexPointers (rebuilt from typed AABB rows)"
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
            "Exact-pair static-model removal and Clip tree reindex",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "ColMap owner identity, serialized type, collision geometry, dynamic entities, stages, MapEnts and checksum",
                "Every retained ClipStaticModel row and XModel link in original relative order",
                "SModelAabbNode row topology, conservative bounds and nonempty leaf ownership",
                "All unrelated dependencies and the imported serialized IsInUse root value"
            ],
            mutableFields:
            [
                "$.definition.numStaticModels and staticModelList",
                "$.definition.smodelNodes[*].firstChild/childCount",
                "$.references.staticModels and staticModelLinks",
                "$.linkerProvenance owner-local direct aliases (rebuilt from typed collision arrays)"
            ]);
    }

    public StaticModelRemovalPatchCandidate Prepare(
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
            return Invalid(
                "The compiled map bundle has no detached GfxMap baseline.");
        }
        if (!TryGetCollisionBaseline(
                bundle,
                out MapAssetKind collisionKind,
                out ClipMapBuildData? clipBaseline) ||
            clipBaseline is null)
        {
            return Invalid(
                "The compiled map bundle must have exactly one detached " +
                "ColMapMp or ColMapSp baseline.");
        }

        CompiledMapAssetDescriptor gfxDescriptor =
            bundle.RequireAsset(MapAssetKind.GfxMap);
        CompiledMapAssetDescriptor clipDescriptor =
            bundle.RequireAsset(collisionKind);
        StaticModelCorrespondenceCatalog catalog =
            StaticModelCompilationRelationshipResolver.Resolve(
                bundle,
                document,
                cancellationToken);
        Dictionary<SourceBindingId, CompiledSourceBinding> bindings =
            BuildBindingCatalog(sourceBindings, diagnostics);
        EditorStaticModel[] removedModels = document.StaticModels
            .Where(value =>
                value.IsImported &&
                value.CompiledDisposition ==
                StaticModelCompiledDisposition.Removed)
            .ToArray();
        var patches = new List<StaticModelRemovalPatch>();
        var consumed = new HashSet<MapObjectId>();
        foreach (EditorStaticModel render in removedModels
                     .Where(value =>
                         value.Representation ==
                         StaticModelRepresentation.Render)
                     .OrderBy(value => value.SourceOrdinal.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.TryGetByRenderObjectId(
                    render.Id,
                    out StaticModelCompilationRelationship? relationship) ||
                relationship is null)
            {
                diagnostics.Add(
                    $"Removed render static model {render.Id} has no " +
                    "ExactBundleUnique collision relationship.");
                continue;
            }
            EditorStaticModel? collision = document.StaticModels
                .SingleOrDefault(value =>
                    value.IsImported &&
                    value.Id == relationship.CollisionObjectId);
            if (collision is null ||
                collision.CompiledDisposition !=
                    StaticModelCompiledDisposition.Removed)
            {
                diagnostics.Add(
                    $"Removed render static model {render.Id} does not have " +
                    "its exact collision counterpart removed.");
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
            if (!render.HasTransform(render.ImportedTransform) ||
                !collision.HasTransform(collision.ImportedTransform))
            {
                diagnostics.Add(
                    $"Removed static-model pair {render.Id} also contains an " +
                    "authored transform.");
                continue;
            }

            StaticModelRemovalEligibilityAssessment eligibility =
                StaticModelRemovalEligibilityEvaluator.Evaluate(
                    bundle,
                    catalog,
                    relationship);
            diagnostics.AddRange(eligibility.Issues.Select(issue =>
                $"Static-model removal {render.Id} failed {issue.Kind}: " +
                issue.Detail));
            IReadOnlyList<StaticModelRemovalSourceBindingAuthority>
                bindingAuthorities;
            try
            {
                bindingAuthorities =
                    StaticModelRemovalBindingAuthorityResolver.Resolve(
                        document,
                        eligibility);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(
                    $"Static-model removal {render.Id} has invalid binding " +
                    $"authority: {exception.Message}");
                continue;
            }
            bool receiverValid = true;
            foreach (StaticModelRemovalSourceBindingAuthority receiver in
                     bindingAuthorities.Where(value =>
                         value.Role ==
                         StaticModelRemovalSourceBindingRole
                             .GfxProviderReceiverRow))
            {
                EditorStaticModel receiverModel =
                    document.GetRequiredObject<EditorStaticModel>(
                        receiver.ObjectId);
                if (receiverModel.CompiledDisposition !=
                        StaticModelCompiledDisposition.BaselinePresent ||
                    !receiverModel.HasTransform(
                        receiverModel.ImportedTransform))
                {
                    diagnostics.Add(
                        $"Static-model removal {render.Id} requires Gfx " +
                        $"provider receiver ordinal {receiver.SourceOrdinal} " +
                        "to remain baseline-present and unedited.");
                    receiverValid = false;
                }
            }
            ValidateBindings(
                bundle,
                gfxDescriptor,
                clipDescriptor,
                relationship,
                bindingAuthorities,
                bindings,
                diagnostics);
            if (!eligibility.IsPatchEligible ||
                !receiverValid ||
                !consumed.Add(render.Id) ||
                !consumed.Add(collision.Id))
            {
                continue;
            }
            patches.Add(new(
                eligibility,
                bindingAuthorities));
        }

        foreach (EditorStaticModel orphan in removedModels.Where(value =>
                     !consumed.Contains(value.Id)))
        {
            diagnostics.Add(
                $"Removed {orphan.Representation.ToString().ToLowerInvariant()} " +
                $"static model {orphan.Id} is not part of one authorized " +
                "atomic pair.");
        }

        GfxStaticModelRemovalAssessment gfxRemoval =
            GfxStaticModelRemovalAssessor.Assess(
                gfxBaseline,
                patches.Select(value => value.GfxSourceOrdinal));
        ClipStaticModelRemovalAssessment clipRemoval =
            ClipStaticModelRemovalAssessor.Assess(
                clipBaseline,
                patches.Select(value => value.ClipSourceOrdinal));
        diagnostics.AddRange(gfxRemoval.Issues.Select(issue =>
            $"Gfx batch removal failed {issue.Kind}: {issue.Detail}"));
        diagnostics.AddRange(clipRemoval.Issues.Select(issue =>
            $"Clip batch removal failed {issue.Kind}: {issue.Detail}"));

        string gfxDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                gfxBaseline,
                cancellationToken);
        string clipDigest =
            RelocationInvariantAssetSemanticDigest.Compute(
                clipBaseline,
                cancellationToken);
        GfxWorldBuildData? gfxCandidate = null;
        ClipMapBuildData? clipCandidate = null;
        if (patches.Count != 0 &&
            gfxRemoval.IsEligible &&
            clipRemoval.IsEligible)
        {
            gfxCandidate =
                gfxBaseline.WithRemovedStaticModels(gfxRemoval);
            clipCandidate =
                clipBaseline.WithRemovedStaticModels(clipRemoval);
            diagnostics.AddRange(ValidatePreservation(
                    gfxBaseline,
                    clipBaseline,
                    gfxCandidate,
                    clipCandidate,
                    patches,
                    cancellationToken)
                .Diagnostics);
        }

        if (!SameDigest(gfxBaseline, gfxDigest) ||
            !SameDigest(clipBaseline, clipDigest))
        {
            diagnostics.Add(
                "Preparing static-model removal mutated an immutable " +
                "compiled baseline.");
        }

        return new(
            gfxDescriptor,
            clipDescriptor,
            gfxBaseline,
            clipBaseline,
            gfxCandidate,
            clipCandidate,
            patches,
            gfxDigest,
            clipDigest,
            new MapPatchValidation(diagnostics));
    }

    public MapPatchValidation ValidatePreservation(
        GfxWorldBuildData gfxBaseline,
        ClipMapBuildData clipBaseline,
        GfxWorldBuildData gfxCandidate,
        ClipMapBuildData clipCandidate,
        IEnumerable<StaticModelRemovalPatch> patches,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gfxBaseline);
        ArgumentNullException.ThrowIfNull(clipBaseline);
        ArgumentNullException.ThrowIfNull(gfxCandidate);
        ArgumentNullException.ThrowIfNull(clipCandidate);
        ArgumentNullException.ThrowIfNull(patches);
        StaticModelRemovalPatch[] patchCopy = patches.ToArray();
        var diagnostics = new List<string>();
        if (patchCopy.Length == 0 ||
            patchCopy.Select(value => value.GfxSourceOrdinal)
                .Distinct().Count() != patchCopy.Length ||
            patchCopy.Select(value => value.ClipSourceOrdinal)
                .Distinct().Count() != patchCopy.Length)
        {
            diagnostics.Add(
                "Static-model removal patches must form a nonempty " +
                "one-to-one set.");
            return new MapPatchValidation(diagnostics);
        }

        GfxStaticModelRemovalAssessment expectedGfxAssessment =
            GfxStaticModelRemovalAssessor.Assess(
                gfxBaseline,
                patchCopy.Select(value => value.GfxSourceOrdinal));
        ClipStaticModelRemovalAssessment expectedClipAssessment =
            ClipStaticModelRemovalAssessor.Assess(
                clipBaseline,
                patchCopy.Select(value => value.ClipSourceOrdinal));
        if (!expectedGfxAssessment.IsEligible ||
            !expectedClipAssessment.IsEligible)
        {
            diagnostics.Add(
                "The immutable baselines no longer pass the removal " +
                "invariant group.");
            return new MapPatchValidation(diagnostics);
        }

        GfxWorldBuildData expectedGfx =
            gfxBaseline.WithRemovedStaticModels(
                expectedGfxAssessment);
        ClipMapBuildData expectedClip =
            clipBaseline.WithRemovedStaticModels(
                expectedClipAssessment);
        if (!SameSemantic(
                expectedGfx,
                gfxCandidate,
                cancellationToken))
        {
            diagnostics.Add(
                "Gfx candidate differs outside the canonical static-model " +
                "removal and ordinal rebuild.");
        }
        if (!SameSemantic(
                expectedClip,
                clipCandidate,
                cancellationToken))
        {
            diagnostics.Add(
                "ColMap candidate differs outside the canonical static-model " +
                "removal and tree reindex.");
        }
        if (!GfxNestedProviderSequence(gfxBaseline)
                .SequenceEqual(
                    GfxNestedProviderSequence(gfxCandidate)))
        {
            diagnostics.Add(
                "Gfx candidate changed the ordered nested-XModel provider " +
                "sequence instead of carrying the exact inline definition " +
                "forward.");
        }

        int expectedGfxCount = checked(
            (int)gfxBaseline.Definition.Dpvs.SModelCount -
            patchCopy.Length);
        int expectedClipCount =
            clipBaseline.Definition.NumStaticModels -
            patchCopy.Length;
        if (gfxCandidate.Definition.Dpvs.SModelCount !=
                expectedGfxCount ||
            gfxCandidate.Definition.Dpvs.SModelDrawInsts.Count !=
                expectedGfxCount ||
            gfxCandidate.Definition.Dpvs.SModelInsts.Count !=
                expectedGfxCount)
        {
            diagnostics.Add(
                "Gfx candidate does not have the exact compacted static-model " +
                "cardinality.");
        }
        if (clipCandidate.Definition.NumStaticModels !=
                expectedClipCount ||
            clipCandidate.Definition.StaticModelList.Count !=
                expectedClipCount ||
            clipCandidate.Definition.SModelNodeCount !=
                clipBaseline.Definition.SModelNodeCount ||
            clipCandidate.Definition.SModelNodes.Count !=
                clipBaseline.Definition.SModelNodes.Count)
        {
            diagnostics.Add(
                "ColMap candidate changed an unsupported static-model or " +
                "spatial-node cardinality.");
        }

        GfxStaticModelShadowMembershipAssessment shadows =
            GfxStaticModelShadowMembershipAssessor.Assess(
                gfxCandidate);
        diagnostics.AddRange(shadows.Issues.Select(issue =>
            $"Rebuilt shadow partition failed {issue.Kind}: " +
            issue.Detail));
        ValidateCandidateSpatialTrees(
            gfxCandidate,
            clipCandidate,
            diagnostics);
        diagnostics.AddRange(GfxEmitter.Validate(gfxCandidate).Select(value =>
            $"GfxMap emitter validation failed at {value.Path}: " +
            value.Message));
        diagnostics.AddRange(
            new ClipMapBodyEmitter(clipCandidate.SerializedType)
                .Validate(clipCandidate)
                .Select(value =>
                    $"ColMap emitter validation failed at {value.Path}: " +
                    value.Message));
        return new MapPatchValidation(diagnostics);
    }

    public void ApplyValidatedGfxCandidate(
        GfxWorldDraft draft,
        StaticModelRemovalPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValid(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.GfxBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged GfxMap draft no longer matches the exact " +
                "static-model removal baseline.");
        }
        draft.Replace(candidate.GfxBuildData!);
    }

    public void ApplyValidatedCollisionCandidate(
        ClipMapDraft draft,
        StaticModelRemovalPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValid(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.ClipBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged ColMap draft no longer matches the exact " +
                "static-model removal baseline.");
        }
        draft.Replace(candidate.ClipBuildData!);
    }

    private static void ValidateCandidateSpatialTrees(
        GfxWorldBuildData gfx,
        ClipMapBuildData clip,
        ICollection<string> diagnostics)
    {
        if (gfx.Definition.Dpvs.SModelDrawInsts.Count != 0)
        {
            GfxStaticModelDrawInst row =
                gfx.Definition.Dpvs.SModelDrawInsts[0];
            GfxStaticModelTranslationSpatialAssessment spatial =
                GfxStaticModelTranslationSpatialAssessor.Assess(
                    gfx,
                    new StaticModelTranslationEdit(
                        0,
                        row.Placement.Origin[0],
                        row.Placement.Origin[1],
                        row.Placement.Origin[2]));
            foreach (GfxStaticModelTranslationSpatialIssue issue in
                     spatial.Issues)
            {
                diagnostics.Add(
                    $"Rebuilt Gfx spatial tree failed {issue.Kind}: " +
                    issue.Detail);
            }
        }
        if (clip.Definition.StaticModelList.Count != 0)
        {
            ClipStaticModel row =
                clip.Definition.StaticModelList[0];
            ClipStaticModelTranslationSpatialAssessment spatial =
                clip.AssessConservativeStaticModelTranslation(
                    new StaticModelTranslationEdit(
                        0,
                        row.Origin.X,
                        row.Origin.Y,
                        row.Origin.Z));
            foreach (ClipStaticModelTranslationSpatialIssue issue in
                     spatial.Issues)
            {
                diagnostics.Add(
                    $"Rebuilt Clip spatial tree failed {issue.Kind}: " +
                    issue.Detail);
            }
        }
    }

    private static void ValidateBindings(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor gfxDescriptor,
        CompiledMapAssetDescriptor clipDescriptor,
        StaticModelCompilationRelationship relationship,
        IEnumerable<StaticModelRemovalSourceBindingAuthority>
            bindingAuthorities,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding> catalog,
        ICollection<string> diagnostics)
    {
        StaticModelRemovalSourceBindingAuthority[] authorities =
            bindingAuthorities.ToArray();
        if (authorities.Count(value =>
                value.Role ==
                StaticModelRemovalSourceBindingRole.RemovedGfxRow) != 1 ||
            authorities.Count(value =>
                value.Role ==
                StaticModelRemovalSourceBindingRole
                    .RemovedCollisionRow) != 1 ||
            authorities.Single(value =>
                    value.Role ==
                    StaticModelRemovalSourceBindingRole.RemovedGfxRow)
                .SourceOrdinal != relationship.GfxSourceOrdinal ||
            authorities.Single(value =>
                    value.Role ==
                    StaticModelRemovalSourceBindingRole
                        .RemovedCollisionRow)
                .SourceOrdinal != relationship.ClipSourceOrdinal)
        {
            diagnostics.Add(
                "Static-model removal binding roles do not match the exact " +
                "Gfx/Clip relationship.");
            return;
        }

        foreach (StaticModelRemovalSourceBindingAuthority authority in
                 authorities)
        {
            CompiledMapAssetDescriptor descriptor =
                authority.Role ==
                StaticModelRemovalSourceBindingRole.RemovedCollisionRow
                    ? clipDescriptor
                    : gfxDescriptor;
            string expectedPath =
                authority.Role ==
                StaticModelRemovalSourceBindingRole.RemovedCollisionRow
                    ? "$.definition.staticModelList" +
                      $"[{authority.SourceOrdinal}]"
                    : "$.definition.dpvs.sModelDrawInsts" +
                      $"[{authority.SourceOrdinal}]";
            if (!catalog.TryGetValue(
                    authority.SourceBinding,
                    out CompiledSourceBinding? binding))
            {
                diagnostics.Add(
                    $"Static-model removal role {authority.Role} has no " +
                    "compiled source-binding catalog entry.");
                continue;
            }
            SourceBindingId expectedId =
                DeterministicMapIdentity.Binding(
                    bundle.MapIdentity,
                    descriptor.SerializedType.ToString(),
                    descriptor.AssetName,
                    expectedPath,
                    authority.SourceOrdinal);
            if (binding.Id != expectedId ||
                binding.AssetType != descriptor.SerializedType ||
                binding.OwnerRow != descriptor.OwnerRow ||
                binding.SourceOrdinal != authority.SourceOrdinal ||
                !string.Equals(
                    binding.FieldPath,
                    expectedPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    binding.BaselineDigest,
                    descriptor.BaselineDigest,
                    StringComparison.Ordinal) ||
                binding.Provenance !=
                    MapValueProvenance.ExactDecodedRuntime)
            {
                diagnostics.Add(
                    $"Static-model removal role {authority.Role} binding " +
                    $"{binding.Id} is not exact authority for " +
                    $"'{expectedPath}'.");
            }
        }
    }

    private static void RequireValid(
        StaticModelRemovalPatchCandidate candidate)
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
                "removal candidate cannot replace staged drafts.");
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
            if (binding is null ||
                !result.TryAdd(binding.Id, binding))
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

    private static StaticModelRemovalPatchCandidate Invalid(
        params string[] diagnostics) =>
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

    private static IReadOnlyList<GfxNestedProviderEvent>
        GfxNestedProviderSequence(
            GfxWorldBuildData value)
    {
        var result = new List<GfxNestedProviderEvent>();
        for (int ordinal = 0;
             ordinal <
             value.References.StaticModelDrawInstLinks.Count;
             ordinal++)
        {
            NestedXAssetBuildLink? link =
                value.References.StaticModelDrawInstLinks[ordinal];
            if (link?.SourceForm is not (
                    NestedXAssetPointerSourceForm.Inline or
                    NestedXAssetPointerSourceForm.Insert))
            {
                continue;
            }
            IXAssetBuildData? definition =
                link.IncomingDefinition ??
                (ordinal <
                    value.References
                        .StaticModelDrawInstDefinitions.Count
                    ? value.References
                        .StaticModelDrawInstDefinitions[ordinal]
                    : null);
            result.Add(new(
                link.AliasKey,
                link.SourceForm,
                definition is null
                    ? "<missing>"
                    : RelocationInvariantAssetSemanticDigest.Compute(
                        definition)));
        }
        return result;
    }

    private readonly record struct GfxNestedProviderEvent(
        string AliasKey,
        NestedXAssetPointerSourceForm SourceForm,
        string DefinitionDigest);
}
