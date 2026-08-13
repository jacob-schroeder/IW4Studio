using IW4.Assets.Assets.Material;

namespace IW4.Render.Textures;

public static class RsxSamplerDecoder
{
    private const int Ps3SamplerClampMax = 7;
    private const int DefaultAnisotropyMin = 1;
    private const int DefaultAnisotropyMax = 16;
    private const float DefaultMipLodBias = 0f;

    public static RsxSamplerState Decode(
        byte samplerState,
        byte minLodControl = 0,
        byte useSrgbReads = 0) =>
        Decode(
            (MaterialSamplerState)samplerState,
            minLodControl,
            useSrgbReads);

    public static RsxSamplerState Decode(
        MaterialSamplerState samplerState,
        byte minLodControl = 0,
        byte useSrgbReads = 0)
    {
        byte rawState = (byte)samplerState;
        int tableIndex = rawState &
            (byte)(MaterialSamplerState.FilterMask |
                   MaterialSamplerState.MipMapMask);
        MaterialSamplerState filter = samplerState &
            MaterialSamplerState.FilterMask;
        MaterialSamplerState mipMap = samplerState &
            MaterialSamplerState.MipMapMask;
        int filterClass = (byte)filter;
        int mipClass = (byte)mipMap >> 3;
        TextureFilter mipFilter = DecodeMipFilter(mipMap);
        TextureFilter minFilter;
        TextureFilter magFilter;
        int maxAnisotropy;

        switch (filter)
        {
            case MaterialSamplerState.FilterLinear:
                minFilter = TextureFilter.Linear;
                magFilter = TextureFilter.Linear;
                maxAnisotropy = mipFilter == TextureFilter.None
                    ? 1
                    : Math.Clamp(DefaultAnisotropyMin, 1, DefaultAnisotropyMax);
                break;

            case MaterialSamplerState.FilterAnisotropic2X:
                minFilter = TextureFilter.Anisotropic;
                magFilter = TextureFilter.Anisotropic;
                maxAnisotropy = Math.Clamp(2, DefaultAnisotropyMin, DefaultAnisotropyMax);
                break;

            case MaterialSamplerState.FilterAnisotropic4X:
                minFilter = TextureFilter.Anisotropic;
                magFilter = TextureFilter.Anisotropic;
                maxAnisotropy = Math.Clamp(4, DefaultAnisotropyMin, DefaultAnisotropyMax);
                break;

            default:
                minFilter = TextureFilter.Point;
                magFilter = TextureFilter.Point;
                maxAnisotropy = 1;
                break;
        }

        return new RsxSamplerState(
            rawState,
            Ps3SamplerClampMax,
            minLodControl,
            useSrgbReads,
            BuildRsxSamplerCachePayload(
                samplerState,
                minLodControl,
                useSrgbReads),
            BuildRsxTexEnablePayload(samplerState, minLodControl),
            BuildRsxTexFilterPayload(samplerState, minLodControl),
            BuildRsxTexWrapPayload(samplerState, useSrgbReads),
            tableIndex,
            filterClass,
            mipClass,
            minFilter,
            magFilter,
            mipFilter,
            maxAnisotropy,
            DefaultMipLodBias,
            (samplerState & MaterialSamplerState.ClampU) == 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
            (samplerState & MaterialSamplerState.ClampV) == 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
            (samplerState & MaterialSamplerState.ClampW) == 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp);
    }

    public static uint RsxTexWrapMethod(ushort samplerSlot) => 0x1a08u + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexEnableMethod(ushort samplerSlot) => 0x1a0cu + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexFilterMethod(ushort samplerSlot) => 0x1a14u + ((uint)samplerSlot * 0x20u);

    private static uint BuildRsxSamplerCachePayload(
        MaterialSamplerState samplerState,
        byte minLodControl,
        byte useSrgbReads)
    {
        uint state = ClampSamplerState(samplerState);
        return ((uint)minLodControl << 2) |
               (((state & 0xffu) | ((uint)useSrgbReads << 8)) << 16);
    }

    private static uint BuildRsxTexEnablePayload(
        MaterialSamplerState samplerState,
        byte minLodControl)
    {
        uint state = ClampSamplerState(samplerState);
        uint filterClass = state & (byte)MaterialSamplerState.FilterMask;
        uint descriptorControl = (uint)minLodControl << 2;
        if (descriptorControl != 0)
        {
            uint filterEnable = filterClass switch
            {
                (byte)MaterialSamplerState.FilterLinear => 0u,
                (byte)MaterialSamplerState.FilterAnisotropic2X => 0x10u,
                (byte)MaterialSamplerState.FilterNearest => 0u,
                _ => 0x20u
            };
            return 0x80060000u | (descriptorControl << 19) | filterEnable;
        }

        return filterClass switch
        {
            (byte)MaterialSamplerState.FilterLinear => 0x80060000u,
            (byte)MaterialSamplerState.FilterAnisotropic2X => 0x80060010u,
            (byte)MaterialSamplerState.FilterNearest => 0x80060000u,
            _ => 0x80060020u
        };
    }

    private static uint BuildRsxTexFilterPayload(
        MaterialSamplerState samplerState,
        byte minLodControl)
    {
        uint state = ClampSamplerState(samplerState);
        uint filterClass = state & (byte)MaterialSamplerState.FilterMask;
        if (minLodControl != 0)
        {
            return filterClass == (byte)MaterialSamplerState.FilterNearest
                ? 0x01053fa0u
                : 0x02063fa0u;
        }

        uint filterBase = filterClass ==
            (byte)MaterialSamplerState.FilterNearest
                ? 0x01000000u
                : 0x02000000u;
        uint filterIndex = filterClass ==
            (byte)MaterialSamplerState.FilterNearest
                ? 1u
                : 2u;
        if ((state & (byte)MaterialSamplerState.MipMapMask) ==
            (byte)MaterialSamplerState.MipMapNearest)
            filterIndex += 2u;
        else if ((state & (byte)MaterialSamplerState.MipMapMask) ==
                 (byte)MaterialSamplerState.MipMapLinear)
            filterIndex += 4u;

        return filterBase | (filterIndex << 16) | 0x3fa0u;
    }

    private static uint BuildRsxTexWrapPayload(
        MaterialSamplerState samplerState,
        byte useSrgbReads)
    {
        uint state = ClampSamplerState(samplerState);
        uint gammaReadMask = (useSrgbReads & 1) == 0
            ? 0u
            : 0x00700000u;
        uint addressU = (state & (byte)MaterialSamplerState.ClampU) == 0
            ? 0x00000001u
            : 0x00000003u;
        uint addressV = (state & (byte)MaterialSamplerState.ClampV) == 0
            ? 0x00000100u
            : 0x00000300u;
        uint addressW = (state & (byte)MaterialSamplerState.ClampW) == 0
            ? 0x00010000u
            : 0x00030000u;
        return 0x40000000u |
               gammaReadMask |
               addressW |
               addressV |
               addressU;
    }

    private static uint ClampSamplerState(
        MaterialSamplerState samplerState)
    {
        uint state = (byte)samplerState;
        uint filterClass = state & (byte)MaterialSamplerState.FilterMask;
        return filterClass <= Ps3SamplerClampMax
            ? state
            : (state & 0xfffffff8u) | Ps3SamplerClampMax;
    }

    private static TextureFilter DecodeMipFilter(
        MaterialSamplerState mipMap)
    {
        return mipMap switch
        {
            MaterialSamplerState.MipMapDisabled => TextureFilter.None,
            MaterialSamplerState.MipMapNearest => TextureFilter.Point,
            MaterialSamplerState.MipMapLinear => TextureFilter.Linear,
            _ => TextureFilter.None
        };
    }
}
