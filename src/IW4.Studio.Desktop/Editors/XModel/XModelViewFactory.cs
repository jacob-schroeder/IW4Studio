using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XModel;

/// <summary>Desktop host for the XModel asset editor.</summary>
public sealed class XModelViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.XModel;

    public AssetEditorViewHost Create(AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        var viewModel = new XModelEditorViewModel(editorSession);
        var view = new XModelEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(view, viewModel);
    }
}
