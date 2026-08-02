using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Textures;

public sealed record MapRenderSamplerState(
    byte RawState,
    int RsxClampMax,
    byte RsxDescriptorPad0F,
    byte RsxDescriptorPad1B,
    uint RsxSamplerCachePayload,
    uint RsxTexEnablePayload,
    uint RsxTexFilterPayload,
    uint RsxTexWrapPayload,
    int TableIndex,
    int FilterClass,
    int MipClass,
    MapRenderTextureFilter MinFilter,
    MapRenderTextureFilter MagFilter,
    MapRenderTextureFilter MipFilter,
    int MaxAnisotropy,
    float MipLodBias,
    MapRenderTextureAddressMode AddressU,
    MapRenderTextureAddressMode AddressV,
    MapRenderTextureAddressMode AddressW);
