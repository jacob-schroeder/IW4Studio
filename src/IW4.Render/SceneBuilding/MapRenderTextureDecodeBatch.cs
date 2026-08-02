using System.Runtime.ExceptionServices;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

internal readonly record struct MapRenderTextureDecodeRequest(
    MapRenderTextureCacheKey Key)
{
    internal static MapRenderTextureDecodeRequest Create(
        MaterialTextureDef materialTexture,
        GfxImageAsset image,
        bool includeAuthoredMipChain) => new(
            MapRenderTextureCacheKey.Standard(
                materialTexture,
                image,
                includeAuthoredMipChain));
}

internal readonly record struct MapRenderTextureDecodeResult(
    MapRenderTexture? Texture,
    ExceptionDispatchInfo? Exception)
{
    internal bool Success => Texture is not null;

    internal bool HasDecodedRgba =>
        Texture?.HasCompleteDecodedRgbaPayload == true;
}

internal delegate bool MapRenderTryDecodeRgba(
    GfxImageAsset image,
    IReadOnlyList<byte> payloadBytes,
    int width,
    int height,
    out DecodedRgbaGfxImage decoded,
    out string reason);

internal static class MapRenderTextureDecodeBatch
{
    internal static MapRenderTextureDecodeResult Decode(
        MapRenderTextureDecodeRequest request,
        IGfxImagePayloadResolver imageStreams) =>
        Decode(
            request,
            imageStreams,
            GfxImageDecoder.TryDecodeRgba,
            preferProvenAuthoredPayloads: false);

    internal static MapRenderTextureDecodeResult Decode(
        MapRenderTextureDecodeRequest request,
        IGfxImagePayloadResolver imageStreams,
        bool preferProvenAuthoredPayloads) =>
        Decode(
            request,
            imageStreams,
            GfxImageDecoder.TryDecodeRgba,
            preferProvenAuthoredPayloads);

    internal static MapRenderTextureDecodeResult Decode(
        MapRenderTextureDecodeRequest request,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTryDecodeRgba tryDecodeRgba,
        bool preferProvenAuthoredPayloads = false)
    {
        ArgumentNullException.ThrowIfNull(tryDecodeRgba);
        try
        {
            ValidateStandardRequest(request);
            MapRenderDecodedPixelChain? pixels = DecodePixels(
                new MapRenderPixelDecodeKey(
                    request.Key.Image,
                    request.Key.IncludeAuthoredMipChain),
                imageStreams,
                tryDecodeRgba,
                preferProvenAuthoredPayloads);
            return new MapRenderTextureDecodeResult(
                pixels is null
                    ? null
                    : MaterializeTexture(request.Key, pixels),
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new MapRenderTextureDecodeResult(
                null,
                ExceptionDispatchInfo.Capture(exception));
        }
    }

    internal static void DecodeUnique(
        IReadOnlyList<MapRenderTextureDecodeRequest> requests,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(imageStreams);
        ArgumentNullException.ThrowIfNull(textureCache);
        ArgumentNullException.ThrowIfNull(failedTextureCacheKeys);

        var seen = new HashSet<MapRenderTextureCacheKey>();
        MapRenderTextureDecodeRequest[] pending = requests
            .Where(request =>
                !textureCache.ContainsKey(request.Key) &&
                !failedTextureCacheKeys.Contains(request.Key) &&
                seen.Add(request.Key))
            .ToArray();
        if (pending.Length == 0)
            return;

        foreach (MapRenderTextureDecodeRequest request in pending)
            ValidateStandardRequest(request);

        var pixelOrdinalByKey = new Dictionary<MapRenderPixelDecodeKey, int>();
        var pixelKeys = new List<MapRenderPixelDecodeKey>();
        foreach (MapRenderTextureDecodeRequest request in pending)
        {
            var pixelKey = new MapRenderPixelDecodeKey(
                request.Key.Image,
                request.Key.IncludeAuthoredMipChain);
            if (!pixelOrdinalByKey.ContainsKey(pixelKey))
            {
                pixelOrdinalByKey.Add(pixelKey, pixelKeys.Count);
                pixelKeys.Add(pixelKey);
            }
        }

        var pixelResults = new MapRenderPixelDecodeResult[pixelKeys.Count];
        if (pixelKeys.Count == 1)
        {
            pixelResults[0] = DecodePixelsSafely(
                pixelKeys[0],
                imageStreams,
                GfxImageDecoder.TryDecodeRgba,
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
                    GfxImageDecoder.TryDecodeRgba,
                    textureCache.PreferProvenAuthoredPayloads));
        }

        // Worker completion order is deliberately irrelevant. Publish cache
        // entries, diagnostics, and exceptions in first-request order.
        for (int index = 0; index < pending.Length; index++)
        {
            MapRenderTextureDecodeRequest request = pending[index];
            var pixelKey = new MapRenderPixelDecodeKey(
                request.Key.Image,
                request.Key.IncludeAuthoredMipChain);
            MapRenderPixelDecodeResult pixelResult =
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

    private static MapRenderPixelDecodeResult DecodePixelsSafely(
        MapRenderPixelDecodeKey key,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTryDecodeRgba tryDecodeRgba,
        bool preferProvenAuthoredPayloads)
    {
        try
        {
            return new MapRenderPixelDecodeResult(
                DecodePixels(
                    key,
                    imageStreams,
                    tryDecodeRgba,
                    preferProvenAuthoredPayloads),
                null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new MapRenderPixelDecodeResult(
                null,
                ExceptionDispatchInfo.Capture(exception));
        }
    }

    private static MapRenderDecodedPixelChain? DecodePixels(
        MapRenderPixelDecodeKey key,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTryDecodeRgba tryDecodeRgba,
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
            new List<MapRenderTextureAuthoredSubresource>();
        string authoredFormat = GfxImageDecoder.DescribeFormat(image.Format);
        if (MapRenderAuthoredTexturePayloadCapture.TryCaptureTwoDimensional(
                image,
                payload,
                width,
                height,
                mipLevel: 0,
                authoredFormat,
                out MapRenderTextureAuthoredSubresource authoredTop))
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
                bool capturedAuthored = MapRenderAuthoredTexturePayloadCapture
                    .TryCaptureTwoDimensional(
                        image,
                        mip.Payload,
                        mip.Width,
                        mip.Height,
                        sourceMipIndex,
                        authoredFormat,
                        out MapRenderTextureAuthoredSubresource authoredMip);
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
            MapRenderAuthoredTexturePayloadCapture
                .IsCompleteProvenChain(
                    authoredSubresources,
                    MapRenderTextureTarget.Texture2D,
                    width,
                    height))
        {
            return new MapRenderDecodedPixelChain(
                Top: null,
                image.Name ?? "unnamed_image",
                width,
                height,
                authoredFormat,
                MipLevels: [],
                authoredSubresources);
        }

        bool hasDecodedTop = tryDecodeRgba(
            image,
            payload,
            width,
            height,
            out DecodedRgbaGfxImage decoded,
            out reason);
        // Authored-only publication is reserved for the complete proven-chain
        // fast path above. A partial capture must never masquerade as a usable
        // texture, and Neutral builds retain their decoded compatibility
        // representation.
        if (!hasDecodedTop)
            return null;

        List<MapRenderTextureMip> mipLevels = [];
        if (key.IncludeAuthoredMipChain && resolvedStream)
        {
            bool decodedMipChainOpen = hasDecodedTop;
            for (int sourceMipIndex = 1;
                 sourceMipIndex < streamMips.Count;
                 sourceMipIndex++)
            {
                GfxImagePayload mip = streamMips[sourceMipIndex];
                DecodedRgbaGfxImage mipDecoded = default;
                bool decodedMip = decodedMipChainOpen && tryDecodeRgba(
                    image,
                    mip.Payload,
                    mip.Width,
                    mip.Height,
                    out mipDecoded,
                    out _);
                if (decodedMip)
                {
                    mipLevels.Add(new MapRenderTextureMip(
                        mipDecoded.Width,
                        mipDecoded.Height,
                        mipDecoded.RgbaBytes));
                }
                else
                {
                    decodedMipChainOpen = false;
                    if (!capturedStreamMips[sourceMipIndex])
                        break;
                }
            }
        }

        return new MapRenderDecodedPixelChain(
            hasDecodedTop ? decoded : null,
            image.Name ?? "unnamed_image",
            width,
            height,
            authoredFormat,
            mipLevels,
            authoredSubresources);
    }

    private static MapRenderTexture MaterializeTexture(
        MapRenderTextureCacheKey key,
        MapRenderDecodedPixelChain pixels)
    {
        GfxImageAsset image = key.Image;
        DecodedRgbaGfxImage? decoded = pixels.Top;
        return new MapRenderTexture(
            decoded?.Name ?? pixels.Name,
            decoded?.Width ?? pixels.Width,
            decoded?.Height ?? pixels.Height,
            decoded?.Format ?? pixels.Format,
            key.SamplerState,
            MapRenderSamplerDecoder.Decode(
                key.SamplerState,
                image.Pad0F,
                image.Pad1B),
            MapRenderRsxTextureCommandBuilder.FromImage(image),
            decoded?.HasTransparency ?? true,
            decoded?.RgbaBytes ?? [],
            decoded is null ? [] : pixels.MipLevels,
            AuthoredSubresources: pixels.AuthoredSubresources);
    }

    private static void ValidateStandardRequest(
        MapRenderTextureDecodeRequest request)
    {
        if (request.Key.Kind != MapRenderTextureCacheKeyKind.Standard ||
            request.Key.Image is null)
        {
            throw new ArgumentException(
                "Batched scene texture decoding requires a standard request with a canonical image.",
                nameof(request));
        }
    }

    private readonly record struct MapRenderPixelDecodeKey(
        GfxImageAsset Image,
        bool IncludeAuthoredMipChain);

    private readonly record struct MapRenderPixelDecodeResult(
        MapRenderDecodedPixelChain? Pixels,
        ExceptionDispatchInfo? Exception);

    private sealed record MapRenderDecodedPixelChain(
        DecodedRgbaGfxImage? Top,
        string Name,
        int Width,
        int Height,
        string Format,
        IReadOnlyList<MapRenderTextureMip> MipLevels,
        IReadOnlyList<MapRenderTextureAuthoredSubresource>
            AuthoredSubresources)
    {
        internal bool HasDecodedTop => Top is not null;
    }
}
