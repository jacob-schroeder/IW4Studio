using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Tracer;

internal sealed class TracerViewFactory(AssetReferencePickerService? picker) : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Tracer;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("A tracer editor requires a loaded tracer definition.");

        var model = new TracerEditorViewModel(session);
        var view = new TracerEditorView { DataContext = model, MaterialPicker = picker };
        return new AssetEditorViewHost(view, model, usesWorkbenchScrollViewer: false);
    }
}
