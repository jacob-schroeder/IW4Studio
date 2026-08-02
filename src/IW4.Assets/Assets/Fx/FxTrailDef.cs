using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxTrailDef
{
    public const int SerializedSize = 0x24;
    public const int VertexSerializedSize = 0x14;

    public int ScrollTimeMsec { get; init; }
    public int RepeatDist { get; init; }
    public float InvSplitDist { get; init; }
    public float InvSplitArcDist { get; init; }
    public float InvSplitTime { get; init; }
    public int VertCount { get; init; }
    public XPointer<FxTrailVertex[]> VertsPointer { get; init; }
    public IReadOnlyList<FxTrailVertex> Verts { get; init; } = [];
    public int IndCount { get; init; }
    public XPointer<ushort[]> IndsPointer { get; init; }
    public IReadOnlyList<ushort> Inds { get; init; } = [];
}
