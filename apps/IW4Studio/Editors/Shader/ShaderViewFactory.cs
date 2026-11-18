using IW4.Game.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Shader;

internal sealed class ShaderViewFactory(XAssetType stage) : IAssetEditorViewFactory
{
    public XAssetType AssetType => stage;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The shader editor requires a loaded shader definition.");
        var model = new ShaderEditorViewModel(session);
        return new AssetEditorViewHost(new ShaderEditorView { DataContext = model }, model, usesWorkbenchScrollViewer: false);
    }
}
