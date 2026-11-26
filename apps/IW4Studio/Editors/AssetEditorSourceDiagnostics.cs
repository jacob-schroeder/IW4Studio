using System.ComponentModel;
using IW4.Gsc.Syntax;
using IW4.Gsc.Workspace;

namespace IW4.Studio.Desktop.Editors;

/// <summary>
/// Character-based location in the current editor buffer. Offsets, lines, and
/// columns are zero-based so they can be passed to an editor without further
/// conversion.
/// </summary>
public readonly record struct EditorTextLocation
{
    public EditorTextLocation(int start, int length, int line, int character)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(character);
        _ = checked(start + length);

        Start = start;
        Length = length;
        Line = line;
        Character = character;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);

    public int Line { get; }

    public int Character { get; }

    public string DisplayText => $"Ln {Line + 1}, Col {Character + 1}";
}

public enum EditorSourceDiagnosticSeverity
{
    Warning,
    Error
}

/// <summary>
/// Immutable source-language diagnostic addressed to the current editor
/// buffer or a dependency script rather than to an authored asset field.
/// </summary>
public sealed record EditorSourceDiagnostic
{
    public EditorSourceDiagnostic(
        string code,
        EditorSourceDiagnosticSeverity severity,
        string message,
        EditorTextLocation location,
        GscScriptPath? dependencyPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));

        Code = code;
        Severity = severity;
        Message = message;
        Location = location;
        DependencyPath = dependencyPath;
    }

    public string Code { get; }

    public EditorSourceDiagnosticSeverity Severity { get; }

    public string Message { get; }

    public EditorTextLocation Location { get; }

    public GscScriptPath? DependencyPath { get; }

    public string DisplayLocation => DependencyPath is { } path
        ? $"{path.Value}: {Location.DisplayText}"
        : Location.DisplayText;

    internal static EditorSourceDiagnostic FromGsc(GscDiagnostic diagnostic, GscScriptPath? dependencyPath = null) =>
        new(
            diagnostic.Code,
            diagnostic.Severity == GscDiagnosticSeverity.Error
                ? EditorSourceDiagnosticSeverity.Error
                : EditorSourceDiagnosticSeverity.Warning,
            diagnostic.Message,
            new EditorTextLocation(
                diagnostic.Span.Start,
                diagnostic.Span.Length,
                diagnostic.LineSpan.Start.Line,
                diagnostic.LineSpan.Start.Character),
            dependencyPath);
}

/// <summary>
/// Opt-in diagnostics contract for editor buffers with addressable source
/// text. It remains separate from field-oriented asset validation.
/// </summary>
public interface IAssetEditorSourceDiagnostics : INotifyPropertyChanged
{
    IReadOnlyList<EditorSourceDiagnostic> SourceDiagnostics { get; }
}

/// <summary>
/// Optional signal for an editor that wants its source findings tool shown.
/// Continuous background analysis updates diagnostics without raising it.
/// </summary>
public interface IAssetEditorSourceDiagnosticsPresentation
{
    event EventHandler? SourceDiagnosticsPresentationRequested;
}

/// <summary>Implemented by editor views that can select a source location.</summary>
public interface IEditorTextNavigator
{
    void NavigateTo(EditorTextLocation location);
}
