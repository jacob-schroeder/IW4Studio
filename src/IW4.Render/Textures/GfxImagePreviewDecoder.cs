using IW4.Assets.Assets.Image;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Textures;

/// <summary>
/// Read-only decoded image payload suitable for editor and diagnostic
/// previews. PNG ownership remains inside the snapshot.
/// </summary>
public sealed class GfxImagePreviewSnapshot
{
    private readonly byte[] _pngBytes;

    internal GfxImagePreviewSnapshot(DecodedGfxImage decoded)
    {
        Name = decoded.Name;
        Width = decoded.Width;
        Height = decoded.Height;
        Format = decoded.Format;
        HasTransparency = decoded.HasTransparency;
        _pngBytes = decoded.PngBytes;
    }

    public string Name { get; }

    public int Width { get; }

    public int Height { get; }

    public string Format { get; }

    public bool HasTransparency { get; }

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
        out string reason)
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
                out streamedReason))
        {
            preview = new GfxImagePreviewSnapshot(streamed);
            reason = string.Empty;
            return true;
        }

        if (GfxImageDecoder.TryDecodePng(
                image,
                out DecodedGfxImage embedded,
                out string embeddedReason))
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
