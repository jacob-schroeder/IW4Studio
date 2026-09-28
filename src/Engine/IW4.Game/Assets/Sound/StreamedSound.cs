using IW4.Game.Pointers;

namespace IW4.Game.Assets.Sound;

public sealed class StreamedSound : SoundFilePayload
{
    // Authored map convention; requires the matching PS3 executable patch.
    public const uint NamedFileIndex = uint.MaxValue;

    public uint FileIndex { get; init; }
    public StreamedSoundSource? Source { get; init; }
    public StreamedSoundFileSource? StreamFile => Source as StreamedSoundFileSource;
    public ExternalStreamedSoundSource? ExternalFile => Source as ExternalStreamedSoundSource;

    public static string GetPackageFileName(uint fileIndex, string fastFilePath) =>
        fileIndex == NamedFileIndex
            ? $"{Path.GetFileNameWithoutExtension(fastFilePath)}_snd.pak"
            : $"packfile{fileIndex}.pak";
}
