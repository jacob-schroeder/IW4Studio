using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class SpeakerLevels
{
    public const int SerializedSize = 0x10;

    public int Speaker { get; init; }
    public int NumLevels { get; init; }
    public float Level0 { get; init; }
    public float Level1 { get; init; }
}
