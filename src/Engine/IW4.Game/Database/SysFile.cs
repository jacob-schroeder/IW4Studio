namespace IW4.Game.Database;

// The managed representation retains the engine descriptor's start offset.
// Loader IO owns the live managed handle associated with this value.
public sealed class SysFile
{
    public SysFile(int startOffset)
    {
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset));

        StartOffset = startOffset;
    }

    public int StartOffset { get; }
}
