using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Game.ScriptStrings;

namespace IW4.Game.Assets.GameMap;

public sealed class PathNodeConstant
{
    public const int SerializedSize = 0x40;

    public PathNodeType NodeType { get; init; }
    public PathNodeSpawnFlags SpawnFlags { get; init; }
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
    // +0x30: keep the exact word, including unknown error codes and floating-point payloads.
    public uint MinUseDistSqOrErrorBits { get; init; }

    /// <summary>Minimum use distance in squared world units when the diagnostic flag is clear.</summary>
    public float? MinUseDistSq
    {
        get => (SpawnFlags & PathNodeSpawnFlags.HasError) == 0
            ? BitConverter.UInt32BitsToSingle(MinUseDistSqOrErrorBits)
            : null;
        init
        {
            if (value is float distanceSquared)
            {
                MinUseDistSqOrErrorBits = BitConverter.SingleToUInt32Bits(distanceSquared);
                SpawnFlags &= ~PathNodeSpawnFlags.HasError;
            }
        }
    }

    public PathNodeErrorCode? Error
    {
        get => (SpawnFlags & PathNodeSpawnFlags.HasError) != 0
            ? (PathNodeErrorCode)unchecked((int)MinUseDistSqOrErrorBits)
            : null;
        init
        {
            if (value is PathNodeErrorCode code)
            {
                MinUseDistSqOrErrorBits = unchecked((uint)code);
                SpawnFlags |= PathNodeSpawnFlags.HasError;
            }
        }
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
