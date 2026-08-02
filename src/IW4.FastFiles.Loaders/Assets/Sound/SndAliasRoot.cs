using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Sound;

internal sealed record SndAliasRoot(
    int Offset,
    XString AliasNamePointer,
    XString SubtitlePointer,
    XString SecondaryAliasNamePointer,
    XString ChainAliasNamePointer,
    XString MixerGroupPointer,
    XPointer<SoundFile[]> SoundFilesPointer,
    int Sequence,
    float VolumeMin,
    float VolumeMax,
    float PitchMin,
    float PitchMax,
    float DistanceMin,
    float DistanceMax,
    float VelocityMin,
    int Flags,
    float SlavePercentage,
    float Probability,
    float LfePercentage,
    float CenterPercentage,
    int StartDelay,
    XPointer<SndCurve> VolumeFalloffCurvePointer,
    float EnvelopMin,
    float EnvelopMax,
    float EnvelopPercentage,
    XPointer<SpeakerMap> SpeakerMapPointer);
