using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Game.ScriptStrings;

namespace IW4.Game.Assets.GameMap;

public sealed class PathNodeConstant
{
    public const int SerializedSize = 0x40;

    public PathNodeType NodeType { get; init; }
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
    /// <summary>Uninterpreted dword at +0x30, preserved by the loader and linker.</summary>
    public uint MinUseDistSqOrErrorBits { get; init; }

    public float MinUseDistSq
    {
        get => BitConverter.Int32BitsToSingle(unchecked((int)MinUseDistSqOrErrorBits));
        init => MinUseDistSqOrErrorBits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
    }

    /// <summary>
    /// Alternative PC-correlated view of +0x30. This view does not determine
    /// whether the node currently holds an error or a distance.
    /// </summary>
    public PathNodeErrorCode Error
    {
        get => (PathNodeErrorCode)unchecked((int)MinUseDistSqOrErrorBits);
        init => MinUseDistSqOrErrorBits = unchecked((uint)value);
    }

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
