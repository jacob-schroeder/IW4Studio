using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.LoadedSounds;

internal sealed class LoadedSoundViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.LoadedSound;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The audio editor needs a loaded sound definition.");
        var editor = new LoadedSoundEditorViewModel(session);
        return new AssetEditorViewHost(new LoadedSoundEditorView { DataContext = editor }, editor, false);
    }
}
