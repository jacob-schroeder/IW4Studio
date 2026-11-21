using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.PhysCollmap;

internal sealed class PhysCollmapViewFactory(AssetReferencePickerService? picker) : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.PhysCollmap;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The collision editor requires a loaded collision map.");
        var editor = new PhysCollmapEditorViewModel(session);
        return new AssetEditorViewHost(new PhysCollmapEditorView(editor, picker), editor, false);
    }
}
