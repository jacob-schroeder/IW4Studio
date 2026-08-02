using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.StructuredData;

public sealed class StructuredDataIndexedArray
{
    public const int SerializedSize = 0x10;

    public int ArraySize { get; init; }
    public StructuredDataType ElementType { get; init; } = new();
    public uint ElementSize { get; init; }
}
