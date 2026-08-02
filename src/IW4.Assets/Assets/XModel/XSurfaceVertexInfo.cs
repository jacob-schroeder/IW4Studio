using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.XModel;

public sealed class XSurfaceVertexInfo
{
    public ushort Blend0 { get; init; }
    public ushort Blend1 { get; init; }
    public ushort Blend2 { get; init; }
    public ushort Blend3 { get; init; }
    public XPointer<ushort[]> VertsBlendPointer { get; init; }
    public XBlockAddress? VertsBlendRuntimeAddress { get; init; }
    public IReadOnlyList<ushort> VertsBlend { get; init; } = [];
}
