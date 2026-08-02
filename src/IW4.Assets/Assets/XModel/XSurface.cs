using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.XModel;

public sealed class XSurface
{
    public const int SerializedSize = 0x54;

    public ushort FlagsOrPad00 { get; init; }
    public byte StreamFlags { get; init; }
    public byte Pad03 { get; init; }
    public ushort VertCount { get; init; }
    public ushort TriCount { get; init; }
    public XPointer<ushort[]> TriIndicesPointer { get; init; }
    public XBlockAddress? TriIndicesRuntimeAddress { get; init; }
    public IReadOnlyList<ushort> TriIndices { get; init; } = [];
    public XSurfaceVertexInfo VertexInfo { get; init; } = new();
    public XPointer<byte[]> Verts0Pointer { get; init; }
    public XBlockAddress? Verts0RuntimeAddress { get; init; }
    public IReadOnlyList<byte> Verts0 { get; init; } = [];
    public GfxVertexBuffer Vb0 { get; init; } = new();
    public XPointer<byte[]> Verts1Pointer { get; init; }
    public XBlockAddress? Verts1RuntimeAddress { get; init; }
    public IReadOnlyList<byte> Verts1 { get; init; } = [];
    public GfxVertexBuffer Vb1 { get; init; } = new();
    public int VertListCount { get; init; }
    public XPointer<XRigidVertList[]> VertListPointer { get; init; }
    public IReadOnlyList<XRigidVertList> VertList { get; init; } = [];
    public GfxIndexBuffer IndexBuffer { get; init; } = new();
    public IReadOnlyList<uint> PartBits { get; init; } = [];
}
