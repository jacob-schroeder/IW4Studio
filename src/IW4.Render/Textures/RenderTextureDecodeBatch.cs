using System.Runtime.ExceptionServices;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Textures;

internal readonly record struct RenderTextureDecodeRequest(
    RenderTextureCacheKey Key)
{
    internal static RenderTextureDecodeRequest Create(
        GfxImageAsset image,
        byte samplerState,
        bool includeAuthoredMipChain) => new(
            RenderTextureCacheKey.TwoDimensionalImage(
                image,
                samplerState,
                includeAuthoredMipChain));

    internal static RenderTextureDecodeRequest Create(
        GfxImageAsset image,
        MaterialSamplerState samplerState,
        bool includeAuthoredMipChain) =>
        Create(image, (byte)samplerState, includeAuthoredMipChain);
}

internal readonly record struct RenderTextureDecodeResult(
    Texture? Texture,
    ExceptionDispatchInfo? Exception);

internal static class RenderTextureDecodeBatch
{
    internal static RenderTextureDecodeResult Decode(
        RenderTextureDecodeRequest request,
        IGfxImagePayloadResolver imageStreams,
        bool preferProvenAuthoredPayloads) =>
        Decode(
            request,
            imageStreams,
            GfxImageDecoder.TryDecodeTexture,
            preferProvenAuthoredPayloads);

    private static RenderTextureDecodeResult Decode(
        RenderTextureDecodeRequest request,
        IGfxImagePayloadResolver imageStreams,
        TryDecodeTexture tryDecodeTexture,
        bool preferProvenAuthoredPayloads)
    {
        ArgumentNullException.ThrowIfNull(tryDecodeTexture);
        try
        {
            ValidateTwoDimensionalRequest(request);
            DecodedPixelChain? pixels = DecodePixels(
                new PixelDecodeKey(
                    request.Key.Image,
                    request.Key.IncludeAuthoredMipChain),
                imageStreams,
                tryDecodeTexture,
                preferProvenAuthoredPayloads);
            return new RenderTextureDecodeResult(
                pixels is null
                    ? null
                    : MaterializeTexture(request.Key, pixels),
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new RenderTextureDecodeResult(
                null,
                ExceptionDispatchInfo.Capture(exception));
        }
    }

    internal static void DecodeUnique(
        IReadOnlyList<RenderTextureDecodeRequest> requests,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(imageStreams);
        ArgumentNullException.ThrowIfNull(textureCache);
        ArgumentNullException.ThrowIfNull(failedTextureCacheKeys);

        var seen = new HashSet<RenderTextureCacheKey>();
        RenderTextureDecodeRequest[] pending = requests
            .Where(request =>
                !textureCache.ContainsKey(request.Key) &&
                !failedTextureCacheKeys.Contains(request.Key) &&
                seen.Add(request.Key))
            .ToArray();
        if (pending.Length == 0)
            return;

        foreach (RenderTextureDecodeRequest request in pending)
            ValidateTwoDimensionalRequest(request);

        var pixelOrdinalByKey = new Dictionary<PixelDecodeKey, int>();
        var pixelKeys = new List<PixelDecodeKey>();
        foreach (RenderTextureDecodeRequest request in pending)
        {
            var pixelKey = new PixelDecodeKey(
                request.Key.Image,
                request.Key.IncludeAuthoredMipChain);
            if (!pixelOrdinalByKey.ContainsKey(pixelKey))
            {
                pixelOrdinalByKey.Add(pixelKey, pixelKeys.Count);
                pixelKeys.Add(pixelKey);
            }
        }

        var pixelResults = new PixelDecodeResult[pixelKeys.Count];
        if (pixelKeys.Count == 1)
        {
            pixelResults[0] = DecodePixelsSafely(
                pixelKeys[0],
                imageStreams,
                GfxImageDecoder.TryDecodeTexture,
                textureCache.PreferProvenAuthoredPayloads);
        }
        else
        {
            Parallel.For(
                0,
                pixelKeys.Count,
                new ParallelOptions
                {
                    // RGBA decode expands compressed input substantially. Four
                    // workers keep useful CPU overlap without multiplying the
                    // already-large scene working set by every host core.
                    MaxDegreeOfParallelism = Math.Min(
                        Environment.ProcessorCount,
                        4)
                },
                index => pixelResults[index] = DecodePixelsSafely(
                    pixelKeys[index],
                    imageStreams,
                    GfxImageDecoder.TryDecodeTexture,
                    textureCache.PreferProvenAuthoredPayloads));
        }

        // Worker completion order is deliberately irrelevant. Publish cache
        // entries, diagnostics, and exceptions in first-request order.
        for (int index = 0; index < pending.Length; index++)
        {
            RenderTextureDecodeRequest request = pending[index];
            var pixelKey = new PixelDecodeKey(
                request.Key.Image,
                request.Key.IncludeAuthoredMipChain);
            PixelDecodeResult pixelResult =
                pixelResults[pixelOrdinalByKey[pixelKey]];
            pixelResult.Exception?.Throw();
            if (pixelResult.Pixels is { } pixels)
            {
                textureCache.Add(
                    request.Key,
                    MaterializeTexture(request.Key, pixels));
                if (pixels.HasDecodedTop)
                    decodedTextureCount++;
                else
                    skippedTextureCount++;
            }
            else
            {
                failedTextureCacheKeys.Add(request.Key);
                skippedTextureCount++;
            }
        }
    }

    private static PixelDecodeResult DecodePixelsSafely(
        PixelDecodeKey key,
        IGfxImagePayloadResolver imageStreams,
        TryDecodeTexture tryDecodeTexture,
        bool preferProvenAuthoredPayloads)
    {
        try
        {
            return new PixelDecodeResult(
                DecodePixels(
                    key,
                    imageStreams,
                    tryDecodeTexture,
                    preferProvenAuthoredPayloads),
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new PixelDecodeResult(
                null,
                ExceptionDispatchInfo.Capture(exception));
        }
    }

    private static DecodedPixelChain? DecodePixels(
        PixelDecodeKey key,
        IGfxImagePayloadResolver imageStreams,
        TryDecodeTexture tryDecodeTexture,
        bool preferProvenAuthoredPayloads)
    {
        GfxImageAsset image = key.Image;
        IReadOnlyList<byte> payload = image.PayloadBytes;
        int width = image.Width;
        int height = image.Height;
        IReadOnlyList<GfxImagePayload> streamMips = [];
        bool resolvedStream;
        string reason;
        if (key.IncludeAuthoredMipChain)
        {
            resolvedStream = imageStreams.TryResolveMipPayloads(
                image,
                out streamMips,
                out reason);
            if (resolvedStream && streamMips.Count > 0)
            {
                GfxImagePayload top = streamMips[0];
                payload = top.Payload;
                width = top.Width;
                height = top.Height;
            }
        }
        else
        {
            resolvedStream = imageStreams.TryResolveBestPayload(
                image,
                out GfxImagePayload topPayload,
                out reason);
            if (resolvedStream)
            {
                payload = topPayload.Payload;
                width = topPayload.Width;
                height = topPayload.Height;
            }
        }

        if (payload.Count == 0 && !resolvedStream)
            return null;

        var authoredSubresources =
            new List<TextureAuthoredSubresource>();
        string authoredFormat = GfxImageDecoder.DescribeFormat(image.Format);
        if (AuthoredTexturePayloadCapture.TryCaptureTwoDimensional(
                image,
                payload,
                width,
                height,
                mipLevel: 0,
                authoredFormat,
                out TextureAuthoredSubresource authoredTop))
        {
            authoredSubresources.Add(authoredTop);
        }

        bool[] capturedStreamMips =
            new bool[streamMips.Count];
        if (capturedStreamMips.Length > 0)
        {
            capturedStreamMips[0] =
                authoredSubresources.Count != 0;
        }
        if (key.IncludeAuthoredMipChain && resolvedStream)
        {
            for (int sourceMipIndex = 1;
                 sourceMipIndex < streamMips.Count;
                 sourceMipIndex++)
            {
                GfxImagePayload mip = streamMips[sourceMipIndex];
                bool capturedAuthored = AuthoredTexturePayloadCapture
                    .TryCaptureTwoDimensional(
                        image,
                        mip.Payload,
                        mip.Width,
                        mip.Height,
                        sourceMipIndex,
                        authoredFormat,
                        out TextureAuthoredSubresource authoredMip);
                if (capturedAuthored)
                {
                    authoredSubresources.Add(authoredMip);
                    capturedStreamMips[sourceMipIndex] = true;
                }
            }
        }

        bool capturedCompleteSource =
            !(key.IncludeAuthoredMipChain &&
              resolvedStream &&
              streamMips.Count > 0)
                ? authoredSubresources.Count == 1
                : capturedStreamMips.All(value => value);
        if (preferProvenAuthoredPayloads &&
            capturedCompleteSource &&
            AuthoredTexturePayloadCapture
                .IsCompleteProvenChain(
                    authoredSubresources,
                    TextureTarget.Texture2D,
                    width,
                    height))
        {
            return new DecodedPixelChain(
                Top: null,
                image.Name ?? "unnamed_image",
                width,
                height,
                authoredFormat,
                MipLevels: [],
                authoredSubresources);
        }

        bool hasDecodedTop = tryDecodeTexture(
            image,
            payload,
            width,
            height,
            out DecodedTextureImage decoded,
            out reason);
        // Authored-only publication is reserved for the complete proven-chain
        // fast path above. A partial capture must never masquerade as a usable
        // texture, and Neutral builds retain their decoded compatibility
        // representation.
        if (!hasDecodedTop)
            return null;

        List<TextureMip> mipLevels = [];
        if (key.IncludeAuthoredMipChain && resolvedStream)
        {
            bool decodedMipChainOpen = hasDecodedTop;
            for (int sourceMipIndex = 1;
                 sourceMipIndex < streamMips.Count;
                 sourceMipIndex++)
            {
                GfxImagePayload mip = streamMips[sourceMipIndex];
                DecodedTextureImage mipDecoded = default;
                bool decodedMip = decodedMipChainOpen && tryDecodeTexture(
                    image,
                    mip.Payload,
                    mip.Width,
                    mip.Height,
                    out mipDecoded,
                    out _);
                if (decodedMip)
                {
                    mipLevels.Add(new TextureMip(
                        mipDecoded.Width,
                        mipDecoded.Height,
                        mipDecoded.PixelBytes));
                }
                else
                {
                    decodedMipChainOpen = false;
                    if (!capturedStreamMips[sourceMipIndex])
                        break;
                }
            }
        }

        return new DecodedPixelChain(
            hasDecodedTop ? decoded : null,
            image.Name ?? "unnamed_image",
            width,
            height,
            authoredFormat,
            mipLevels,
            authoredSubresources);
    }

    private static Texture MaterializeTexture(
        RenderTextureCacheKey key,
        DecodedPixelChain pixels)
    {
        GfxImageAsset image = key.Image;
        DecodedTextureImage? decoded = pixels.Top;
        return new Texture(
            decoded?.Name ?? pixels.Name,
            decoded?.Width ?? pixels.Width,
            decoded?.Height ?? pixels.Height,
            decoded?.Format ?? pixels.Format,
            key.SamplerState,
            RsxSamplerDecoder.Decode(
                key.SamplerState,
                image.MinLodControl,
                image.UseSrgbReads),
            RsxTextureCommandBuilder.FromImage(image),
            decoded?.HasTransparency ?? true,
            decoded?.PixelBytes ?? [],
            decoded is null ? [] : pixels.MipLevels,
            AuthoredSubresources: pixels.AuthoredSubresources,
            PixelFormat: decoded?.PixelFormat ??
                DecodedTexturePixelFormat.Rgba8Unorm);
    }

    private static void ValidateTwoDimensionalRequest(
        RenderTextureDecodeRequest request)
    {
        if (request.Key.Kind != RenderTextureCacheKeyKind.TwoDimensionalImage ||
            request.Key.Image is null)
        {
            throw new ArgumentException(
                "Batched texture decoding requires a two-dimensional image request with a canonical image.",
                nameof(request));
        }
    }

    private readonly record struct PixelDecodeKey(
        GfxImageAsset Image,
        bool IncludeAuthoredMipChain);

    private readonly record struct PixelDecodeResult(
        DecodedPixelChain? Pixels,
        ExceptionDispatchInfo? Exception);

    private sealed record DecodedPixelChain(
        DecodedTextureImage? Top,
        string Name,
        int Width,
        int Height,
        string Format,
        IReadOnlyList<TextureMip> MipLevels,
        IReadOnlyList<TextureAuthoredSubresource>
            AuthoredSubresources)
    {
        internal bool HasDecodedTop => Top is not null;
    }

    private delegate bool TryDecodeTexture(
        GfxImageAsset image,
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        out DecodedTextureImage decoded,
        out string reason);
}
