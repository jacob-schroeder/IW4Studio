using IW4.Game.Assets.Fx;
using IW4.Formats.SourceFormat.Fx;
using IW4.Game.Zone;
using IW4.Studio.Fx;
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
        AssetEditorSession? session = surface as AssetEditorSession;
        if (session is not null)
        {
            FxDraft draft = session.OpenDraft<FxDraft>();
            effect = new FxExchange().LinkJson(draft.Json, draft.AssetName);
        }
        var viewModel = new FxEditorViewModel(effect, session?.Workspace ?? _workspace);
        var view = new FxEditorView { DataContext = viewModel };
        view.SetEditorSession(session);
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
