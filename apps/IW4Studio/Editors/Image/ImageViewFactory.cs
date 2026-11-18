using IW4.Game.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Image;

public sealed class ImageViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Image;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The Image editor requires an authoring session.");
        var viewModel = new ImageEditorViewModel(session);
        var view = new ImageEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
