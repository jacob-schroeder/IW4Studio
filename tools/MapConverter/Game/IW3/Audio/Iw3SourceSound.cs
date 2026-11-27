using IW4.Game.Assets.Sound;

namespace MapConverter.Game.IW3.Audio;

internal sealed record Iw3SourceSoundRow(
    string? SecondaryAlias, string? ChainAlias,
    int Sequence, float VolumeMin, float VolumeMax, float PitchMin, float PitchMax,
    float DistanceMin, float DistanceMax, int Flags,
    float SlavePercentage, float Probability, float LfePercentage,
    float CenterPercentage, int StartDelay, float EnvelopMin, float EnvelopMax,
    float EnvelopPercentage, SpeakerMap? SpeakerMap,
    string? LoadedSoundName, byte[]? LoadedPayload,
    uint PackageIndex, int PackageOffset, int PackageLength);

internal sealed record Iw3SourceSound(string Name, IReadOnlyList<Iw3SourceSoundRow> Rows);
