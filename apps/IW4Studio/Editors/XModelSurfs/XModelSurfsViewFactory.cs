using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XModelSurfs;

internal sealed class XModelSurfsViewFactory(AssetReferencePickerService? picker) : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.XModelSurfs;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The mesh editor requires a loaded surface collection.");
        var editor = new XModelSurfsEditorViewModel(session);
        return new AssetEditorViewHost(new XModelSurfsEditorView(editor, picker), editor, false);
    }
}
