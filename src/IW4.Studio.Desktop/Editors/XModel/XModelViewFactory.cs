using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.Editors.AssetReferences;

namespace IW4.Studio.Desktop.Editors.XModel;

/// <summary>Desktop host for the XModel asset editor.</summary>
public sealed class XModelViewFactory : IAssetEditorViewFactory
{
    private readonly AssetReferencePickerService? _assetReferencePicker;
    public XModelViewFactory(AssetReferencePickerService? assetReferencePicker = null) =>
        _assetReferencePicker = assetReferencePicker;
    public XAssetType AssetType => XAssetType.XModel;

    public AssetEditorViewHost Create(AssetEditorSession editorSession)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        var viewModel = new XModelEditorViewModel(editorSession);
        var view = _assetReferencePicker is null
            ? new XModelEditorView { DataContext = viewModel }
            : new XModelEditorView(viewModel, _assetReferencePicker);
        return new AssetEditorViewHost(view, viewModel);
    }
}
