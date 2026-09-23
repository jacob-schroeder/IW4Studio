namespace IW4.Runtime.Assets.Lifecycle.State;

/// <summary>
/// Mutable 0x100-byte collision singleton state.
/// </summary>
public sealed class ClipMapRuntimeState : IClipMapRuntimeState
{
    public const int Size = 0x100;

    private byte[] _bytes;

    public ClipMapRuntimeState()
        : this(new byte[Size])
    {
    }

    public ClipMapRuntimeState(ReadOnlySpan<byte> bytes)
    {
        ValidateLength(bytes);
        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public void Replace(ReadOnlySpan<byte> bytes)
    {
        ValidateLength(bytes);
        _bytes = bytes.ToArray();
    }

    public void ResetPreservingIdentity()
    {
        Span<byte> identity = stackalloc byte[sizeof(uint)];
        _bytes.AsSpan(0, identity.Length).CopyTo(identity);
        Array.Clear(_bytes);
        identity.CopyTo(_bytes);
    }

    public IXAssetRuntimeStateSnapshot CaptureSnapshot() =>
        new ClipMapRuntimeSnapshot((byte[])_bytes.Clone());

    public void RestoreSnapshot(IXAssetRuntimeStateSnapshot snapshot)
    {
        if (snapshot is not ClipMapRuntimeSnapshot typed)
            throw new ArgumentException("Snapshot does not belong to ClipMap runtime state.", nameof(snapshot));

        _bytes = (byte[])typed.Bytes.Clone();
    }

    private static void ValidateLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
        {
            throw new ArgumentException(
                $"ClipMap runtime state must be exactly 0x{Size:X} bytes.",
                nameof(bytes));
        }
    }
}
