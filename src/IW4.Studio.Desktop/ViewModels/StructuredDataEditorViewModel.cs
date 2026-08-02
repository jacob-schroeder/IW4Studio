using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>Navigable typed-summary host for the detached StructuredData graph.</summary>
public sealed class StructuredDataEditorViewModel : ObservableObject
{
    private readonly AssetEditorSession _session;
    private StructuredDataDraft? _draft;
    public StructuredDataEditorViewModel(AssetEditorSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (session.Entry.AssetType != IW4.FastFiles.Zone.XAssetType.StructuredDataDef) throw new InvalidDataException("The StructuredData view model can host only StructuredDataDef sessions.");
        if (session.Mode == AssetEditorMode.Editable) _draft = session.OpenDraft<StructuredDataDraft>();
    }
    public bool IsEditable => _session.Mode == AssetEditorMode.Editable;
    public int DefinitionCount => _draft?.BuildData.Definitions.Count ?? 0;
    public string StatusMessage => IsEditable
        ? "Detached StructuredData graph. Stored checksums are preserved; transformations requiring an unknown checksum algorithm are blocked."
        : _session.Mode == AssetEditorMode.ReadOnly ? "Resolved dependency graph is read-only." : "StructuredData content is unavailable.";
    public IReadOnlyList<AssetValidationIssue> Diagnostics => _session.Validation.Issues;
    public void RevertDraft() { if (IsEditable) { _session.Revert(); _draft = _session.ReadDraft<StructuredDataDraft>(); OnPropertyChanged(nameof(DefinitionCount)); OnPropertyChanged(nameof(Diagnostics)); } }
}
