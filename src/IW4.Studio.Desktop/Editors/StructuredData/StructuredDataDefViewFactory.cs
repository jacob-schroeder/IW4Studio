using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.StructuredData;

/// <summary>Desktop host for the concrete StructuredDataDef editor.</summary>
public sealed class StructuredDataDefViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.StructuredDataDef;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException(
                "StructuredDataDef requires an authoring session.");
        var viewModel = new StructuredDataDefEditorViewModel(editorSession);
        var view = new StructuredDataDefEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
