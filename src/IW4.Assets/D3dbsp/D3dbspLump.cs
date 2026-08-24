namespace IW4.Assets.D3dbsp;

public sealed class D3dbspLump
{
    public D3dbspLump(
        D3dbspLumpType type,
        byte[] data,
        byte[] padding)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(padding);
        int expectedPaddingLength = (int)((0u - checked((uint)data.Length)) & 3u);
        if (padding.Length != expectedPaddingLength)
        {
            throw new ArgumentException(
                $"A {data.Length}-byte d3dbsp lump requires {expectedPaddingLength} padding bytes.",
                nameof(padding));
        }

        Type = type;
        Data = data;
        Padding = padding;
    }

    public D3dbspLumpType Type { get; }
    public byte[] Data { get; }
    public byte[] Padding { get; }
    public uint Length => checked((uint)Data.Length);
}
