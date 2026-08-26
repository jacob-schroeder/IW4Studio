using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Sound;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Sound;

public sealed class SoundViewFactory : IAssetEditorViewFactory
{
    private readonly FastFileWorkspace? _workspace;

    public SoundViewFactory(FastFileWorkspace? workspace = null) =>
        _workspace = workspace;

    public XAssetType AssetType => XAssetType.Sound;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        SoundAliasListAsset sound = surface.Definition as SoundAliasListAsset
            ?? throw new InvalidDataException(
                "Sound preview requires a loaded SoundAliasList definition.");
        ISoundPayloadResolver resolver = UnavailableSoundPayloadResolver.Instance;
        string? unavailableReason = null;
        if (_workspace is not null)
        {
            _workspace.TryGetSoundPayloadResolver(
                surface.Entry,
                out resolver,
                out unavailableReason);
        }

        var viewModel = new SoundPreviewViewModel(
            sound,
            resolver,
            unavailableReason,
            editorSession: surface as AssetEditorSession);
        var view = new SoundPreviewView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
