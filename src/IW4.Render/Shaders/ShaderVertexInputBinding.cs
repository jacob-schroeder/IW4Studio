
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

public sealed record ShaderVertexInputBinding(
    int RouteIndex,
    MaterialStreamSource Source,
    MaterialStreamDestination Destination,
    byte StreamIndex,
    int Stride,
    int Offset,
    byte ComponentCount,
    RsxVertexElementType RsxType)
{
    public string RsxTypeName => RsxType switch
    {
        RsxVertexElementType.Disabled => "DISABLED",
        RsxVertexElementType.Signed16Normalized => "V16_SNORM",
        RsxVertexElementType.Float32 => "V32_FLOAT",
        RsxVertexElementType.Float16 => "V16_FLOAT",
        RsxVertexElementType.Unsigned8Normalized => "U8_UNORM",
        RsxVertexElementType.Signed16Unnormalized => "V16_SSCALED",
        RsxVertexElementType.Signed11_11_10Normalized => "S11_11_10_NR",
        RsxVertexElementType.Unsigned8Unnormalized => "U8_USCALED",
        _ => $"RSX_TYPE_0x{(byte)RsxType:X2}"
    };

    public bool IsDisabledDefaultAttribute =>
        StreamIndex == 2 && Stride == 0 && Offset == 0 &&
        ComponentCount == 0 && RsxType == RsxVertexElementType.Disabled;
}
