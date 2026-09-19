using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.MaterialTechset;

public sealed class MaterialTechsetViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Techset;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        MaterialTechniqueSetAsset techniqueSet = surface.Definition as MaterialTechniqueSetAsset
            ?? throw new InvalidDataException(
                "MaterialTechset viewer requires a loaded MaterialTechniqueSet definition.");
        var viewModel = new MaterialTechsetViewerViewModel(techniqueSet);
        var view = new MaterialTechsetViewerView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
