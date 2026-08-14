using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Localize;

/// <summary>Desktop host for the concrete detached Localize editor.</summary>
public sealed class LocalizeViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.Localize;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException("Localize requires an authoring session.");
        var viewModel = new LocalizeEditorViewModel(editorSession);
        var view = new LocalizeEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(view, viewModel);
    }
}
