using IW4.AssetExchange.SourceFormat.Image;
using IW4.Assets.Assets.Image;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>
/// Converts complete PS3 image payloads into the mip-major RGBA8 layout used
/// by the source DDS writer. Package completeness remains a source-dump
/// requirement even though interactive rendering can use a partial prefix.
/// </summary>
internal static class SourceImageDumpDecoder
{
    internal static IReadOnlyList<ImageSourceMipLevel> Decode(
        GfxImageAsset image,
        WorkspaceGfxImagePayloadResolver payloadResolver)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payloadResolver);

        if (payloadResolver.TryResolveMipPayloads(
                image,
                out IReadOnlyList<GfxImagePayload> streamedMips,
                out string streamedReason) &&
            streamedMips.Count != 0)
        {
            IReadOnlyList<GfxImagePayload> completeMips = IsVolume(image)
                ? ExpandStreamedVolumeMipTails(image, streamedMips)
                : streamedMips;
            ValidateCompleteStreamChain(image, completeMips);
            return DecodeStreamedMips(image, completeMips);
        }

        if (image.PayloadBytes.Count != 0)
            return DecodeInlineMips(image);

        throw new InvalidDataException(
            string.IsNullOrWhiteSpace(streamedReason)
                ? "no inline or streamed image payload is available"
                : streamedReason);
    }

    private static IReadOnlyList<ImageSourceMipLevel> DecodeInlineMips(
        GfxImageAsset image)
    {
        if (image.LevelCount == 0)
            throw new InvalidDataException("inline image payload has no mip levels");

        int expectedPayloadBytes = GfxImagePixelLayout.ComputePayloadByteCount(
            image.FormatEncoding,
            image.LevelCount,
            image.IsCubemap,
            image.TextureRemap,
            image.Width,
            image.Height,
            image.Depth);
        if (image.PayloadBytes.Count != expectedPayloadBytes)
        {
            throw new InvalidDataException(
                $"inline payload has 0x{image.PayloadBytes.Count:X} byte(s); " +
                $"the complete {image.LevelCount}-mip RSX layout needs " +
                $"0x{expectedPayloadBytes:X}");
        }

        if (IsCube(image))
            return DecodeInlineCube(image);
        if (IsVolume(image))
            return DecodeInlineLinearChain(image, isVolume: true);
        if (IsTwoDimensional(image))
            return DecodeInlineLinearChain(image, isVolume: false);

        throw UnsupportedShape(image);
    }

    private static IReadOnlyList<ImageSourceMipLevel> DecodeInlineLinearChain(
        GfxImageAsset image,
        bool isVolume)
    {
        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.FormatEncoding,
            image.TextureRemap);
        byte[] payload = image.PayloadBytes as byte[] ??
            image.PayloadBytes.ToArray();
        var decodedMips = new ImageSourceMipLevel[image.LevelCount];
        int offset = 0;
        for (int mipLevel = 0; mipLevel < decodedMips.Length; mipLevel++)
        {
            int width = Math.Max(1, image.Width >> mipLevel);
            int height = Math.Max(1, image.Height >> mipLevel);
            int depth = isVolume
                ? Math.Max(1, image.Depth >> mipLevel)
                : 1;
            int byteCount = GfxImagePixelLayout.ComputeMipByteCount(
                formatKey,
                width,
                height,
                depth);
            if (byteCount <= 0)
            {
                throw new NotSupportedException(
                    $"native image format key 0x{formatKey:X8} has no proven " +
                    "PS3 payload layout");
            }

            byte[] rawMip = payload.AsSpan(offset, byteCount).ToArray();
            byte[] rgba;
            string reason;
            bool decoded = isVolume
                ? GfxImageDecoder.TryDecodeVolumeRgba(
                    image,
                    rawMip,
                    width,
                    height,
                    depth,
                    out rgba,
                    out reason)
                : TryDecodeTwoDimensional(
                    image,
                    rawMip,
                    width,
                    height,
                    out rgba,
                    out reason);
            if (!decoded)
            {
                throw new InvalidDataException(
                    $"mip {mipLevel} ({width}x{height}x{depth}) could not be " +
                    $"decoded: {reason}");
            }

            decodedMips[mipLevel] = new ImageSourceMipLevel(
                width,
                height,
                depth,
                rgba);
            offset = checked(offset + byteCount);
        }

        return decodedMips;
    }

    private static IReadOnlyList<GfxImagePayload> ExpandStreamedVolumeMipTails(
        GfxImageAsset image,
        IReadOnlyList<GfxImagePayload> resolvedPayloads)
    {
        int topDepth = GetStreamTopDepth(image);
        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.FormatEncoding,
            image.TextureRemap);
        var expanded = new List<GfxImagePayload>();
        foreach (GfxImagePayload payload in resolvedPayloads)
        {
            GfxImageStreamData? streamPart = image.StreamData.FirstOrDefault(
                value => value.HasStreamingData &&
                         value.Width == payload.Width &&
                         value.Height == payload.Height);
            int tailLevelCount = streamPart?.LevelMarker ?? 0;
            int firstDepth = Math.Max(1, topDepth >> expanded.Count);
            if (tailLevelCount >= 2 &&
                TrySplitVolumeMipTail(
                    formatKey,
                    payload,
                    firstDepth,
                    tailLevelCount,
                    out IReadOnlyList<GfxImagePayload> tailMips))
            {
                expanded.AddRange(tailMips);
            }
            else
            {
                expanded.Add(payload);
            }
        }

        return expanded.AsReadOnly();
    }

    private static bool TrySplitVolumeMipTail(
        uint formatKey,
        GfxImagePayload payload,
        int depth,
        int levelCount,
        out IReadOnlyList<GfxImagePayload> mips)
    {
        mips = [];
        var layouts = new (int Width, int Height, int Depth, int ByteCount)[
            levelCount];
        int width = payload.Width;
        int height = payload.Height;
        int unalignedByteCount = 0;
        for (int level = 0; level < levelCount; level++)
        {
            int byteCount = GfxImagePixelLayout.ComputeMipByteCount(
                formatKey,
                width,
                height,
                depth);
            if (byteCount <= 0)
                return false;

            layouts[level] = (width, height, depth, byteCount);
            unalignedByteCount = checked(unalignedByteCount + byteCount);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            depth = Math.Max(1, depth / 2);
        }

        int alignedByteCount = checked(
            (unalignedByteCount + 0x7f) & ~0x7f);
        if (payload.Payload.Count != alignedByteCount)
            return false;

        byte[] source = payload.Payload as byte[] ?? payload.Payload.ToArray();
        var split = new GfxImagePayload[levelCount];
        int offset = 0;
        for (int level = 0; level < layouts.Length; level++)
        {
            var (mipWidth, mipHeight, _, byteCount) = layouts[level];
            split[level] = new GfxImagePayload(
                mipWidth,
                mipHeight,
                source.AsSpan(offset, byteCount).ToArray());
            offset = checked(offset + byteCount);
        }

        mips = split;
        return true;
    }

    private static int GetStreamTopDepth(GfxImageAsset image)
    {
        if (image.BaseDepth == 0)
        {
            throw new InvalidDataException(
                "streamed volume image has no authored base depth");
        }

        return image.BaseDepth;
    }

    private static IReadOnlyList<ImageSourceMipLevel> DecodeInlineCube(
        GfxImageAsset image)
    {
        if (!CubeTextureDecoder.TryDecode(
                image,
                image.PayloadBytes,
                image.Width,
                image.Height,
                image.LevelCount,
                out DecodedCubeTexture cube,
                out string reason))
        {
            throw new InvalidDataException(
                $"cubemap payload could not be decoded: {reason}");
        }

        var decodedMips = new ImageSourceMipLevel[image.LevelCount];
        for (int mipLevel = 0; mipLevel < decodedMips.Length; mipLevel++)
            decodedMips[mipLevel] = FlattenCubeMip(cube, mipLevel);
        return decodedMips;
    }

    private static IReadOnlyList<ImageSourceMipLevel> DecodeStreamedMips(
        GfxImageAsset image,
        IReadOnlyList<GfxImagePayload> streamedMips)
    {
        bool isCube = IsCube(image);
        bool isVolume = IsVolume(image);
        bool isTwoDimensional = IsTwoDimensional(image);
        if (!isCube && !isVolume && !isTwoDimensional)
            throw UnsupportedShape(image);

        var decodedMips = new ImageSourceMipLevel[streamedMips.Count];
        for (int mipLevel = 0; mipLevel < streamedMips.Count; mipLevel++)
        {
            GfxImagePayload source = streamedMips[mipLevel];
            if (isCube)
            {
                if (!CubeTextureDecoder.TryDecode(
                        image,
                        source.Payload,
                        source.Width,
                        source.Height,
                        mipCount: 1,
                        out DecodedCubeTexture cube,
                        out string cubeReason))
                {
                    throw new InvalidDataException(
                        $"streamed cubemap mip {mipLevel} could not be " +
                        $"decoded: {cubeReason}");
                }

                decodedMips[mipLevel] = FlattenCubeMip(cube, mipLevel: 0);
                continue;
            }

            int depth = isVolume
                ? Math.Max(1, GetStreamTopDepth(image) >> mipLevel)
                : 1;
            bool decoded = isVolume
                ? GfxImageDecoder.TryDecodeVolumeRgba(
                    image,
                    source.Payload,
                    source.Width,
                    source.Height,
                    depth,
                    out byte[] rgba,
                    out string reason)
                : TryDecodeTwoDimensional(
                    image,
                    source.Payload,
                    source.Width,
                    source.Height,
                    out rgba,
                    out reason);
            if (!decoded)
            {
                throw new InvalidDataException(
                    $"streamed mip {mipLevel} could not be decoded: {reason}");
            }

            decodedMips[mipLevel] = new ImageSourceMipLevel(
                source.Width,
                source.Height,
                depth,
                rgba);
        }

        return decodedMips;
    }

    private static void ValidateCompleteStreamChain(
        GfxImageAsset image,
        IReadOnlyList<GfxImagePayload> streamedMips)
    {
        GfxImageStreamData[] activeParts = image.StreamData
            .Where(value => value.HasStreamingData)
            .ToArray();
        if (activeParts.Length == 0)
        {
            throw new InvalidDataException(
                "the payload resolver returned stream mips for an image with " +
                "no active stream profile");
        }

        int expectedMipCount = activeParts.Max(value => value.LevelMarker);
        if (expectedMipCount <= 0)
        {
            throw new InvalidDataException(
                "the active image stream profile has no mip-level marker");
        }
        if (streamedMips.Count != expectedMipCount)
        {
            throw new InvalidDataException(
                $"stream resolver returned {streamedMips.Count} mip(s); the " +
                $"complete package profile requires {expectedMipCount}");
        }

        GfxImageStreamData topPart = activeParts.MaxBy(value =>
            (long)value.Width * value.Height)!;
        int expectedWidth = topPart.Width;
        int expectedHeight = topPart.Height;
        for (int mipLevel = 0; mipLevel < streamedMips.Count; mipLevel++)
        {
            GfxImagePayload mip = streamedMips[mipLevel];
            if (mip.Width != expectedWidth || mip.Height != expectedHeight)
            {
                throw new InvalidDataException(
                    $"streamed mip {mipLevel} is {mip.Width}x{mip.Height}; " +
                    $"the complete package profile requires " +
                    $"{expectedWidth}x{expectedHeight}");
            }

            expectedWidth = Math.Max(1, expectedWidth / 2);
            expectedHeight = Math.Max(1, expectedHeight / 2);
        }
    }

    private static bool TryDecodeTwoDimensional(
        GfxImageAsset image,
        IReadOnlyList<byte> payload,
        int width,
        int height,
        out byte[] rgba,
        out string reason)
    {
        if (GfxImageDecoder.TryDecodeRgba(
                image,
                payload,
                width,
                height,
                out DecodedRgbaGfxImage decoded,
                out reason))
        {
            rgba = decoded.RgbaBytes;
            return true;
        }

        rgba = [];
        return false;
    }

    private static ImageSourceMipLevel FlattenCubeMip(
        DecodedCubeTexture cube,
        int mipLevel)
    {
        if (cube.Faces.Count != 6 ||
            cube.Faces.Any(face => mipLevel >= face.Count))
        {
            throw new InvalidDataException(
                $"decoded cubemap does not contain all six faces for mip {mipLevel}");
        }

        TextureMip topFace = cube.Faces[0][mipLevel];
        int faceByteCount = checked(topFace.Width * topFace.Height * 4);
        byte[] rgba = new byte[checked(faceByteCount * 6)];
        for (int faceOrdinal = 0; faceOrdinal < 6; faceOrdinal++)
        {
            TextureMip face = cube.Faces[faceOrdinal][mipLevel];
            if (face.Width != topFace.Width ||
                face.Height != topFace.Height ||
                face.PixelBytes.Length != faceByteCount)
            {
                throw new InvalidDataException(
                    $"decoded cubemap face {faceOrdinal} has an inconsistent " +
                    $"layout at mip {mipLevel}");
            }

            face.PixelBytes.CopyTo(rgba, faceOrdinal * faceByteCount);
        }

        return new ImageSourceMipLevel(
            topFace.Width,
            topFace.Height,
            Depth: 1,
            rgba);
    }

    private static bool IsTwoDimensional(GfxImageAsset image) =>
        image.MapType == MapType.TwoDimensional &&
        image.DimensionCount == GfxImageDimension.TwoDimensional &&
        !image.IsCubemap &&
        image.Depth == 1;

    private static bool IsCube(GfxImageAsset image) =>
        image.MapType == MapType.Cube &&
        image.DimensionCount == GfxImageDimension.TwoDimensional &&
        image.IsCubemap &&
        image.Depth == 1;

    private static bool IsVolume(GfxImageAsset image) =>
        image.MapType == MapType.ThreeDimensional &&
        image.DimensionCount == GfxImageDimension.ThreeDimensional &&
        !image.IsCubemap;

    private static NotSupportedException UnsupportedShape(
        GfxImageAsset image) => new(
        $"unsupported image shape {image.MapType}/{image.DimensionCount} " +
        $"(cubemap={image.IsCubemap}, depth={image.Depth})");
}
