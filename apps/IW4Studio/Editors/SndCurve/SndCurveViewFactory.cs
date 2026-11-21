using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.SndCurve;

internal sealed class SndCurveViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.SndCurve;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The falloff editor requires a loaded sound curve.");
        var editor = new SndCurveEditorViewModel(session);
        return new AssetEditorViewHost(new SndCurveEditorView { DataContext = editor }, editor, false);
    }
}
