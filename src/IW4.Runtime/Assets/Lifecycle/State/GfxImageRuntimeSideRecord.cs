namespace IW4.Runtime.Assets.Lifecycle.State;

/// <summary>
/// Opaque indexed 0x50-byte record copied, compared, swapped, and zeroed by
/// GfxImage lifecycle operations. Its internal byte layout is intentionally
/// opaque.
/// </summary>
public sealed class GfxImageRuntimeSideRecord
{
    public const int Size = 0x50;

    private readonly byte[] _bytes;

    public GfxImageRuntimeSideRecord(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
        {
            throw new ArgumentException(
                $"A GfxImage runtime side record must be exactly 0x{Size:X} bytes.",
                nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static GfxImageRuntimeSideRecord Zero() => new(new byte[Size]);

    public bool ContentEquals(GfxImageRuntimeSideRecord other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _bytes.AsSpan().SequenceEqual(other._bytes);
    }

    public GfxImageRuntimeSideRecord Copy() => new(_bytes);
}
