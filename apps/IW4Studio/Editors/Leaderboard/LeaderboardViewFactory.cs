using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Leaderboard;

internal sealed class LeaderboardViewFactory : IAssetEditorViewFactory
{
    public XAssetType AssetType => XAssetType.LeaderboardDef;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        if (surface is not AssetEditorSession session)
            throw new InvalidDataException("The leaderboard editor needs a loaded definition.");
        var editor = new LeaderboardEditorViewModel(session);
        return new AssetEditorViewHost(new LeaderboardEditorView(editor), editor, false);
    }
}
