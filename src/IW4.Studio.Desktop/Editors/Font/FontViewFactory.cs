using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Font;

public sealed class FontViewFactory : IAssetEditorViewFactory
{
    private readonly IMenuPreviewMaterialResolver _materialResolver;

    public FontViewFactory(IMenuPreviewMaterialResolver materialResolver) =>
        _materialResolver = materialResolver ??
            throw new ArgumentNullException(nameof(materialResolver));

    public XAssetType AssetType => XAssetType.Font;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException(
                "Font requires an authoring session.");
        var viewModel = new FontViewerViewModel(editorSession, _materialResolver);
        var view = new FontViewerView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
