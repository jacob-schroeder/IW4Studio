
namespace IW4.Render.Textures;

public sealed record RsxSamplerState(
    byte RawState,
    int RsxClampMax,
    byte MinLodControl,
    byte UseSrgbReads,
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
    TextureAddressMode AddressW)
{
    /// <summary>
    /// RSX gamma decode is controlled only by bit 0 of the preserved native
    /// useSrgbReads byte. Bits 1..7 participate in IW4's cache key but do not
    /// change the emitted texture-address state.
    /// </summary>
    public bool UsesSrgbReads => (UseSrgbReads & 1) != 0;
}
