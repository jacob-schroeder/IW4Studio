using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Shaders;

public sealed record MapRenderShaderVertexInputBinding(
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
