using IW4.Studio.Desktop.Editors;

namespace IW4.Studio.Desktop.Workbench.Tools.Diagnostics;

/// <summary>
/// Severity displayed by the workbench Diagnostics tool.
/// </summary>
public enum WorkbenchDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Immutable diagnostic whose key is stable within its source.
/// </summary>
public sealed record WorkbenchDiagnostic
{
    public WorkbenchDiagnostic(
        string key,
        WorkbenchDiagnosticSeverity severity,
        string source,
        string message,
        EditorTextLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Key = key;
        Severity = severity;
        Source = source;
        Message = message;
        Location = location;
    }

    public string Key { get; }

    public WorkbenchDiagnosticSeverity Severity { get; }

    public string Source { get; }

    public string Message { get; }

    public EditorTextLocation? Location { get; }

    public string LocationText => Location?.DisplayText ?? string.Empty;
}

public sealed class WorkbenchDiagnosticActivatedEventArgs : EventArgs
{
    public WorkbenchDiagnosticActivatedEventArgs(WorkbenchDiagnostic diagnostic)
    {
        Diagnostic = diagnostic
            ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public WorkbenchDiagnostic Diagnostic { get; }
}
