using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
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
/// One constrained authored pair and the freshly evaluated compiled
/// authorization that produces it. Only the two imported template records
/// are journal authority; authored destination rows never masquerade as
/// imported bindings.
/// </summary>
public sealed class StaticModelDuplicationPatch
{
    internal StaticModelDuplicationPatch(
        AuthoredStaticModelDuplicatePairState authoredPair,
        StaticModelDuplicationEligibilityAssessment eligibility)
    {
        AuthoredPair = authoredPair ??
            throw new ArgumentNullException(nameof(authoredPair));
        Eligibility = eligibility ??
            throw new ArgumentNullException(nameof(eligibility));
        if (!eligibility.IsPatchEligible ||
            eligibility.Gfx is null ||
            eligibility.Collision is null)
        {
            throw new ArgumentException(
                "A static-model duplication patch requires complete typed " +
                "Gfx and collision eligibility.",
                nameof(eligibility));
        }
        if (authoredPair.GfxTemplateOrdinal !=
                eligibility.Relationship.GfxSourceOrdinal ||
            authoredPair.ClipTemplateOrdinal !=
                eligibility.Relationship.ClipSourceOrdinal ||
            authoredPair.Destination != eligibility.DestinationOrigin ||
            authoredPair.CollisionAssetKind !=
                eligibility.Relationship.CollisionAssetKind ||
            authoredPair.GfxProjectedOrdinal !=
                eligibility.Gfx.NewOrdinal ||
            authoredPair.ClipProjectedOrdinal !=
                eligibility.Collision.NewOrdinal)
        {
            throw new ArgumentException(
                "The authored duplicate state does not match its freshly " +
                "evaluated compiled authorization.",
                nameof(authoredPair));
        }

        SourceBindings = new ReadOnlyCollection<SourceBindingId>(
            authoredPair.TemplateRecordBindings.ToArray());
    }

    public AuthoredStaticModelDuplicatePairState AuthoredPair { get; }
    public StaticModelDuplicationEligibilityAssessment Eligibility { get; }
    public StaticModelCompilationRelationship Relationship =>
        Eligibility.Relationship;
    public IReadOnlyList<SourceBindingId> SourceBindings { get; }
}

internal sealed class StaticModelDuplicationPatchCandidate
{
    public StaticModelDuplicationPatchCandidate(
        CompiledMapAssetDescriptor? gfxDescriptor,
        CompiledMapAssetDescriptor? clipDescriptor,
        GfxWorldBuildData? gfxBaseline,
        ClipMapBuildData? clipBaseline,
        GfxWorldBuildData? gfxBuildData,
        ClipMapBuildData? clipBuildData,
        IEnumerable<StaticModelDuplicationPatch> patches,
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
        Patches = new ReadOnlyCollection<StaticModelDuplicationPatch>(
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
    public IReadOnlyList<StaticModelDuplicationPatch> Patches { get; }
    public string? GfxBaselineSemanticDigest { get; }
    public string? ClipBaselineSemanticDigest { get; }
    public MapPatchValidation Validation { get; }
}

/// <summary>
/// Compiles exactly one authored duplicate pair through the typed Gfx and
/// Clip cardinality builders. It re-derives every relationship, destination,
/// dependency, spatial, lighting, and shadow proof from immutable baselines
/// immediately before candidate construction.
/// </summary>
internal sealed class StaticModelDuplicationPatcher
{
    private static readonly GfxWorldBodyEmitter GfxEmitter = new();

    public static MapPreservationCoverage GfxPreservationCoverage { get; } =
        new(
            MapAssetKind.GfxMap,
            "Constrained static-model duplication and typed ordinal rebuild",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "GfxWorld identity, root scalars, surfaces, cells, portals, DPVS planes and checksums",
                "Every imported static-model draw/instance/reference row at its original ordinal",
                "Nested XModel provider sequence and every unrelated dependency",
                "Existing AABB and shadow memberships, primary-light rows and conservative envelopes",
                "Existing visibility, probe, material-skin, ground-lighting and baked-lighting assignments"
            ],
            mutableFields:
            [
                "$.definition.dpvs.sModelCount and appended draw/instance row",
                "$.definition.cellTrees[*].aabbTrees[*].sModelIndexes",
                "$.definition.shadowGeom[*].sModelIndex",
                "$.references.staticModelDrawInsts and parallel definitions/links",
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
            "Constrained static-model duplication and Clip tree reindex",
            MapPreservationCoverageStatus.Proven,
            preservedFields:
            [
                "ColMap identity, serialized type, collision geometry, dynamic entities, stages, MapEnts and checksum",
                "Every imported ClipStaticModel and XModel link in original relative order",
                "SModelAabbNode topology and all unrelated dependencies",
                "Imported serialized IsInUse root value and all unrelated semantic collision/reference arrays"
            ],
            mutableFields:
            [
                "$.definition.numStaticModels and inserted staticModelList row",
                "$.definition.smodelNodes[*].firstChild/childCount/bounds",
                "$.references.staticModels and staticModelLinks",
                "$.linkerProvenance owner-local direct aliases and imported planes/leaf-brush/partition pointer raws (canonicalized from typed collision arrays)"
            ]);
    }

    public StaticModelDuplicationPatchCandidate Prepare(
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
        Dictionary<SourceBindingId, CompiledSourceBinding> bindings =
            BuildBindingCatalog(sourceBindings, diagnostics);
        StaticModelCorrespondenceCatalog catalog =
            StaticModelCompilationRelationshipResolver.Resolve(
                bundle,
                document,
                cancellationToken);

        EditorStaticModel[] authored = document.StaticModels
            .Where(value =>
                value.LineageKind ==
                StaticModelLineageKind.AuthoredDuplicate)
            .ToArray();
        AuthoredStaticModelDuplicatePairState[] states = authored
            .Select(value => value.AuthoredDuplicatePair)
            .Where(value => value is not null)
            .Cast<AuthoredStaticModelDuplicatePairState>()
            .Distinct()
            .ToArray();
        var patches = new List<StaticModelDuplicationPatch>();
        if (authored.Length != 2 ||
            states.Length != 1)
        {
            diagnostics.Add(
                "Static-model duplication persistence requires exactly two " +
                "authored rows sharing one duplicate-pair authority.");
        }
        else
        {
            AuthoredStaticModelDuplicatePairState state = states[0];
            ValidateSemanticPair(
                document,
                bundle,
                authored,
                state,
                collisionKind,
                catalog,
                out StaticModelCompilationRelationship? relationship,
                diagnostics);
            if (relationship is not null)
            {
                StaticModelDuplicationEligibilityAssessment eligibility =
                    StaticModelDuplicationEligibilityEvaluator.Evaluate(
                        bundle,
                        document,
                        catalog,
                        relationship,
                        state.Destination);
                diagnostics.AddRange(eligibility.Issues.Select(issue =>
                    $"Static-model duplication {state.OperationId} failed " +
                    $"{issue.Kind}: {issue.Detail}"));
                ValidateBindings(
                    bundle,
                    gfxDescriptor,
                    clipDescriptor,
                    state,
                    bindings,
                    diagnostics);
                if (eligibility.IsPatchEligible)
                {
                    try
                    {
                        patches.Add(new(state, eligibility));
                    }
                    catch (ArgumentException exception)
                    {
                        diagnostics.Add(exception.Message);
                    }
                }
            }
        }

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
        if (patches.Count == 1)
        {
            StaticModelDuplicationPatch patch = patches[0];
            gfxCandidate = gfxBaseline.WithDuplicatedStaticModel(
                patch.Eligibility.Gfx!);
            clipCandidate = clipBaseline.WithDuplicatedStaticModel(
                patch.Eligibility.Collision!);
            diagnostics.AddRange(
                ValidatePreservation(
                    gfxBaseline,
                    clipBaseline,
                    gfxCandidate,
                    clipCandidate,
                    patch,
                    cancellationToken)
                .Diagnostics);
        }

        if (!SameDigest(gfxBaseline, gfxDigest) ||
            !SameDigest(clipBaseline, clipDigest))
        {
            diagnostics.Add(
                "Preparing static-model duplication mutated an immutable " +
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
        StaticModelDuplicationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gfxBaseline);
        ArgumentNullException.ThrowIfNull(clipBaseline);
        ArgumentNullException.ThrowIfNull(gfxCandidate);
        ArgumentNullException.ThrowIfNull(clipCandidate);
        ArgumentNullException.ThrowIfNull(patch);
        var diagnostics = new List<string>();

        GfxStaticModelDuplicationAssessment gfxAssessment =
            GfxStaticModelDuplicationAssessor.Assess(
                gfxBaseline,
                patch.Eligibility.Gfx!.SpatialAssessment);
        ClipStaticModelDuplicationAssessment clipAssessment =
            ClipStaticModelDuplicationAssessor.Assess(
                clipBaseline,
                patch.Eligibility.Collision!.SpatialAssessment);
        if (!gfxAssessment.IsEligible ||
            !clipAssessment.IsEligible)
        {
            diagnostics.Add(
                "The immutable baselines no longer pass the static-model " +
                "duplication invariant group.");
            return new MapPatchValidation(diagnostics);
        }

        GfxWorldBuildData expectedGfx =
            gfxBaseline.WithDuplicatedStaticModel(gfxAssessment);
        ClipMapBuildData expectedClip =
            clipBaseline.WithDuplicatedStaticModel(clipAssessment);
        if (!SameSemantic(expectedGfx, gfxCandidate, cancellationToken))
        {
            diagnostics.Add(
                "Gfx candidate differs outside the canonical typed " +
                "static-model duplication rebuild.");
        }
        if (!SameSemantic(expectedClip, clipCandidate, cancellationToken))
        {
            diagnostics.Add(
                "ColMap candidate differs outside the canonical typed " +
                "static-model duplication rebuild.");
        }
        if (!GfxNestedProviderSequence(gfxBaseline)
                .SequenceEqual(
                    GfxNestedProviderSequence(gfxCandidate)))
        {
            diagnostics.Add(
                "Gfx candidate changed the ordered nested-XModel provider " +
                "sequence instead of retaining every imported provider.");
        }

        int expectedGfxCount = checked(
            (int)gfxBaseline.Definition.Dpvs.SModelCount + 1);
        int expectedClipCount = checked(
            clipBaseline.Definition.NumStaticModels + 1);
        if (gfxCandidate.Definition.Dpvs.SModelCount !=
                expectedGfxCount ||
            gfxCandidate.Definition.Dpvs.SModelDrawInsts.Count !=
                expectedGfxCount ||
            gfxCandidate.Definition.Dpvs.SModelInsts.Count !=
                expectedGfxCount ||
            gfxAssessment.NewOrdinal != expectedGfxCount - 1)
        {
            diagnostics.Add(
                "Gfx candidate does not have the exact appended " +
                "static-model cardinality.");
        }
        if (clipCandidate.Definition.NumStaticModels !=
                expectedClipCount ||
            clipCandidate.Definition.StaticModelList.Count !=
                expectedClipCount ||
            clipCandidate.Definition.SModelNodeCount !=
                clipBaseline.Definition.SModelNodeCount ||
            clipCandidate.Definition.SModelNodes.Count !=
                clipBaseline.Definition.SModelNodes.Count ||
            clipAssessment.NewOrdinal !=
                clipAssessment.SourceOrdinal + 1)
        {
            diagnostics.Add(
                "ColMap candidate changed an unsupported static-model or " +
                "spatial-node cardinality.");
        }

        ValidateGfxDependencyReuse(
            gfxCandidate.References,
            gfxAssessment.SourceOrdinal,
            gfxAssessment.NewOrdinal,
            diagnostics);
        ValidateDefinitionFreePackedAlias(
            clipCandidate.References.StaticModelLinks,
            clipCandidate.References.StaticModels,
            clipAssessment.SourceOrdinal,
            clipAssessment.NewOrdinal,
            "ColMap",
            diagnostics);
        if (clipCandidate.LinkerProvenance.ImportedIsInUse !=
                clipBaseline.LinkerProvenance.ImportedIsInUse)
        {
            diagnostics.Add(
                "ColMap candidate did not preserve the imported serialized " +
                "IsInUse root value.");
        }
        if (clipCandidate.LinkerProvenance.ImportedPlanesPackedRaw is not null ||
            clipCandidate.LinkerProvenance
                .LeafBrushNodeBrushesPointerRaws.Count != 0 ||
            clipCandidate.LinkerProvenance
                .PartitionBordersPointerRaws.Count != 0)
        {
            diagnostics.Add(
                "ColMap candidate retained owner-local imported direct " +
                "pointer raws instead of canonicalizing typed linker " +
                "provenance.");
        }
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
        StaticModelDuplicationPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValid(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.GfxBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged GfxMap draft no longer matches the exact " +
                "static-model duplication baseline.");
        }
        draft.Replace(candidate.GfxBuildData!);
    }

    public void ApplyValidatedCollisionCandidate(
        ClipMapDraft draft,
        StaticModelDuplicationPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RequireValid(candidate);
        if (!SameDigest(
                draft.Data,
                candidate.ClipBaselineSemanticDigest!))
        {
            throw new InvalidOperationException(
                "The staged ColMap draft no longer matches the exact " +
                "static-model duplication baseline.");
        }
        draft.Replace(candidate.ClipBuildData!);
    }

    private static void ValidateSemanticPair(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        IReadOnlyList<EditorStaticModel> authored,
        AuthoredStaticModelDuplicatePairState state,
        MapAssetKind collisionKind,
        StaticModelCorrespondenceCatalog catalog,
        out StaticModelCompilationRelationship? relationship,
        ICollection<string> diagnostics)
    {
        relationship = null;
        EditorStaticModel? render = authored.SingleOrDefault(value =>
            value.Id == state.RenderObjectId &&
            value.Representation == StaticModelRepresentation.Render);
        EditorStaticModel? collision = authored.SingleOrDefault(value =>
            value.Id == state.CollisionObjectId &&
            value.Representation == StaticModelRepresentation.Collision);
        if (render is null ||
            collision is null ||
            !ReferenceEquals(render.AuthoredDuplicatePair, state) ||
            !ReferenceEquals(collision.AuthoredDuplicatePair, state) ||
            render.CompiledDisposition !=
                StaticModelCompiledDisposition.AuthoredPending ||
            collision.CompiledDisposition !=
                StaticModelCompiledDisposition.AuthoredPending ||
            render.Origin.Value != state.Destination ||
            collision.Origin.Value != state.Destination)
        {
            diagnostics.Add(
                "The authored duplicate rows do not exactly match their " +
                "shared pending pair authority.");
            return;
        }
        if (state.CollisionAssetKind != collisionKind ||
            !string.Equals(
                state.BundleBaselineDigest,
                bundle.BaselineDigest,
                StringComparison.Ordinal))
        {
            diagnostics.Add(
                "The authored duplicate pair is not owned by this exact " +
                "Gfx/Col compiled baseline.");
            return;
        }

        EditorStaticModel renderTemplate;
        EditorStaticModel collisionTemplate;
        try
        {
            renderTemplate =
                document.GetRequiredObject<EditorStaticModel>(
                    state.RenderTemplateObjectId);
            collisionTemplate =
                document.GetRequiredObject<EditorStaticModel>(
                    state.CollisionTemplateObjectId);
        }
        catch (Exception exception)
            when (exception is KeyNotFoundException or
                  InvalidOperationException)
        {
            diagnostics.Add(
                "The authored duplicate pair lost an imported template " +
                $"object: {exception.Message}");
            return;
        }
        if (!renderTemplate.IsImported ||
            !collisionTemplate.IsImported ||
            renderTemplate.Representation !=
                StaticModelRepresentation.Render ||
            collisionTemplate.Representation !=
                StaticModelRepresentation.Collision ||
            renderTemplate.SourceOrdinal.Value !=
                state.GfxTemplateOrdinal ||
            collisionTemplate.SourceOrdinal.Value !=
                state.ClipTemplateOrdinal ||
            renderTemplate.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent ||
            collisionTemplate.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent ||
            !renderTemplate.HasTransform(renderTemplate.ImportedTransform) ||
            !collisionTemplate.HasTransform(
                collisionTemplate.ImportedTransform))
        {
            diagnostics.Add(
                "Static-model duplication templates must remain exact, " +
                "imported, baseline-present, and otherwise unedited.");
            return;
        }

        if (!catalog.TryGetByRenderObjectId(
                renderTemplate.Id,
                out relationship) ||
            relationship is null ||
            relationship.CollisionObjectId != collisionTemplate.Id ||
            relationship.GfxSourceOrdinal != state.GfxTemplateOrdinal ||
            relationship.ClipSourceOrdinal != state.ClipTemplateOrdinal ||
            relationship.CollisionAssetKind != state.CollisionAssetKind)
        {
            diagnostics.Add(
                "The authored duplicate templates no longer resolve to one " +
                "ExactBundleUnique compiled relationship.");
            relationship = null;
        }
    }

    private static void ValidateBindings(
        CompiledMapBundle bundle,
        CompiledMapAssetDescriptor gfxDescriptor,
        CompiledMapAssetDescriptor clipDescriptor,
        AuthoredStaticModelDuplicatePairState state,
        IReadOnlyDictionary<SourceBindingId, CompiledSourceBinding> catalog,
        ICollection<string> diagnostics)
    {
        Validate(
            state.GfxTemplateRecordBinding,
            gfxDescriptor,
            state.GfxTemplateOrdinal,
            "$.definition.dpvs.sModelDrawInsts" +
            $"[{state.GfxTemplateOrdinal}]",
            "Gfx");
        Validate(
            state.ClipTemplateRecordBinding,
            clipDescriptor,
            state.ClipTemplateOrdinal,
            "$.definition.staticModelList" +
            $"[{state.ClipTemplateOrdinal}]",
            "Col");
        return;

        void Validate(
            SourceBindingId id,
            CompiledMapAssetDescriptor descriptor,
            int ordinal,
            string expectedPath,
            string role)
        {
            if (!catalog.TryGetValue(
                    id,
                    out CompiledSourceBinding? binding))
            {
                diagnostics.Add(
                    $"Static-model duplication {role} template has no " +
                    "compiled source-binding catalog entry.");
                return;
            }
            SourceBindingId expectedId =
                DeterministicMapIdentity.Binding(
                    bundle.MapIdentity,
                    descriptor.SerializedType.ToString(),
                    descriptor.AssetName,
                    expectedPath,
                    ordinal);
            if (binding.Id != expectedId ||
                binding.AssetType != descriptor.SerializedType ||
                binding.OwnerRow != descriptor.OwnerRow ||
                binding.SourceOrdinal != ordinal ||
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
                    $"Static-model duplication {role} binding {binding.Id} " +
                    $"is not exact imported template authority for " +
                    $"'{expectedPath}'.");
            }
        }
    }

    private static void ValidateGfxDependencyReuse(
        GfxWorldReferenceBuildData references,
        int sourceOrdinal,
        int newOrdinal,
        ICollection<string> diagnostics)
    {
        IReadOnlyList<NestedXAssetBuildLink?> links =
            references.StaticModelDrawInstLinks;
        IReadOnlyList<SymbolicXAssetReference?> identities =
            references.StaticModelDrawInsts;
        IReadOnlyList<IXAssetBuildData?> definitions =
            references.StaticModelDrawInstDefinitions;
        if (sourceOrdinal < 0 ||
            newOrdinal < 0 ||
            sourceOrdinal >= links.Count ||
            newOrdinal >= links.Count ||
            sourceOrdinal >= identities.Count ||
            newOrdinal >= identities.Count ||
            sourceOrdinal >= definitions.Count ||
            newOrdinal >= definitions.Count)
        {
            diagnostics.Add(
                "Gfx duplicated XModel dependency rows are outside the " +
                "candidate reference tables.");
            return;
        }

        NestedXAssetBuildLink? source = links[sourceOrdinal];
        NestedXAssetBuildLink? duplicate = links[newOrdinal];
        SymbolicXAssetReference? identity =
            identities[sourceOrdinal];
        IXAssetBuildData? sourceDefinition =
            definitions[sourceOrdinal];
        bool sharedIdentity =
            identity is
            {
                AssetType: XAssetType.XModel,
                IsExternalReference: true
            } &&
            identities[newOrdinal] == identity &&
            source is not null &&
            duplicate is not null &&
            source.Reference == identity &&
            duplicate.Reference == identity &&
            string.Equals(
                source.AliasKey,
                duplicate.AliasKey,
                StringComparison.Ordinal);
        bool duplicateIsDefinitionFreeAlias =
            duplicate is
            {
                SourceForm: NestedXAssetPointerSourceForm.PackedAlias,
                IncomingDefinition: null,
                ImportedOwnerCellRaw: null
            } &&
            definitions[newOrdinal] is null;
        bool sourceIsPackedAlias =
            sourceDefinition is null &&
            source is
            {
                SourceForm: NestedXAssetPointerSourceForm.PackedAlias,
                IncomingDefinition: null
            } &&
            IsOffsetOrNull(source.ImportedPackedRaw) &&
            IsOffsetOrNull(source.ImportedOwnerCellRaw) &&
            duplicate?.ImportedPackedRaw ==
                source.ImportedPackedRaw;
        bool sourceIsExactInlineProvider =
            sourceDefinition is not null &&
            identity is not null &&
            HasSameXModelIdentity(sourceDefinition, identity) &&
            source is
            {
                SourceForm: NestedXAssetPointerSourceForm.Inline,
                IncomingDefinition: not null,
                ImportedPackedRaw: null,
                ImportedOwnerCellRaw: { } ownerCellRaw
            } &&
            ReferenceEquals(
                sourceDefinition,
                source.IncomingDefinition) &&
            XPointerCodec.GetType(ownerCellRaw) ==
                PointerType.Offset &&
            duplicate?.ImportedPackedRaw is null;
        if (!(sharedIdentity &&
              duplicateIsDefinitionFreeAlias &&
              (sourceIsPackedAlias ||
               sourceIsExactInlineProvider)))
        {
            diagnostics.Add(
                "Gfx duplication did not retain one exact packed-alias or " +
                "Inline-provider XModel source and append its " +
                "definition-free PackedAlias consumer.");
        }
    }

    private static void ValidateDefinitionFreePackedAlias(
        IReadOnlyList<NestedXAssetBuildLink?> links,
        IReadOnlyList<SymbolicXAssetReference?> references,
        int sourceOrdinal,
        int newOrdinal,
        string role,
        ICollection<string> diagnostics)
    {
        if (sourceOrdinal < 0 ||
            newOrdinal < 0 ||
            sourceOrdinal >= links.Count ||
            newOrdinal >= links.Count ||
            sourceOrdinal >= references.Count ||
            newOrdinal >= references.Count)
        {
            diagnostics.Add(
                $"{role} duplicated XModel dependency rows are outside the " +
                "candidate reference tables.");
            return;
        }
        NestedXAssetBuildLink? source = links[sourceOrdinal];
        NestedXAssetBuildLink? duplicate = links[newOrdinal];
        if (source is null ||
            duplicate is null ||
            source.SourceForm !=
                NestedXAssetPointerSourceForm.PackedAlias ||
            duplicate.SourceForm !=
                NestedXAssetPointerSourceForm.PackedAlias ||
            source.IncomingDefinition is not null ||
            duplicate.IncomingDefinition is not null ||
            !string.Equals(
                source.AliasKey,
                duplicate.AliasKey,
                StringComparison.Ordinal) ||
            references[sourceOrdinal] != references[newOrdinal])
        {
            diagnostics.Add(
                $"{role} duplication did not preserve one definition-free " +
                "PackedAlias XModel identity.");
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

    private static bool IsOffsetOrNull(int? raw) =>
        raw is not { } value ||
        XPointerCodec.GetType(value) == PointerType.Offset;

    private static void RequireValid(
        StaticModelDuplicationPatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!GfxPreservationCoverage.IsProven ||
            candidate.ClipDescriptor is null ||
            !CollisionPreservationCoverage(
                candidate.ClipDescriptor.Kind).IsProven ||
            !candidate.Validation.IsValid ||
            candidate.Patches.Count != 1 ||
            candidate.GfxBuildData is null ||
            candidate.ClipBuildData is null)
        {
            throw new InvalidOperationException(
                "An invalid, non-singular, or coverage-incomplete " +
                "static-model duplication candidate cannot replace staged " +
                "drafts.");
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

    private static StaticModelDuplicationPatchCandidate Invalid(
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
