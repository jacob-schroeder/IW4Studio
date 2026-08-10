namespace IW4.Linker.Model;

/// <summary>
/// Opaque capture-time identity. It is never a block address: TEMP addresses
/// may be reused by several distinct lifetimes.
/// </summary>
public readonly record struct CaptureOccurrence(long Value)
{
    internal static CaptureOccurrence Create(long value) =>
        value > 0 ? new(value) : throw new ArgumentOutOfRangeException(nameof(value));
}
