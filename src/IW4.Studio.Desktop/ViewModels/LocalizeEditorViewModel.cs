using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Desktop façade over one Localize editor session. The localized value is
/// staged in the text buffer until Apply; the key remains locked because it
/// is the serialized row identity.
/// </summary>
public sealed class LocalizeEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorDiagnostics,
      IAssetEditorStagingState
{
    private readonly AssetEditorSession _editorSession;
    private LocalizeDraft? _draft;
    private LocalizeReadOnlySnapshot? _readOnlySnapshot;
    private string _valueInput = string.Empty;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];

    public LocalizeEditorViewModel(AssetEditorSession editorSession)
    {
        _editorSession = editorSession
            ?? throw new ArgumentNullException(nameof(editorSession));
        if (editorSession.Entry.AssetType != XAssetType.Localize)
        {
            throw new InvalidDataException(
                "The Localize view model can host only Localize editor sessions.");
        }

        switch (editorSession.Mode)
        {
            case AssetEditorMode.Editable:
                _draft = editorSession.OpenDraft<LocalizeDraft>();
                _valueInput = DisplayValue(_draft.Value);
                _diagnostics = editorSession.Validation.Issues;
                _statusMessage =
                    "Value edits are staged until Apply. The key remains locked as row identity.";
                break;

            case AssetEditorMode.ReadOnly:
                try
                {
                    _readOnlySnapshot =
                        LocalizeReadOnlySnapshot.CaptureResolvedProvider(
                            editorSession);
                    _valueInput = DisplayValue(_readOnlySnapshot.Value);
                    _statusMessage =
                        "Detached read-only copy of the catalog-resolved provider.";
                }
                catch (InvalidDataException exception)
                {
                    _statusMessage = exception.Message;
                    _diagnostics =
                    [
                        new AssetValidationIssue(
                            "provider",
                            exception.Message,
                            AssetValidationSeverity.Error)
                    ];
                }
                break;

            case AssetEditorMode.ContentUnavailable:
                _statusMessage =
                    "Localize content is unavailable because this reference has no resolved provider.";
                break;

            default:
                throw new InvalidDataException(
                    $"Unknown Localize editor mode '{editorSession.Mode}'.");
        }
    }

    public AssetEditorMode Mode => _editorSession.Mode;
    public bool IsEditable => Mode == AssetEditorMode.Editable;
    public bool IsInputReadOnly => !IsEditable;
    public bool CanApply =>
        IsEditable &&
        _draft is not null &&
        !string.Equals(
            ValueInput,
            DisplayValue(_draft.Value),
            StringComparison.Ordinal);
    public bool HasUnappliedChanges => CanApply;
    public bool CanRevert => IsEditable;

    public string OriginalName =>
        _draft?.Name
        ?? _readOnlySnapshot?.Name
        ?? _editorSession.Entry.OriginalName
        ?? string.Empty;

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _valueInput, value))
                return;

            NotifyStagingStateChanged();
            OnPropertyChanged(nameof(EditorProperties));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public IReadOnlyList<AssetValidationIssue> Diagnostics
    {
        get => _diagnostics;
        private set
        {
            _diagnostics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsSummary));
        }
    }

    public bool HasDiagnostics => Diagnostics.Count != 0;

    public string DiagnosticsSummary => string.Join(
        Environment.NewLine,
        Diagnostics.Select(issue =>
            $"{issue.Severity}: {issue.FieldPath} — {issue.Message}"));

    public string PropertySectionName => "Localize";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
    [
        new("Characters", ValueInput.Length.ToString("N0")),
        new(
            "Applied storage",
            CurrentStoredValue() is null ? "NULL" : "String"),
        new("Key", "Locked row identity")
    ];

    public void ApplyChanges()
    {
        if (!CanApply || _draft is null)
        {
            StatusMessage = IsEditable
                ? "There is no staged Localize change to apply."
                : "This Localize value is read-only or unavailable.";
            return;
        }

        string valueSnapshot = ValueInput;
        try
        {
            _draft = _editorSession.ApplyAndRead<LocalizeDraft>(
                draft => draft.SetValue(valueSnapshot),
                out bool changed);

            Diagnostics = _editorSession.Validation.Issues;
            ValueInput = DisplayValue(_draft.Value);
            StatusMessage = changed
                ? "Applied the localized value."
                : "The localized value already matched the current draft.";
            NotifyStagingStateChanged();
            OnPropertyChanged(nameof(EditorProperties));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            StatusMessage = exception.Message;
        }
    }

    public void RevertDraft()
    {
        if (!CanRevert)
        {
            StatusMessage =
                "Read-only Localize content cannot be reverted because it has no target-owned draft.";
            return;
        }

        _ = _editorSession.Revert();
        _draft = _editorSession.ReadDraft<LocalizeDraft>();
        Diagnostics = _editorSession.Validation.Issues;
        ValueInput = DisplayValue(_draft.Value);
        StatusMessage =
            "Reverted the detached Localize draft to its authored baseline.";
        NotifyStagingStateChanged();
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void NotifyStagingStateChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(HasUnappliedChanges));
    }

    private string? CurrentStoredValue() => _draft is not null
        ? _draft.Value
        : _readOnlySnapshot?.Value;

    private static string DisplayValue(string? value) => value ?? string.Empty;
}
