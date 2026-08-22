using IW4.Assets.Assets.Font;
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
        FontAsset font = surface.Definition as FontAsset
            ?? throw new InvalidDataException(
                "Font viewer requires a loaded Font definition.");
        var viewModel = new FontViewerViewModel(font, _materialResolver);
        var view = new FontViewerView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
