using System.ComponentModel;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors;

/// <summary>
/// One explicit, display-ready property contributed by a concrete asset
/// editor to the shared workbench Properties pane.
/// </summary>
public sealed record AssetEditorProperty(string Name, string Value);

/// <summary>
/// Opt-in metadata contract for editor-specific properties. The Properties
/// pane observes changes without reflecting over arbitrary editor view models.
/// </summary>
public interface IAssetEditorProperties : INotifyPropertyChanged
{
    string PropertySectionName { get; }

    IReadOnlyList<AssetEditorProperty> EditorProperties { get; }
}

/// <summary>
/// Opt-in validation contract for hosted editors. The workbench diagnostics
/// pane can observe this without knowing each concrete editor view model.
/// </summary>
public interface IAssetEditorDiagnostics : INotifyPropertyChanged
{
    IReadOnlyList<AssetValidationIssue> Diagnostics { get; }
}

/// <summary>
/// Opt-in state for input that still belongs to an editor view and has not
/// been applied to the session-owned asset draft. Tab lifetime follows this
/// state; whole-fastfile persistence continues to follow the editing session.
/// </summary>
public interface IAssetEditorStagingState : INotifyPropertyChanged
{
    bool HasUnappliedChanges { get; }
}
