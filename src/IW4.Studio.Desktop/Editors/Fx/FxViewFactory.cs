using IW4.Assets.Assets.Fx;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Fx;

public sealed class FxViewFactory : IAssetEditorViewFactory
{
    private readonly FastFileWorkspace? _workspace;

    public FxViewFactory(FastFileWorkspace? workspace = null) =>
        _workspace = workspace;

    public XAssetType AssetType => XAssetType.Fx;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        FxEffectDefAsset effect = surface.Definition as FxEffectDefAsset
            ?? throw new InvalidDataException(
                "FX editor requires a loaded FxEffectDef definition.");
        var viewModel = new FxEditorViewModel(effect, _workspace);
        var view = new FxEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
