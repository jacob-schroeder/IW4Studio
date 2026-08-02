using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Sound;

public sealed class ExternalStreamedSoundSource : StreamedSoundSource
{
    public XPointer<string>? DirectoryPointer { get; init; }
    public string? Directory { get; init; }
    public XPointer<string>? FilenamePointer { get; init; }
    public string? Filename { get; init; }
}
