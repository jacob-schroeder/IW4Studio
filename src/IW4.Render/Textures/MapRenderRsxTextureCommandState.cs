using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Textures;

public sealed record MapRenderRsxTextureCommandState(
    uint TexOffsetPayload,
    uint TexFormatPayload,
    uint TexNpotSizePayload,
    uint TexSize1Payload,
    uint TexSwizzlePayload);
