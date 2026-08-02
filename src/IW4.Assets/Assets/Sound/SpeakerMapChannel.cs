using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class SpeakerMapChannel
{
    public const int SerializedSize = 0xC8;

    public IReadOnlyList<XAudioChannelMap> Outputs { get; init; } = [];
}
