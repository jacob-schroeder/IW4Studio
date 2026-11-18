using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.PhysPreset;

public sealed class PhysPresetViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.PhysPreset;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("A PhysPreset editor requires a loaded preset session.");

        var model = new PhysPresetEditorViewModel(session);
        return new AssetEditorViewHost(new PhysPresetEditorView { DataContext = model }, model,
            usesWorkbenchScrollViewer: false);
    }
}
