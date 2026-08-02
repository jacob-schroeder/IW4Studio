using IW4.FastFiles.Zone;
namespace IW4.Runtime.Database;

/// <summary>
/// Stable managed identity for one registered XZone.
/// </summary>
public readonly record struct DbZoneHandle
{
    internal DbZoneHandle(long value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public long Value { get; }

    public bool IsNone => Value == 0;

    public override string ToString() => IsNone ? "ZONE:none" : $"ZONE:{Value}";
}
