using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Entities;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Editing.Commands;

public readonly record struct MapEditCommandId
{
    public MapEditCommandId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public static MapEditCommandId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

/// <summary>
/// Public, renderer-neutral description of one semantic map operation.
/// Executable command implementations are closed by <see cref="MapEditCommand"/>
/// so arbitrary callers cannot bypass document transaction guarantees.
/// </summary>
public interface IMapEditCommand
{
    MapEditCommandId Id { get; }
    string Description { get; }
    MapEditKind Kind { get; }
    MapEditImpact Impact { get; }
    IReadOnlyList<MapObjectId> TargetObjects { get; }
}

public abstract class MapEditCommand : IMapEditCommand
{
    private readonly IReadOnlyList<MapObjectId> _targetObjects;

    protected MapEditCommand(
        MapEditCommandId id,
        string description,
        MapEditKind kind,
        MapEditImpact impact,
        IEnumerable<MapObjectId> targetObjects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(targetObjects);
        if (id.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if ((kind == MapEditKind.EditorOnly) !=
            (impact.Classification == MapSaveClassification.EditorOnly))
        {
            throw new ArgumentException(
                "Editor-only command kinds and save classifications must agree.",
                nameof(impact));
        }

        MapObjectId[] targets = targetObjects.Distinct().ToArray();
        if (targets.Length == 0 ||
            targets.Any(target => target.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "A map edit command must identify at least one semantic target.",
                nameof(targetObjects));
        }

        Id = id;
        Description = description;
        Kind = kind;
        Impact = impact;
        _targetObjects = new ReadOnlyCollection<MapObjectId>(targets);
    }

    public MapEditCommandId Id { get; }
    public string Description { get; }
    public MapEditKind Kind { get; }
    public MapEditImpact Impact { get; }
    public IReadOnlyList<MapObjectId> TargetObjects => _targetObjects;

    internal abstract PreparedMapEdit Prepare(EditorMapDocument document);
}

public enum PrimaryLightColorComponent
{
    Red,
    Green,
    Blue
}

/// <summary>
/// Changes exactly one existing ComMap primary-light color component. The
/// command preserves the imported source ordinal, binding, and provenance.
/// </summary>
public sealed class SetPrimaryLightColorComponentCommand : MapEditCommand
{
    public SetPrimaryLightColorComponentCommand(
        MapObjectId lightId,
        PrimaryLightColorComponent component,
        float value)
        : this(MapEditCommandId.New(), lightId, component, value)
    {
    }

    internal SetPrimaryLightColorComponentCommand(
        MapEditCommandId id,
        MapObjectId lightId,
        PrimaryLightColorComponent component,
        float value)
        : base(
            id,
            $"Set primary-light {component.ToString().ToLowerInvariant()} component",
            MapEditKind.PrimaryLightColor,
            new MapEditImpact(
                MapSaveClassification.PatchSaveable,
                [MapAssetKind.ComMap],
                MapDerivedSubsystem.None,
                saveBlocker: null),
            [lightId])
    {
        if (!Enum.IsDefined(component))
            throw new ArgumentOutOfRangeException(nameof(component));
        if (!float.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Primary-light color components must be finite and nonnegative.");
        }

        LightId = lightId;
        Component = component;
        Value = value;
    }

    public MapObjectId LightId { get; }
    public PrimaryLightColorComponent Component { get; }
    public float Value { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        EditorPrimaryLight light =
            document.GetRequiredObject<EditorPrimaryLight>(LightId);
        MapVector3 before = light.Color.Value;
        MapVector3 after = Component switch
        {
            PrimaryLightColorComponent.Red => before with { X = Value },
            PrimaryLightColorComponent.Green => before with { Y = Value },
            PrimaryLightColorComponent.Blue => before with { Z = Value },
            _ => throw new ArgumentOutOfRangeException(nameof(Component))
        };

        if (before == after)
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [light.Color.SourceBinding]),
            [
                new PrimaryLightColorMutation(light, before, after)
            ]);
    }
}

/// <summary>
/// Changes exactly one existing ComMap primary-light exponent byte while
/// retaining the imported light row and exact exponent source binding.
/// </summary>
public sealed class SetPrimaryLightExponentCommand : MapEditCommand
{
    public SetPrimaryLightExponentCommand(
        MapObjectId lightId,
        byte value)
        : this(MapEditCommandId.New(), lightId, value)
    {
    }

    internal SetPrimaryLightExponentCommand(
        MapEditCommandId id,
        MapObjectId lightId,
        byte value)
        : base(
            id,
            "Set primary-light exponent",
            MapEditKind.PrimaryLightExponent,
            MapEditImpactTaxonomy.PrimaryLightExponent(),
            [lightId])
    {
        LightId = lightId;
        Value = value;
    }

    public MapObjectId LightId { get; }
    public byte Value { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        EditorPrimaryLight light =
            document.GetRequiredObject<EditorPrimaryLight>(LightId);
        byte before = light.Exponent.Value;
        if (before == Value)
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [light.Exponent.SourceBinding]),
            [
                new PrimaryLightExponentMutation(light, before, Value)
            ]);
    }
}

/// <summary>
/// Changes the inner cone cosine of one existing type-2 ComMap spotlight.
/// Outer/expanded cone values and every spatial assignment remain immutable.
/// </summary>
public sealed class SetPrimaryLightCosHalfFovInnerCommand : MapEditCommand
{
    public SetPrimaryLightCosHalfFovInnerCommand(
        MapObjectId lightId,
        float value)
        : this(MapEditCommandId.New(), lightId, value)
    {
    }

    internal SetPrimaryLightCosHalfFovInnerCommand(
        MapEditCommandId id,
        MapObjectId lightId,
        float value)
        : base(
            id,
            "Set primary-light spot falloff",
            MapEditKind.PrimaryLightSpotFalloff,
            MapEditImpactTaxonomy.PrimaryLightSpotFalloff(),
            [lightId])
    {
        if (!float.IsFinite(value) || value <= 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A spotlight inner-cone cosine must be finite and in (0, 1].");
        }

        LightId = lightId;
        Value = value;
    }

    public MapObjectId LightId { get; }
    public float Value { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        EditorPrimaryLight light =
            document.GetRequiredObject<EditorPrimaryLight>(LightId);
        ValidateEditableSpotlight(light);

        float before = light.CosHalfFovInner.Value;
        if (SameBits(before, Value))
            return PreparedMapEdit.NoChange(this);
        if (Value <= light.CosHalfFovOuter.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Value),
                "A spotlight inner-cone cosine must be greater than its " +
                "immutable outer-cone cosine.");
        }

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [light.CosHalfFovInner.SourceBinding]),
            [
                new PrimaryLightSpotFalloffMutation(light, before, Value)
            ]);
    }

    private static void ValidateEditableSpotlight(EditorPrimaryLight light)
    {
        float imported = light.ImportedCosHalfFovInner.Value;
        float current = light.CosHalfFovInner.Value;
        float outer = light.CosHalfFovOuter.Value;
        if (light.LightType.Value != 2 ||
            !float.IsFinite(outer) ||
            !float.IsFinite(imported) ||
            !float.IsFinite(current) ||
            outer <= 0f ||
            outer >= imported ||
            imported > 1f ||
            outer >= current ||
            current > 1f)
        {
            throw new InvalidOperationException(
                "Spot-falloff editing requires an imported and current " +
                "type-2 light satisfying 0 < outer < inner <= 1.");
        }
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);
}

/// <summary>
/// Changes the serialized HalfThickness scalar of one existing FxGlassDef.
/// Initial-piece views remain derived through DefIndex and are included as
/// semantic targets, but no piece row or runtime cache is edited.
/// </summary>
public sealed class SetFxGlassDefinitionHalfThicknessCommand
    : MapEditCommand
{
    public SetFxGlassDefinitionHalfThicknessCommand(
        EditorMapDocument document,
        MapObjectId definitionId,
        float halfThickness)
        : this(CreateArguments(
            document,
            definitionId,
            halfThickness))
    {
    }

    private SetFxGlassDefinitionHalfThicknessCommand(
        CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            $"Set FX glass definition " +
            $"{arguments.DefinitionOrdinal} half thickness",
            MapEditKind.FxGlassDefinitionHalfThickness,
            MapEditImpactTaxonomy.FxGlassDefinitionHalfThickness(),
            arguments.TargetObjects)
    {
        ExpectedDocumentId = arguments.ExpectedDocumentId;
        DefinitionId = arguments.DefinitionId;
        DefinitionOrdinal = arguments.DefinitionOrdinal;
        HalfThickness = arguments.HalfThickness;
        _definitionState = arguments.DefinitionState;
        _expectedTargets = arguments.TargetObjects;
    }

    private readonly EditorFxGlassDefinitionState _definitionState;
    private readonly IReadOnlyList<MapObjectId> _expectedTargets;

    public MapDocumentId ExpectedDocumentId { get; }
    public MapObjectId DefinitionId { get; }
    public int DefinitionOrdinal { get; }
    public float HalfThickness { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id != ExpectedDocumentId)
        {
            throw new InvalidOperationException(
                "The FX glass definition command belongs to another map " +
                "document.");
        }

        EditorGlassObject definition =
            document.GetRequiredObject<EditorGlassObject>(DefinitionId);
        RequireDefinitionAuthority(
            document,
            definition,
            _definitionState,
            DefinitionOrdinal);
        MapObjectId[] currentTargets =
            ResolveTargets(
                document,
                definition,
                _definitionState,
                DefinitionOrdinal);
        if (!_expectedTargets.SequenceEqual(currentTargets))
        {
            throw new InvalidOperationException(
                "The FX glass definition dependency projection changed after " +
                "the command was created.");
        }

        float before = _definitionState.CurrentHalfThickness;
        if (SameBits(before, HalfThickness))
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [_definitionState.SourceBinding]),
            [
                new FxGlassDefinitionHalfThicknessMutation(
                    _definitionState,
                    before,
                    HalfThickness)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        MapObjectId definitionId,
        float halfThickness)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!float.IsFinite(halfThickness) || halfThickness <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(halfThickness),
                "FX glass definition half thickness must be finite and " +
                "strictly positive.");
        }

        EditorGlassObject definition =
            document.GetRequiredObject<EditorGlassObject>(definitionId);
        EditorFxGlassDefinitionState state =
            definition.DefinitionState ??
            throw new InvalidOperationException(
                "The selected glass object has no imported FX definition " +
                "authority.");
        int ordinal = state.DefinitionOrdinal;
        RequireDefinitionAuthority(
            document,
            definition,
            state,
            ordinal);
        MapObjectId[] targets =
            ResolveTargets(
                document,
                definition,
                state,
                ordinal);
        return new CommandArguments(
            document.Id,
            definitionId,
            ordinal,
            halfThickness,
            state,
            Array.AsReadOnly(targets));
    }

    private static MapObjectId[] ResolveTargets(
        EditorMapDocument document,
        EditorGlassObject definition,
        EditorFxGlassDefinitionState state,
        int ordinal)
    {
        EditorGlassObject[] matching = document.Glass
            .Where(value =>
                ReferenceEquals(value.DefinitionState, state))
            .ToArray();
        EditorGlassObject[] definitions = matching
            .Where(value =>
                value.Representation ==
                    GlassRepresentation.FxDefinition)
            .ToArray();
        if (definitions.Length != 1 ||
            !ReferenceEquals(definitions[0], definition))
        {
            throw new InvalidOperationException(
                "The imported FX glass definition authority is not unique.");
        }

        EditorGlassObject[] dependents = document.Glass
            .Where(value =>
                value.Representation ==
                    GlassRepresentation.FxInitialPiece &&
                value.DefinitionIndex.Value == ordinal)
            .OrderBy(value => value.SourceOrdinal.Value)
            .ToArray();
        if (dependents.Any(value =>
                !ReferenceEquals(value.DefinitionState, state) ||
                value.HalfThickness.SourceBinding !=
                    state.SourceBinding ||
                value.HalfThickness.Value is not { } valueThickness ||
                !SameBits(
                    valueThickness,
                    state.CurrentHalfThickness)))
        {
            throw new InvalidOperationException(
                "An FX glass initial-piece projection does not share its " +
                "definition's exact HalfThickness authority.");
        }
        if (matching.Any(value =>
                value.Representation ==
                    GlassRepresentation.GameplayPiece) ||
            matching.Length != dependents.Length + 1)
        {
            throw new InvalidOperationException(
                "The FX glass definition authority is attached to an " +
                "unexpected semantic representation.");
        }

        return
        [
            definition.Id,
            .. dependents.Select(value => value.Id)
        ];
    }

    private static void RequireDefinitionAuthority(
        EditorMapDocument document,
        EditorGlassObject definition,
        EditorFxGlassDefinitionState state,
        int ordinal)
    {
        if (!document.Glass.Contains(definition) ||
            definition.Representation !=
                GlassRepresentation.FxDefinition ||
            !ReferenceEquals(definition.DefinitionState, state) ||
            definition.Id != state.DefinitionObjectId ||
            definition.SourceOrdinal.Value != ordinal ||
            definition.DefinitionIndex.Value != ordinal ||
            definition.HalfThickness.SourceBinding !=
                state.SourceBinding ||
            definition.HalfThickness.Value is not { } current ||
            !SameBits(current, state.CurrentHalfThickness))
        {
            throw new InvalidOperationException(
                "The selected object is not the exact authoritative imported " +
                "FX glass definition.");
        }
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private sealed record CommandArguments(
        MapDocumentId ExpectedDocumentId,
        MapObjectId DefinitionId,
        int DefinitionOrdinal,
        float HalfThickness,
        EditorFxGlassDefinitionState DefinitionState,
        IReadOnlyList<MapObjectId> TargetObjects);
}

/// <summary>
/// Changes the packed RGBA value owned by one existing FxGlassDef. The color
/// has no initial-piece projection, so the definition is the sole semantic
/// target and its exact color binding is the sole journal authority.
/// </summary>
public sealed class SetFxGlassDefinitionColorCommand : MapEditCommand
{
    public SetFxGlassDefinitionColorCommand(
        EditorMapDocument document,
        MapObjectId definitionId,
        uint color)
        : this(CreateArguments(document, definitionId, color))
    {
    }

    private SetFxGlassDefinitionColorCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            $"Set FX glass definition {arguments.DefinitionOrdinal} color",
            MapEditKind.FxGlassDefinitionColor,
            MapEditImpactTaxonomy.FxGlassDefinitionColor(),
            [arguments.DefinitionId])
    {
        ExpectedDocumentId = arguments.ExpectedDocumentId;
        DefinitionId = arguments.DefinitionId;
        DefinitionOrdinal = arguments.DefinitionOrdinal;
        Color = arguments.Color;
        _definitionState = arguments.DefinitionState;
    }

    private readonly EditorFxGlassDefinitionState _definitionState;

    public MapDocumentId ExpectedDocumentId { get; }
    public MapObjectId DefinitionId { get; }
    public int DefinitionOrdinal { get; }
    public uint Color { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id != ExpectedDocumentId)
        {
            throw new InvalidOperationException(
                "The FX glass definition color command belongs to another " +
                "map document.");
        }

        EditorGlassObject definition =
            document.GetRequiredObject<EditorGlassObject>(DefinitionId);
        RequireDefinitionAuthority(
            document,
            definition,
            _definitionState,
            DefinitionOrdinal);

        uint before = _definitionState.CurrentColor;
        if (before == Color)
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [_definitionState.ColorSourceBinding]),
            [
                new FxGlassDefinitionColorMutation(
                    _definitionState,
                    before,
                    Color)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        MapObjectId definitionId,
        uint color)
    {
        ArgumentNullException.ThrowIfNull(document);
        EditorGlassObject definition =
            document.GetRequiredObject<EditorGlassObject>(definitionId);
        EditorFxGlassDefinitionState state =
            definition.DefinitionState ??
            throw new InvalidOperationException(
                "The selected glass object has no imported FX definition " +
                "authority.");
        int ordinal = state.DefinitionOrdinal;
        RequireDefinitionAuthority(document, definition, state, ordinal);
        return new CommandArguments(
            document.Id,
            definitionId,
            ordinal,
            color,
            state);
    }

    private static void RequireDefinitionAuthority(
        EditorMapDocument document,
        EditorGlassObject definition,
        EditorFxGlassDefinitionState state,
        int ordinal)
    {
        EditorGlassObject[] definitions = document.Glass
            .Where(value =>
                ReferenceEquals(value.DefinitionState, state) &&
                value.Representation == GlassRepresentation.FxDefinition)
            .ToArray();
        if (definitions.Length != 1 ||
            !ReferenceEquals(definitions[0], definition) ||
            definition.Id != state.DefinitionObjectId ||
            definition.SourceOrdinal.Value != ordinal ||
            definition.DefinitionIndex.Value != ordinal ||
            definition.Color.SourceBinding != state.ColorSourceBinding ||
            definition.Color.Value is not { } current ||
            current != state.CurrentColor)
        {
            throw new InvalidOperationException(
                "The selected object is not the exact authoritative imported " +
                "FX glass definition color.");
        }
    }

    private sealed record CommandArguments(
        MapDocumentId ExpectedDocumentId,
        MapObjectId DefinitionId,
        int DefinitionOrdinal,
        uint Color,
        EditorFxGlassDefinitionState DefinitionState);
}

/// <summary>
/// Changes viewport visibility only. This does not suppress compiled render or
/// collision records and is therefore always excluded from fastfile output.
/// </summary>
public sealed class SetEditorObjectVisibilityCommand : MapEditCommand
{
    public SetEditorObjectVisibilityCommand(
        MapObjectId objectId,
        EditorObjectVisibility visibility)
        : this(MapEditCommandId.New(), objectId, visibility)
    {
    }

    internal SetEditorObjectVisibilityCommand(
        MapEditCommandId id,
        MapObjectId objectId,
        EditorObjectVisibility visibility)
        : base(
            id,
            $"Set editor visibility to {visibility}",
            MapEditKind.EditorOnly,
            new MapEditImpact(
                MapSaveClassification.EditorOnly,
                [],
                MapDerivedSubsystem.None,
                saveBlocker: null),
            [objectId])
    {
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));

        ObjectId = objectId;
        Visibility = visibility;
    }

    public MapObjectId ObjectId { get; }
    public EditorObjectVisibility Visibility { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        EditorMapObject target = document.GetRequiredObject(ObjectId);
        EditorObjectVisibility before = target.Visibility;
        if (before == Visibility)
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(Description, Kind),
            [
                new EditorVisibilityMutation(target, before, Visibility)
            ]);
    }
}

/// <summary>
/// Changes the preview translation of one render static model. The command
/// also translates its semantic bounds so selection and viewport projection
/// remain coherent. Compiled persistence stays blocked until authoritative
/// render/collision identity and all required rebuilders exist.
/// </summary>
public sealed class SetStaticModelOriginCommand : MapEditCommand
{
    public SetStaticModelOriginCommand(
        MapObjectId modelId,
        MapVector3 origin)
        : this(MapEditCommandId.New(), modelId, origin)
    {
    }

    internal SetStaticModelOriginCommand(
        MapEditCommandId id,
        MapObjectId modelId,
        MapVector3 origin)
        : base(
            id,
            "Translate render static model",
            MapEditKind.StaticModelTransform,
            MapEditImpactTaxonomy.StaticModelTransform(),
            [modelId])
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                "Static-model origins must contain only finite components.");
        }

        ModelId = modelId;
        Origin = origin;
    }

    public MapObjectId ModelId { get; }

    public MapVector3 Origin { get; }

    internal override PreparedMapEdit Prepare(
        EditorMapDocument document)
    {
        EditorStaticModel model =
            document.GetRequiredObject<EditorStaticModel>(ModelId);
        if (!model.IsImported ||
            model.Representation != StaticModelRepresentation.Render)
        {
            throw new InvalidOperationException(
                "Preview translation is limited to imported render static " +
                "models; authored render/collision pairs must remain atomic.");
        }

        EditorStaticModelTransformState before = model.Transform;
        EditorStaticModelTransformState after =
            model.ImportedTransform.WithOrigin(Origin);
        if (before == after)
            return PreparedMapEdit.NoChange(this);

        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [
                    model.Origin.SourceBinding,
                    model.Bounds.SourceBinding
                ]),
            [
                new StaticModelTransformMutation(
                    model,
                    before,
                    after)
            ]);
    }
}

/// <summary>
/// Canonical, reviewed authored shape for the narrow Phase 6 script_origin
/// cardinality slice.
/// </summary>
public sealed record ScriptOriginEntityDefinition
{
    public ScriptOriginEntityDefinition(
        MapVector3 origin,
        MapVector3? angles = null,
        float? angle = null,
        string? target = null,
        string? targetName = null,
        int? spawnFlags = null)
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                "script_origin origin must contain finite components.");
        }
        if (angles is { IsFinite: false })
        {
            throw new ArgumentOutOfRangeException(
                nameof(angles),
                "script_origin angles must contain finite components.");
        }
        if (angle is { } scalar && !float.IsFinite(scalar))
        {
            throw new ArgumentOutOfRangeException(
                nameof(angle),
                "script_origin angle must be finite.");
        }
        if (angles is not null && angle is not null)
        {
            throw new ArgumentException(
                "Use either script_origin angles or angle, not both.");
        }

        Origin = origin;
        Angles = angles;
        Angle = angle;
        Target = target;
        TargetName = targetName;
        SpawnFlags = spawnFlags;
    }

    public MapVector3 Origin { get; }
    public MapVector3? Angles { get; }
    public float? Angle { get; }
    public string? Target { get; }
    public string? TargetName { get; }
    public int? SpawnFlags { get; }

    internal IReadOnlyList<KeyValuePair<string, string>>
        ToCanonicalProperties()
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("classname", "script_origin"),
            new("origin", Format(Origin))
        };
        if (Angles is { } angles)
            values.Add(new("angles", Format(angles)));
        if (Angle is { } angle)
            values.Add(new("angle", Format(angle)));
        if (Target is not null)
            values.Add(new("target", Target));
        if (TargetName is not null)
            values.Add(new("targetname", TargetName));
        if (SpawnFlags is { } spawnFlags)
        {
            values.Add(new(
                "spawnflags",
                spawnFlags.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        return Array.AsReadOnly(values.ToArray());
    }

    private static string Format(MapVector3 value) =>
        string.Join(
            ' ',
            Format(value.X),
            Format(value.Y),
            Format(value.Z));

    private static string Format(float value) =>
        value.ToString(
            "R",
            System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Appends one canonical script_origin immediately before the compiled
/// entity-string trailing NUL. Identity and authored field bindings are
/// generated once with the command and survive undo/redo.
/// </summary>
public sealed class AppendScriptOriginEntityCommand : MapEditCommand
{
    private readonly EditorMapDocument _originatingDocument;
    private readonly AuthoredEntityIdentity _authoredIdentity;

    public AppendScriptOriginEntityCommand(
        EditorMapDocument document,
        ScriptOriginEntityDefinition definition)
        : this(CreateArguments(document, definition))
    {
    }

    private AppendScriptOriginEntityCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            "Append script_origin MapEnt",
            MapEditKind.MapEntityCardinality,
            PatchableImpact(),
            [arguments.Identity.EntityId])
    {
        _originatingDocument = arguments.Document;
        _authoredIdentity = arguments.Identity;
        ExpectedDocumentId = arguments.Document.Id;
        ExpectedSourceDigest = arguments.ExpectedSourceDigest;
        Definition = arguments.Definition;
        ExpectedAssessment = arguments.Assessment;
    }

    public MapDocumentId ExpectedDocumentId { get; }
    public string ExpectedSourceDigest { get; }
    public ScriptOriginEntityDefinition Definition { get; }
    public MapObjectId EntityId => _authoredIdentity.EntityId;
    public MapEntityCardinalityAssessment ExpectedAssessment { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        RequireOriginatingDocument(
            document,
            _originatingDocument,
            ExpectedDocumentId);
        EditorMapEntitySource source = RequireCurrentSource(
            document,
            ExpectedSourceDigest);
        MapEntsCardinalityEdit syntaxEdit =
            source.Syntax.PrepareScriptOriginAppend(
                Definition.ToCanonicalProperties());
        MapEntityCardinalityAssessment assessment =
            Assess(syntaxEdit, MapEntityCardinalityOperation.Append);
        if (assessment != ExpectedAssessment ||
            !assessment.IsPatchAuthorized)
        {
            throw new InvalidOperationException(
                "The executable-backed script_origin append evidence is " +
                "stale or inconsistent.");
        }

        EditorEntityCollectionState before =
            document.CaptureEntityCollectionState();
        EditorEntity entity = CreateAuthoredEntity(
            syntaxEdit.After.GetEntity(syntaxEdit.EntityOrdinal),
            _authoredIdentity);
        var after = new EditorEntityCollectionState(
            syntaxEdit.After,
            before.Entities.Append(entity));
        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [source.SourceBinding]),
            [
                new MapEntityCollectionMutation(
                    document,
                    before,
                    after)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        ScriptOriginEntityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);
        return document.ReadConsistent(_ =>
        {
            EditorMapEntitySource source = document.EntitySource ??
                throw new InvalidOperationException(
                    "The editor document has no byte-authoritative MapEnt source.");
            MapEntsCardinalityEdit syntaxEdit =
                source.Syntax.PrepareScriptOriginAppend(
                    definition.ToCanonicalProperties());
            MapEntityCardinalityAssessment assessment =
                Assess(syntaxEdit, MapEntityCardinalityOperation.Append);
            if (!assessment.IsPatchAuthorized)
            {
                throw new InvalidOperationException(
                    "script_origin append is not authorized: " +
                    assessment.Evidence);
            }
            int propertyCount = syntaxEdit.After
                .GetEntity(syntaxEdit.EntityOrdinal)
                .Properties.Count;
            return new CommandArguments(
                document,
                source.CurrentDigest,
                definition,
                AuthoredEntityIdentity.Create(propertyCount),
                assessment);
        });
    }

    private sealed record CommandArguments(
        EditorMapDocument Document,
        string ExpectedSourceDigest,
        ScriptOriginEntityDefinition Definition,
        AuthoredEntityIdentity Identity,
        MapEntityCardinalityAssessment Assessment);

    internal sealed record AuthoredEntityIdentity(
        MapObjectId EntityId,
        SourceBindingId OrdinalBinding,
        SourceBindingId OffsetBinding,
        SourceBindingId LengthBinding,
        IReadOnlyList<SourceBindingId> KeyBindings,
        IReadOnlyList<SourceBindingId> ValueBindings)
    {
        public static AuthoredEntityIdentity Create(int propertyCount)
        {
            if (propertyCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(propertyCount));
            return new AuthoredEntityIdentity(
                new MapObjectId(Guid.NewGuid()),
                NewBinding(),
                NewBinding(),
                NewBinding(),
                Array.AsReadOnly(
                    Enumerable.Range(0, propertyCount)
                        .Select(_ => NewBinding())
                        .ToArray()),
                Array.AsReadOnly(
                    Enumerable.Range(0, propertyCount)
                        .Select(_ => NewBinding())
                        .ToArray()));
        }

        private static SourceBindingId NewBinding() =>
            new(Guid.NewGuid());
    }

    private static EditorEntity CreateAuthoredEntity(
        MapEntsSyntaxEntity syntax,
        AuthoredEntityIdentity identity)
    {
        if (identity.KeyBindings.Count != syntax.Properties.Count ||
            identity.ValueBindings.Count != syntax.Properties.Count)
        {
            throw new InvalidOperationException(
                "Authored script_origin identity does not match its canonical property count.");
        }

        EditorEntityProperty[] properties = syntax.Properties
            .Select((value, index) => new EditorEntityProperty(
                value.Ordinal,
                new MapValue<string>(
                    value.Key,
                    MapValueProvenance.Authored,
                    identity.KeyBindings[index]),
                new MapValue<string>(
                    value.Value,
                    MapValueProvenance.Authored,
                    identity.ValueBindings[index]),
                value.Span,
                value.KeyTokenSpan,
                value.KeyContentSpan,
                value.ValueTokenSpan,
                value.ValueContentSpan))
            .ToArray();
        MapEntityCompilationAssessment compilationAssessment =
            MapEntityConsumerCatalog.ConservativeIw4.Classify(
                properties.Select(value =>
                    new KeyValuePair<string, string>(
                        value.Key,
                        value.Value)));
        return new EditorEntity(
            identity.EntityId,
            new MapValue<int>(
                syntax.Ordinal.Value,
                MapValueProvenance.Authored,
                identity.OrdinalBinding),
            new MapValue<int>(
                syntax.Span.Offset,
                MapValueProvenance.Authored,
                identity.OffsetBinding),
            new MapValue<int>(
                syntax.Span.Length,
                MapValueProvenance.Authored,
                identity.LengthBinding),
            "script_origin",
            compilationAssessment,
            properties);
    }

    internal static MapEntityCardinalityAssessment Assess(
        MapEntsCardinalityEdit edit,
        MapEntityCardinalityOperation operation)
    {
        MapEntsSyntaxDocument syntax =
            operation == MapEntityCardinalityOperation.Append
                ? edit.After
                : edit.Before;
        MapEntsSyntaxEntity entity =
            syntax.GetEntity(edit.EntityOrdinal);
        return MapEntityConsumerCatalog.ConservativeIw4
            .ClassifyCardinalityEdit(
                entity.Properties.Select(value =>
                    new KeyValuePair<string, string>(
                        value.Key,
                        value.Value)),
                operation,
                isPhysicalTail:
                    edit.EntityOrdinal.Value ==
                    syntax.Entities.Count - 1);
    }

    internal static MapEditImpact PatchableImpact() =>
        new(
            MapSaveClassification.PatchSaveable,
            [MapAssetKind.MapEnts],
            MapDerivedSubsystem.None,
            saveBlocker: null);

    internal static void RequireOriginatingDocument(
        EditorMapDocument document,
        EditorMapDocument originatingDocument,
        MapDocumentId expectedDocumentId)
    {
        if (!ReferenceEquals(document, originatingDocument) ||
            document.Id != expectedDocumentId)
        {
            throw new InvalidOperationException(
                "The MapEnt cardinality command belongs to another semantic " +
                "document instance.");
        }
    }

    internal static EditorMapEntitySource RequireCurrentSource(
        EditorMapDocument document,
        string expectedDigest)
    {
        EditorMapEntitySource source = document.EntitySource ??
            throw new InvalidOperationException(
                "The editor document has no byte-authoritative MapEnt source.");
        if (!string.Equals(
                source.CurrentDigest,
                expectedDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The MapEnt cardinality command is stale because the " +
                "byte-authoritative source changed after command creation.");
        }
        return source;
    }
}

/// <summary>
/// Removes only the physical final MapEnt row when it is an exact reviewed
/// script_origin shape. Undo restores the same semantic object identity.
/// </summary>
public sealed class RemoveFinalScriptOriginEntityCommand : MapEditCommand
{
    private readonly EditorMapDocument _originatingDocument;

    public RemoveFinalScriptOriginEntityCommand(EditorMapDocument document)
        : this(CreateArguments(document))
    {
    }

    private RemoveFinalScriptOriginEntityCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            "Remove final script_origin MapEnt",
            MapEditKind.MapEntityCardinality,
            AppendScriptOriginEntityCommand.PatchableImpact(),
            [arguments.EntityId])
    {
        _originatingDocument = arguments.Document;
        ExpectedDocumentId = arguments.Document.Id;
        ExpectedSourceDigest = arguments.ExpectedSourceDigest;
        EntityId = arguments.EntityId;
        ExpectedAssessment = arguments.Assessment;
    }

    public MapDocumentId ExpectedDocumentId { get; }
    public string ExpectedSourceDigest { get; }
    public MapObjectId EntityId { get; }
    public MapEntityCardinalityAssessment ExpectedAssessment { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        AppendScriptOriginEntityCommand.RequireOriginatingDocument(
            document,
            _originatingDocument,
            ExpectedDocumentId);
        EditorMapEntitySource source =
            AppendScriptOriginEntityCommand.RequireCurrentSource(
                document,
                ExpectedSourceDigest);
        EditorEntity entity = document.Entities.LastOrDefault() ??
            throw new InvalidOperationException(
                "The document has no final MapEnt row to remove.");
        if (entity.Id != EntityId)
        {
            throw new InvalidOperationException(
                "The final MapEnt row changed after the remove command was " +
                "created.");
        }

        MapEntsCardinalityEdit syntaxEdit =
            source.Syntax.PrepareFinalScriptOriginRemoval(
                entity.SyntaxOrdinal);
        MapEntityCardinalityAssessment assessment =
            AppendScriptOriginEntityCommand.Assess(
                syntaxEdit,
                MapEntityCardinalityOperation.Remove);
        if (assessment != ExpectedAssessment ||
            !assessment.IsPatchAuthorized)
        {
            throw new InvalidOperationException(
                "The executable-backed script_origin removal evidence is " +
                "stale or inconsistent.");
        }

        EditorEntityCollectionState before =
            document.CaptureEntityCollectionState();
        var after = new EditorEntityCollectionState(
            syntaxEdit.After,
            before.Entities.Take(before.Entities.Count - 1));
        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [source.SourceBinding]),
            [
                new MapEntityCollectionMutation(
                    document,
                    before,
                    after)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ReadConsistent(_ =>
        {
            EditorMapEntitySource source = document.EntitySource ??
                throw new InvalidOperationException(
                    "The editor document has no byte-authoritative MapEnt source.");
            EditorEntity entity = document.Entities.LastOrDefault() ??
                throw new InvalidOperationException(
                    "The document has no final MapEnt row to remove.");
            MapEntsCardinalityEdit edit =
                source.Syntax.PrepareFinalScriptOriginRemoval(
                    entity.SyntaxOrdinal);
            MapEntityCardinalityAssessment assessment =
                AppendScriptOriginEntityCommand.Assess(
                    edit,
                    MapEntityCardinalityOperation.Remove);
            if (!assessment.IsPatchAuthorized)
            {
                throw new InvalidOperationException(
                    "Final script_origin removal is not authorized: " +
                    assessment.Evidence);
            }
            return new CommandArguments(
                document,
                source.CurrentDigest,
                entity.Id,
                assessment);
        });
    }

    private sealed record CommandArguments(
        EditorMapDocument Document,
        string ExpectedSourceDigest,
        MapObjectId EntityId,
        MapEntityCardinalityAssessment Assessment);
}

/// <summary>
/// Replaces one existing MapEnt key or value by stable entity/property
/// ordinals. It never changes entity or property cardinality.
/// </summary>
public sealed class SetMapEntityPropertyCommand : MapEditCommand
{
    private readonly EditorMapDocument _originatingDocument;

    public SetMapEntityPropertyCommand(
        EditorMapDocument document,
        MapObjectId entityId,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string replacement)
        : this(CreateArguments(
            document,
            entityId,
            propertyOrdinal,
            field,
            replacement))
    {
    }

    private SetMapEntityPropertyCommand(CommandArguments arguments)
        : base(
            MapEditCommandId.New(),
            arguments.Description,
            MapEditKind.MapEntityKeyValue,
            arguments.Impact,
            arguments.TargetObjects)
    {
        _originatingDocument = arguments.OriginatingDocument;
        ExpectedDocumentId = arguments.ExpectedDocumentId;
        ExpectedSourceDigest = arguments.ExpectedSourceDigest;
        ExpectedOriginalText = arguments.ExpectedOriginalText;
        EntityId = arguments.EntityId;
        PropertyOrdinal = arguments.PropertyOrdinal;
        Field = arguments.Field;
        Replacement = arguments.Replacement;
        ExpectedBeforeAssessment = arguments.BeforeAssessment;
        ExpectedAfterAssessment = arguments.AfterAssessment;
        ExpectedPropertyEditAssessment =
            arguments.PropertyEditAssessment;
    }

    public MapDocumentId ExpectedDocumentId { get; }
    public string ExpectedSourceDigest { get; }
    public string ExpectedOriginalText { get; }
    public MapObjectId EntityId { get; }
    public MapEntPropertyOrdinal PropertyOrdinal { get; }
    public MapEntPropertyField Field { get; }
    public string Replacement { get; }
    public MapEntityCompilationAssessment ExpectedBeforeAssessment { get; }
    public MapEntityCompilationAssessment ExpectedAfterAssessment { get; }
    public MapEntityPropertyEditAssessment
        ExpectedPropertyEditAssessment { get; }

    internal override PreparedMapEdit Prepare(EditorMapDocument document)
    {
        if (!ReferenceEquals(document, _originatingDocument) ||
            document.Id != ExpectedDocumentId)
        {
            throw new InvalidOperationException(
                "The MapEnt command belongs to another semantic document instance.");
        }

        EditorEntity entity =
            document.GetRequiredObject<EditorEntity>(EntityId);
        EditorMapEntitySource source = document.EntitySource ??
            throw new InvalidOperationException(
                "The editor document has no byte-authoritative MapEnt source.");
        if (!string.Equals(
                source.CurrentDigest,
                ExpectedSourceDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The MapEnt command is stale because the byte-authoritative source snapshot changed after command creation.");
        }
        if (!source.Syntax.CanEdit)
        {
            throw new InvalidOperationException(
                "The MapEnt syntax source failed strict validation and cannot be edited.");
        }

        EditorEntityProperty currentProperty =
            entity.GetProperty(PropertyOrdinal);
        MapEntsSyntaxProperty currentSyntaxProperty =
            source.Syntax.GetProperty(
                entity.SyntaxOrdinal,
                PropertyOrdinal);
        string currentSemanticText = Field switch
        {
            MapEntPropertyField.Key => currentProperty.Key,
            MapEntPropertyField.Value => currentProperty.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(Field))
        };
        string currentSyntaxText = Field switch
        {
            MapEntPropertyField.Key => currentSyntaxProperty.Key,
            MapEntPropertyField.Value => currentSyntaxProperty.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(Field))
        };
        if (!string.Equals(
                currentSemanticText,
                ExpectedOriginalText,
                StringComparison.Ordinal) ||
            !string.Equals(
                currentSyntaxText,
                ExpectedOriginalText,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The MapEnt command is stale because its target field no longer matches the exact text captured at command creation.");
        }

        MapEntityPropertyEditAssessment currentPropertyAssessment =
            AssessPropertyEdit(
                entity,
                currentProperty,
                Field,
                Replacement);
        if (currentPropertyAssessment != ExpectedPropertyEditAssessment)
        {
            throw new InvalidOperationException(
                "The MapEnt command's exact property-operation consumer " +
                "evidence is stale or inconsistent.");
        }

        MapEntityCompilationAssessment currentAssessment =
            Assess(entity.KeyValues);
        if (entity.CompilationAssessment !=
                ExpectedBeforeAssessment ||
            currentAssessment !=
                ExpectedBeforeAssessment)
        {
            throw new InvalidOperationException(
                "The MapEnt command's supplied consumer assessment does not match an independent classification of the current entity.");
        }

        MapEntityCompilationAssessment afterAssessment =
            Assess(ProjectPairs(
                entity,
                PropertyOrdinal,
                Field,
                Replacement));
        if (afterAssessment != ExpectedAfterAssessment)
        {
            throw new InvalidOperationException(
                "The MapEnt command's projected consumer assessment is stale or inconsistent.");
        }

        MapEntsPropertyEdit syntaxEdit =
            source.Syntax.PreparePropertyReplacement(
                entity.SyntaxOrdinal,
                PropertyOrdinal,
                Field,
                Replacement);
        if (syntaxEdit.IsNoChange)
            return PreparedMapEdit.NoChange(this);

        EditorEntityState[] beforeStates = document.Entities
            .Select(value => value.CaptureState())
            .ToArray();
        var afterStates =
            new EditorEntityState[document.Entities.Count];
        for (int index = 0;
             index < document.Entities.Count;
             index++)
        {
            EditorEntity current = document.Entities[index];
            MapEntityCompilationAssessment verifiedBefore =
                Assess(current.KeyValues);
            if (verifiedBefore != current.CompilationAssessment)
            {
                throw new InvalidOperationException(
                    $"Semantic MapEnt entity {current.SyntaxOrdinal} has stale consumer evidence.");
            }

            MapEntsSyntaxEntity projectedSyntax =
                syntaxEdit.After.Entities[index];
            MapEntityCompilationAssessment projectedAssessment =
                Assess(projectedSyntax.Properties.Select(value =>
                    new KeyValuePair<string, string>(
                        value.Key,
                        value.Value)));
            afterStates[index] = current.ProjectState(
                projectedSyntax,
                projectedAssessment);
        }
        if (afterStates[entity.SyntaxOrdinal.Value]
                .CompilationAssessment != ExpectedAfterAssessment)
        {
            throw new InvalidOperationException(
                "The prepared MapEnt syntax projection does not match the command's classified result.");
        }

        SourceBindingId editedBinding = Field switch
        {
            MapEntPropertyField.Key => currentProperty.KeySourceBinding,
            MapEntPropertyField.Value => currentProperty.ValueSourceBinding,
            _ => throw new ArgumentOutOfRangeException(nameof(Field))
        };
        return new PreparedMapEdit(
            this,
            new MapPendingEdit(
                Description,
                Kind,
                [editedBinding]),
            [
                new MapEntitySyntaxMutation(
                    document,
                    syntaxEdit.Before,
                    syntaxEdit.After,
                    beforeStates,
                    afterStates)
            ]);
    }

    private static CommandArguments CreateArguments(
        EditorMapDocument document,
        MapObjectId entityId,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Enum.IsDefined(field))
            throw new ArgumentOutOfRangeException(nameof(field));

        return document.ReadConsistent(_ => CreateArgumentsConsistent(
            document,
            entityId,
            propertyOrdinal,
            field,
            replacement));
    }

    private static CommandArguments CreateArgumentsConsistent(
        EditorMapDocument document,
        MapObjectId entityId,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string replacement)
    {
        EditorEntity entity =
            document.GetRequiredObject<EditorEntity>(entityId);
        if (entity.SourceOrdinal.Provenance ==
            MapValueProvenance.Authored)
        {
            throw new InvalidOperationException(
                "Follow-up property edits on an authored script_origin are " +
                "outside the narrow cardinality persistence slice. Remove and " +
                "re-append the row with the desired canonical definition.");
        }
        EditorMapEntitySource source = document.EntitySource ??
            throw new InvalidOperationException(
                "The editor document has no byte-authoritative MapEnt source.");
        EditorEntityProperty property =
            entity.GetProperty(propertyOrdinal);
        MapEntsSyntaxProperty syntaxProperty =
            source.Syntax.GetProperty(
                entity.SyntaxOrdinal,
                propertyOrdinal);
        string originalText = field switch
        {
            MapEntPropertyField.Key => property.Key,
            MapEntPropertyField.Value => property.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        string originalSyntaxText = field switch
        {
            MapEntPropertyField.Key => syntaxProperty.Key,
            MapEntPropertyField.Value => syntaxProperty.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        if (!string.Equals(
                originalText,
                originalSyntaxText,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The semantic MapEnt property does not match its byte-authoritative syntax source.");
        }
        MapEntityCompilationAssessment before =
            entity.CompilationAssessment;
        MapEntityCompilationAssessment after = Assess(ProjectPairs(
            entity,
            propertyOrdinal,
            field,
            replacement));
        MapEntityPropertyEditAssessment propertyEditAssessment =
            AssessPropertyEdit(
                entity,
                property,
                field,
                replacement);
        bool changesLength = replacement.Length !=
            (field == MapEntPropertyField.Key
                ? property.KeyContentSpan.Length
                : property.ValueContentSpan.Length);
        MapObjectId[] targets = document.Entities
            .Where(value =>
                value.Id == entityId ||
                (changesLength &&
                 value.SyntaxOrdinal.Value >
                 entity.SyntaxOrdinal.Value))
            .Select(value => value.Id)
            .ToArray();
        return new CommandArguments(
            document,
            document.Id,
            source.CurrentDigest,
            originalText,
            entityId,
            propertyOrdinal,
            field,
            replacement,
            before,
            after,
            propertyEditAssessment,
            CreateImpact(
                before,
                after,
                propertyEditAssessment),
            targets,
            $"Set MapEnt entity {entity.SyntaxOrdinal} property " +
            $"{propertyOrdinal} {field.ToString().ToLowerInvariant()}");
    }

    private static MapEditImpact CreateImpact(
        MapEntityCompilationAssessment before,
        MapEntityCompilationAssessment after,
        MapEntityPropertyEditAssessment propertyEdit)
    {
        if (before.Relationship ==
                MapEntityCompilationRelationship.CompiledCounterpart ||
            after.Relationship ==
                MapEntityCompilationRelationship.CompiledCounterpart ||
            propertyEdit.Relationship ==
                MapEntityCompilationRelationship.CompiledCounterpart)
        {
            return new MapEditImpact(
                MapSaveClassification.PartialRebuildRequired,
                [
                    MapAssetKind.MapEnts,
                    MapAssetKind.GfxMap,
                    MapAssetKind.ColMapSp,
                    MapAssetKind.ColMapMp,
                    MapAssetKind.GameMapMp
                ],
                MapDerivedSubsystem
                    .MapEntBrushModelAndEntityIndices |
                MapDerivedSubsystem.DependenciesSidecarsAndChecksums,
                "MapEnt property persistence requires unavailable compiled " +
                $"counterpart rebuilds. Before: {before.Evidence} After: " +
                $"{after.Evidence} Property operation: " +
                propertyEdit.Evidence);
        }

        if (before.Relationship ==
                MapEntityCompilationRelationship.Unknown ||
            after.Relationship ==
                MapEntityCompilationRelationship.Unknown ||
            !propertyEdit.IsPatchAuthorized)
        {
            return new MapEditImpact(
                MapSaveClassification.Unsupported,
                [MapAssetKind.MapEnts],
                MapDerivedSubsystem.None,
                "MapEnt property persistence is unsupported because the " +
                "exact entity/property consumer relationship is unknown. " +
                $"Before: {before.Evidence} After: {after.Evidence} " +
                $"Property operation: {propertyEdit.Evidence}");
        }

        return new MapEditImpact(
            MapSaveClassification.PatchSaveable,
            [MapAssetKind.MapEnts],
            MapDerivedSubsystem.None,
            saveBlocker: null);
    }

    private static MapEntityCompilationAssessment Assess(
        IEnumerable<EditorEntityProperty> properties) =>
        Assess(properties.Select(value =>
            new KeyValuePair<string, string>(
                value.Key,
                value.Value)));

    private static MapEntityCompilationAssessment Assess(
        IEnumerable<KeyValuePair<string, string>> properties) =>
        MapEntityConsumerCatalog.ConservativeIw4.Classify(properties);

    private static MapEntityPropertyEditAssessment AssessPropertyEdit(
        EditorEntity entity,
        EditorEntityProperty property,
        MapEntPropertyField field,
        string replacement)
    {
        MapEntityPropertyEditOperation operation = field switch
        {
            MapEntPropertyField.Value =>
                MapEntityPropertyEditOperation.ReplaceValue,
            MapEntPropertyField.Key =>
                MapEntityPropertyEditOperation.ReplaceKey,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        return MapEntityConsumerCatalog.ConservativeIw4
            .ClassifyExistingPropertyEdit(
                entity.KeyValues.Select(value =>
                    new KeyValuePair<string, string>(
                        value.Key,
                        value.Value)),
                property.Key,
                operation,
                field == MapEntPropertyField.Key
                    ? replacement
                    : null);
    }

    private static KeyValuePair<string, string>[] ProjectPairs(
        EditorEntity entity,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string replacement)
    {
        EditorEntityProperty selected =
            entity.GetProperty(propertyOrdinal);
        return entity.KeyValues
            .Select(value => value.Ordinal == selected.Ordinal
                ? field switch
                {
                    MapEntPropertyField.Key =>
                        new KeyValuePair<string, string>(
                            replacement,
                            value.Value),
                    MapEntPropertyField.Value =>
                        new KeyValuePair<string, string>(
                            value.Key,
                            replacement),
                    _ => throw new ArgumentOutOfRangeException(nameof(field))
                }
                : new KeyValuePair<string, string>(
                    value.Key,
                    value.Value))
            .ToArray();
    }

    private sealed record CommandArguments(
        EditorMapDocument OriginatingDocument,
        MapDocumentId ExpectedDocumentId,
        string ExpectedSourceDigest,
        string ExpectedOriginalText,
        MapObjectId EntityId,
        MapEntPropertyOrdinal PropertyOrdinal,
        MapEntPropertyField Field,
        string Replacement,
        MapEntityCompilationAssessment BeforeAssessment,
        MapEntityCompilationAssessment AfterAssessment,
        MapEntityPropertyEditAssessment PropertyEditAssessment,
        MapEditImpact Impact,
        IReadOnlyList<MapObjectId> TargetObjects,
        string Description);
}

internal interface IMapEditMutation
{
    void Apply();
    void Revert();
}

internal sealed class PreparedMapEdit
{
    private readonly IReadOnlyList<IMapEditMutation> _mutations;

    public PreparedMapEdit(
        MapEditCommand command,
        MapPendingEdit pendingEdit,
        IEnumerable<IMapEditMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(pendingEdit);
        ArgumentNullException.ThrowIfNull(mutations);

        IMapEditMutation[] copy = mutations.ToArray();
        if (copy.Length == 0 || copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "A prepared map edit must contain at least one mutation.",
                nameof(mutations));
        }

        Command = command;
        PendingEdit = pendingEdit;
        _mutations = Array.AsReadOnly(copy);
    }

    private PreparedMapEdit(MapEditCommand command)
    {
        Command = command;
        PendingEdit = new MapPendingEdit(command.Description, command.Kind);
        _mutations = Array.Empty<IMapEditMutation>();
    }

    public MapEditCommand Command { get; }
    public MapPendingEdit PendingEdit { get; }
    public bool IsNoChange => _mutations.Count == 0;

    public static PreparedMapEdit NoChange(MapEditCommand command) =>
        new(command);

    public void Apply()
    {
        int applied = 0;
        try
        {
            for (; applied < _mutations.Count; applied++)
                _mutations[applied].Apply();
        }
        catch
        {
            for (int index = applied - 1; index >= 0; index--)
                _mutations[index].Revert();

            throw;
        }
    }

    public void Revert()
    {
        int reverted = 0;
        try
        {
            for (int index = _mutations.Count - 1; index >= 0; index--)
            {
                _mutations[index].Revert();
                reverted++;
            }
        }
        catch
        {
            int firstRevertedIndex = _mutations.Count - reverted;
            for (int index = firstRevertedIndex;
                 index < _mutations.Count;
                 index++)
            {
                _mutations[index].Apply();
            }

            throw;
        }
    }
}

internal sealed class MapEntitySyntaxMutation : IMapEditMutation
{
    private readonly EditorMapDocument _document;
    private readonly MapEntsSyntaxDocument _beforeSyntax;
    private readonly MapEntsSyntaxDocument _afterSyntax;
    private readonly IReadOnlyList<EditorEntityState> _beforeStates;
    private readonly IReadOnlyList<EditorEntityState> _afterStates;

    public MapEntitySyntaxMutation(
        EditorMapDocument document,
        MapEntsSyntaxDocument beforeSyntax,
        MapEntsSyntaxDocument afterSyntax,
        IReadOnlyList<EditorEntityState> beforeStates,
        IReadOnlyList<EditorEntityState> afterStates)
    {
        _document = document;
        _beforeSyntax = beforeSyntax;
        _afterSyntax = afterSyntax;
        _beforeStates = beforeStates;
        _afterStates = afterStates;
    }

    public void Apply() =>
        _document.ApplyEntitySyntaxState(
            _beforeSyntax,
            _afterSyntax,
            _beforeStates,
            _afterStates);

    public void Revert() =>
        _document.ApplyEntitySyntaxState(
            _afterSyntax,
            _beforeSyntax,
            _afterStates,
            _beforeStates);
}

internal sealed class MapEntityCollectionMutation : IMapEditMutation
{
    private readonly EditorMapDocument _document;
    private readonly EditorEntityCollectionState _before;
    private readonly EditorEntityCollectionState _after;

    public MapEntityCollectionMutation(
        EditorMapDocument document,
        EditorEntityCollectionState before,
        EditorEntityCollectionState after)
    {
        _document =
            document ?? throw new ArgumentNullException(nameof(document));
        _before =
            before ?? throw new ArgumentNullException(nameof(before));
        _after =
            after ?? throw new ArgumentNullException(nameof(after));
    }

    public void Apply() =>
        _document.ApplyEntityCollectionState(_before, _after);

    public void Revert() =>
        _document.ApplyEntityCollectionState(_after, _before);
}

internal sealed class PrimaryLightColorMutation : IMapEditMutation
{
    private readonly EditorPrimaryLight _light;
    private readonly MapVector3 _before;
    private readonly MapVector3 _after;

    public PrimaryLightColorMutation(
        EditorPrimaryLight light,
        MapVector3 before,
        MapVector3 after)
    {
        _light = light;
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _light.SetColor(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _light.SetColor(_before);
    }

    private void RequireCurrent(MapVector3 expected, string operation)
    {
        if (_light.Color.Value != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} primary-light color command because its semantic value changed outside the command journal.");
        }
    }
}

internal sealed class PrimaryLightExponentMutation : IMapEditMutation
{
    private readonly EditorPrimaryLight _light;
    private readonly byte _before;
    private readonly byte _after;

    public PrimaryLightExponentMutation(
        EditorPrimaryLight light,
        byte before,
        byte after)
    {
        _light = light;
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _light.SetExponent(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _light.SetExponent(_before);
    }

    private void RequireCurrent(byte expected, string operation)
    {
        if (_light.Exponent.Value != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} primary-light exponent command because " +
                "its semantic value changed outside the command journal.");
        }
    }
}

internal sealed class PrimaryLightSpotFalloffMutation : IMapEditMutation
{
    private readonly EditorPrimaryLight _light;
    private readonly float _before;
    private readonly float _after;

    public PrimaryLightSpotFalloffMutation(
        EditorPrimaryLight light,
        float before,
        float after)
    {
        _light = light;
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _light.SetCosHalfFovInner(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _light.SetCosHalfFovInner(_before);
    }

    private void RequireCurrent(float expected, string operation)
    {
        if (BitConverter.SingleToInt32Bits(
                _light.CosHalfFovInner.Value) !=
            BitConverter.SingleToInt32Bits(expected))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} primary-light spot-falloff command " +
                "because its semantic value changed outside the command " +
                "journal.");
        }
    }
}

internal sealed class FxGlassDefinitionHalfThicknessMutation
    : IMapEditMutation
{
    private readonly EditorFxGlassDefinitionState _definition;
    private readonly float _before;
    private readonly float _after;

    public FxGlassDefinitionHalfThicknessMutation(
        EditorFxGlassDefinitionState definition,
        float before,
        float after)
    {
        _definition =
            definition ?? throw new ArgumentNullException(nameof(definition));
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _definition.SetCurrentHalfThickness(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _definition.SetCurrentHalfThickness(_before);
    }

    private void RequireCurrent(float expected, string operation)
    {
        if (BitConverter.SingleToInt32Bits(
                _definition.CurrentHalfThickness) !=
            BitConverter.SingleToInt32Bits(expected))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} FX glass definition HalfThickness " +
                "because its semantic value changed outside the command " +
                "journal.");
        }
    }
}

internal sealed class FxGlassDefinitionColorMutation : IMapEditMutation
{
    private readonly EditorFxGlassDefinitionState _definition;
    private readonly uint _before;
    private readonly uint _after;

    public FxGlassDefinitionColorMutation(
        EditorFxGlassDefinitionState definition,
        uint before,
        uint after)
    {
        _definition =
            definition ?? throw new ArgumentNullException(nameof(definition));
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _definition.SetCurrentColor(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _definition.SetCurrentColor(_before);
    }

    private void RequireCurrent(uint expected, string operation)
    {
        if (_definition.CurrentColor != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} FX glass definition color because its " +
                "semantic value changed outside the command journal.");
        }
    }
}

internal sealed class StaticModelTransformMutation : IMapEditMutation
{
    private readonly EditorStaticModel _model;
    private readonly EditorStaticModelTransformState _before;
    private readonly EditorStaticModelTransformState _after;

    public StaticModelTransformMutation(
        EditorStaticModel model,
        EditorStaticModelTransformState before,
        EditorStaticModelTransformState after)
    {
        _model = model;
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _model.SetTransform(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _model.SetTransform(_before);
    }

    private void RequireCurrent(
        EditorStaticModelTransformState expected,
        string operation)
    {
        if (!_model.HasTransform(expected))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} static-model transform command because its semantic value changed outside the command journal.");
        }
    }
}

internal sealed class EditorVisibilityMutation : IMapEditMutation
{
    private readonly EditorMapObject _target;
    private readonly EditorObjectVisibility _before;
    private readonly EditorObjectVisibility _after;

    public EditorVisibilityMutation(
        EditorMapObject target,
        EditorObjectVisibility before,
        EditorObjectVisibility after)
    {
        _target = target;
        _before = before;
        _after = after;
    }

    public void Apply()
    {
        RequireCurrent(_before, "apply");
        _target.SetEditorVisibility(_after);
    }

    public void Revert()
    {
        RequireCurrent(_after, "revert");
        _target.SetEditorVisibility(_before);
    }

    private void RequireCurrent(
        EditorObjectVisibility expected,
        string operation)
    {
        if (_target.Visibility != expected)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} editor-visibility command because its semantic value changed outside the command journal.");
        }
    }
}
