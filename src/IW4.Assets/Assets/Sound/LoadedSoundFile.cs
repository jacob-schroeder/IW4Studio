using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class LoadedSoundFile : SoundFilePayload
{
    public XPointer<LoadedSound> LoadedSoundPointer { get; init; }
    public LoadedSound? LoadedSound { get; init; }
}
