using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class SpeakerMap
{
    public const int SerializedSize = 0x198;

    public int Offset { get; init; }
    public byte IsDefault { get; init; }
    public byte[] Padding { get; init; } = [];
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<SpeakerMapChannel> Channels { get; init; } = [];
}
