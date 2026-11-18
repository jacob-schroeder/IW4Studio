using IW4.Game.Assets.Image;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Textures;

/// <summary>
/// Read-only decoded image payload suitable for editor and diagnostic
/// previews. Raw RGBA and PNG ownership remain inside the snapshot; callers
/// receive copies so presentation backends cannot mutate decoder state.
/// </summary>
public sealed class GfxImagePreviewSnapshot
{
    private readonly byte[] _rgbaBytes;
    private readonly byte[] _pngBytes;

    internal GfxImagePreviewSnapshot(DecodedGfxImage decoded)
    {
        Name = decoded.Name;
        Width = decoded.Width;
        Height = decoded.Height;
        Format = decoded.Format;
        HasTransparency = decoded.HasTransparency;
        _rgbaBytes = decoded.RgbaBytes;
        _pngBytes = decoded.PngBytes;
    }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    public string Format { get; }

    public bool HasTransparency { get; }

    public byte[] GetRgbaBytesCopy() => _rgbaBytes.ToArray();

    public byte[] GetPngBytesCopy() => _pngBytes.ToArray();
}

/// <summary>
/// Public preview boundary over the renderer's canonical GfxImage decoder.
/// It can resolve the best available streamed level or decode embedded
/// payloads without creating renderer resources or mutating runtime state.
/// </summary>
public static class GfxImagePreviewDecoder
{
    public static bool TryDecodeBestAvailable(
        GfxImageAsset image,
        IGfxImagePayloadResolver payloadResolver,
        out GfxImagePreviewSnapshot? preview,
        out string reason) =>
        TryDecodeBestAvailable(image, payloadResolver, out preview, out reason, sampleNativeChannels: false);

    /// <summary>
    /// Decodes a preview using RSX component selection for native BC textures.
    /// </summary>
    public static bool TryDecodeSampledBestAvailable(
        GfxImageAsset image,
        IGfxImagePayloadResolver payloadResolver,
        out GfxImagePreviewSnapshot? preview,
        out string reason) =>
        TryDecodeBestAvailable(image, payloadResolver, out preview, out reason, sampleNativeChannels: true);

    private static bool TryDecodeBestAvailable(
        GfxImageAsset image,
        IGfxImagePayloadResolver payloadResolver,
        out GfxImagePreviewSnapshot? preview,
        out string reason,
        bool sampleNativeChannels)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payloadResolver);

        preview = null;
        string? streamedReason = null;
        if (payloadResolver.TryResolveBestPayload(
                image,
                out GfxImagePayload payload,
                out streamedReason) &&
            GfxImageDecoder.TryDecodePng(
                image,
                payload.Payload,
                payload.Width,
                payload.Height,
                out DecodedGfxImage streamed,
                out streamedReason,
                sampleNativeChannels))
        {
            preview = new GfxImagePreviewSnapshot(streamed);
            reason = string.Empty;
            return true;
        }

        if (GfxImageDecoder.TryDecodePng(
                image,
                image.PayloadBytes,
                image.Width,
                image.Height,
                out DecodedGfxImage embedded,
                out string embeddedReason,
                sampleNativeChannels))
        {
            preview = new GfxImagePreviewSnapshot(embedded);
            reason = string.Empty;
            return true;
        }

        reason = string.IsNullOrWhiteSpace(streamedReason)
            ? embeddedReason
            : $"{streamedReason}; embedded fallback: {embeddedReason}";
        return false;
    }

    // The inline texture stores consecutive mip levels followed by 128-byte
    // alignment padding. Streamed parts and pitched layouts have different contracts.
    public static IReadOnlyList<ReadOnlyMemory<byte>>? DecodeEmbeddedMipTail(
        GfxImageAsset image, GfxImagePreviewSnapshot top)
    {
        if (image.StreamImageIndex is not null || image.IsCubemap || image.Depth > 1 ||
            image.Width != top.Width || image.Height != top.Height || image.LevelCount == 0 ||
            image.LevelCount > 1 + System.Numerics.BitOperations.Log2((uint)Math.Max(top.Width, top.Height)))
            return null;
        if (image.LevelCount == 1)
            return [];
        if (image.FormatEncoding.IsLinear || image.FormatEncoding.UsesUnnormalizedCoordinates)
            return null;

        uint formatKey = GfxImagePixelLayout.BuildFormatKey(image.FormatEncoding, image.TextureRemap);
        int width = top.Width;
        int height = top.Height;
        int offset = GfxImagePixelLayout.ComputeMipByteCount(formatKey, width, height, 1);
        if (offset == 0 || offset > image.PayloadBytes.Count)
            return null;

        var tail = new ReadOnlyMemory<byte>[image.LevelCount - 1];
        byte[] payload = image.PayloadBytes as byte[] ?? image.PayloadBytes.ToArray();
        for (int index = 0; index < tail.Length; index++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            int size = GfxImagePixelLayout.ComputeMipByteCount(formatKey, width, height, 1);
            if (size > payload.Length - offset ||
                !GfxImageDecoder.TryDecodeRgba(image, new ArraySegment<byte>(payload, offset, size),
                    width, height, out var decoded, out _, sampleNativeChannels: true))
                return null;

            tail[index] = decoded.RgbaBytes;
            offset += size;
        }
        return Array.AsReadOnly(tail);
    }

    public static bool TryDecodeStreamed(
        GfxImageAsset image,
        IGfxImagePayloadResolver payloadResolver,
        out GfxImagePreviewSnapshot? preview,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payloadResolver);

        preview = null;
        if (!payloadResolver.TryResolveBestPayload(
                image,
                out GfxImagePayload payload,
                out reason))
        {
            return false;
        }

        if (!GfxImageDecoder.TryDecodePng(
                image,
                payload.Payload,
                payload.Width,
                payload.Height,
                out DecodedGfxImage decoded,
                out reason))
        {
            return false;
        }

        preview = new GfxImagePreviewSnapshot(decoded);
        return true;
    }
}
