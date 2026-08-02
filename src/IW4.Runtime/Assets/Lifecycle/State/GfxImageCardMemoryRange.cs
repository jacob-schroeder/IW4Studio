namespace IW4.Runtime.Assets.Lifecycle.State;

public readonly record struct GfxImageCardMemoryRange
{
    public GfxImageCardMemoryRange(uint start, uint end)
    {
        if (end <= start)
            throw new ArgumentOutOfRangeException(nameof(end), "A card-memory range must be non-empty.");

        Start = start;
        End = end;
    }

    public uint Start { get; }

    public uint End { get; }
}
