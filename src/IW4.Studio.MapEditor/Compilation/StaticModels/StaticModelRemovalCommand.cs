using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Removes one ExactBundleUnique render/collision static-model pair from the
/// compiled projection. Undo restores the same semantic objects and IDs.
/// </summary>
public sealed class RemoveCompiledStaticModelCommand : MapEditCommand
{
    public RemoveCompiledStaticModelCommand(
        StaticModelRemovalEligibilityAssessment eligibility)
        : base(
            MapEditCommandId.New(),
            "Remove compiled render/collision static-model pair",
            MapEditKind.StaticModelCardinality,
            MapEditImpactTaxonomy.CompiledStaticModelRemoval(
                RequireEligible(eligibility)
                    .Relationship.CollisionAssetKind),
            [
                eligibility.Relationship.RenderObjectId,
                eligibility.Relationship.CollisionObjectId
            ])
    {
        Eligibility = eligibility;
    }

    public StaticModelRemovalEligibilityAssessment Eligibility { get; }
    public StaticModelCompilationRelationship Relationship =>
        Eligibility.Relationship;

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EditorStaticModel render =
            document.GetRequiredObject<EditorStaticModel>(
                Relationship.RenderObjectId);
        EditorStaticModel collision =
            document.GetRequiredObject<EditorStaticModel>(
                Relationship.CollisionObjectId);
        ValidateProjection(render, collision);
        IReadOnlyList<StaticModelRemovalSourceBindingAuthority>
            bindingAuthorities =
                StaticModelRemovalBindingAuthorityResolver.Resolve(
                    document,
                    Eligibility);
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
                throw new InvalidOperationException(
                    "The exact Gfx provider receiver must remain " +
                    "baseline-present and unedited when removal is applied.");
            }
        }
        if (render.CompiledDisposition ==
                StaticModelCompiledDisposition.Removed &&
            collision.CompiledDisposition ==
                StaticModelCompiledDisposition.Removed)
        {
            return PreparedMapEdit.NoChange(this);
        }
        if (render.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent ||
            collision.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent)
        {
            throw new InvalidOperationException(
                "Only a baseline-present static-model pair can be removed.");
        }

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                bindingAuthorities.Select(value =>
                    value.SourceBinding)),
            [
                new RemovalMutation(render, collision)
            ]);
    }

    private void ValidateProjection(
        EditorStaticModel render,
        EditorStaticModel collision)
    {
        if (!Eligibility.IsPatchEligible ||
            !render.IsImported ||
            !collision.IsImported ||
            render.Representation != StaticModelRepresentation.Render ||
            collision.Representation !=
                StaticModelRepresentation.Collision ||
            render.SourceOrdinal.Value !=
                Relationship.GfxSourceOrdinal ||
            collision.SourceOrdinal.Value !=
                Relationship.ClipSourceOrdinal ||
            !render.HasTransform(render.ImportedTransform) ||
            !collision.HasTransform(collision.ImportedTransform))
        {
            throw new InvalidOperationException(
                "The authorized exact-bundle static-model removal no longer " +
                "matches the baseline semantic pair.");
        }
    }

    private static StaticModelRemovalEligibilityAssessment RequireEligible(
        StaticModelRemovalEligibilityAssessment eligibility)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        if (!eligibility.IsPatchEligible)
        {
            throw new ArgumentException(
                "Static-model removal requires a complete eligible " +
                "cross-asset assessment.",
                nameof(eligibility));
        }
        return eligibility;
    }

    private sealed class RemovalMutation : IMapEditMutation
    {
        private readonly EditorStaticModel _render;
        private readonly EditorStaticModel _collision;

        public RemovalMutation(
            EditorStaticModel render,
            EditorStaticModel collision)
        {
            _render = render;
            _collision = collision;
        }

        public void Apply() =>
            Transition(
                StaticModelCompiledDisposition.BaselinePresent,
                StaticModelCompiledDisposition.Removed,
                "apply");

        public void Revert() =>
            Transition(
                StaticModelCompiledDisposition.Removed,
                StaticModelCompiledDisposition.BaselinePresent,
                "revert");

        private void Transition(
            StaticModelCompiledDisposition expected,
            StaticModelCompiledDisposition replacement,
            string operation)
        {
            if (!_render.HasCompiledDisposition(expected) ||
                !_collision.HasCompiledDisposition(expected))
            {
                throw new InvalidOperationException(
                    $"Cannot {operation} static-model removal because the " +
                    "paired semantic state changed outside the command " +
                    "journal.");
            }

            _render.SetCompiledDisposition(replacement);
            _collision.SetCompiledDisposition(replacement);
        }
    }
}
