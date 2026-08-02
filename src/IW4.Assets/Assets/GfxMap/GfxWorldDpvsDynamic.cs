using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxWorldDpvsDynamic
{
    public const int SerializedSize = 0x30;

    public IReadOnlyList<uint> DynEntClientWordCount { get; init; } = [];
    public IReadOnlyList<uint> DynEntClientCount { get; init; } = [];
    public IReadOnlyList<XPointer<uint[]>> DynEntCellBitsPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<uint>> DynEntCellBits { get; init; } = [];
    public IReadOnlyList<XPointer<byte[]>> DynEntVisDataPointers { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<byte>> DynEntVisData { get; init; } = [];
}
