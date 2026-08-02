using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class StreamedSoundFileSource : StreamedSoundSource
{
    public int StreamFileOffset { get; init; }
    public int StreamFileLength { get; init; }
}
