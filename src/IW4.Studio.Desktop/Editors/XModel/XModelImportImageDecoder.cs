using System.Runtime.InteropServices;
using IW4.AssetExchange.XModel;
using SkiaSharp;

namespace IW4.Studio.Desktop.Editors.XModel;

internal static class XModelImportImageDecoder
{
    private const int MaximumWeaponCamoRgbaByteCount = 64 * 1024 * 1024;

    internal static XModelImportImage Decode(
        string sourceDescription,
        ReadOnlyMemory<byte> encoded) =>
        DecodeCore(
            sourceDescription,
            encoded,
            requirePngOrJpeg: false,
            maximumRgbaByteCount: null);

    internal static XModelImportImage DecodeWeaponCamo(
        string sourceDescription,
        ReadOnlyMemory<byte> encoded) =>
        DecodeCore(
            sourceDescription,
            encoded,
            requirePngOrJpeg: true,
            maximumRgbaByteCount: MaximumWeaponCamoRgbaByteCount);

    private static XModelImportImage DecodeCore(
        string sourceDescription,
        ReadOnlyMemory<byte> encoded,
        bool requirePngOrJpeg,
        int? maximumRgbaByteCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
        if (encoded.IsEmpty)
            throw new InvalidDataException($"The {sourceDescription} image is empty.");

        using SKData data = SKData.CreateCopy(encoded.ToArray());
        using SKCodec codec = SKCodec.Create(data) ??
            throw new InvalidDataException(
                $"The {sourceDescription} image could not be decoded.");
        if (requirePngOrJpeg && codec.EncodedFormat is not
            (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg))
        {
            throw new InvalidDataException(
                $"The {sourceDescription} image must contain PNG or JPEG data.");
        }
        if (codec.Info.Width is <= 0 or > ushort.MaxValue ||
            codec.Info.Height is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                $"The {sourceDescription} image dimensions exceed IW4 limits.");
        }

        int pixelByteCount;
        try
        {
            pixelByteCount = checked(codec.Info.Width * codec.Info.Height * 4);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"The {sourceDescription} image dimensions are too large.",
                exception);
        }
        if (maximumRgbaByteCount is { } maximum && pixelByteCount > maximum)
        {
            throw new InvalidDataException(
                $"The {sourceDescription} image exceeds the 64 MiB decoded camo limit.");
        }

        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        SKCodecResult result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException(
                $"The {sourceDescription} image could not be decoded ({result}).");
        }

        byte[] rgba = new byte[pixelByteCount];
        Marshal.Copy(bitmap.GetPixels(), rgba, 0, rgba.Length);
        return new XModelImportImage(
            info.Width,
            info.Height,
            Array.AsReadOnly(rgba));
    }
}
