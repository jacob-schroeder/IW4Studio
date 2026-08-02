using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class XAudioChannelMap
{
    public const int SerializedSize = 0x64;

    public int EntryCount { get; init; }
    public IReadOnlyList<SpeakerLevels> Speakers { get; init; } = [];
}
