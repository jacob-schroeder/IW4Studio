using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.MapEnts;

public sealed class Stage
{
    public const int SerializedSize = 0x14;

    public XPointer<string> StageNamePointer { get; init; }
    public string? StageName { get; init; }
    public Vec3 Origin { get; init; }
    public ushort TriggerIndex { get; init; }
    public byte SunPrimaryLightIndex { get; init; }
    public byte Pad13 { get; init; }
}
