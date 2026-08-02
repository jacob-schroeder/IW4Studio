using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimPackedDataStreams
{
    public IReadOnlyList<byte> QuantizedBytes { get; init; } = [];
    public IReadOnlyList<short> QuantizedShorts { get; init; } = [];
    public IReadOnlyList<int> QuantizedInts { get; init; } = [];
    public IReadOnlyList<short> RandomizedQuantizedShorts { get; init; } = [];
    public IReadOnlyList<byte> RandomizedQuantizedBytes { get; init; } = [];
    public IReadOnlyList<int> RandomizedQuantizedInts { get; init; } = [];
}
