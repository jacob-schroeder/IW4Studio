
using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

public sealed record RsxTextureCommandState(
    uint TexOffsetPayload,
    uint TexFormatPayload,
    uint TexNpotSizePayload,
    uint TexSize1Payload,
    uint TexSwizzlePayload)
{
    public GfxImageFormat Format =>
        new((byte)(TexFormatPayload >> 8));

    public GfxImageTextureRemap TextureRemap =>
        new(TexSwizzlePayload);
}
