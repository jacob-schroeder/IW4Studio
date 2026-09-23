namespace IW4.Gsc.Syntax;

/// <summary>A zero-based line and UTF-16 character position.</summary>
public readonly record struct GscLinePosition
{
    public GscLinePosition(int line, int character)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(character);

        Line = line;
        Character = character;
    }

    public int Line { get; }

    public int Character { get; }
}

/// <summary>A half-open range expressed as zero-based line positions.</summary>
public readonly record struct GscLinePositionSpan
{
    public GscLinePositionSpan(GscLinePosition start, GscLinePosition end)
    {
        if (end.Line < start.Line ||
            end.Line == start.Line && end.Character < start.Character)
        {
            throw new ArgumentException("The line-span end cannot precede its start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public GscLinePosition Start { get; }

    public GscLinePosition End { get; }
}
