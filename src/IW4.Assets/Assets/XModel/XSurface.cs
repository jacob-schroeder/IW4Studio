using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

public sealed class XSurface
{
    public const int SerializedSize = 0x54;

    public XSurfaceTileMode TileMode { get; init; }
    public byte DeformedRaw { get; init; }
    public bool Deformed => DeformedRaw != 0;
    public XSurfaceStreamFlags StreamFlags { get; init; }
    public byte Pad03 { get; init; }
    public ushort VertCount { get; init; }
    public ushort TriCount { get; init; }
    public XPointer<ushort[]> TriIndicesPointer { get; init; }
    public IReadOnlyList<ushort> TriIndices { get; init; } = [];
    public XSurfaceVertexInfo VertexInfo { get; init; } = new();
    public XPointer<byte[]> Verts0Pointer { get; init; }
    public IReadOnlyList<byte> Verts0 { get; init; } = [];
    public GfxVertexBuffer Vb0 { get; init; } = new();
    public XPointer<byte[]> Verts1Pointer { get; init; }
    public IReadOnlyList<byte> Verts1 { get; init; } = [];
    public GfxVertexBuffer Vb1 { get; init; } = new();
    public int VertListCount { get; init; }
    public XPointer<XRigidVertList[]> VertListPointer { get; init; }
    public IReadOnlyList<XRigidVertList> VertList { get; init; } = [];
    public GfxIndexBuffer IndexBuffer { get; init; } = new();
    public IReadOnlyList<uint> PartBits { get; init; } = [];
}
