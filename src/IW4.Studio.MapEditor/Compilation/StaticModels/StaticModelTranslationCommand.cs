using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Immutable proof handle issued by the compiled-map translation eligibility
/// evaluator. Its constructor is intentionally internal: an arbitrary caller
/// cannot promote a preview transform to a patch-saveable command.
/// </summary>
public sealed class StaticModelTranslationAuthorization
{
    internal StaticModelTranslationAuthorization(
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin,
        string bundleBaselineDigest,
        string evidence)
    {
        Relationship = relationship ??
            throw new ArgumentNullException(nameof(relationship));
        if (!destinationOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOrigin));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleBaselineDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);

        DestinationOrigin = destinationOrigin;
        BundleBaselineDigest = bundleBaselineDigest;
        Evidence = evidence;
    }

    public StaticModelCompilationRelationship Relationship { get; }
    public MapVector3 DestinationOrigin { get; }
    public string BundleBaselineDigest { get; }
    public string Evidence { get; }
}

/// <summary>
/// Atomically translates one mutually unique Gfx/Col static-model pair after
/// every retained compiled spatial and lighting invariant has been proven.
/// Save preparation re-evaluates those invariants against the immutable
/// baseline; this command's proof handle only controls semantic admission.
/// </summary>
public sealed class TranslateCompiledStaticModelCommand : MapEditCommand
{
    public TranslateCompiledStaticModelCommand(
        StaticModelTranslationAuthorization authorization)
        : this(
            MapEditCommandId.New(),
            authorization)
    {
    }

    internal TranslateCompiledStaticModelCommand(
        MapEditCommandId id,
        StaticModelTranslationAuthorization authorization)
        : base(
            id,
            "Translate compiled render/collision static-model pair",
            MapEditKind.StaticModelTransform,
            MapEditImpactTaxonomy.CompiledStaticModelTranslation(
                authorization?.Relationship.CollisionAssetKind ??
                throw new ArgumentNullException(nameof(authorization))),
            [
                authorization.Relationship.RenderObjectId,
                authorization.Relationship.CollisionObjectId
            ])
    {
        Authorization = authorization;
    }

    public StaticModelTranslationAuthorization Authorization { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
        => StaticModelPairTranslationSemantics.Prepare(
            this,
            document,
            Authorization.Relationship,
            Authorization.DestinationOrigin,
            StaticModelPairTranslationMode.ProofGated);
}

/// <summary>
/// Keeps an exact imported render/collision pair synchronized while the
/// requested destination is outside the currently proven compiled-save
/// envelope. This journal entry remains save-blocked because it carries no
/// compiled authorization, but it never leaves a half-moved render/collision
/// semantic pair.
/// </summary>
public sealed class PreviewPairedStaticModelTranslationCommand
    : MapEditCommand
{
    public PreviewPairedStaticModelTranslationCommand(
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin)
        : this(
            MapEditCommandId.New(),
            relationship,
            destinationOrigin)
    {
    }

    internal PreviewPairedStaticModelTranslationCommand(
        MapEditCommandId id,
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin)
        : base(
            id,
            "Preview render/collision static-model pair translation",
            MapEditKind.StaticModelTransform,
            MapEditImpactTaxonomy.StaticModelTransform(),
            [
                relationship?.RenderObjectId ??
                    throw new ArgumentNullException(nameof(relationship)),
                relationship.CollisionObjectId
            ])
    {
        if (!destinationOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOrigin),
                "Static-model origins must contain only finite components.");
        }

        Relationship = relationship;
        DestinationOrigin = destinationOrigin;
    }

    public StaticModelCompilationRelationship Relationship { get; }

    public MapVector3 DestinationOrigin { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
        => StaticModelPairTranslationSemantics.Prepare(
            this,
            document,
            Relationship,
            DestinationOrigin,
            StaticModelPairTranslationMode.PreviewRecovery);
}

internal enum StaticModelPairTranslationMode
{
    ProofGated,
    PreviewRecovery
}

internal static class StaticModelPairTranslationSemantics
{
    public static PreparedMapEdit Prepare(
        MapEditCommand command,
        EditorMapDocument document,
        StaticModelCompilationRelationship relationship,
        MapVector3 destinationOrigin,
        StaticModelPairTranslationMode mode)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relationship);
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!destinationOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOrigin),
                "Static-model origins must contain only finite components.");
        }

        EditorStaticModel render =
            document.GetRequiredObject<EditorStaticModel>(
                relationship.RenderObjectId);
        EditorStaticModel collision =
            document.GetRequiredObject<EditorStaticModel>(
                relationship.CollisionObjectId);

        ValidateRelationshipProjection(
            relationship,
            render,
            collision);
        if (render.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent ||
            collision.CompiledDisposition !=
                StaticModelCompiledDisposition.BaselinePresent)
        {
            throw new InvalidOperationException(
                "A suppressed static-model pair cannot also be translated.");
        }
        if (mode == StaticModelPairTranslationMode.ProofGated &&
            !SameExact(
                render.Transform.Origin,
                collision.Transform.Origin))
        {
            throw new InvalidOperationException(
                "The exact render/collision pair has divergent semantic " +
                "origins and cannot be translated atomically.");
        }

        EditorStaticModelTransformState renderBefore =
            render.Transform;
        EditorStaticModelTransformState collisionBefore =
            collision.Transform;
        EditorStaticModelTransformState renderAfter =
            render.ImportedTransform.WithOrigin(
                destinationOrigin);
        EditorStaticModelTransformState collisionAfter =
            collision.ImportedTransform.WithOrigin(
                destinationOrigin);
        bool renderChanges = renderBefore != renderAfter;
        bool collisionChanges = collisionBefore != collisionAfter;
        if (!renderChanges && !collisionChanges)
        {
            return PreparedMapEdit.NoChange(command);
        }
        if (mode == StaticModelPairTranslationMode.ProofGated &&
            renderChanges != collisionChanges)
        {
            throw new InvalidOperationException(
                "The exact render/collision pair has divergent transform " +
                "state and cannot expose a half-applied translation.");
        }

        SourceBindingId[] bindings =
            GetMutableTranslationBindings(render, collision);
        var mutations = new List<IMapEditMutation>(2);
        if (renderChanges)
        {
            mutations.Add(new StaticModelTransformMutation(
                render,
                renderBefore,
                renderAfter));
        }
        if (collisionChanges)
        {
            mutations.Add(new StaticModelTransformMutation(
                collision,
                collisionBefore,
                collisionAfter));
        }

        return new PreparedMapEdit(
            command,
            new MapPendingEdit(
                command.Description,
                command.Kind,
                bindings),
            mutations);
    }

    private static void ValidateRelationshipProjection(
        StaticModelCompilationRelationship relationship,
        EditorStaticModel render,
        EditorStaticModel collision)
    {
        if (!render.IsImported ||
            !collision.IsImported ||
            render.Representation != StaticModelRepresentation.Render ||
            collision.Representation !=
                StaticModelRepresentation.Collision ||
            render.SourceOrdinal.Value !=
                relationship.GfxSourceOrdinal ||
            collision.SourceOrdinal.Value !=
                relationship.ClipSourceOrdinal)
        {
            throw new InvalidOperationException(
                "The exact-bundle static-model relationship no longer " +
                "matches its semantic render/collision rows.");
        }
        if (!render.CompiledFieldBindings
                .HasCompleteTranslationBindings ||
            !collision.CompiledFieldBindings
                .HasCompleteTranslationBindings)
        {
            throw new InvalidOperationException(
                "The static-model pair lacks complete exact bindings for " +
                "every directly translated compiled field.");
        }
        if (!SameExact(
                render.ImportedTransform.Origin,
                relationship.Origin.Value) ||
            !SameBounds(
                render.ImportedTransform.Bounds,
                relationship.Bounds.GfxBounds) ||
            !SameBounds(
                collision.ImportedTransform.Bounds,
                relationship.Bounds.ClipBounds))
        {
            throw new InvalidOperationException(
                "The exact-bundle relationship evidence no longer matches " +
                "the imported semantic transforms.");
        }
    }

    private static SourceBindingId[] GetMutableTranslationBindings(
        EditorStaticModel render,
        EditorStaticModel collision)
    {
        StaticModelCompiledFieldBindings gfx =
            render.CompiledFieldBindings;
        StaticModelCompiledFieldBindings clip =
            collision.CompiledFieldBindings;
        if (gfx.LightingOriginBinding is not { } lightingOrigin)
        {
            throw new InvalidOperationException(
                "The render row lacks its exact lighting-origin binding.");
        }

        SourceBindingId[] bindings =
        [
            gfx.OriginBinding,
            gfx.BoundsMidpointBinding,
            lightingOrigin,
            clip.OriginBinding,
            clip.BoundsMidpointBinding
        ];
        if (bindings.Distinct().Count() != bindings.Length)
        {
            throw new InvalidOperationException(
                "Compiled static-model translation fields do not have five " +
                "distinct source bindings.");
        }
        return bindings;
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
}
