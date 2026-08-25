using Avalonia.Media.Imaging;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.MapRender;

/// <summary>
/// Right-dock launcher for the native Silk.NET renderer. Scene preparation is
/// scoped to the native preview lifetime rather than retained by the workbench.
/// </summary>
public sealed class MapRenderToolViewModel : ObservableObject, IDisposable
{
    private const string LevelBriefingMaterialName = "$levelbriefing";

    private readonly CancellationTokenSource _previewCancellation = new();
    private Bitmap? _levelBriefingPreview;
    private bool _disposed;

    public MapRenderToolViewModel(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentName = Path.GetFileName(workspace.Document.Request.Path);
        ZoneName = workspace.LoadedZone.Zone.Name;
        CanLaunch = FastFileRenderViewService.CanRenderTargetMap(workspace);

        if (CanLaunch)
        {
            LevelBriefingPreviewSource? previewSource =
                FindLevelBriefingPreviewSource(workspace);
            if (previewSource is not null)
                _ = LoadLevelBriefingPreviewAsync(previewSource);
        }
    }

    public event EventHandler? LaunchRequested;

    public string DocumentName { get; }

    public string ZoneName { get; }

    public Bitmap? LevelBriefingPreview
    {
        get => _levelBriefingPreview;
        private set
        {
            Bitmap? previous = _levelBriefingPreview;
            if (!SetProperty(ref _levelBriefingPreview, value))
                return;

            previous?.Dispose();
            OnPropertyChanged(nameof(HasLevelBriefingPreview));
            OnPropertyChanged(nameof(HasNoLevelBriefingPreview));
        }
    }

    public bool HasLevelBriefingPreview => LevelBriefingPreview is not null;

    public bool HasNoLevelBriefingPreview => !HasLevelBriefingPreview;

    public string StatusHeading => "Live Preview";

    public string StatusMessage =>
        "Open the native preview to prepare this map's render scene.";

    public bool CanLaunch { get; }

    public void RequestLaunch()
        => LaunchRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _previewCancellation.Cancel();
        _previewCancellation.Dispose();
        LevelBriefingPreview = null;
    }

    private static LevelBriefingPreviewSource? FindLevelBriefingPreviewSource(
        FastFileWorkspace workspace)
    {
        string targetZoneName = workspace.LoadedZone.Zone.Name;
        string mapZoneName = targetZoneName.EndsWith(
            "_load",
            StringComparison.OrdinalIgnoreCase)
                ? targetZoneName[..^"_load".Length]
                : targetZoneName;
        if (!mapZoneName.StartsWith("mp_", StringComparison.OrdinalIgnoreCase))
            return null;

        string loadZoneName = mapZoneName + "_load";
        WorkspaceZone? loadZone = workspace.LoadedZones.FirstOrDefault(zone =>
            string.Equals(zone.LogicalZoneName, loadZoneName, StringComparison.OrdinalIgnoreCase));
        if (loadZone is null)
            return null;

        MaterialAsset? material = loadZone.LoadResult.LoadedAssets
            .Select(result => result.Materialization.RootProvider?.Asset)
            .OfType<MaterialAsset>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Info.Name,
                LevelBriefingMaterialName,
                StringComparison.OrdinalIgnoreCase));
        if (material is null)
            return null;

        GfxImageAsset[] images = material.Textures
            .Select(texture => texture.Image)
            .Where(image => image is not null)
            .Select(image => image!)
            .Distinct<GfxImageAsset>(ReferenceEqualityComparer.Instance)
            .ToArray();
        return images.Length == 0
            ? null
            : new LevelBriefingPreviewSource(
                images,
                loadZone.LoadResult.ImagePayloadResolver);
    }

    private async Task LoadLevelBriefingPreviewAsync(
        LevelBriefingPreviewSource source)
    {
        LevelBriefingPreviewLoadResult result;
        try
        {
            result = await Task.Run(
                () => DecodeLevelBriefingPreview(source),
                _previewCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        if (_disposed || _previewCancellation.IsCancellationRequested)
            return;

        try
        {
            using var stream = new MemoryStream(
                result.PngBytes,
                writable: false);
            LevelBriefingPreview = new Bitmap(stream);
        }
        catch (Exception) when (!_disposed)
        {
            LevelBriefingPreview = null;
        }
    }

    private static LevelBriefingPreviewLoadResult DecodeLevelBriefingPreview(
        LevelBriefingPreviewSource source)
    {
        foreach (GfxImageAsset image in source.Images)
        {
            if (GfxImagePreviewDecoder.TryDecodeBestAvailable(
                    image,
                    source.PayloadResolver,
                    out GfxImagePreviewSnapshot? preview,
                    out _) &&
                preview is not null)
            {
                return new LevelBriefingPreviewLoadResult(
                    preview.GetPngBytesCopy());
            }
        }

        throw new InvalidDataException(
            "The level briefing material contains no decodable image.");
    }

    private sealed record LevelBriefingPreviewSource(
        IReadOnlyList<GfxImageAsset> Images,
        IGfxImagePayloadResolver PayloadResolver);

    private sealed record LevelBriefingPreviewLoadResult(byte[] PngBytes);
}
