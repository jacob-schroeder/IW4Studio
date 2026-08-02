using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Suppresses one artifact-local, mutually unique Gfx/Col static-model pair.
/// This is deliberately a separate operation from editor visibility and
/// static-model transformation.
/// </summary>
public sealed class SuppressCompiledStaticModelCommand : MapEditCommand
{
    public SuppressCompiledStaticModelCommand(
        StaticModelCompilationRelationship relationship)
        : this(MapEditCommandId.New(), relationship)
    {
    }

    internal SuppressCompiledStaticModelCommand(
        MapEditCommandId id,
        StaticModelCompilationRelationship relationship)
        : base(
            id,
            "Suppress compiled render/collision static-model pair",
            MapEditKind.StaticModelVisibility,
            MapEditImpactTaxonomy.StaticModelSuppression(
                relationship?.CollisionAssetKind ??
                throw new ArgumentNullException(nameof(relationship))),
            [
                relationship.RenderObjectId,
                relationship.CollisionObjectId
            ])
    {
        Relationship = relationship;
    }

    public StaticModelCompilationRelationship Relationship { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EditorStaticModel render =
            document.GetRequiredObject<EditorStaticModel>(
                Relationship.RenderObjectId);
        EditorStaticModel collision =
            document.GetRequiredObject<EditorStaticModel>(
                Relationship.CollisionObjectId);

        ValidateRelationshipProjection(render, collision);

        StaticModelCompiledDisposition renderDisposition =
            render.CompiledDisposition;
        StaticModelCompiledDisposition collisionDisposition =
            collision.CompiledDisposition;
        if (renderDisposition == StaticModelCompiledDisposition.Suppressed &&
            collisionDisposition ==
                StaticModelCompiledDisposition.Suppressed)
        {
            return PreparedMapEdit.NoChange(this);
        }
        if (renderDisposition !=
            StaticModelCompiledDisposition.BaselinePresent ||
            collisionDisposition !=
            StaticModelCompiledDisposition.BaselinePresent)
        {
            throw new InvalidOperationException(
                "The render/collision pair has inconsistent compiled " +
                "disposition and cannot be suppressed.");
        }

        SourceBindingId[] bindings =
            GetMutableSuppressionBindings(render, collision);
        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                bindings),
            [
                new StaticModelSuppressionMutation(
                    render,
                    collision)
            ]);
    }

    private void ValidateRelationshipProjection(
        EditorStaticModel render,
        EditorStaticModel collision)
    {
        if (!render.IsImported ||
            !collision.IsImported ||
            render.Representation != StaticModelRepresentation.Render ||
            collision.Representation !=
                StaticModelRepresentation.Collision ||
            render.SourceOrdinal.Value !=
                Relationship.GfxSourceOrdinal ||
            collision.SourceOrdinal.Value !=
                Relationship.ClipSourceOrdinal)
        {
            throw new InvalidOperationException(
                "The exact-bundle static-model relationship no longer " +
                "matches its semantic render/collision rows.");
        }
        if (!render.CompiledFieldBindings
                .HasCompleteSuppressionBindings ||
            !collision.CompiledFieldBindings
                .HasCompleteSuppressionBindings)
        {
            throw new InvalidOperationException(
                "The static-model pair lacks complete exact bindings for " +
                "every compiled tombstone field.");
        }
        if (!render.HasTransform(render.ImportedTransform) ||
            !collision.HasTransform(collision.ImportedTransform))
        {
            throw new InvalidOperationException(
                "Compiled suppression cannot be combined with a preview " +
                "static-model transform.");
        }
        if (!SameExact(
                render.ImportedTransform.Origin,
                Relationship.Origin.Value) ||
            !SameBounds(
                render.ImportedTransform.Bounds,
                Relationship.Bounds.GfxBounds) ||
            !SameBounds(
                collision.ImportedTransform.Bounds,
                Relationship.Bounds.ClipBounds))
        {
            throw new InvalidOperationException(
                "The exact-bundle relationship evidence no longer matches " +
                "the imported semantic transforms.");
        }
    }

    private static SourceBindingId[] GetMutableSuppressionBindings(
        EditorStaticModel render,
        EditorStaticModel collision)
    {
        StaticModelCompiledFieldBindings gfx =
            render.CompiledFieldBindings;
        StaticModelCompiledFieldBindings clip =
            collision.CompiledFieldBindings;
        if (gfx.CullDistanceBinding is not { } cullDistance ||
            gfx.FlagsBinding is not { } flags ||
            gfx.LightingOriginBinding is not { } lightingOrigin)
        {
            throw new InvalidOperationException(
                "The render row lacks an exact compiled tombstone binding.");
        }

        return
        [
            gfx.OriginBinding,
            cullDistance,
            flags,
            gfx.BoundsMidpointBinding,
            lightingOrigin,
            clip.OriginBinding,
            clip.BoundsMidpointBinding
        ];
    }

    private static bool SameBounds(
        MapBounds? actual,
        MapBounds expected) =>
        actual is { } value &&
        SameExact(value.MidPoint, expected.MidPoint) &&
        SameExact(value.HalfSize, expected.HalfSize);

    private static bool SameExact(
        MapVector3 left,
        MapVector3 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z);

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private sealed class StaticModelSuppressionMutation
        : IMapEditMutation
    {
        private readonly EditorStaticModel _render;
        private readonly EditorStaticModel _collision;

        public StaticModelSuppressionMutation(
            EditorStaticModel render,
            EditorStaticModel collision)
        {
            _render = render;
            _collision = collision;
        }

        public void Apply() =>
            Transition(
                StaticModelCompiledDisposition.BaselinePresent,
                StaticModelCompiledDisposition.Suppressed,
                "apply");

        public void Revert() =>
            Transition(
                StaticModelCompiledDisposition.Suppressed,
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
                    $"Cannot {operation} compiled static-model suppression " +
                    "because the paired semantic state changed outside the " +
                    "command journal.");
            }

            // Both setters are non-allocating enum assignments after the
            // complete paired precondition check, so the transition cannot
            // expose a half-suppressed semantic pair.
            _render.SetCompiledDisposition(replacement);
            _collision.SetCompiledDisposition(replacement);
        }
    }
}
