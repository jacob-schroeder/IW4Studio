using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class StreamedSound : SoundFilePayload
{
    public uint FileIndex { get; init; }
    public StreamedSoundSource? Source { get; init; }
    public StreamedSoundFileSource? StreamFile => Source as StreamedSoundFileSource;
    public ExternalStreamedSoundSource? ExternalFile => Source as ExternalStreamedSoundSource;
}
