using System.Globalization;
using System.Text;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Textures;

public static class RsxTextureCommandBuilder
{
    public static RsxTextureCommandState FromImage(GfxImageAsset image)
    {
        return new RsxTextureCommandState(
            image.PixelsOffset,
            BuildTexFormatPayload(image, image.LevelCount, image.PixelDataBlock),
            ((uint)image.Width << 16) | image.Height,
            ((uint)image.Depth << 20) | image.RenderTargetPitch,
            image.TextureFlags);
    }

    public static RsxTextureCommandState FromDescriptor(GfxTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        uint header = texture.Words[0];
        uint depthAndBlock = texture.Words[3];
        byte format = (byte)(header >> 24);
        byte levelCount = (byte)(header >> 16);
        byte dimensionCount = (byte)(header >> 8);
        byte multiFaceControl = (byte)header;
        ushort depth = (ushort)(depthAndBlock >> 16);
        byte pixelDataBlock = (byte)(depthAndBlock >> 8);
        return new RsxTextureCommandState(
            texture.Words[5],
            (uint)(pixelDataBlock + 1) |
            ((uint)multiFaceControl << 2) |
            ((uint)dimensionCount << 4) |
            ((uint)format << 8) |
            ((uint)levelCount << 16) |
            0x8u,
            texture.Words[2],
            ((uint)depth << 20) | texture.Words[4],
            texture.Words[1]);
    }

    public static uint RsxTexOffsetMethod(ushort samplerSlot) => 0x1a00u + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexFormatMethod(ushort samplerSlot) => 0x1a04u + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexSwizzleMethod(ushort samplerSlot) => 0x1a10u + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexNpotSizeMethod(ushort samplerSlot) => 0x1a18u + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexSize1Method(ushort samplerSlot) => 0x1840u + ((uint)samplerSlot * 0x04u);

    private static uint BuildTexFormatPayload(GfxImageAsset image, byte levelCount, byte pixelDataBlock)
    {
        return (uint)(pixelDataBlock + 1) |
               ((uint)image.MultiFaceControl << 2) |
               ((uint)image.DimensionCount << 4) |
               ((uint)image.Format << 8) |
               ((uint)levelCount << 16) |
               0x8u;
    }
}
