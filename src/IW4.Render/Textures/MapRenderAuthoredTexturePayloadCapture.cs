using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

/// <summary>
/// Partitions PS3 block-compressed image bytes into immutable render
/// subresources without changing the authored block contents. The executable
/// size switch proves the sequential tight block layout, while the canonical
/// decoder consumes the retained endpoints and indices in standard BC little-
/// endian order. Backend format support remains an independent runtime check.
/// </summary>
internal static class MapRenderAuthoredTexturePayloadCapture
{
    internal static bool IsCompleteProvenChain(
        IReadOnlyList<MapRenderTextureAuthoredSubresource> subresources,
        MapRenderTextureTarget target,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(subresources);
        int faceCount = target switch
        {
            MapRenderTextureTarget.Texture2D => 1,
            MapRenderTextureTarget.TextureCube
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
        MapRenderAuthoredBlockCompression compression =
            MapRenderAuthoredBlockCompression.Unknown;
        foreach (MapRenderTextureAuthoredSubresource? subresource in
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
                MapRenderAuthoredBlockCompression.Unknown)
            {
                compression = subresource.BlockCompression;
            }
            else if (compression != subresource.BlockCompression)
            {
                return false;
            }
            if (compression ==
                MapRenderAuthoredBlockCompression.Unknown)
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
        out MapRenderTextureAuthoredSubresource subresource)
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
                out MapRenderAuthoredBlockCompression blockCompression) ||
            payload.Count < slicePitch)
        {
            return false;
        }

        subresource = new MapRenderTextureAuthoredSubresource(
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
        out IReadOnlyList<MapRenderTextureAuthoredSubresource> subresources)
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

        var captured = new List<MapRenderTextureAuthoredSubresource>(
            checked(6 * mipCount));
        for (int face = 0; face < 6; face++)
        {
            int mipOffset = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);
                int slicePitch = slicePitches[mip];
                captured.Add(new MapRenderTextureAuthoredSubresource(
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
        out MapRenderAuthoredBlockCompression blockCompression)
    {
        rowPitch = 0;
        slicePitch = 0;
        blockCompression =
            MapRenderAuthoredBlockCompression.Unknown;
        if (width <= 0 || height <= 0 || image.Depth != 1)
            return false;

        byte baseFormat = (byte)(image.Format & 0x9f);
        int blockBytes = baseFormat switch
        {
            0x86 => 8,
            0x87 or 0x88 => 16,
            _ => 0,
        };
        if (blockBytes == 0)
            return false;

        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.Format,
            image.TextureFlags);
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
            blockCompression != MapRenderAuthoredBlockCompression.Unknown;
    }

    private static MapRenderAuthoredBlockCompression
        DescribeBlockCompression(byte format) =>
        (byte)(format & 0x9f) switch
        {
            0x86 => MapRenderAuthoredBlockCompression.Bc1,
            0x87 => MapRenderAuthoredBlockCompression.Bc2,
            0x88 => MapRenderAuthoredBlockCompression.Bc3,
            _ => MapRenderAuthoredBlockCompression.Unknown,
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
