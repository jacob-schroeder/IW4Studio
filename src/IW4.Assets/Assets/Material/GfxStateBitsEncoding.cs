namespace IW4.Assets.Assets.Material;

/// <summary>PS3 IW4 blend-factor field values stored in GfxStateBits word 0.</summary>
public enum GfxBlend : uint
{
    Disabled = 0x0,
    Zero = 0x1,
    One = 0x2,
    SourceColor = 0x3,
    InverseSourceColor = 0x4,
    SourceAlpha = 0x5,
    InverseSourceAlpha = 0x6,
    DestinationAlpha = 0x7,
    InverseDestinationAlpha = 0x8,
    DestinationColor = 0x9,
    InverseDestinationColor = 0xa
}

/// <summary>PS3 IW4 blend-operation field values stored in GfxStateBits word 0.</summary>
public enum GfxBlendOperation : uint
{
    Disabled = 0x0,
    Add = 0x1,
    Subtract = 0x2,
    ReverseSubtract = 0x3,
    Minimum = 0x4,
    Maximum = 0x5
}

public enum GfxAlphaTest : uint
{
    GreaterThanZero = 0x1,
    LessThan128 = 0x2,
    GreaterThanOrEqualTo128 = 0x3
}

public enum GfxCullFace : uint
{
    None = 0x1,
    Back = 0x2,
    Front = 0x3
}

public enum GfxDepthTest : uint
{
    Always = 0x0,
    Less = 0x1,
    Equal = 0x2,
    LessThanOrEqual = 0x3
}

public enum GfxPolygonOffset : uint
{
    Disabled = 0x0,
    Offset1 = 0x1,
    Offset2 = 0x2,
    Inherit = 0x3
}

public enum GfxStencilOperation : uint
{
    Keep = 0x0,
    Zero = 0x1,
    Replace = 0x2,
    IncrementSaturate = 0x3,
    DecrementSaturate = 0x4,
    Invert = 0x5,
    IncrementWrap = 0x6,
    DecrementWrap = 0x7
}

public enum GfxStencilFunction : uint
{
    Never = 0x0,
    Less = 0x1,
    Equal = 0x2,
    LessThanOrEqual = 0x3,
    Greater = 0x4,
    NotEqual = 0x5,
    GreaterThanOrEqual = 0x6,
    Always = 0x7
}

[Flags]
public enum GfxStateBits0Flags : uint
{
    None = 0,
    AlphaTestDisabled = 0x00000800,
    ColorWriteRgb = 0x08000000,
    ColorWriteAlpha = 0x10000000,
    GammaWrite = 0x40000000,
    PolygonModeLine = 0x80000000
}

[Flags]
public enum GfxStateBits1Flags : uint
{
    None = 0,
    DepthWrite = 0x00000001,
    DepthTestDisabled = 0x00000002,
    StencilEnabled = 0x00000040,
    StencilBackFaceIndependent = 0x00000080
}

/// <summary>PS3 IW4 shifts and masks for the two encoded GfxStateBits words.</summary>
public static class GfxStateBitsEncoding
{
    public const int SourceBlendRgbShift = 0;
    public const uint SourceBlendRgbMask = 0x0000000f;
    public const int DestinationBlendRgbShift = 4;
    public const uint DestinationBlendRgbMask = 0x000000f0;
    public const int BlendOperationRgbShift = 8;
    public const uint BlendOperationRgbMask = 0x00000700;
    public const int AlphaTestShift = 12;
    public const uint AlphaTestMask = 0x00003000;
    public const int CullFaceShift = 14;
    public const uint CullFaceMask = 0x0000c000;
    public const int SourceBlendAlphaShift = 16;
    public const uint SourceBlendAlphaMask = 0x000f0000;
    public const int DestinationBlendAlphaShift = 20;
    public const uint DestinationBlendAlphaMask = 0x00f00000;
    public const int BlendOperationAlphaShift = 24;
    public const uint BlendOperationAlphaMask = 0x07000000;

    public const int DepthTestShift = 2;
    public const uint DepthTestMask = 0x0000000c;
    public const int PolygonOffsetShift = 4;
    public const uint PolygonOffsetMask = 0x00000030;

    public const int StencilFrontPassShift = 8;
    public const uint StencilFrontPassMask = 0x00000700;
    public const int StencilFrontFailShift = 11;
    public const uint StencilFrontFailMask = 0x00003800;
    public const int StencilFrontDepthFailShift = 14;
    public const uint StencilFrontDepthFailMask = 0x0001c000;
    public const int StencilFrontFunctionShift = 17;
    public const uint StencilFrontFunctionMask = 0x000e0000;
    public const int StencilBackPassShift = 20;
    public const uint StencilBackPassMask = 0x00700000;
    public const int StencilBackFailShift = 23;
    public const uint StencilBackFailMask = 0x03800000;
    public const int StencilBackDepthFailShift = 26;
    public const uint StencilBackDepthFailMask = 0x1c000000;
    public const int StencilBackFunctionShift = 29;
    public const uint StencilBackFunctionMask = 0xe0000000;
}
