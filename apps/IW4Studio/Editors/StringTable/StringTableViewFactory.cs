using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.StringTable;

/// <summary>Desktop host for the concrete detached StringTable editor.</summary>
public sealed class StringTableViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.StringTable;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException("StringTable requires an authoring session.");
        var viewModel = new StringTableEditorViewModel(editorSession);
        var view = new StringTableEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
