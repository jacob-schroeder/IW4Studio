using Avalonia.Media.Imaging;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.LightDef;
using IW4.Render.Lighting;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class LightDefEditorViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IDisposable
{
    private readonly CancellationTokenSource _previewCancellation = new();
    private Bitmap? _attenuationPreview;
    private string _previewStatus = "Preview unavailable";
    private string _previewDetails = string.Empty;
    private bool _isPreviewLoading;
    private bool _disposed;

    public LightDefEditorViewModel(
        LightDefAsset lightDef,
        FastFileWorkspace? workspace = null)
    {
        ArgumentNullException.ThrowIfNull(lightDef);

        Name = string.IsNullOrWhiteSpace(lightDef.Name)
            ? "<unnamed LightDef>"
            : lightDef.Name;
        PurposeText =
            "A LightDef supplies the reusable falloff lookup and RSX sampling " +
            "state selected by a ComWorld point or spot light. The ComWorld " +
            "light supplies position, direction, color, radius, cone, and " +
            "exponent; those values are not stored in this asset.";

        GfxImageAsset? image = lightDef.Image;
        ImageName = image?.Name ?? "<unresolved>";
        ImageDimensionsText = image is null
            ? "Unavailable"
            : FormatDimensions(image);
        ImageFormatText = image is null
            ? "Unavailable"
            : $"{image.FormatEncoding.BaseFormat} · " +
              $"{(image.FormatEncoding.IsLinear ? "linear" : "swizzled")} · " +
              $"0x{image.Format:X2}";
        ImageShapeText = image is null
            ? "Unavailable"
            : $"{image.MapType} · {image.DimensionCount}" +
              (image.IsCubemap ? " · cubemap" : string.Empty);
        ImageMipText = image is null
            ? "Unavailable"
            : $"{image.LevelCount:N0} " +
              (image.LevelCount == 1 ? "level" : "levels");

        RsxSamplerState sampler = RsxSamplerDecoder.Decode(
            lightDef.SamplerState,
            image?.MinLodControl ?? 0,
            image?.UseSrgbReads ?? 0);
        SamplerStateText = $"0x{(byte)lightDef.SamplerState:X2}";
        FilterText = FormatFilters(sampler);
        AddressingText =
            $"U {sampler.AddressU} · V {sampler.AddressV} · W {sampler.AddressW}";
        GammaReadsText = sampler.UsesSrgbReads ? "Enabled" : "Disabled";
        LookupStartText =
            $"{lightDef.LmapLookupStart:N0} (0x{lightDef.LmapLookupStart:X8})";
        if (image is null)
        {
            LookupPlacementText = "Unavailable";
            LookupPlacementDetailsText =
                "The renderer requires a materialized attenuation image " +
                "before it can produce light-falloff placement values.";
        }
        else
        {
            float normalizedWidth =
                image.Width / (float)LightFalloffLookupLayout.Width;
            float normalizedStart =
                lightDef.LmapLookupStart /
                (float)LightFalloffLookupLayout.Width;
            LookupPlacementText =
                $"width {normalizedWidth:0.######} · " +
                $"start {normalizedStart:0.######}";
            LookupPlacementDetailsText =
                "The renderer sends these normalized values to the " +
                "light-falloff shader. They describe lookup placement, not " +
                "the light's radius or exponent.";
        }

        EditorProperties =
        [
            new("Attenuation image", ImageName),
            new("Dimensions", ImageDimensionsText),
            new("Image format", ImageFormatText),
            new("Image shape", ImageShapeText),
            new("Mip levels", ImageMipText),
            new("Sampler state", SamplerStateText),
            new("Filtering", FilterText),
            new("Addressing", AddressingText),
            new("sRGB reads", GammaReadsText),
            new("Generated atlas offset", LookupStartText)
        ];

        BeginPreviewLoad(image, workspace);
    }

    public string Name { get; }

    public string PurposeText { get; }

    public string ImageName { get; }

    public string ImageDimensionsText { get; }

    public string ImageFormatText { get; }

    public string ImageShapeText { get; }

    public string ImageMipText { get; }

    public string SamplerStateText { get; }

    public string FilterText { get; }

    public string AddressingText { get; }

    public string GammaReadsText { get; }

    public string LookupStartText { get; }

    public string LookupPlacementText { get; }

    public string LookupPlacementDetailsText { get; }

    public Bitmap? AttenuationPreview
    {
        get => _attenuationPreview;
        private set
        {
            Bitmap? previous = _attenuationPreview;
            if (!SetProperty(ref _attenuationPreview, value))
                return;

            previous?.Dispose();
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(ShowsPreviewUnavailable));
        }
    }

    public bool HasPreview => AttenuationPreview is not null;

    public bool ShowsPreviewUnavailable => !IsPreviewLoading && !HasPreview;

    public string PreviewStatus
    {
        get => _previewStatus;
        private set => SetProperty(ref _previewStatus, value);
    }

    public string PreviewDetails
    {
        get => _previewDetails;
        private set => SetProperty(ref _previewDetails, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set
        {
            if (!SetProperty(ref _isPreviewLoading, value))
                return;

            OnPropertyChanged(nameof(ShowsPreviewUnavailable));
        }
    }

    public string PropertySectionName => "LIGHTDEF DATA";

    public IReadOnlyList<AssetEditorProperty> EditorProperties { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _previewCancellation.Cancel();
        _previewCancellation.Dispose();
        AttenuationPreview = null;
    }

    private void BeginPreviewLoad(
        GfxImageAsset? image,
        FastFileWorkspace? workspace)
    {
        if (image is null)
        {
            PreviewStatus = "No attenuation image";
            PreviewDetails =
                "This LightDef has no materialized attenuation image. Its " +
                "sampler and lookup metadata are still shown.";
            return;
        }

        IGfxImagePayloadResolver resolver =
            UnavailableGfxImagePayloadResolver.Instance;
        string payloadSource = "the embedded image payload";
        if (workspace is not null)
        {
            var workspaceResolver = new WorkspaceGfxImagePayloadResolver(workspace);
            resolver = workspaceResolver;
            payloadSource = workspaceResolver.DescribeSource(image);
        }

        IsPreviewLoading = true;
        PreviewStatus = "Loading preview…";
        PreviewDetails = $"Resolving the attenuation lookup from {payloadSource}.";
        _ = LoadPreviewAsync(
            image,
            resolver,
            payloadSource,
            _previewCancellation.Token);
    }

    private async Task LoadPreviewAsync(
        GfxImageAsset image,
        IGfxImagePayloadResolver resolver,
        string payloadSource,
        CancellationToken cancellationToken)
    {
        PreviewLoadResult result;
        try
        {
            result = await Task.Run(
                () => PreviewLoadResult.Decode(
                    image,
                    resolver,
                    payloadSource),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = PreviewLoadResult.Failed(
                payloadSource,
                exception.Message);
        }

        if (_disposed || cancellationToken.IsCancellationRequested)
            return;

        IsPreviewLoading = false;
        if (!result.Success || result.Preview is null)
        {
            PreviewStatus = "Preview unavailable";
            PreviewDetails =
                $"The attenuation image could not be decoded from " +
                $"{result.PayloadSource}: {result.Reason}";
            return;
        }

        try
        {
            using var stream = new MemoryStream(
                result.Preview.GetPngBytesCopy(),
                writable: false);
            AttenuationPreview = new Bitmap(stream);
            PreviewStatus =
                $"{result.Preview.Width:N0} × " +
                $"{result.Preview.Height:N0} · {result.Preview.Format}";
            PreviewDetails =
                $"Decoded from {result.PayloadSource}. The lookup is expanded to " +
                "fill the preview so narrow falloff strips remain visible.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            PreviewStatus = "Preview unavailable";
            PreviewDetails =
                $"The attenuation image could not be displayed: " +
                exception.Message;
        }
    }

    private static string FormatDimensions(GfxImageAsset image)
    {
        if (image.IsCubemap)
            return $"{image.Width:N0} × {image.Height:N0} × 6 faces";
        if (image.Depth > 1)
            return $"{image.Width:N0} × {image.Height:N0} × {image.Depth:N0}";
        return $"{image.Width:N0} × {image.Height:N0}";
    }

    private static string FormatFilters(RsxSamplerState sampler)
    {
        string anisotropy = sampler.MaxAnisotropy > 1
            ? $" · {sampler.MaxAnisotropy}×"
            : string.Empty;
        return
            $"min {sampler.MinFilter} · mag {sampler.MagFilter} · " +
            $"mip {sampler.MipFilter}{anisotropy}";
    }

    private sealed record PreviewLoadResult(
        bool Success,
        GfxImagePreviewSnapshot? Preview,
        string PayloadSource,
        string Reason)
    {
        internal static PreviewLoadResult Decode(
            GfxImageAsset image,
            IGfxImagePayloadResolver resolver,
            string payloadSource) =>
            GfxImagePreviewDecoder.TryDecodeBestAvailable(
                image,
                resolver,
                out GfxImagePreviewSnapshot? preview,
                out string reason) &&
            preview is not null
                ? new PreviewLoadResult(
                    true,
                    preview,
                    payloadSource,
                    string.Empty)
                : Failed(payloadSource, reason);

        internal static PreviewLoadResult Failed(
            string payloadSource,
            string reason) =>
            new(false, null, payloadSource, reason);
    }
}
