using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Vehicle;

internal sealed class VehicleViewFactory(AssetReferencePickerService? picker,
    Action<XAssetType, string>? openReference) : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Vehicle;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("Vehicle requires an authoring session.");
        var model = new VehicleEditorViewModel(session, openReference);
        return new AssetEditorViewHost(new VehicleEditorView { DataContext = model, Picker = picker },
            model, usesWorkbenchScrollViewer: false);
    }
}
