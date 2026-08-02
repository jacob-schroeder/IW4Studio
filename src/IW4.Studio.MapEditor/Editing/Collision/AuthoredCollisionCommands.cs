using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Editing.Collision;

/// <summary>
/// Adds one canonical authored collision source. Its local provenance binding
/// is intentionally absent from the compiled-source journal.
/// </summary>
public sealed class AddAuthoredCollisionSourceCommand : MapEditCommand
{
    private readonly EditorMapDocument _originatingDocument;
    private readonly EditorAuthoredCollisionCollectionState _before;
    private readonly EditorAuthoredCollisionCollectionState _after;

    public AddAuthoredCollisionSourceCommand(
        EditorMapDocument document,
        AuthoredCollisionSource source)
        : this(CreateArguments(document, source))
    {
    }

    private AddAuthoredCollisionSourceCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            "Add authored collision source",
            MapEditKind.CollisionCardinality,
            MapEditImpactTaxonomy.AuthoredCollisionCardinality(),
            [arguments.Authored.Id])
    {
        _originatingDocument = arguments.Document;
        _before = arguments.Before;
        _after = new EditorAuthoredCollisionCollectionState(
        [
            .. arguments.Before.AuthoredCollision,
            arguments.Authored
        ]);
        Authored = arguments.Authored;
    }

    public EditorAuthoredCollisionObject Authored { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        RequireOriginatingDocument(document, _originatingDocument);
        return new PreparedMapEdit(
            this,
            CreatePendingEdit(this),
            [
                new AuthoredCollisionCollectionMutation(
                    document,
                    _before,
                    _after)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        AuthoredCollisionSource source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        return document.ReadConsistent(revision =>
        {
            _ = revision;
            if (document.TryGetObject(source.ObjectId, out _))
            {
                throw new InvalidOperationException(
                    $"Semantic object {source.ObjectId} already exists in " +
                    "the document.");
            }

            return new CommandArguments(
                document,
                document.CaptureAuthoredCollisionCollectionState(),
                EditorAuthoredCollisionObject.Create(source));
        });
    }

    private sealed record CommandArguments(
        EditorMapDocument Document,
        EditorAuthoredCollisionCollectionState Before,
        EditorAuthoredCollisionObject Authored);

    internal static MapPendingEdit CreatePendingEdit(
        MapEditCommand command) =>
        new(
            command.Description,
            command.Kind,
            sourceBindings: null,
            preservationCoverageProven: false,
            hasRequiredBuilder: false);

    internal static void RequireOriginatingDocument(
        EditorMapDocument document,
        EditorMapDocument originatingDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!ReferenceEquals(document, originatingDocument) ||
            document.Id != originatingDocument.Id)
        {
            throw new InvalidOperationException(
                "The authored-collision command belongs to another semantic " +
                "document instance.");
        }
    }
}

/// <summary>
/// Atomically replaces one authored source while preserving its semantic
/// object identity and editor-only provenance binding.
/// </summary>
public sealed class ReplaceAuthoredCollisionSourceCommand : MapEditCommand
{
    private readonly EditorMapDocument _originatingDocument;
    private readonly EditorAuthoredCollisionCollectionState _before;
    private readonly EditorAuthoredCollisionCollectionState _after;
    private readonly bool _isNoChange;

    public ReplaceAuthoredCollisionSourceCommand(
        EditorMapDocument document,
        AuthoredCollisionSource replacement)
        : this(CreateArguments(document, replacement))
    {
    }

    private ReplaceAuthoredCollisionSourceCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            "Replace authored collision geometry",
            MapEditKind.CollisionGeometry,
            MapEditImpactTaxonomy.AuthoredCollisionGeometry(),
            [arguments.Replacement.Id])
    {
        _originatingDocument = arguments.Document;
        _before = arguments.Before;
        Replacement = arguments.Replacement;
        _isNoChange = ReferenceEquals(
            arguments.Current,
            arguments.Replacement);
        _after = _isNoChange
            ? _before
            : new EditorAuthoredCollisionCollectionState(
                arguments.Before.AuthoredCollision.Select(value =>
                    ReferenceEquals(value, arguments.Current)
                        ? arguments.Replacement
                        : value));
    }

    public EditorAuthoredCollisionObject Replacement { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        AddAuthoredCollisionSourceCommand.RequireOriginatingDocument(
            document,
            _originatingDocument);
        if (_isNoChange)
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            AddAuthoredCollisionSourceCommand.CreatePendingEdit(this),
            [
                new AuthoredCollisionCollectionMutation(
                    document,
                    _before,
                    _after)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        AuthoredCollisionSource replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        return document.ReadConsistent(_ =>
        {
            EditorAuthoredCollisionObject current =
                document.GetRequiredObject<EditorAuthoredCollisionObject>(
                    replacement.ObjectId);
            return new CommandArguments(
                document,
                document.CaptureAuthoredCollisionCollectionState(),
                current,
                current.WithSource(replacement));
        });
    }

    private sealed record CommandArguments(
        EditorMapDocument Document,
        EditorAuthoredCollisionCollectionState Before,
        EditorAuthoredCollisionObject Current,
        EditorAuthoredCollisionObject Replacement);
}

/// <summary>
/// Removes one authored collision source. Imported collision cannot satisfy
/// this command's typed target and therefore remains immutable.
/// </summary>
public sealed class RemoveAuthoredCollisionSourceCommand : MapEditCommand
{
    private readonly EditorMapDocument _originatingDocument;
    private readonly EditorAuthoredCollisionCollectionState _before;
    private readonly EditorAuthoredCollisionCollectionState _after;

    public RemoveAuthoredCollisionSourceCommand(
        EditorMapDocument document,
        MapObjectId objectId)
        : this(CreateArguments(document, objectId))
    {
    }

    private RemoveAuthoredCollisionSourceCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            "Remove authored collision source",
            MapEditKind.CollisionCardinality,
            MapEditImpactTaxonomy.AuthoredCollisionCardinality(),
            [arguments.Target.Id])
    {
        _originatingDocument = arguments.Document;
        _before = arguments.Before;
        _after = new EditorAuthoredCollisionCollectionState(
            arguments.Before.AuthoredCollision.Where(value =>
                !ReferenceEquals(value, arguments.Target)));
        Target = arguments.Target;
    }

    public EditorAuthoredCollisionObject Target { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        AddAuthoredCollisionSourceCommand.RequireOriginatingDocument(
            document,
            _originatingDocument);
        return new PreparedMapEdit(
            this,
            AddAuthoredCollisionSourceCommand.CreatePendingEdit(this),
            [
                new AuthoredCollisionCollectionMutation(
                    document,
                    _before,
                    _after)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        MapObjectId objectId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ReadConsistent(_ =>
        {
            EditorAuthoredCollisionObject target =
                document.GetRequiredObject<EditorAuthoredCollisionObject>(
                    objectId);
            return new CommandArguments(
                document,
                document.CaptureAuthoredCollisionCollectionState(),
                target);
        });
    }

    private sealed record CommandArguments(
        EditorMapDocument Document,
        EditorAuthoredCollisionCollectionState Before,
        EditorAuthoredCollisionObject Target);
}

internal sealed class AuthoredCollisionCollectionMutation
    : IMapEditMutation
{
    private readonly EditorMapDocument _document;
    private readonly EditorAuthoredCollisionCollectionState _before;
    private readonly EditorAuthoredCollisionCollectionState _after;

    public AuthoredCollisionCollectionMutation(
        EditorMapDocument document,
        EditorAuthoredCollisionCollectionState before,
        EditorAuthoredCollisionCollectionState after)
    {
        _document =
            document ?? throw new ArgumentNullException(nameof(document));
        _before =
            before ?? throw new ArgumentNullException(nameof(before));
        _after =
            after ?? throw new ArgumentNullException(nameof(after));
    }

    public void Apply() =>
        _document.ApplyAuthoredCollisionCollectionState(_before, _after);

    public void Revert() =>
        _document.ApplyAuthoredCollisionCollectionState(_after, _before);
}
