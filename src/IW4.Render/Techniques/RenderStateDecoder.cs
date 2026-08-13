using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Techniques;

public static class RenderStateDecoder
{
    public static bool TryDecode(
        MaterialAsset material,
        int techniqueSlot,
        IStateLoadBitsResolver loadBitsResolver,
        out RenderState state)
    {
        return TryDecode(material, techniqueSlot, 0, loadBitsResolver, out state);
    }

    public static bool TryDecode(
        MaterialAsset material,
        int techniqueSlot,
        int passIndex,
        IStateLoadBitsResolver loadBitsResolver,
        out RenderState state)
    {
        state = RenderState.Default;
        if ((uint)techniqueSlot >= MaterialAsset.TechniqueSlotCount ||
            material.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount ||
            passIndex < 0)
            return false;

        IReadOnlyList<MaterialTechniqueSlot>? techniqueSlots =
            material.TechniqueSet?.TechniqueSlots;
        if (techniqueSlots is { Count: > 0 })
        {
            if (techniqueSlots.Count != MaterialAsset.TechniqueSlotCount)
                return false;
            MaterialTechniqueAsset? technique =
                techniqueSlots[techniqueSlot].Technique;
            if (technique is not null &&
                ((uint)passIndex >= (uint)technique.Passes.Count ||
                 technique.PassCount != technique.Passes.Count))
            {
                return false;
            }
        }

        return TryDecodeStateBitsIndex(
            material,
            material.StateBitsEntries[techniqueSlot].StateBitsIndex + passIndex,
            loadBitsResolver,
            out state);
    }

    public static bool TryDecodeStateBitsIndex(
        MaterialAsset material,
        int stateIndex,
        IStateLoadBitsResolver loadBitsResolver,
        out RenderState state)
    {
        state = RenderState.Default;
        if ((uint)stateIndex >= (uint)material.StateBits.Count)
            return false;

        GfxStateBits source = material.StateBits[stateIndex];
        IReadOnlyList<uint> loadBits = loadBitsResolver.ResolveStateLoadBits(source);
        if (loadBits.Count < 2)
            return false;

        state = Decode(
            loadBits[0],
            loadBits[1],
            source.CommandWordCount);
        return true;
    }

    internal static RenderState Decode(
        uint w0,
        uint w1,
        uint commandWordCount)
    {
        // Bit 30 of loadBits[0] supplies the exact 0/1 payload for RSX method
        // 0x1FEC (NV4097_SET_SHADER_PACKER sRGB output).
        var flags0 = (GfxStateBits0Flags)w0;
        bool shaderPackerSrgbEnabled =
            (flags0 & GfxStateBits0Flags.GammaWrite) != 0;

        uint colorGate =
            w0 & (uint)GfxStateBits0Flags.ColorWriteRgb;
        uint colorBase = (Srawi31(colorGate - 1) & 0xfffefeffu) + 0x10000u + 0x101u;
        var colorMask = (RsxColorMask)(colorBase | Rlwinm(
            (uint)Rldicl(
                w0 & (uint)GfxStateBits0Flags.ColorWriteAlpha,
                0x24,
                0x3f),
            0x18,
            0,
            7));

        bool alphaTestEnabled =
            (flags0 & GfxStateBits0Flags.AlphaTestDisabled) == 0;
        RsxCompareFunction alphaFunc = RsxCompareFunction.Always;
        byte alphaRef = 0;
        if (alphaTestEnabled)
        {
            GfxAlphaTest alphaTest = ReadField<GfxAlphaTest>(
                w0,
                GfxStateBitsEncoding.AlphaTestMask,
                GfxStateBitsEncoding.AlphaTestShift);
            if (alphaTest == GfxAlphaTest.GreaterThanZero)
                alphaFunc = RsxCompareFunction.Greater;
            else if (alphaTest == GfxAlphaTest.LessThan128)
            {
                alphaFunc = RsxCompareFunction.Less;
                alphaRef = 0x80;
            }
            else
            {
                alphaFunc = RsxCompareFunction.GreaterThanOrEqual;
                // The GEQUAL branch changes only the function and retains the
                // alpha reference 0x80.
                alphaRef = 0x80;
            }
        }

        GfxCullFace cullFaceBits = ReadField<GfxCullFace>(
            w0,
            GfxStateBitsEncoding.CullFaceMask,
            GfxStateBitsEncoding.CullFaceShift);
        uint cullBits =
            ((uint)cullFaceBits << GfxStateBitsEncoding.CullFaceShift) &
            GfxStateBitsEncoding.CullFaceMask;
        bool cullEnabled = cullFaceBits != GfxCullFace.None;
        RsxCullFace cullFace = RsxCullFace.Front;
        if (cullEnabled)
        {
            uint a = cullBits ^ 0xc000u;
            cullFace = (RsxCullFace)(0x405u -
                Rlwinm(0u - a, 1, 31, 31));
        }

        RsxPolygonMode polygonMode =
            (flags0 & GfxStateBits0Flags.PolygonModeLine) != 0
                ? RsxPolygonMode.Line
                : RsxPolygonMode.Fill;

        GfxBlendOperation blendOperationRgb =
            ReadField<GfxBlendOperation>(
                w0,
                GfxStateBitsEncoding.BlendOperationRgbMask,
                GfxStateBitsEncoding.BlendOperationRgbShift);
        bool blendEnabled =
            blendOperationRgb != GfxBlendOperation.Disabled;
        RsxBlendEquation blendEquationRgb = RsxBlendEquation.Add;
        RsxBlendEquation blendEquationAlpha = RsxBlendEquation.Add;
        RsxBlendFactor blendSourceRgb = RsxBlendFactor.One;
        RsxBlendFactor blendSourceAlpha = RsxBlendFactor.One;
        RsxBlendFactor blendDestinationRgb = RsxBlendFactor.Zero;
        RsxBlendFactor blendDestinationAlpha = RsxBlendFactor.Zero;
        if (blendEnabled)
        {
            blendEquationRgb = DecodeBlendOperation(blendOperationRgb);
            blendEquationAlpha = DecodeBlendOperation(
                ReadField<GfxBlendOperation>(
                    w0,
                    GfxStateBitsEncoding.BlendOperationAlphaMask,
                    GfxStateBitsEncoding.BlendOperationAlphaShift));
            // Form the 0x0314 source payload from bits 0..3 (RGB) and 16..19
            // (alpha), then the adjacent destination payload from bits 4..7
            // (RGB) and 20..23 (alpha).
            blendSourceRgb = DecodeBlend(ReadField<GfxBlend>(
                    w0,
                    GfxStateBitsEncoding.SourceBlendRgbMask,
                    GfxStateBitsEncoding.SourceBlendRgbShift));
            blendSourceAlpha = DecodeBlend(ReadField<GfxBlend>(
                    w0,
                    GfxStateBitsEncoding.SourceBlendAlphaMask,
                    GfxStateBitsEncoding.SourceBlendAlphaShift));
            blendDestinationRgb = DecodeBlend(ReadField<GfxBlend>(
                    w0,
                    GfxStateBitsEncoding.DestinationBlendRgbMask,
                    GfxStateBitsEncoding.DestinationBlendRgbShift));
            blendDestinationAlpha = DecodeBlend(ReadField<GfxBlend>(
                    w0,
                    GfxStateBitsEncoding.DestinationBlendAlphaMask,
                    GfxStateBitsEncoding.DestinationBlendAlphaShift));
        }

        var flags1 = (GfxStateBits1Flags)w1;
        bool depthDisabled =
            (flags1 & GfxStateBits1Flags.DepthTestDisabled) != 0;
        bool depthWriteEnabled = !depthDisabled &&
            (flags1 & GfxStateBits1Flags.DepthWrite) != 0;
        RsxCompareFunction depthFunc = depthDisabled
            ? RsxCompareFunction.Always
            : DecodeDepthTest(ReadField<GfxDepthTest>(
                w1,
                GfxStateBitsEncoding.DepthTestMask,
                GfxStateBitsEncoding.DepthTestShift));

        StencilState stencil = DecodeStencil(w1);

        RenderPolygonOffsetMode polygonOffsetMode;
        float polygonOffsetFactor = 0f;
        float polygonOffsetUnits = 0f;
        GfxPolygonOffset polygonOffset = ReadField<GfxPolygonOffset>(
            w1,
            GfxStateBitsEncoding.PolygonOffsetMask,
            GfxStateBitsEncoding.PolygonOffsetShift);
        if (polygonOffset == GfxPolygonOffset.Inherit)
        {
            polygonOffsetMode = RenderPolygonOffsetMode.Inherit;
        }
        else
        {
            ulong index = (uint)polygonOffset;
            polygonOffsetFactor = -(float)index;
            polygonOffsetUnits = (float)index * -50f;
            polygonOffsetMode = polygonOffset == GfxPolygonOffset.Disabled
                ? RenderPolygonOffsetMode.Disabled
                : RenderPolygonOffsetMode.Explicit;
        }

        return new RenderState(
            HasState: true,
            LoadBits0: w0,
            LoadBits1: w1,
            CommandWordCount: commandWordCount,
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
            PolygonOffsetMode: polygonOffsetMode,
            PolygonOffsetFactor: polygonOffsetFactor,
            PolygonOffsetUnits: polygonOffsetUnits);
    }

    private static StencilState DecodeStencil(uint stateBits1)
    {
        // Bit 0x40 controls NV30_3D_STENCIL_ENABLE. Bit 0x80 chooses
        // independently encoded back-face fields; otherwise the front 12-bit
        // field is mirrored into the back-face field before extraction.
        var flags = (GfxStateBits1Flags)stateBits1;
        bool enabled =
            (flags & GfxStateBits1Flags.StencilEnabled) != 0;
        if (!enabled)
            return StencilState.Disabled;

        bool backFaceStateIsIndependent =
            (flags & GfxStateBits1Flags.StencilBackFaceIndependent) != 0;
        uint normalized = backFaceStateIsIndependent
            ? stateBits1
            : Rlwinm(stateBits1, 0, 0x0c, 0x1f) |
              Rlwinm(stateBits1, 0x0c, 0x00, 0x0b);

        var front = new StencilFaceState(
            Function: DecodeStencilFunction(ReadField<GfxStencilFunction>(
                normalized,
                GfxStateBitsEncoding.StencilFrontFunctionMask,
                GfxStateBitsEncoding.StencilFrontFunctionShift)),
            Reference: 0,
            CompareMask: 0xff,
            FailOperation: DecodeStencilOperation(ReadField<GfxStencilOperation>(
                normalized,
                GfxStateBitsEncoding.StencilFrontFailMask,
                GfxStateBitsEncoding.StencilFrontFailShift)),
            DepthFailOperation: DecodeStencilOperation(ReadField<GfxStencilOperation>(
                normalized,
                GfxStateBitsEncoding.StencilFrontDepthFailMask,
                GfxStateBitsEncoding.StencilFrontDepthFailShift)),
            PassOperation: DecodeStencilOperation(ReadField<GfxStencilOperation>(
                normalized,
                GfxStateBitsEncoding.StencilFrontPassMask,
                GfxStateBitsEncoding.StencilFrontPassShift)));
        var back = new StencilFaceState(
            Function: DecodeStencilFunction(ReadField<GfxStencilFunction>(
                normalized,
                GfxStateBitsEncoding.StencilBackFunctionMask,
                GfxStateBitsEncoding.StencilBackFunctionShift)),
            Reference: 0,
            CompareMask: 0xff,
            FailOperation: DecodeStencilOperation(ReadField<GfxStencilOperation>(
                normalized,
                GfxStateBitsEncoding.StencilBackFailMask,
                GfxStateBitsEncoding.StencilBackFailShift)),
            DepthFailOperation: DecodeStencilOperation(ReadField<GfxStencilOperation>(
                normalized,
                GfxStateBitsEncoding.StencilBackDepthFailMask,
                GfxStateBitsEncoding.StencilBackDepthFailShift)),
            PassOperation: DecodeStencilOperation(ReadField<GfxStencilOperation>(
                normalized,
                GfxStateBitsEncoding.StencilBackPassMask,
                GfxStateBitsEncoding.StencilBackPassShift)));

        return new StencilState(
            enabled,
            backFaceStateIsIndependent,
            front,
            back);
    }

    private static T ReadField<T>(uint word, uint mask, int shift)
        where T : struct, Enum =>
        (T)Enum.ToObject(typeof(T), (word & mask) >> shift);

    private static RsxBlendEquation DecodeBlendOperation(
        GfxBlendOperation operation) =>
        operation switch
        {
            GfxBlendOperation.Add => RsxBlendEquation.Add,
            GfxBlendOperation.Subtract => RsxBlendEquation.Subtract,
            GfxBlendOperation.ReverseSubtract =>
                RsxBlendEquation.ReverseSubtract,
            GfxBlendOperation.Minimum => RsxBlendEquation.Minimum,
            GfxBlendOperation.Maximum => RsxBlendEquation.Maximum,
            _ => default
        };

    private static RsxBlendFactor DecodeBlend(GfxBlend blend) => blend switch
    {
        GfxBlend.Disabled or GfxBlend.Zero => RsxBlendFactor.Zero,
        GfxBlend.One => RsxBlendFactor.One,
        GfxBlend.SourceColor => RsxBlendFactor.SourceColor,
        GfxBlend.InverseSourceColor => RsxBlendFactor.OneMinusSourceColor,
        GfxBlend.SourceAlpha => RsxBlendFactor.SourceAlpha,
        GfxBlend.InverseSourceAlpha => RsxBlendFactor.OneMinusSourceAlpha,
        GfxBlend.DestinationAlpha => RsxBlendFactor.DestinationAlpha,
        GfxBlend.InverseDestinationAlpha =>
            RsxBlendFactor.OneMinusDestinationAlpha,
        GfxBlend.DestinationColor => RsxBlendFactor.DestinationColor,
        GfxBlend.InverseDestinationColor =>
            RsxBlendFactor.OneMinusDestinationColor,
        _ => default
    };

    private static RsxCompareFunction DecodeDepthTest(GfxDepthTest depthTest) =>
        depthTest switch
        {
            GfxDepthTest.Always => RsxCompareFunction.Always,
            GfxDepthTest.Less => RsxCompareFunction.Less,
            GfxDepthTest.Equal => RsxCompareFunction.Equal,
            GfxDepthTest.LessThanOrEqual =>
                RsxCompareFunction.LessThanOrEqual,
            _ => default
        };

    private static RsxStencilOperation DecodeStencilOperation(
        GfxStencilOperation operation) =>
        operation switch
        {
            GfxStencilOperation.Keep => RsxStencilOperation.Keep,
            GfxStencilOperation.Zero => RsxStencilOperation.Zero,
            GfxStencilOperation.Replace => RsxStencilOperation.Replace,
            GfxStencilOperation.IncrementSaturate =>
                RsxStencilOperation.IncrementSaturate,
            GfxStencilOperation.DecrementSaturate =>
                RsxStencilOperation.DecrementSaturate,
            GfxStencilOperation.Invert => RsxStencilOperation.Invert,
            GfxStencilOperation.IncrementWrap =>
                RsxStencilOperation.IncrementWrap,
            GfxStencilOperation.DecrementWrap =>
                RsxStencilOperation.DecrementWrap,
            _ => default
        };

    private static RsxCompareFunction DecodeStencilFunction(
        GfxStencilFunction function) =>
        function switch
        {
            GfxStencilFunction.Never => RsxCompareFunction.Never,
            GfxStencilFunction.Less => RsxCompareFunction.Less,
            GfxStencilFunction.Equal => RsxCompareFunction.Equal,
            GfxStencilFunction.LessThanOrEqual =>
                RsxCompareFunction.LessThanOrEqual,
            GfxStencilFunction.Greater => RsxCompareFunction.Greater,
            GfxStencilFunction.NotEqual => RsxCompareFunction.NotEqual,
            GfxStencilFunction.GreaterThanOrEqual =>
                RsxCompareFunction.GreaterThanOrEqual,
            GfxStencilFunction.Always => RsxCompareFunction.Always,
            _ => default
        };

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
