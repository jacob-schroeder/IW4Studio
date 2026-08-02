using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;

namespace IW4.Assets.Assets.GameMap;

public sealed class PathNodeConstant
{
    public const int SerializedSize = 0x40;

    public int NodeType { get; init; }
    public ushort SpawnFlags { get; init; }
    public ScriptStringReference TargetName { get; init; } = NullScriptString();
    public ScriptStringReference ScriptLinkName { get; init; } = NullScriptString();
    public ScriptStringReference ScriptNoteworthy { get; init; } = NullScriptString();
    public ScriptStringReference Target { get; init; } = NullScriptString();
    public ScriptStringReference AnimScript { get; init; } = NullScriptString();
    public int AnimScriptFunc { get; init; }
    public Vec3 Origin { get; init; }
    public float Angle { get; init; }
    public float ForwardX { get; init; }
    public float ForwardY { get; init; }
    public float Radius { get; init; }
    public float MinUseDistSq { get; init; }
    public short OverlapNode0 { get; init; }
    public short OverlapNode1 { get; init; }
    public ushort TotalLinkCount { get; init; }
    public ushort Pad3A { get; init; }
    public XPointer<PathLink[]> LinksPointer { get; init; }
    public IReadOnlyList<PathLink> Links { get; init; } = [];

    private static ScriptStringReference NullScriptString() => new(
        RawLocalIndex: 0,
        Text: null,
        RuntimeHandle: ScriptStringHandle.Null,
        DestinationCellAddress: default);
}
