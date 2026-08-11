using IW4.Assets.Assets.Image;
using IW4.FastFiles.Streaming.Images;
using IW4.Runtime.Assets.Images;

namespace IW4.FastFiles.Loaders.Streaming.Images;

/// <summary>
/// Loader-owned binding between the backend-neutral Runtime contract and the
/// imagefile*.pak implementation owned by FastFiles.Streaming.
/// </summary>
public sealed class GfxImageStreamPayloadResolver : IGfxImagePayloadResolver
{
    private readonly GfxImageStreamResolver _streams;

    public GfxImageStreamPayloadResolver(GfxImageStreamResolver streams)
    {
        _streams = streams ?? throw new ArgumentNullException(nameof(streams));
    }

    public bool TryResolveBestPayload(
        GfxImageAsset image,
        out GfxImagePayload payload,
        out string reason)
    {
        if (!_streams.TryReadBestPayload(
                image,
                out byte[] bytes,
                out int width,
                out int height,
                out reason))
        {
            payload = default;
            return false;
        }

        payload = new GfxImagePayload(width, height, bytes);
        return true;
    }

    public bool TryResolveMipPayloads(
        GfxImageAsset image,
        out IReadOnlyList<GfxImagePayload> mips,
        out string reason)
    {
        if (!_streams.TryReadMipPayloads(
                image,
                out IReadOnlyList<GfxImageStreamMipPayload> streamMips,
                out reason))
        {
            mips = [];
            return false;
        }

        var resolved = new GfxImagePayload[streamMips.Count];
        for (int index = 0; index < resolved.Length; index++)
        {
            GfxImageStreamMipPayload streamMip = streamMips[index];
            resolved[index] = new GfxImagePayload(
                streamMip.Width,
                streamMip.Height,
                streamMip.Payload);
        }

        mips = resolved;
        return true;
    }
}
