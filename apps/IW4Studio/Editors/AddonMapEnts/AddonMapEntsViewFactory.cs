using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.AddonMapEnts;

internal sealed class AddonMapEntsViewFactory(FastFileWorkspace? workspace) : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.AddonMapEnts;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The mission scene editor needs a loaded definition.");
        var editor = new AddonMapEntsEditorViewModel(session);
        return new(new AddonMapEntsEditorView(editor, workspace), editor, false);
    }
}
