using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class SoundFile
{
    public const int SerializedSize = 0x10;

    public int Offset { get; init; }
    public SndAliasType Type { get; init; }
    public byte Exists { get; init; }
    public ushort Padding { get; init; }
    public SoundFilePayload? Payload { get; init; }
    public LoadedSoundFile? Loaded => Payload as LoadedSoundFile;
    public StreamedSound? Streamed => Payload as StreamedSound;
}
