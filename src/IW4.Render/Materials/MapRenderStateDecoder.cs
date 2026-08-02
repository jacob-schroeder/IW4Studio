using IW4.Assets.Assets.Material;

namespace IW4.Render.Materials;

public static class MapRenderStateDecoder
{
    public static bool TryDecode(
        MaterialAsset material,
        int techniqueSlot,
        IMapRenderStateLoadBitsResolver loadBitsResolver,
        out MapRenderState state)
    {
        return TryDecode(material, techniqueSlot, 0, loadBitsResolver, out state);
    }

    public static bool TryDecode(
        MaterialAsset material,
        int techniqueSlot,
        int passIndex,
        IMapRenderStateLoadBitsResolver loadBitsResolver,
        out MapRenderState state)
    {
        state = MapRenderState.Default;
        if ((uint)techniqueSlot >= (uint)material.StateBitsEntries.Count ||
            passIndex < 0)
            return false;

        return TryDecodeStateBitsIndex(
            material,
            material.StateBitsEntries[techniqueSlot].StateBitsIndex + passIndex,
            loadBitsResolver,
            out state);
    }

    public static bool TryDecodeStateBitsIndex(
        MaterialAsset material,
        int stateIndex,
        IMapRenderStateLoadBitsResolver loadBitsResolver,
        out MapRenderState state)
    {
        state = MapRenderState.Default;
        if ((uint)stateIndex >= (uint)material.StateBits.Count)
            return false;

        GfxStateBits source = material.StateBits[stateIndex];
        IReadOnlyList<uint> loadBits = loadBitsResolver.ResolveStateLoadBits(source);
        if (loadBits.Count < 2)
            return false;

        state = Decode(loadBits[0], loadBits[1], source.Tail);
        return true;
    }

    internal static MapRenderState Decode(uint w0, uint w1, uint tail)
    {
        // Bit 30 of loadBits[0] supplies the exact 0/1 payload for RSX method
        // 0x1FEC (NV4097_SET_SHADER_PACKER sRGB output).
        bool shaderPackerSrgbEnabled = (w0 & 0x40000000u) != 0;

        uint colorGate = Rlwinm(w0, 0, 4, 4);
        uint colorBase = (Srawi31(colorGate - 1) & 0xfffefeffu) + 0x10000u + 0x101u;
        uint colorMask = colorBase | Rlwinm((uint)Rldicl(w0, 0x24, 0x3f), 0x18, 0, 7);

        bool alphaTestEnabled = Rldicl(w0 ^ 0x800u, 0x35, 0x3f) != 0;
        uint alphaFunc = 0x0207;
        byte alphaRef = 0;
        if (Rlwinm(w0, 0, 0x14, 0x14) == 0)
        {
            uint alphaBits = Rlwinm(w0, 0, 0x12, 0x13);
            if (alphaBits == 0x1000)
                alphaFunc = 0x0204;
            else if (alphaBits == 0x2000)
            {
                alphaFunc = 0x0201;
                alphaRef = 0x80;
            }
            else
            {
                alphaFunc = 0x0206;
                // The GEQUAL branch changes only the function and retains the
                // alpha reference 0x80.
                alphaRef = 0x80;
            }
        }

        uint cullBits = Rlwinm(w0, 0, 0x10, 0x11);
        bool cullEnabled = cullBits != 0x4000;
        uint cullFace = 0x0404;
        if (cullEnabled)
        {
            uint a = cullBits ^ 0xc000u;
            cullFace = 0x405u - Rlwinm(0u - a, 1, 31, 31);
        }

        uint polygonMode = Rlwinm(~w0, 1, 31, 31) + 0x1b01u;

        bool blendEnabled = Rlwinm(w0, 0, 21, 23) != 0;
        uint blendEquationRgb = 0x8006;
        uint blendEquationAlpha = 0x8006;
        uint blendSourceRgb = 1;
        uint blendSourceAlpha = 1;
        uint blendDestinationRgb = 0;
        uint blendDestinationAlpha = 0;
        if (blendEnabled)
        {
            SplitPacked(Lookup(Rlwinm(w0, 0x1a, 0x1b, 0x1d)), Lookup(Rlwinm(w0, 0x0a, 0x1b, 0x1d)), out blendEquationRgb, out blendEquationAlpha);
            // Form the 0x0314 source payload from bits 0..3 (RGB) and 16..19
            // (alpha), then the adjacent destination payload from bits 4..7
            // (RGB) and 20..23 (alpha).
            SplitPacked(
                Lookup(0x18 + Rlwinm(w0, 0x02, 0x1a, 0x1d)),
                Lookup(0x18 + Rlwinm(w0, 0x12, 0x1a, 0x1d)),
                out blendSourceRgb,
                out blendSourceAlpha);
            SplitPacked(
                Lookup(0x18 + Rlwinm(w0, 0x1e, 0x1a, 0x1d)),
                Lookup(0x18 + Rlwinm(w0, 0x0e, 0x1a, 0x1d)),
                out blendDestinationRgb,
                out blendDestinationAlpha);
        }

        bool depthDisabled = Rlwinm(w1, 0, 30, 30) != 0;
        bool depthWriteEnabled = !depthDisabled && Rlwinm(w1, 0, 31, 31) != 0;
        uint depthFunc = depthDisabled ? 0x0207u : Lookup(0xc4 + Rlwinm(w1, 0, 28, 29));

        MapRenderStencilState stencil = DecodeStencil(w1);

        bool polygonOffsetEnabled = false;
        float polygonOffsetFactor = 0f;
        float polygonOffsetUnits = 0f;
        uint polygonOffsetBits = Rlwinm(w1, 0, 26, 27);
        if (polygonOffsetBits != 0x30)
        {
            ulong index = Rldicl(polygonOffsetBits, 0x3c, 0x3e);
            polygonOffsetFactor = -(float)index;
            polygonOffsetUnits = (float)index * -50f;
            polygonOffsetEnabled = polygonOffsetFactor != 0f || polygonOffsetUnits != 0f;
        }

        return new MapRenderState(
            HasState: true,
            LoadBits0: w0,
            LoadBits1: w1,
            Tail: tail,
            ShaderPackerSrgbEnabled: shaderPackerSrgbEnabled,
            ColorMask: colorMask,
            AlphaTestEnabled: alphaTestEnabled,
            AlphaFunc: alphaFunc,
            AlphaRef: alphaRef,
            CullEnabled: cullEnabled,
            CullFace: cullFace,
            PolygonMode: polygonMode,
            BlendEnabled: blendEnabled,
            BlendEquationRgb: blendEquationRgb,
            BlendEquationAlpha: blendEquationAlpha,
            BlendSourceRgb: blendSourceRgb,
            BlendSourceAlpha: blendSourceAlpha,
            BlendDestinationRgb: blendDestinationRgb,
            BlendDestinationAlpha: blendDestinationAlpha,
            DepthTestEnabled: !depthDisabled,
            DepthWriteEnabled: depthWriteEnabled,
            DepthFunc: depthFunc,
            Stencil: stencil,
            PolygonOffsetEnabled: polygonOffsetEnabled,
            PolygonOffsetFactor: polygonOffsetFactor,
            PolygonOffsetUnits: polygonOffsetUnits);
    }

    private static MapRenderStencilState DecodeStencil(uint stateBits1)
    {
        // Bit 0x40 controls NV30_3D_STENCIL_ENABLE. Bit 0x80 chooses
        // independently encoded back-face fields; otherwise the front 12-bit
        // field is mirrored into the back-face field before extraction.
        bool enabled = (stateBits1 & 0x40u) != 0;
        if (!enabled)
            return MapRenderStencilState.Disabled;

        bool backFaceStateIsIndependent = (stateBits1 & 0x80u) != 0;
        uint normalized = backFaceStateIsIndependent
            ? stateBits1
            : Rlwinm(stateBits1, 0, 0x0c, 0x1f) |
              Rlwinm(stateBits1, 0x0c, 0x00, 0x0b);

        var front = new MapRenderStencilFaceState(
            Function: Lookup(0x64 + Rlwinm(normalized, 0x11, 0x1b, 0x1d)),
            Reference: 0,
            CompareMask: 0xff,
            FailOperation: Lookup(0x44 + Rlwinm(normalized, 0x17, 0x1b, 0x1d)),
            DepthFailOperation: Lookup(0x44 + Rlwinm(normalized, 0x14, 0x1b, 0x1d)),
            PassOperation: Lookup(0x44 + Rlwinm(normalized, 0x1a, 0x1b, 0x1d)));
        var back = new MapRenderStencilFaceState(
            Function: Lookup(0x64 + Rlwinm(normalized, 0x05, 0x1b, 0x1d)),
            Reference: 0,
            CompareMask: 0xff,
            FailOperation: Lookup(0x44 + Rlwinm(normalized, 0x0b, 0x1b, 0x1d)),
            DepthFailOperation: Lookup(0x44 + Rlwinm(normalized, 0x08, 0x1b, 0x1d)),
            PassOperation: Lookup(0x44 + Rlwinm(normalized, 0x0e, 0x1b, 0x1d)));

        return new MapRenderStencilState(
            enabled,
            backFaceStateIsIndependent,
            front,
            back);
    }

    private static void SplitPacked(uint low, uint high, out uint rgb, out uint alpha)
    {
        rgb = low & 0xffffu;
        alpha = high & 0xffffu;
    }

    private static uint Lookup(uint byteOffset)
    {
        return byteOffset switch
        {
            0x00 => 0,
            0x04 => 0x8006,
            0x08 => 0x800a,
            0x0c => 0x800b,
            0x10 => 0x8007,
            0x14 => 0x8008,
            0x18 => 0,
            0x1c => 0,
            0x20 => 1,
            0x24 => 0x0300,
            0x28 => 0x0301,
            0x2c => 0x0302,
            0x30 => 0x0303,
            0x34 => 0x0304,
            0x38 => 0x0305,
            0x3c => 0x0306,
            0x40 => 0x0307,
            0x44 => 0x1e00,
            0x48 => 0,
            0x4c => 0x1e01,
            0x50 => 0x1e02,
            0x54 => 0x1e03,
            0x58 => 0x150a,
            0x5c => 0x8507,
            0x60 => 0x8508,
            0x64 => 0x0200,
            0x68 => 0x0201,
            0x6c => 0x0202,
            0x70 => 0x0203,
            0x74 => 0x0204,
            0x78 => 0x0205,
            0x7c => 0x0206,
            0x80 => 0x0207,
            0xc4 => 0x0207,
            0xc8 => 0x0201,
            0xcc => 0x0202,
            0xd0 => 0x0203,
            _ => 0
        };
    }

    private static uint Rlwinm(uint value, int shift, int maskBegin, int maskEnd)
    {
        return RotateLeft(value, shift) & Mask32(maskBegin, maskEnd);
    }

    private static ulong Rldicl(uint value, int shift, int maskBegin)
    {
        ulong rotated = RotateLeft64(value, shift);
        return rotated & ((1UL << (64 - maskBegin)) - 1UL);
    }

    private static uint Srawi31(uint value)
    {
        return (value & 0x80000000u) == 0 ? 0 : 0xffffffffu;
    }

    private static uint RotateLeft(uint value, int shift)
    {
        shift &= 31;
        return shift == 0 ? value : (value << shift) | (value >> (32 - shift));
    }

    private static ulong RotateLeft64(ulong value, int shift)
    {
        shift &= 63;
        return shift == 0 ? value : (value << shift) | (value >> (64 - shift));
    }

    private static uint Mask32(int maskBegin, int maskEnd)
    {
        uint mask = 0;
        for (int bit = 0; bit < 32; bit++)
        {
            bool included = maskBegin <= maskEnd
                ? bit >= maskBegin && bit <= maskEnd
                : bit >= maskBegin || bit <= maskEnd;
            if (included)
                mask |= 1u << (31 - bit);
        }

        return mask;
    }
}
