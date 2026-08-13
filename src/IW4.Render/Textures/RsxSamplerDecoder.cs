namespace IW4.Render.Textures;

public static class RsxSamplerDecoder
{
    private const int Ps3SamplerClampMax = 7;
    private const int DefaultAnisotropyMin = 1;
    private const int DefaultAnisotropyMax = 16;
    private const float DefaultMipLodBias = 0f;

    public static RsxSamplerState Decode(byte samplerState, byte descriptorPad0F = 0, byte descriptorPad1B = 0)
    {
        int tableIndex = samplerState & 0x1f;
        int filterClass = tableIndex & 0x07;
        int mipClass = (tableIndex & 0x18) >> 3;
        TextureFilter mipFilter = DecodeMipFilter(mipClass);
        TextureFilter minFilter;
        TextureFilter magFilter;
        int maxAnisotropy;

        switch (filterClass)
        {
            case 2:
                minFilter = TextureFilter.Linear;
                magFilter = TextureFilter.Linear;
                maxAnisotropy = mipFilter == TextureFilter.None
                    ? 1
                    : Math.Clamp(DefaultAnisotropyMin, 1, DefaultAnisotropyMax);
                break;

            case 3:
                minFilter = TextureFilter.Anisotropic;
                magFilter = TextureFilter.Anisotropic;
                maxAnisotropy = Math.Clamp(2, DefaultAnisotropyMin, DefaultAnisotropyMax);
                break;

            case 4:
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
            samplerState,
            Ps3SamplerClampMax,
            descriptorPad0F,
            descriptorPad1B,
            BuildRsxSamplerCachePayload(samplerState, descriptorPad0F, descriptorPad1B),
            BuildRsxTexEnablePayload(samplerState, descriptorPad0F),
            BuildRsxTexFilterPayload(samplerState, descriptorPad0F),
            BuildRsxTexWrapPayload(samplerState, descriptorPad1B),
            tableIndex,
            filterClass,
            mipClass,
            minFilter,
            magFilter,
            mipFilter,
            maxAnisotropy,
            DefaultMipLodBias,
            (samplerState & 0x20) == 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
            (samplerState & 0x40) == 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp,
            (samplerState & 0x80) == 0 ? TextureAddressMode.Wrap : TextureAddressMode.Clamp);
    }

    public static uint RsxTexWrapMethod(ushort samplerSlot) => 0x1a08u + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexEnableMethod(ushort samplerSlot) => 0x1a0cu + ((uint)samplerSlot * 0x20u);

    public static uint RsxTexFilterMethod(ushort samplerSlot) => 0x1a14u + ((uint)samplerSlot * 0x20u);

    private static uint BuildRsxSamplerCachePayload(byte samplerState, byte descriptorPad0F, byte descriptorPad1B)
    {
        uint state = ClampSamplerState(samplerState);
        return ((uint)descriptorPad0F << 2) |
               (((state & 0xffu) | ((uint)descriptorPad1B << 8)) << 16);
    }

    private static uint BuildRsxTexEnablePayload(byte samplerState, byte descriptorPad0F)
    {
        uint state = ClampSamplerState(samplerState);
        uint filterClass = state & 7u;
        uint descriptorControl = (uint)descriptorPad0F << 2;
        if (descriptorControl != 0)
        {
            uint filterEnable = filterClass switch
            {
                2 => 0u,
                3 => 0x10u,
                1 => 0u,
                _ => 0x20u
            };
            return 0x80060000u | (descriptorControl << 19) | filterEnable;
        }

        return filterClass switch
        {
            2 => 0x80060000u,
            3 => 0x80060010u,
            1 => 0x80060000u,
            _ => 0x80060020u
        };
    }

    private static uint BuildRsxTexFilterPayload(byte samplerState, byte descriptorPad0F)
    {
        uint state = ClampSamplerState(samplerState);
        uint filterClass = state & 7u;
        if (descriptorPad0F != 0)
        {
            return filterClass == 1
                ? 0x01053fa0u
                : 0x02063fa0u;
        }

        uint filterBase = filterClass == 1 ? 0x01000000u : 0x02000000u;
        uint filterIndex = filterClass == 1 ? 1u : 2u;
        if ((state & 0x18u) == 0x08u)
            filterIndex += 2u;
        else if ((state & 0x18u) == 0x10u)
            filterIndex += 4u;

        return filterBase | (filterIndex << 16) | 0x3fa0u;
    }

    private static uint BuildRsxTexWrapPayload(byte samplerState, byte descriptorPad1B)
    {
        uint state = ClampSamplerState(samplerState);
        uint border = (descriptorPad1B & 1) == 0 ? 0u : 0x00700000u;
        uint addressU = (state & 0x20u) == 0 ? 0x00000001u : 0x00000003u;
        uint addressV = (state & 0x40u) == 0 ? 0x00000100u : 0x00000300u;
        uint addressW = (state & 0x80u) == 0 ? 0x00010000u : 0x00030000u;
        return 0x40000000u | border | addressW | addressV | addressU;
    }

    private static uint ClampSamplerState(byte samplerState)
    {
        uint state = samplerState;
        uint filterClass = state & 7u;
        return filterClass <= Ps3SamplerClampMax
            ? state
            : (state & 0xfffffff8u) | Ps3SamplerClampMax;
    }

    private static TextureFilter DecodeMipFilter(int mipClass)
    {
        return mipClass switch
        {
            0 => TextureFilter.None,
            1 => TextureFilter.Point,
            2 => TextureFilter.Linear,
            _ => TextureFilter.None
        };
    }
}
