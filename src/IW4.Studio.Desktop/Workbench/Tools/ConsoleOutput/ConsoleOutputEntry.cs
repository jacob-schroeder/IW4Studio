namespace IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;

/// <summary>
/// Display level for one Studio workbench activity entry.
/// </summary>
public enum ConsoleOutputLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error
}

/// <summary>
/// Immutable console/activity output suitable for retaining in the workbench.
/// </summary>
public sealed record ConsoleOutputEntry
{
    public ConsoleOutputEntry(
        DateTimeOffset timestamp,
        ConsoleOutputLevel level,
        string source,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(message);

        Timestamp = timestamp;
        Level = level;
        Source = source;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public ConsoleOutputLevel Level { get; }

    public string Source { get; }

    public string Message { get; }
}
