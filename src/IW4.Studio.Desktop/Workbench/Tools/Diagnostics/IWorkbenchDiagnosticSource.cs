namespace IW4.Studio.Desktop.Workbench.Tools.Diagnostics;

/// <summary>
/// Snapshot-producing diagnostic source that can be observed by the workbench.
/// Implementations should raise <see cref="DiagnosticsChanged"/> after updating
/// <see cref="CurrentDiagnostics"/>.
/// </summary>
public interface IWorkbenchDiagnosticSource
{
    string Source { get; }

    IReadOnlyList<WorkbenchDiagnostic> CurrentDiagnostics { get; }

    event EventHandler? DiagnosticsChanged;
}
