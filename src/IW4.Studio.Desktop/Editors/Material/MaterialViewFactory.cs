using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Material;

public sealed class MaterialViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Material;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException(
                "Material requires an authoring session.");
        var viewModel = new MaterialEditorViewModel(editorSession);
        var view = new MaterialEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
