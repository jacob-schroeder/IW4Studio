using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Atomically creates one authored render/collision pair from an exact,
/// eligible imported pair. The authored rows have local identities; only the
/// two imported template-record bindings enter the compiled edit journal.
/// </summary>
public sealed class DuplicateCompiledStaticModelCommand : MapEditCommand
{
    private readonly AuthoredBindingSet _authoredBindings;

    public DuplicateCompiledStaticModelCommand(
        StaticModelDuplicationEligibilityAssessment assessment)
        : this(
            MapEditCommandId.New(),
            RequireEligible(assessment),
            CreatePairSeed(assessment))
    {
    }

    private DuplicateCompiledStaticModelCommand(
        MapEditCommandId id,
        StaticModelDuplicationEligibilityAssessment assessment,
        PairSeed seed)
        : base(
            id,
            "Duplicate compiled render/collision static-model pair",
            MapEditKind.StaticModelDuplication,
            MapEditImpactTaxonomy.CompiledStaticModelDuplication(
                assessment.Relationship.CollisionAssetKind),
            [
                seed.Pair.RenderObjectId,
                seed.Pair.CollisionObjectId
            ])
    {
        Eligibility = assessment;
        PairState = seed.Pair;
        _authoredBindings = seed.Bindings;
    }

    public StaticModelDuplicationEligibilityAssessment Eligibility { get; }
    public AuthoredStaticModelDuplicatePairState PairState { get; }
    public MapObjectId RenderObjectId => PairState.RenderObjectId;
    public MapObjectId CollisionObjectId => PairState.CollisionObjectId;

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id != Eligibility.DocumentId)
        {
            throw new InvalidOperationException(
                "The static-model duplication proof belongs to a different " +
                "semantic document.");
        }
        if (document.StaticModels.Any(value => !value.IsImported))
        {
            throw new InvalidOperationException(
                "The constrained duplication boundary supports one authored " +
                "render/collision pair per imported document.");
        }
        if (document.TryGetObject(RenderObjectId, out _) ||
            document.TryGetObject(CollisionObjectId, out _))
        {
            throw new InvalidOperationException(
                "An authored static-model identity already exists in the " +
                "document.");
        }

        StaticModelCompilationRelationship relationship =
            Eligibility.Relationship;
        EditorStaticModel renderTemplate =
            document.GetRequiredObject<EditorStaticModel>(
                relationship.RenderObjectId);
        EditorStaticModel collisionTemplate =
            document.GetRequiredObject<EditorStaticModel>(
                relationship.CollisionObjectId);
        ValidateTemplate(
            renderTemplate,
            StaticModelRepresentation.Render,
            relationship.GfxSourceOrdinal,
            PairState.GfxTemplateRecordBinding);
        ValidateTemplate(
            collisionTemplate,
            StaticModelRepresentation.Collision,
            relationship.ClipSourceOrdinal,
            PairState.ClipTemplateRecordBinding);
        if (PairState.BundleBaselineDigest !=
                Eligibility.BundleBaselineDigest ||
            PairState.Destination != Eligibility.DestinationOrigin ||
            PairState.GfxProjectedOrdinal !=
                Eligibility.Gfx!.NewOrdinal ||
            PairState.ClipProjectedOrdinal !=
                Eligibility.Collision!.NewOrdinal ||
            PairState.GfxProjectedOrdinal !=
                document.StaticModels.Count(value =>
                    value.IsImported &&
                    value.Representation ==
                    StaticModelRepresentation.Render))
        {
            throw new InvalidOperationException(
                "The static-model duplication proof no longer matches the " +
                "imported semantic projection.");
        }

        EditorStaticModel authoredRender =
            EditorStaticModel.CreateAuthoredDuplicate(
                StaticModelRepresentation.Render,
                PairState,
                renderTemplate,
                _authoredBindings.RenderSourceOrdinal,
                _authoredBindings.RenderModelName,
                _authoredBindings.RenderOrigin,
                _authoredBindings.RenderScale,
                _authoredBindings.RenderBounds);
        EditorStaticModel authoredCollision =
            EditorStaticModel.CreateAuthoredDuplicate(
                StaticModelRepresentation.Collision,
                PairState,
                collisionTemplate,
                _authoredBindings.CollisionSourceOrdinal,
                _authoredBindings.CollisionModelName,
                _authoredBindings.CollisionOrigin,
                _authoredBindings.CollisionScale,
                _authoredBindings.CollisionBounds);

        EditorStaticModelCollectionState before =
            document.CaptureStaticModelCollectionState();
        var after = new EditorStaticModelCollectionState(
        [
            .. before.StaticModels,
            authoredRender,
            authoredCollision
        ]);
        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                PairState.TemplateRecordBindings,
                preservationCoverageProven: true,
                hasRequiredBuilder: true),
            [
                new StaticModelCollectionMutation(
                    document,
                    before,
                    after)
            ]);
    }

    private static void ValidateTemplate(
        EditorStaticModel template,
        StaticModelRepresentation representation,
        int sourceOrdinal,
        SourceBindingId templateRecordBinding)
    {
        if (!template.IsImported ||
            template.Representation != representation ||
            template.SourceOrdinal.Value != sourceOrdinal ||
            template.SourceOrdinal.SourceBinding != templateRecordBinding ||
            template.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent ||
            !template.HasTransform(template.ImportedTransform))
        {
            throw new InvalidOperationException(
                $"The {representation.ToString().ToLowerInvariant()} " +
                "duplication template is no longer the exact, unedited " +
                "imported record authorized by the assessment.");
        }
    }

    private static StaticModelDuplicationEligibilityAssessment
        RequireEligible(
            StaticModelDuplicationEligibilityAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.IsPatchEligible ||
            assessment.Gfx is not { IsEligible: true } ||
            assessment.Collision is not { IsEligible: true } ||
            assessment.GfxTemplateRecordBinding is null ||
            assessment.ClipTemplateRecordBinding is null)
        {
            throw new ArgumentException(
                "Static-model duplication requires complete semantic, " +
                "destination, alias, Gfx, and collision eligibility.",
                nameof(assessment));
        }
        return assessment;
    }

    private static PairSeed CreatePairSeed(
        StaticModelDuplicationEligibilityAssessment assessment)
    {
        assessment = RequireEligible(assessment);
        StaticModelCompilationRelationship relationship =
            assessment.Relationship;
        var usedObjectIds = new HashSet<MapObjectId>
        {
            relationship.RenderObjectId,
            relationship.CollisionObjectId
        };
        MapObjectId renderObjectId = NextObjectId(usedObjectIds);
        MapObjectId collisionObjectId = NextObjectId(usedObjectIds);

        SourceBindingId gfxTemplateBinding =
            assessment.GfxTemplateRecordBinding!.Value;
        SourceBindingId clipTemplateBinding =
            assessment.ClipTemplateRecordBinding!.Value;
        var usedBindings = new HashSet<SourceBindingId>
        {
            gfxTemplateBinding,
            clipTemplateBinding
        };
        var bindings = new AuthoredBindingSet(
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings),
            NextBinding(usedBindings));
        var pair = new AuthoredStaticModelDuplicatePairState(
            StaticModelDuplicationOperationId.New(),
            renderObjectId,
            collisionObjectId,
            relationship.RenderObjectId,
            relationship.CollisionObjectId,
            relationship.GfxSourceOrdinal,
            relationship.ClipSourceOrdinal,
            assessment.Gfx!.NewOrdinal,
            assessment.Collision!.NewOrdinal,
            assessment.DestinationOrigin,
            relationship.CollisionAssetKind,
            assessment.BundleBaselineDigest,
            gfxTemplateBinding,
            clipTemplateBinding);
        return new(pair, bindings);
    }

    private static MapObjectId NextObjectId(
        ISet<MapObjectId> used)
    {
        while (true)
        {
            var candidate = new MapObjectId(Guid.NewGuid());
            if (used.Add(candidate))
                return candidate;
        }
    }

    private static SourceBindingId NextBinding(
        ISet<SourceBindingId> used)
    {
        while (true)
        {
            var candidate = new SourceBindingId(Guid.NewGuid());
            if (used.Add(candidate))
                return candidate;
        }
    }

    private sealed record PairSeed(
        AuthoredStaticModelDuplicatePairState Pair,
        AuthoredBindingSet Bindings);

    private sealed record AuthoredBindingSet(
        SourceBindingId RenderSourceOrdinal,
        SourceBindingId RenderModelName,
        SourceBindingId RenderOrigin,
        SourceBindingId RenderScale,
        SourceBindingId RenderBounds,
        SourceBindingId CollisionSourceOrdinal,
        SourceBindingId CollisionModelName,
        SourceBindingId CollisionOrigin,
        SourceBindingId CollisionScale,
        SourceBindingId CollisionBounds);

    private sealed class StaticModelCollectionMutation : IMapEditMutation
    {
        private readonly EditorMapDocument _document;
        private readonly EditorStaticModelCollectionState _before;
        private readonly EditorStaticModelCollectionState _after;

        public StaticModelCollectionMutation(
            EditorMapDocument document,
            EditorStaticModelCollectionState before,
            EditorStaticModelCollectionState after)
        {
            _document =
                document ?? throw new ArgumentNullException(nameof(document));
            _before =
                before ?? throw new ArgumentNullException(nameof(before));
            _after =
                after ?? throw new ArgumentNullException(nameof(after));
        }

        public void Apply() =>
            _document.ApplyStaticModelCollectionState(_before, _after);

        public void Revert() =>
            _document.ApplyStaticModelCollectionState(_after, _before);
    }
}
