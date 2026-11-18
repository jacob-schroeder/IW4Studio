using IW4.Game.Assets.Physics;
using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.Editors.AssetReferences;

namespace IW4.Studio.Desktop.Editors.XModel;

/// <summary>Desktop host for the XModel asset editor.</summary>
public sealed class XModelViewFactory : IAssetEditorViewFactory
{
    private readonly AssetReferencePickerService? _assetReferencePicker;
    private readonly Action<PhysPresetAsset>? _openPhysPreset;
    public XModelViewFactory(AssetReferencePickerService? assetReferencePicker = null,
        Action<PhysPresetAsset>? openPhysPreset = null)
    {
        _assetReferencePicker = assetReferencePicker;
        _openPhysPreset = openPhysPreset;
    }
    public XAssetType AssetType => XAssetType.XModel;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException("XModel requires an authoring session.");
        var viewModel = new XModelEditorViewModel(editorSession, _openPhysPreset);
        var view = _assetReferencePicker is null
            ? new XModelEditorView { DataContext = viewModel }
            : new XModelEditorView(viewModel, _assetReferencePicker);
        return new AssetEditorViewHost(view, viewModel);
    }
}
