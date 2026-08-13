
namespace IW4.Render.Shaders;

public sealed record ShaderVertexInputBinding(
    int RouteIndex,
    byte Source,
    byte Destination,
    byte StreamIndex,
    int Stride,
    int Offset,
    byte ComponentCount,
    byte RsxType,
    string RsxTypeName)
{
    public bool IsDisabledDefaultAttribute =>
        StreamIndex == 2 && Stride == 0 && Offset == 0 && ComponentCount == 0 && RsxType == 0;
}
