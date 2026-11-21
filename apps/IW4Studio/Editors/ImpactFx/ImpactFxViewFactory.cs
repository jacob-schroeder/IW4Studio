using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.ImpactFx;

internal sealed class ImpactFxViewFactory(AssetReferencePickerService? picker) : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.ImpactFx;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The impact editor needs a loaded impact table.");
        var editor = new ImpactFxEditorViewModel(session);
        return new AssetEditorViewHost(new ImpactFxEditorView(editor, picker), editor, false);
    }
}
