using IW4.Assets.D3dbsp;
using IW4.FastFiles.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.D3dbsp;

/// <summary>Desktop host for a synchronized multiplayer D3DBSP asset group.</summary>
public sealed class D3dbspViewFactory : IAssetEditorViewFactory
{
    // GfxMap is the primary serialized root used to register the singular
    // group editor. Name-aware routing also sends the other five group types
    // to this factory.
    public XAssetType AssetType => XAssetType.GfxMap;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        AssetEditorSession editorSession = surface as AssetEditorSession
            ?? throw new InvalidDataException("D3DBSP requires an authoring session.");
        if (!D3dbspAssetTypeFacts.IsMultiplayerType(surface.Entry.AssetType) ||
            !D3dbspAssetTypeFacts.IsD3dbspName(surface.Entry.OriginalName))
        {
            throw new InvalidDataException(
                "The D3DBSP editor can host only a multiplayer .d3dbsp asset group.");
        }

        var viewModel = new D3dbspEditorViewModel(editorSession);
        var view = new D3dbspEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(view, viewModel);
    }
}
