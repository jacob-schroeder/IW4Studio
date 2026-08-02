using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

public sealed class XModelSurfsAsset : BaseAsset
{
    public const int SerializedSize = 0x24;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public XPointer<byte[]> SurfsPointer { get; init; }
    public ushort NumSurfs { get; init; }
    public ushort Pad0A { get; init; }
    public IReadOnlyList<uint> PartBits { get; init; } = [];
    public IReadOnlyList<XSurface> Surfaces { get; init; } = [];
}
