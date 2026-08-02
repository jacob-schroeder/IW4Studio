using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>Detached Localize presentation; the key is intentionally locked as row identity.</summary>
public sealed class LocalizeEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private LocalizeDraft? _draft;
    private string _valueInput = string.Empty;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<AssetValidationIssue> _diagnostics = [];

    public LocalizeEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.Localize)
            throw new InvalidDataException("The Localize view model can host only Localize sessions.");
        if (session.Mode == AssetEditorMode.Editable)
        {
            _draft = session.OpenDraft<LocalizeDraft>();
            _valueInput = _draft.Value ?? string.Empty;
            _diagnostics = session.Validation.Issues;
            _statusMessage = "Detached target-owned Localize draft. The key is locked because it is row identity.";
        }
        else
        {
            _statusMessage = session.Mode == AssetEditorMode.ReadOnly
                ? "Resolved dependency Localize content is read-only."
                : "Localize content is unavailable because this target row is unresolved.";
        }
    }

    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public bool IsInputReadOnly => !IsEditable;
    public string Key => _draft?.Name ?? _session.Entry.OriginalName ?? string.Empty;
    public string KeyPolicy => "Key/rename is locked because it defines serialized row identity and identity updates are not supported.";
    public string ValueInput { get => _valueInput; set => SetProperty(ref _valueInput, value ?? string.Empty); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public IReadOnlyList<AssetValidationIssue> Diagnostics { get => _diagnostics; private set { _diagnostics = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiagnosticsSummary)); } }
    public string DiagnosticsSummary => string.Join(Environment.NewLine, Diagnostics.Select(issue => $"{issue.Severity}: {issue.FieldPath} — {issue.Message}"));

    public void ApplyValue()
    {
        if (!IsEditable) { StatusMessage = "This Localize row is read-only or unavailable."; return; }
        _session.Apply<LocalizeDraft>(draft => draft.SetValue(ValueInput));
        _draft = _session.ReadDraft<LocalizeDraft>();
        Diagnostics = _session.Validation.Issues;
        StatusMessage = "Updated the detached Localize value draft.";
    }
    public void RevertDraft()
    {
        if (!IsEditable) return;
        _session.Revert(); _draft = _session.ReadDraft<LocalizeDraft>();
        ValueInput = _draft.Value ?? string.Empty; Diagnostics = _session.Validation.Issues;
        StatusMessage = "Reverted the Localize draft.";
    }
}
