using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class XAudioChannelMap
{
    public const int SerializedSize = 0x64;

    public int EntryCount { get; init; }
    public IReadOnlyList<SpeakerLevels> Speakers { get; init; } = [];
}
