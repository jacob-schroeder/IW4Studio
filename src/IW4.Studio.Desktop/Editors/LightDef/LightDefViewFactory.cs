using IW4.Assets.Assets.LightDef;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.LightDef;

public sealed class LightDefViewFactory : IAssetEditorViewFactory
{
    private readonly FastFileWorkspace? _workspace;

    public LightDefViewFactory(FastFileWorkspace? workspace = null) =>
        _workspace = workspace;

    public XAssetType AssetType => XAssetType.LightDef;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        LightDefAsset lightDef = surface.Definition as LightDefAsset
            ?? throw new InvalidDataException(
                "LightDef viewer requires a loaded LightDef definition.");
        var viewModel = new LightDefEditorViewModel(lightDef, _workspace);
        var view = new LightDefEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
