using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Render.Textures;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.Workbench.Selection;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Tools.ImageFilePak;

/// <summary>
/// One streamed GfxImage definition together with the physical fastfile and
/// image package metadata needed to inspect it.
/// </summary>
public sealed class ImageFilePakEntryViewModel
{
    private readonly GfxImageAsset _image;
    private readonly IGfxImagePayloadResolver _payloadResolver;

    internal ImageFilePakEntryViewModel(
        GfxImageAsset image,
        IGfxImagePayloadResolver payloadResolver,
        string owningFastFilePath,
        WorkbenchStreamedImageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payloadResolver);
        ArgumentException.ThrowIfNullOrWhiteSpace(owningFastFilePath);

        _image = image;
        _payloadResolver = payloadResolver;
        Identity = identity;
        Name = string.IsNullOrWhiteSpace(image.Name)
            ? "<unnamed image>"
            : image.Name;
        OwningFastFilePath = Path.GetFullPath(owningFastFilePath);
        OwningFastFileName = Path.GetFileName(OwningFastFilePath);
        StreamIndexText = image.StreamImageIndex is { } index
            ? $"0x{index:X}"
            : "—";

        var parts = new List<ImageFilePakStreamPartViewModel>();
        for (int partIndex = 0;
             partIndex < image.StreamData.Count;
             partIndex++)
        {
            GfxImageStreamData streamData = image.StreamData[partIndex];
            if (!streamData.HasStreamingData)
                continue;

            int previousByteCount = partIndex == 0
                ? 0
                : image.StreamData[partIndex - 1].CumulativeByteCount;
            int byteCount = Math.Max(
                0,
                streamData.CumulativeByteCount - previousByteCount);
            parts.Add(new ImageFilePakStreamPartViewModel(
                partIndex,
                streamData,
                partIndex < image.StreamEntries.Count
                    ? image.StreamEntries[partIndex]
                    : default,
                byteCount,
                OwningFastFileName));
        }

        StreamParts = Array.AsReadOnly(parts.ToArray());
        PackageText = string.Join(
            ", ",
            StreamParts
                .Where(part => part.IsAvailable)
                .Select(part => part.PackageName)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (PackageText.Length == 0)
            PackageText = "No package entry";

        GfxImageStreamData? largest = image.StreamData
            .Where(part => part.HasStreamingData)
            .OrderByDescending(part => (long)part.Width * part.Height)
            .FirstOrDefault();
        DimensionsText = largest is null
            ? $"{image.Width:N0} × {image.Height:N0}"
            : $"{largest.Width:N0} × {largest.Height:N0}";
        TotalByteCountText =
            $"{image.StreamData.Max(part => part.CumulativeByteCount):N0} bytes";
    }

    public string Name { get; }

    public WorkbenchStreamedImageIdentity Identity { get; }

    public string OwningFastFilePath { get; }

    public string OwningFastFileName { get; }

    public string PackageText { get; }

    public string DimensionsText { get; }

    public string TotalByteCountText { get; }

    public string StreamIndexText { get; }

    public IReadOnlyList<ImageFilePakStreamPartViewModel> StreamParts { get; }

    public WorkbenchAssetSelection ToSelection() =>
        new(
            WorkbenchAssetSelectionIdentity.ForStreamedImage(Identity),
            XAssetType.Image,
            Name,
            XAssetStableIdentity.NormalizeLookupName(Name),
            WorkspaceAssetAccess.ReadOnly,
            "Imagefile.pak stream",
            OwningFastFileName,
            hasEditor: true);

    internal bool TryDecodePreview(
        out GfxImagePreviewSnapshot? preview,
        out string reason) =>
        GfxImagePreviewDecoder.TryDecodeStreamed(
            _image,
            _payloadResolver,
            out preview,
            out reason);
}
