using System.ComponentModel;

namespace IW4.Studio.Desktop.Editors.Inspector;

/// <summary>
/// Opt-in contract for an editor whose local selection contributes editable
/// rows to the shared Properties tool. The editor remains the selection
/// authority; the Properties tool only presents the supplied projection.
/// </summary>
public interface IAssetEditorInspectorSource : INotifyPropertyChanged
{
    InspectorSelectionViewModel? InspectorSelection { get; }
}
