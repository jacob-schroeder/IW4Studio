using System.Buffers.Binary;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;

namespace IW4.Assets.Assets.GfxMap;

/// <summary>
/// Binary contract shared by the PS3 GfxWorld loader and renderer-owned
/// runtime texture producers. GfxTexture is the first 0x18 bytes of GfxImage.
/// </summary>
public static class GfxTextureCodec
{
    public static GfxTexture Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != GfxTexture.SerializedSize)
        {
            throw new ArgumentException(
                $"A PS3 GfxTexture descriptor requires exactly 0x{GfxTexture.SerializedSize:X} bytes; received 0x{bytes.Length:X}.",
                nameof(bytes));
        }

        var words = new uint[GfxTexture.WordCount];
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt32BigEndian(
                bytes.Slice(index * sizeof(uint), sizeof(uint)));
        }

        return new GfxTexture(words);
    }

    public static byte[] Encode(GfxTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var bytes = new byte[GfxTexture.SerializedSize];
        for (int index = 0; index < texture.Words.Count; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)),
                texture.Words[index]);
        }

        return bytes;
    }

    public static GfxTexture FromImage(GfxImageAsset image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new GfxTexture(
        [
            (uint)image.Format << 24 |
            (uint)image.LevelCount << 16 |
            (uint)image.DimensionCount << 8 |
            image.MultiFaceControl,
            image.TextureControl1,
            (uint)image.Width << 16 | image.Height,
            (uint)image.Depth << 16 |
            (uint)image.MemoryLocation << 8 |
            image.MinLodControl,
            image.RenderTargetPitch,
            image.PixelsOffset
        ]);
    }
}
