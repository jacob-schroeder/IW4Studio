using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class StreamedSoundFileSource : StreamedSoundSource
{
    public int StreamFileOffset { get; init; }
    public int StreamFileLength { get; init; }
}
