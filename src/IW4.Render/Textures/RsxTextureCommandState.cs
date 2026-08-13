
namespace IW4.Render.Textures;

public sealed record RsxTextureCommandState(
    uint TexOffsetPayload,
    uint TexFormatPayload,
    uint TexNpotSizePayload,
    uint TexSize1Payload,
    uint TexSwizzlePayload);
