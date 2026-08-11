using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class SndAlias
{
    public const int SerializedSize = 0x64;

    public int Offset { get; init; }
    public XPointer<string> AliasNamePointer { get; init; }
    public string? AliasName { get; init; }
    public XPointer<string> SubtitlePointer { get; init; }
    public string? Subtitle { get; init; }
    public XPointer<string> SecondaryAliasNamePointer { get; init; }
    public string? SecondaryAliasName { get; init; }
    public XPointer<string> ChainAliasNamePointer { get; init; }
    public string? ChainAliasName { get; init; }
    public XPointer<string> MixerGroupPointer { get; init; }
    public string? MixerGroup { get; init; }
    public XPointer<SoundFile[]> SoundFilesPointer { get; init; }
    public int SoundFileCount { get; init; } = 1;
    public IReadOnlyList<SoundFile> SoundFiles { get; init; } = [];
    public int Sequence { get; init; }
    public float VolumeMin { get; init; }
    public float VolumeMax { get; init; }
    public float PitchMin { get; init; }
    public float PitchMax { get; init; }
    public float DistanceMin { get; init; }
    public float DistanceMax { get; init; }
    public float VelocityMin { get; init; }
    public int Flags { get; init; }
    public float SlavePercentage { get; init; }
    public float Probability { get; init; }
    public float LfePercentage { get; init; }
    public float CenterPercentage { get; init; }
    public int StartDelay { get; init; }
    public XPointer<SndCurve> VolumeFalloffCurvePointer { get; init; }
    public SndCurve? VolumeFalloffCurve { get; init; }
    public float EnvelopMin { get; init; }
    public float EnvelopMax { get; init; }
    public float EnvelopPercentage { get; init; }
    public XPointer<SpeakerMap> SpeakerMapPointer { get; init; }
    public SpeakerMap? SpeakerMap { get; init; }
}
