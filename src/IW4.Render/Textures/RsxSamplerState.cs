
namespace IW4.Render.Textures;

public sealed record RsxSamplerState(
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
    TextureFilter MinFilter,
    TextureFilter MagFilter,
    TextureFilter MipFilter,
    int MaxAnisotropy,
    float MipLodBias,
    TextureAddressMode AddressU,
    TextureAddressMode AddressV,
    TextureAddressMode AddressW);
