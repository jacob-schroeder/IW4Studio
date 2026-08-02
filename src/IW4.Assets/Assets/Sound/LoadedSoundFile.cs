using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class LoadedSoundFile : SoundFilePayload
{
    public XPointer<LoadedSound> LoadedSoundPointer { get; init; }
    public LoadedSound? LoadedSound { get; init; }

    /// <summary>
    /// Incoming inline/insert body consumed from this pointer before
    /// DB_AddXAsset canonicalization. Null for null and packed references.
    /// </summary>
    public LoadedSound? IncomingLoadedSound { get; init; }
}
