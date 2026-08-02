using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Inspector projection for one byte-backed MapEnt entity. Property rows keep
/// their syntax ordinals so duplicate keys remain individually addressable.
/// </summary>
public sealed class MapEntityInspectorViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly EditorEntity _entity;
    private readonly EditorMapEntitySource _source;
    private readonly IReadOnlyList<MapEntityPropertyRowViewModel> _properties;
    private string _saveClassificationText = "No Editable Properties";
    private IReadOnlyList<string> _saveBlockers = [];
    private int _patchSaveablePotentialOperationCount;
    private int _potentialOperationCount;
    private bool _showsPotentialOperationSummary;

    public MapEntityInspectorViewModel(
        EditorMapDocument document,
        EditorEntity entity,
        MapEditorEditingContext editingContext,
        Action<IMapEditCommand> execute)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        ArgumentNullException.ThrowIfNull(editingContext);
        ArgumentNullException.ThrowIfNull(execute);
        if (!ReferenceEquals(editingContext.Document, _document))
        {
            throw new ArgumentException(
                "The editing context belongs to another map document.",
                nameof(editingContext));
        }
        if (!_document.TryGetObject(entity.Id, out EditorMapObject? owned) ||
            !ReferenceEquals(owned, entity))
        {
            throw new ArgumentException(
                "The selected entity belongs to another map document.",
                nameof(entity));
        }

        _source = _document.EntitySource ??
            throw new ArgumentException(
                "The selected entity has no byte-authoritative MapEnt source.",
                nameof(document));
        _properties = Array.AsReadOnly(
            entity.KeyValues
                .Select(property => new MapEntityPropertyRowViewModel(
                    document,
                    entity,
                    property.Ordinal,
                    editingContext,
                    execute))
                .ToArray());
        RefreshImpact();
    }

    public IReadOnlyList<MapEntityPropertyRowViewModel> Properties =>
        _properties;

    public string EntityOrdinalText =>
        $"MapEnt entity #{_entity.SyntaxOrdinal.Value}";

    public string SourceByteSpanText
    {
        get
        {
            MapEntSourceSpan span = _source.Syntax
                .GetEntity(_entity.SyntaxOrdinal)
                .Span;
            return $"Exact source bytes [{span.Offset}, {span.End}) · " +
                $"{span.Length:N0} bytes";
        }
    }

    public string ByteProvenanceText =>
        $"Exact serialized MapEnts · baseline {_source.BaselineDigest}";

    public string RelationshipText =>
        SplitWords(_entity.CompilationAssessment.Relationship.ToString());

    public string EvidenceText =>
        _entity.CompilationAssessment.Evidence;

    public string SaveClassificationText => _saveClassificationText;

    public bool HasSaveBlocker =>
        !CanEditProperties ||
        _saveBlockers.Count != 0;

    public bool HasNoSaveBlocker => !HasSaveBlocker;

    public string SaveBlockerText
    {
        get
        {
            if (!_source.Syntax.CanEdit)
            {
                return _source.Syntax.Diagnostics.Count == 0
                    ? "The MapEnt syntax snapshot is not safely editable."
                    : string.Join(
                        "; ",
                        _source.Syntax.Diagnostics.Select(value =>
                            $"{value.Code} at byte {value.Span.Offset}: " +
                    value.Message));
            }

            if (IsAuthoredEntity)
            {
                return
                    "The authored script_origin remains patch-saveable " +
                    "through its append command. Follow-up field edits are " +
                    "outside this narrow persistence slice; remove and " +
                    "re-append the row with the desired canonical definition.";
            }

            if (_properties.Count == 0)
            {
                return "This entity has no parsed key/value property to edit.";
            }

            if (_showsPotentialOperationSummary)
            {
                return
                    $"{_patchSaveablePotentialOperationCount} of " +
                    $"{_potentialOperationCount} current key/value field " +
                    "operations are patch-saveable. Each replacement is " +
                    "reclassified against its exact classname, key, and " +
                    "operation; restricted fields remain fail-closed.";
            }

            return _saveBlockers.Count == 0
                ? "No save blocker for existing property edits."
                : string.Join(" ", _saveBlockers);
        }
    }

    public bool CanEditProperties =>
        _source.Syntax.CanEdit &&
        _properties.Count != 0 &&
        !IsAuthoredEntity;

    private bool IsAuthoredEntity =>
        _entity.SourceOrdinal.Provenance ==
        MapValueProvenance.Authored;

    internal void Refresh()
    {
        foreach (MapEntityPropertyRowViewModel property in _properties)
            property.Refresh();

        RefreshImpact();
        OnPropertyChanged(nameof(EntityOrdinalText));
        OnPropertyChanged(nameof(SourceByteSpanText));
        OnPropertyChanged(nameof(ByteProvenanceText));
        OnPropertyChanged(nameof(RelationshipText));
        OnPropertyChanged(nameof(EvidenceText));
        OnPropertyChanged(nameof(SaveClassificationText));
        OnPropertyChanged(nameof(HasSaveBlocker));
        OnPropertyChanged(nameof(HasNoSaveBlocker));
        OnPropertyChanged(nameof(SaveBlockerText));
        OnPropertyChanged(nameof(CanEditProperties));
    }

    private void RefreshImpact()
    {
        if (IsAuthoredEntity)
        {
            _showsPotentialOperationSummary = false;
            _potentialOperationCount = 0;
            _patchSaveablePotentialOperationCount = 0;
            _saveClassificationText = "Authored Tail Entity";
            _saveBlockers = [];
            return;
        }

        MapEditImpact[] pendingImpacts = _document.History.PendingJournal
            .Select(entry => entry.Command)
            .OfType<SetMapEntityPropertyCommand>()
            .Where(command => command.EntityId == _entity.Id)
            .Select(command => command.Impact)
            .ToArray();
        if (pendingImpacts.Length != 0)
        {
            SetImpactSummary(pendingImpacts);
            return;
        }

        MapEditImpact[] potentialImpacts = _entity.KeyValues
            .SelectMany(property => new[]
            {
                CreateCurrentFieldImpact(
                    property,
                    MapEntPropertyField.Key),
                CreateCurrentFieldImpact(
                    property,
                    MapEntPropertyField.Value)
            })
            .ToArray();
        _potentialOperationCount = potentialImpacts.Length;
        _patchSaveablePotentialOperationCount = potentialImpacts.Count(
            impact =>
                impact.Classification ==
                MapSaveClassification.PatchSaveable);
        _showsPotentialOperationSummary =
            _patchSaveablePotentialOperationCount > 0 &&
            _patchSaveablePotentialOperationCount <
                _potentialOperationCount;
        if (_showsPotentialOperationSummary)
        {
            _saveClassificationText = "Per-property Safety";
            _saveBlockers = [];
            return;
        }

        SetImpactSummary(potentialImpacts);
    }

    private MapEditImpact CreateCurrentFieldImpact(
        EditorEntityProperty property,
        MapEntPropertyField field) =>
        new SetMapEntityPropertyCommand(
            _document,
            _entity.Id,
            property.Ordinal,
            field,
            field == MapEntPropertyField.Key
                ? property.Key
                : property.Value).Impact;

    private void SetImpactSummary(
        IReadOnlyList<MapEditImpact> impacts)
    {
        _showsPotentialOperationSummary = false;
        _potentialOperationCount = 0;
        _patchSaveablePotentialOperationCount = 0;
        _saveClassificationText = impacts.Count == 0
            ? "No Editable Properties"
            : SplitWords(
                impacts
                    .MaxBy(impact =>
                        SaveBlockerPriority(impact.Classification))!
                    .Classification
                    .ToString());
        _saveBlockers = Array.AsReadOnly(
            impacts
                .Select(impact => impact.SaveBlocker)
                .Where(blocker => blocker is not null)
                .Select(blocker => blocker!)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static int SaveBlockerPriority(
        MapSaveClassification classification) =>
        classification switch
        {
            MapSaveClassification.EditorOnly => 0,
            MapSaveClassification.PatchSaveable => 1,
            MapSaveClassification.PartialRebuildRequired => 2,
            MapSaveClassification.FullRebuildRequired => 3,
            MapSaveClassification.Unsupported => 4,
            _ => throw new ArgumentOutOfRangeException(
                nameof(classification))
        };

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
}

/// <summary>
/// One ordered property row. Both fields resolve their current value by the
/// immutable syntax ordinal and can mutate the document only by constructing
/// the closed SetMapEntityProperty command.
/// </summary>
public sealed class MapEntityPropertyRowViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly EditorEntity _entity;
    private readonly MapEditorEditingContext _editingContext;
    private readonly Action<IMapEditCommand> _execute;
    private string? _validationMessage;

    public MapEntityPropertyRowViewModel(
        EditorMapDocument document,
        EditorEntity entity,
        MapEntPropertyOrdinal ordinal,
        MapEditorEditingContext editingContext,
        Action<IMapEditCommand> execute)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _editingContext = editingContext ??
            throw new ArgumentNullException(nameof(editingContext));
        if (!ReferenceEquals(_editingContext.Document, _document))
        {
            throw new ArgumentException(
                "The editing context belongs to another map document.",
                nameof(editingContext));
        }
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        Ordinal = ordinal;
        _ = CurrentProperty;
    }

    public MapEntPropertyOrdinal Ordinal { get; }

    public string OrdinalText => $"#{Ordinal.Value}";

    public string Key
    {
        get => CurrentProperty.Key;
        set => SetField(MapEntPropertyField.Key, value ?? string.Empty);
    }

    public string Value
    {
        get => CurrentProperty.Value;
        set => SetField(MapEntPropertyField.Value, value ?? string.Empty);
    }

    /// <summary>
    /// Immediate UI draft for the key field. Unlike <see cref="Key"/>, this
    /// value survives inspector replacement and is committed at Save As.
    /// </summary>
    public string KeyDraft
    {
        get => _editingContext.ReadPropertyField(
            _entity,
            Ordinal,
            MapEntPropertyField.Key);
        set => SetDraft(
            MapEntPropertyField.Key,
            value ?? string.Empty);
    }

    /// <summary>
    /// Immediate UI draft for the value field. Unlike <see cref="Value"/>, this
    /// value survives inspector replacement and is committed at Save As.
    /// </summary>
    public string ValueDraft
    {
        get => _editingContext.ReadPropertyField(
            _entity,
            Ordinal,
            MapEntPropertyField.Value);
        set => SetDraft(
            MapEntPropertyField.Value,
            value ?? string.Empty);
    }

    public string PropertyByteSpanText =>
        FormatSpan("Pair", CurrentProperty.Span);

    public string KeyProvenanceText =>
        $"{CurrentProperty.KeyProvenance} · " +
        FormatSpan("content", CurrentProperty.KeyContentSpan);

    public string ValueProvenanceText =>
        $"{CurrentProperty.ValueProvenance} · " +
        FormatSpan("content", CurrentProperty.ValueContentSpan);

    public string KeySaveClassificationText =>
        IsAuthoredEntity
            ? "Read only · remove and re-append"
            : $"{FormatClassification(GetCurrentImpact(MapEntPropertyField.Key))}" +
              " · replacement revalidated";

    public string ValueSaveClassificationText =>
        IsAuthoredEntity
            ? "Read only · remove and re-append"
            : FormatClassification(
                GetCurrentImpact(MapEntPropertyField.Value));

    public bool HasValidationError =>
        ValidationMessage.Length != 0;

    public string ValidationMessage =>
        _validationMessage ??
        _editingContext.ReadValidationMessage(
            _entity,
            Ordinal) ??
        string.Empty;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(KeyDraft));
        OnPropertyChanged(nameof(ValueDraft));
        OnPropertyChanged(nameof(PropertyByteSpanText));
        OnPropertyChanged(nameof(KeyProvenanceText));
        OnPropertyChanged(nameof(ValueProvenanceText));
        OnPropertyChanged(nameof(KeySaveClassificationText));
        OnPropertyChanged(nameof(ValueSaveClassificationText));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private EditorEntityProperty CurrentProperty =>
        _entity.GetProperty(Ordinal);

    private bool IsAuthoredEntity =>
        _entity.SourceOrdinal.Provenance ==
        MapValueProvenance.Authored;

    private MapEditImpact GetCurrentImpact(
        MapEntPropertyField field)
    {
        EditorEntityProperty property = CurrentProperty;
        return new SetMapEntityPropertyCommand(
            _document,
            _entity.Id,
            Ordinal,
            field,
            field == MapEntPropertyField.Key
                ? property.Key
                : property.Value).Impact;
    }

    private void SetField(
        MapEntPropertyField field,
        string replacement)
    {
        string current = field == MapEntPropertyField.Key
            ? CurrentProperty.Key
            : CurrentProperty.Value;
        if (string.Equals(current, replacement, StringComparison.Ordinal))
        {
            ClearValidation();
            return;
        }

        try
        {
            _execute(new SetMapEntityPropertyCommand(
                _document,
                _entity.Id,
                Ordinal,
                field,
                replacement));
            ClearValidation();
        }
        catch (MapEntsEditRejectedException exception)
        {
            _validationMessage = exception.Message;
            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(
                field == MapEntPropertyField.Key
                    ? nameof(Key)
                    : nameof(Value));
        }
    }

    private void SetDraft(
        MapEntPropertyField field,
        string replacement)
    {
        _editingContext.SetPropertyDraft(
            _entity,
            Ordinal,
            field,
            replacement);
        ClearValidation();
        OnPropertyChanged(
            field == MapEntPropertyField.Key
                ? nameof(KeyDraft)
                : nameof(ValueDraft));
    }

    private void ClearValidation()
    {
        if (_validationMessage is null)
            return;

        _validationMessage = null;
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private static string FormatSpan(
        string label,
        MapEntSourceSpan span) =>
        $"{label} bytes [{span.Offset}, {span.End})";

    private static string FormatClassification(
        MapEditImpact impact) =>
        string.Concat(
            impact.Classification
                .ToString()
                .Select((character, index) =>
                    index > 0 && char.IsUpper(character)
                        ? $" {character}"
                        : character.ToString()));
}
