using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class LoadedSoundFile : SoundFilePayload
{
    public XPointer<LoadedSound> LoadedSoundPointer { get; init; }
    public LoadedSound? LoadedSound { get; init; }
}
