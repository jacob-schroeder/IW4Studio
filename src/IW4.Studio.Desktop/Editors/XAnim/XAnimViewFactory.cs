using IW4.AssetExchange.SourceFormat.XAnim;
using IW4.Assets.Assets.XAnim;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.Render.EditorPreview;
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

        IReadOnlyList<XAnimPreviewScene> scenes = clip is null
            ? []
            : CreateScenes(clip, ref unavailableReason);
        var viewModel = new XAnimPreviewViewModel(
            animation,
            clip,
            scenes,
            unavailableReason);
        var view = new XAnimEditorView { DataContext = viewModel };
        return new AssetEditorViewHost(
            view,
            viewModel,
            usesWorkbenchScrollViewer: false);
    }

    private IReadOnlyList<XAnimPreviewScene> CreateScenes(
        XAnimPlaybackClip clip,
        ref string? unavailableReason)
    {
        if (_workspace is null || _workspace.IsBlank)
        {
            unavailableReason = "No loaded workspace XModels are available for the skeleton preview.";
            return [];
        }

        XModelAsset[] models = _workspace.LoadedZone.Context.AssetPool.Entries
            .Where(entry =>
                entry.AssetType == XAssetType.XModel &&
                !entry.IsReferencePlaceholder &&
                entry.Asset is XModelAsset)
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (XModelAsset)group.First().Asset)
            .OrderBy(model => model.Name, StringComparer.Ordinal)
            .ToArray();
        if (models.Length == 0)
        {
            unavailableReason = "The loaded workspace contains no XModels for the skeleton preview.";
            return [];
        }

        var scenes = new List<XAnimPreviewScene>(models.Length);
        string? firstBlocker = null;
        foreach (XModelAsset model in models)
        {
            if (XAnimPreviewScene.TryCreate(
                    clip,
                    model,
                    out XAnimPreviewScene? scene,
                    out string reason) &&
                scene is not null)
            {
                scenes.Add(scene);
            }
            else if (firstBlocker is null && !string.IsNullOrWhiteSpace(reason))
            {
                firstBlocker = reason;
            }
        }

        if (scenes.Count == 0)
        {
            unavailableReason = firstBlocker is null
                ? "No loaded XModel has a compatible skeleton for this XAnim."
                : $"No loaded XModel has a compatible skeleton for this XAnim. {firstBlocker}";
        }
        else
        {
            unavailableReason = null;
        }

        return Array.AsReadOnly(scenes.ToArray());
    }
}
