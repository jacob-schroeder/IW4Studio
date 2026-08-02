using IW4.Assets.Assets.Image;

namespace IW4.FastFiles.Streaming.Images;

/// <summary>
/// Splits the cumulative low-resolution tail stored in the first PS3 2D
/// image-stream part. Higher stream parts contribute one successively larger
/// mip each; the smallest part retains the remaining mip chain followed by
/// the native 0x80-byte payload alignment.
/// </summary>
internal static class GfxImageStreamMipTailSplitter
{
    internal static bool TrySplit(
        GfxImageAsset image,
        GfxImageStreamData streamData,
        byte[] payload,
        out IReadOnlyList<GfxImageStreamMipPayload> mips)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(streamData);
        ArgumentNullException.ThrowIfNull(payload);

        mips = [];
        if (image.MapType != 3 ||
            image.DimensionCount != 2 ||
            image.MultiFaceControl != 0 ||
            image.Depth != 1 ||
            streamData.Width == 0 ||
            streamData.Height == 0)
        {
            return false;
        }

        int levelCount = streamData.LevelMarker;
        int maximumLevelCount = MaximumLevelCount(
            streamData.Width,
            streamData.Height);
        if (levelCount is < 2 || levelCount > maximumLevelCount)
            return false;

        uint formatKey = GfxImagePixelLayout.BuildFormatKey(
            image.Format,
            image.TextureFlags);
        var layouts = new List<(int Width, int Height, int ByteCount)>(
            levelCount);
        int width = streamData.Width;
        int height = streamData.Height;
        int unalignedByteCount = 0;
        for (int level = 0; level < levelCount; level++)
        {
            int byteCount = GfxImagePixelLayout.ComputeMipByteCount(
                formatKey,
                width,
                height,
                depth: 1);
            if (byteCount <= 0)
                return false;

            layouts.Add((width, height, byteCount));
            unalignedByteCount = checked(unalignedByteCount + byteCount);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        int alignedByteCount = checked(
            (unalignedByteCount + 0x7f) & ~0x7f);
        if (payload.Length != alignedByteCount)
            return false;

        var resolved = new List<GfxImageStreamMipPayload>(levelCount);
        int offset = 0;
        foreach ((int mipWidth, int mipHeight, int byteCount) in layouts)
        {
            byte[] mipPayload = payload
                .AsSpan(offset, byteCount)
                .ToArray();
            resolved.Add(new GfxImageStreamMipPayload(
                mipWidth,
                mipHeight,
                mipPayload));
            offset = checked(offset + byteCount);
        }

        mips = resolved.AsReadOnly();
        return true;
    }

    private static int MaximumLevelCount(int width, int height)
    {
        int count = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            count++;
        }

        return count;
    }
}
