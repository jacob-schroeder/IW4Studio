namespace IW4.Gsc.Syntax;

/// <summary>A half-open range of UTF-16 character offsets in GSC source text.</summary>
public readonly record struct GscTextSpan
{
    public GscTextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _ = checked(start + length);

        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);
}
