using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

/// <summary>
/// Partitions PS3 block-compressed image bytes into immutable render
/// subresources without changing the authored block contents. The executable
/// size switch proves the sequential tight block layout, while the canonical
/// decoder consumes the retained endpoints and indices in standard BC little-
/// endian order. Backend format support remains an independent runtime check.
/// </summary>
internal static class AuthoredTexturePayloadCapture
{
    internal static bool IsCompleteProvenChain(
        IReadOnlyList<TextureAuthoredSubresource> subresources,
        TextureTarget target,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(subresources);
        int faceCount = target switch
        {
            TextureTarget.Texture2D => 1,
            TextureTarget.TextureCube
                when width == height => 6,
            _ => 0,
        };
        if (faceCount == 0 ||
            width <= 0 ||
            height <= 0 ||
            subresources.Count == 0)
        {
            return false;
        }

        int mipLevelCount;
        try
        {
            mipLevelCount = checked(
                subresources.Max(value => value.MipLevel) + 1);
            if (subresources.Count !=
                checked(faceCount * mipLevelCount))
            {
                return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        int maximumMipLevelCount = 1;
        int maximumMipWidth = width;
        int maximumMipHeight = height;
        while (maximumMipWidth > 1 || maximumMipHeight > 1)
        {
            maximumMipWidth = Math.Max(1, maximumMipWidth / 2);
            maximumMipHeight = Math.Max(1, maximumMipHeight / 2);
            maximumMipLevelCount++;
        }
        if (mipLevelCount > maximumMipLevelCount)
            return false;

        var seen = new bool[subresources.Count];
        AuthoredBlockCompression compression =
            AuthoredBlockCompression.Unknown;
        foreach (TextureAuthoredSubresource? subresource in
                 subresources)
        {
            if (subresource is null ||
                !subresource.IsDirectUploadLayoutProven ||
                subresource.FaceOrdinal >= faceCount ||
                subresource.MipLevel >= mipLevelCount ||
                subresource.Width !=
                    Math.Max(1, width >> subresource.MipLevel) ||
                subresource.Height !=
                    Math.Max(1, height >> subresource.MipLevel))
            {
                return false;
            }
            if (compression ==
                AuthoredBlockCompression.Unknown)
            {
                compression = subresource.BlockCompression;
            }
            else if (compression != subresource.BlockCompression)
            {
                return false;
            }
            if (compression ==
                AuthoredBlockCompression.Unknown)
            {
                return false;
            }

            int coordinate = checked(
                subresource.FaceOrdinal * mipLevelCount +
                subresource.MipLevel);
            if (seen[coordinate])
                return false;
            seen[coordinate] = true;
        }
        return seen.All(value => value);
    }

    internal static bool TryCaptureTwoDimensional(
        GfxImageAsset image,
        IReadOnlyList<byte> payload,
        int width,
        int height,
        int mipLevel,
        string format,
        out TextureAuthoredSubresource subresource)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payload);
        subresource = null!;
        if (!TryDescribeBlockRows(
                image,
                width,
                height,
                out int rowPitch,
                out int slicePitch,
                out AuthoredBlockCompression blockCompression) ||
            payload.Count < slicePitch)
        {
            return false;
        }

        subresource = new TextureAuthoredSubresource(
            faceOrdinal: 0,
            mipLevel,
            width,
            height,
            format,
            rowPitch,
            slicePitch,
            CopyRange(payload, 0, slicePitch),
            blockCompression,
            isDirectUploadLayoutProven: true);
        return true;
    }

    internal static bool TryCaptureCube(
        GfxImageAsset image,
        IReadOnlyList<byte> payload,
        int width,
        int height,
        int mipCount,
        int firstMipLevel,
        string format,
        out IReadOnlyList<TextureAuthoredSubresource> subresources)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payload);
        subresources = [];
        if (mipCount <= 0 || firstMipLevel < 0)
            return false;

        var rowPitches = new int[mipCount];
        var slicePitches = new int[mipCount];
        int facePayloadBytes = 0;
        for (int mip = 0; mip < mipCount; mip++)
        {
            int mipWidth = Math.Max(1, width >> mip);
            int mipHeight = Math.Max(1, height >> mip);
            if (!TryDescribeBlockRows(
                    image,
                    mipWidth,
                    mipHeight,
                    out rowPitches[mip],
                    out slicePitches[mip],
                    out _))
            {
                return false;
            }
            facePayloadBytes = checked(facePayloadBytes + slicePitches[mip]);
        }

        int faceStride = Align(facePayloadBytes, 0x80);
        int requiredBytes = checked(faceStride * 6);
        if (payload.Count != requiredBytes)
            return false;

        var captured = new List<TextureAuthoredSubresource>(
            checked(6 * mipCount));
        for (int face = 0; face < 6; face++)
        {
            int mipOffset = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);
                int slicePitch = slicePitches[mip];
                captured.Add(new TextureAuthoredSubresource(
                    face,
                    checked(firstMipLevel + mip),
                    mipWidth,
                    mipHeight,
                    format,
                    rowPitches[mip],
                    slicePitch,
                    CopyRange(
                        payload,
                        checked(face * faceStride + mipOffset),
                        slicePitch),
                    DescribeBlockCompression(image.Format),
                    isDirectUploadLayoutProven: true));
                mipOffset = checked(mipOffset + slicePitch);
            }
        }

        subresources = captured;
        return true;
    }

    private static bool TryDescribeBlockRows(
        GfxImageAsset image,
        int width,
        int height,
        out int rowPitch,
        out int slicePitch,
        out AuthoredBlockCompression blockCompression)
    {
        rowPitch = 0;
        slicePitch = 0;
        blockCompression =
            AuthoredBlockCompression.Unknown;
        if (width <= 0 || height <= 0 || image.Depth != 1)
            return false;

        int blockBytes = image.FormatEncoding.BaseFormat switch
        {
            GfxImageBaseFormat.CompressedDxt1 => 8,
            GfxImageBaseFormat.CompressedDxt23 or
            GfxImageBaseFormat.CompressedDxt45 => 16,
            _ => 0,
        };
        if (blockBytes == 0)
            return false;

        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.FormatEncoding,
            image.TextureRemap);
        int provenSize = GfxImagePixelLayout.ComputeMipByteCount(
            formatKey,
            width,
            height,
            depth: 1);
        rowPitch = checked(Math.Max(1, (width + 3) >> 2) * blockBytes);
        slicePitch = checked(rowPitch * Math.Max(1, (height + 3) >> 2));
        blockCompression = DescribeBlockCompression(image.Format);
        return provenSize != 0 &&
            provenSize == slicePitch &&
            blockCompression != AuthoredBlockCompression.Unknown;
    }

    private static AuthoredBlockCompression
        DescribeBlockCompression(byte format) =>
        new GfxImageFormat(format).BaseFormat switch
        {
            GfxImageBaseFormat.CompressedDxt1 =>
                AuthoredBlockCompression.Bc1,
            GfxImageBaseFormat.CompressedDxt23 =>
                AuthoredBlockCompression.Bc2,
            GfxImageBaseFormat.CompressedDxt45 =>
                AuthoredBlockCompression.Bc3,
            _ => AuthoredBlockCompression.Unknown,
        };

    private static byte[] CopyRange(
        IReadOnlyList<byte> source,
        int offset,
        int count)
    {
        if (source is byte[] array)
            return array.AsSpan(offset, count).ToArray();

        var result = new byte[count];
        for (int index = 0; index < count; index++)
            result[index] = source[checked(offset + index)];
        return result;
    }

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
