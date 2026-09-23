using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class SpeakerMapChannel
{
    public const int SerializedSize = 0xC8;

    public IReadOnlyList<XAudioChannelMap> Outputs { get; init; } = [];
}
