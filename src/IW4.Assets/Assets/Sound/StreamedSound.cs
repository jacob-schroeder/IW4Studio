using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class StreamedSound : SoundFilePayload
{
    public uint FileIndex { get; init; }
    public StreamedSoundSource? Source { get; init; }
    public StreamedSoundFileSource? StreamFile => Source as StreamedSoundFileSource;
    public ExternalStreamedSoundSource? ExternalFile => Source as ExternalStreamedSoundSource;
}
