using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Weapon;

/// <summary>Desktop host for the existing-asset Weapon authoring surface.</summary>
public sealed class WeaponViewFactory : IAssetEditorViewFactory
{
    private readonly AssetReferencePickerService? _assetReferencePicker;
    public WeaponViewFactory(AssetReferencePickerService? assetReferencePicker = null) => _assetReferencePicker = assetReferencePicker;
    public XAssetType AssetType => XAssetType.Weapon;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession session = surface as AssetEditorSession ?? throw new InvalidDataException("Weapon requires an authoring session.");
        var viewModel = new WeaponEditorViewModel(session, _assetReferencePicker);
        var view = _assetReferencePicker is null ? new WeaponEditorView { DataContext = viewModel } : new WeaponEditorView(viewModel, _assetReferencePicker);
        return new AssetEditorViewHost(view, viewModel, usesWorkbenchScrollViewer: false);
    }
}
