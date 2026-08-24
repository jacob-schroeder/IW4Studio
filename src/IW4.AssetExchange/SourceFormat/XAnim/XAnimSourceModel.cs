namespace IW4.AssetExchange.SourceFormat.XAnim;

internal enum XAnimSourceQuatType
{
    None,
    Simple,
    Normal
}

internal enum XAnimSourceTransType
{
    None,
    Constant,
    Small,
    Large
}

internal readonly record struct XAnimSourceQuat2(short Value0, short Value1);

internal readonly record struct XAnimSourceQuat(
    short Value0,
    short Value1,
    short Value2,
    short Value3);

internal readonly record struct XAnimSourceVec3(float X, float Y, float Z);

internal readonly record struct XAnimSourceSmallTrans(byte X, byte Y, byte Z);

internal readonly record struct XAnimSourceLargeTrans(short X, short Y, short Z);

internal sealed class XAnimSourceQuatTrack
{
    public XAnimSourceQuatType Type { get; init; }
    public bool IsConstant { get; init; }
    public IReadOnlyList<ushort> Indices { get; init; } = [];
    public IReadOnlyList<XAnimSourceQuat2> SimpleFrames { get; init; } = [];
    public IReadOnlyList<XAnimSourceQuat> NormalFrames { get; init; } = [];
}

internal sealed class XAnimSourceTransTrack
{
    public XAnimSourceTransType Type { get; init; }
    public IReadOnlyList<ushort> Indices { get; init; } = [];
    public XAnimSourceVec3 Mins { get; init; }
    public XAnimSourceVec3 Size { get; init; }
    public XAnimSourceVec3 Constant { get; init; }
    public IReadOnlyList<XAnimSourceSmallTrans> SmallFrames { get; init; } = [];
    public IReadOnlyList<XAnimSourceLargeTrans> LargeFrames { get; init; } = [];
}

internal sealed class XAnimSourceBoneTrack
{
    public required string Name { get; init; }
    public required XAnimSourceQuatTrack Quat { get; set; }
    public required XAnimSourceTransTrack Trans { get; set; }
}

internal sealed class XAnimSourceDeltaQuatTrack
{
    public bool Is3D { get; init; }
    public IReadOnlyList<ushort> Indices { get; init; } = [];
    public IReadOnlyList<XAnimSourceQuat2> Frames2D { get; init; } = [];
    public IReadOnlyList<XAnimSourceQuat> Frames3D { get; init; } = [];
}

internal sealed class XAnimSourceDeltaTrack
{
    public XAnimSourceDeltaQuatTrack? Quat { get; init; }
    public XAnimSourceTransTrack? Trans { get; init; }
}

internal readonly record struct XAnimSourceNotify(string Name, float Time);

internal sealed class XAnimSourceParts
{
    public ushort NumFrames { get; init; }
    public bool Looped { get; init; }
    public float Framerate { get; init; }
    public byte AssetType { get; init; }
    public required IReadOnlyList<XAnimSourceBoneTrack> Bones { get; init; }
    public required IReadOnlyList<XAnimSourceNotify> Notifies { get; init; }
    public XAnimSourceDeltaTrack? Delta { get; init; }
}
