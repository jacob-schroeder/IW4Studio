using IW4.FastFiles.Zone;
namespace IW4.Runtime.Assets;

/// <summary>
/// Stable managed identity for one zone-owned definition contributing to a
/// canonical XAsset slot.
/// </summary>
public readonly record struct XAssetProviderId
{
    internal XAssetProviderId(long value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public long Value { get; }

    public bool IsNone => Value == 0;

    public override string ToString() => IsNone ? "PROVIDER:none" : $"PROVIDER:{Value}";
}
