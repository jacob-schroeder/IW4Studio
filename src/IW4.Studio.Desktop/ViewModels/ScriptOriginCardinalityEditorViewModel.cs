using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Authoring surface for the first executable-proven Phase 6 cardinality
/// invariant: append a canonical script_origin, or remove that exact shape
/// only when it is the physical final MapEnt row.
/// </summary>
public sealed class ScriptOriginCardinalityEditorViewModel :
    ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly Func<bool> _canMutate;
    private readonly Action<IMapEditCommand> _execute;
    private readonly Action _undo;
    private readonly Action<MapObjectId?> _select;
    private decimal _x;
    private decimal _y;
    private decimal _z;
    private string? _validationMessage;

    public ScriptOriginCardinalityEditorViewModel(
        EditorMapDocument document,
        Func<bool> canMutate,
        Action<IMapEditCommand> execute,
        Action undo,
        Action<MapObjectId?> select)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _canMutate = canMutate ??
            throw new ArgumentNullException(nameof(canMutate));
        _execute = execute ??
            throw new ArgumentNullException(nameof(execute));
        _undo = undo ??
            throw new ArgumentNullException(nameof(undo));
        _select = select ??
            throw new ArgumentNullException(nameof(select));
        AppendCommand = new ViewModelCommand(
            Append,
            () => CanAppend);
        RemoveFinalCommand = new ViewModelCommand(
            RemoveFinal,
            () => CanRemoveFinal);
    }

    public decimal X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public decimal Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public decimal Z
    {
        get => _z;
        set => SetProperty(ref _z, value);
    }

    public ViewModelCommand AppendCommand { get; }

    public ViewModelCommand RemoveFinalCommand { get; }

    public bool CanAppend =>
        _canMutate() &&
        TryGetAppendAvailability(out _);

    public bool CanRemoveFinal =>
        _canMutate() &&
        TryGetRemovalAvailability(out _);

    public string ClassificationText =>
        TryGetAppendAvailability(out _) ||
        TryGetRemovalAvailability(out _)
            ? "Patch Saveable"
            : "Unavailable";

    public string EvidenceText
    {
        get
        {
            bool canAppend =
                TryGetAppendAvailability(out string appendBlocker);
            bool canRemove =
                TryGetRemovalAvailability(out string removalBlocker);
            string operationSummary =
                FormatAvailability(
                    "Append",
                    canAppend,
                    appendBlocker) +
                " " +
                FormatAvailability(
                    "Final removal",
                    canRemove,
                    removalBlocker);
            return canAppend || canRemove
                ? "IW4 executable-proven tail invariant: canonical " +
                  "script_origin only. " + operationSummary
                : "Cardinality authoring remains fail-closed. " +
                  operationSummary;
        }
    }

    public string FinalEntityText =>
        _document.Entities.LastOrDefault() is { } entity
            ? $"Final row: #{entity.SyntaxOrdinal.Value} · " +
              (entity.ClassName ?? "unknown classname")
            : "No MapEnt rows are available.";

    public bool HasValidationMessage =>
        !string.IsNullOrWhiteSpace(_validationMessage);

    public string ValidationMessage =>
        _validationMessage ?? string.Empty;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(CanAppend));
        OnPropertyChanged(nameof(CanRemoveFinal));
        OnPropertyChanged(nameof(ClassificationText));
        OnPropertyChanged(nameof(EvidenceText));
        OnPropertyChanged(nameof(FinalEntityText));
        AppendCommand.RaiseCanExecuteChanged();
        RemoveFinalCommand.RaiseCanExecuteChanged();
    }

    private void Append()
    {
        try
        {
            var definition = new ScriptOriginEntityDefinition(
                new MapVector3(
                    ToSingle(X),
                    ToSingle(Y),
                    ToSingle(Z)));
            var command = new AppendScriptOriginEntityCommand(
                _document,
                definition);
            _execute(command);
            _select(command.EntityId);
            SetValidationMessage(null);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            SetValidationMessage(exception.Message);
        }
        finally
        {
            Refresh();
        }
    }

    private void RemoveFinal()
    {
        try
        {
            EditorEntity finalEntity =
                _document.Entities.LastOrDefault() ??
                throw new InvalidOperationException(
                    "The document has no final MapEnt row to remove.");
            if (_document.History.ActiveCommands.LastOrDefault() is
                AppendScriptOriginEntityCommand append &&
                append.EntityId == finalEntity.Id)
            {
                _undo();
            }
            else
            {
                _execute(
                    new RemoveFinalScriptOriginEntityCommand(
                        _document));
            }

            _select(_document.Entities.LastOrDefault()?.Id);
            SetValidationMessage(null);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            SetValidationMessage(exception.Message);
        }
        finally
        {
            Refresh();
        }
    }

    private void SetValidationMessage(string? message)
    {
        if (!SetProperty(
                ref _validationMessage,
                message,
                nameof(ValidationMessage)))
        {
            return;
        }

        OnPropertyChanged(nameof(HasValidationMessage));
    }

    private bool TryGetAppendAvailability(out string blocker)
    {
        if (_document.EntitySource is not { } source)
        {
            blocker = "The map document has no byte-authoritative MapEnt source.";
            return false;
        }

        return source.Syntax.CanAppendScriptOrigin(out blocker);
    }

    private bool TryGetRemovalAvailability(out string blocker)
    {
        if (_document.EntitySource is not { } source)
        {
            blocker = "The map document has no byte-authoritative MapEnt source.";
            return false;
        }

        return source.Syntax.CanRemoveFinalScriptOrigin(out blocker);
    }

    private static float ToSingle(decimal value)
    {
        float result = checked((float)value);
        if (!float.IsFinite(result))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "script_origin coordinates must be finite.");
        }

        return result;
    }

    private static string FormatAvailability(
        string operation,
        bool isAvailable,
        string blocker) =>
        isAvailable
            ? $"{operation} available."
            : $"{operation} unavailable: {blocker}";
}
