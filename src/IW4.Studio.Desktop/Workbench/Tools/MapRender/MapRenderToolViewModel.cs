using Avalonia.Media.Imaging;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Workbench.Tools.MapRender;

/// <summary>
/// Right-dock controller for the existing native Silk.NET renderer. It reports
/// scene preparation in the workbench but intentionally does not pretend the
/// native framebuffer is embedded in Avalonia.
/// </summary>
public sealed class MapRenderToolViewModel : ObservableObject, IDisposable
{
    private const string LevelBriefingMaterialName = "$levelbriefing";

    private readonly CancellationTokenSource _previewCancellation = new();
    private string _statusHeading = "Preparing Live Preview";
    private string _statusMessage =
        "Building a reusable scene snapshot from the loaded workspace…";
    private Bitmap? _levelBriefingPreview;
    private bool _isPreparing = true;
    private bool _canLaunch;
    private bool _disposed;

    public MapRenderToolViewModel(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DocumentName = Path.GetFileName(workspace.Document.Request.Path);
        ZoneName = workspace.LoadedZone.Zone.Name;

        LevelBriefingPreviewSource? previewSource =
            FindLevelBriefingPreviewSource(workspace);
        if (previewSource is not null)
            _ = LoadLevelBriefingPreviewAsync(previewSource);
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

    public string StatusHeading
    {
        get => _statusHeading;
        private set => SetProperty(ref _statusHeading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsPreparing
    {
        get => _isPreparing;
        private set => SetProperty(ref _isPreparing, value);
    }

    public bool CanLaunch
    {
        get => _canLaunch;
        private set => SetProperty(ref _canLaunch, value);
    }

    public void ReportProgress(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        IsPreparing = true;
        StatusHeading = "Preparing Live Preview";
        StatusMessage = message;
    }

    public void ReportResult(RenderViewSceneBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsPreparing = false;
        CanLaunch = result.IsRenderable;
        StatusHeading = result.IsRenderable
            ? "Live Preview ready"
            : "Live Preview unavailable";
        StatusMessage = result.IsRenderable
            ? "The scene is cached. Open it in the native Silk.NET render window."
            : result.NonRenderableReason
              ?? "This fastfile does not contain renderable map assets.";
    }

    public void ReportFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        IsPreparing = false;
        CanLaunch = false;
        StatusHeading = "Could not prepare Live Preview";
        StatusMessage = exception.Message;
    }

    public void RequestLaunch()
    {
        if (!CanLaunch)
            return;

        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

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
