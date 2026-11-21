using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.XAnim;
using IW4.Game.Zone;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

public sealed class XAnimViewFactory : IAssetEditorViewFactory
{
    private readonly FastFileWorkspace? _workspace;

    public XAnimViewFactory(FastFileWorkspace? workspace = null) =>
        _workspace = workspace;

    public XAssetType AssetType => XAssetType.XAnim;

    public AssetEditorViewHost Create(AssetEditorSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        XAnimPartsAsset animation = surface.Definition as XAnimPartsAsset
            ?? throw new InvalidDataException(
                "XAnim preview requires a loaded XAnimParts definition.");

        XAnimPlaybackClip? clip = null;
        string? unavailableReason = null;
        try
        {
            clip = new XAnimExchange().Decode(animation);
        }
        catch (Exception exception) when (exception is
                   InvalidDataException or
                   ArgumentException or
                   OverflowException)
        {
            unavailableReason = $"The XAnim streams could not be decoded: {exception.Message}";
        }

        IReadOnlyList<XAnimWeaponPreview> weapons =
            clip is not null && _workspace is { IsBlank: false } && !string.IsNullOrWhiteSpace(animation.Name)
                ? XAnimWeaponPreview.Find(_workspace, animation.Name)
                : [];
        var viewModel = new XAnimPreviewViewModel(
            animation.Name,
            clip,
            previewUnavailableReason: unavailableReason,
            weapons: weapons);
        var view = new XAnimEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }
}
