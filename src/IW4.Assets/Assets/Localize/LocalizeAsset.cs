using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Localize;

public sealed class LocalizeAsset : BaseAsset
{
    public const int SerializedSize = 0x08;

    public XString ValuePointer { get; init; }
    public string? Value { get; init; }
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
}
