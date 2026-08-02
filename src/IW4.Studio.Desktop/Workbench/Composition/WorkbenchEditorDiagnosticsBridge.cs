using System.ComponentModel;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Tools.Diagnostics;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>
/// Window-local adapter from the selected editor's validation contracts into
/// the reusable diagnostics tool. It deliberately knows nothing about dock
/// layout, navigator routing, or window lifecycle.
/// </summary>
public sealed class WorkbenchEditorDiagnosticsBridge : IDisposable
{
    private const string SourceName = "Selected editor";

    private readonly EditorViewModel _editor;
    private readonly DiagnosticsAggregator _diagnostics;
    private INotifyPropertyChanged? _observedHostedViewModel;
    private bool _disposed;

    public WorkbenchEditorDiagnosticsBridge(
        EditorViewModel editor,
        DiagnosticsAggregator diagnostics)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
        _editor.PropertyChanged += Editor_PropertyChanged;
        _diagnostics.DiagnosticActivated += Diagnostics_DiagnosticActivated;
        ObserveHostedViewModel(
            _editor.SelectedTab?.HostedViewModel as INotifyPropertyChanged);
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _editor.PropertyChanged -= Editor_PropertyChanged;
        _diagnostics.DiagnosticActivated -= Diagnostics_DiagnosticActivated;
        ObserveHostedViewModel(null);
        _diagnostics.ClearSource(SourceName);
    }

    private void Editor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(EditorViewModel.SelectedTab))
            return;

        ObserveHostedViewModel(
            _editor.SelectedTab?.HostedViewModel as INotifyPropertyChanged);
        Refresh();
    }

    private void ObserveHostedViewModel(INotifyPropertyChanged? viewModel)
    {
        if (ReferenceEquals(_observedHostedViewModel, viewModel))
            return;

        if (_observedHostedViewModel is not null)
        {
            _observedHostedViewModel.PropertyChanged -=
                HostedEditor_PropertyChanged;
        }

        _observedHostedViewModel = viewModel;
        if (_observedHostedViewModel is not null)
        {
            _observedHostedViewModel.PropertyChanged +=
                HostedEditor_PropertyChanged;
        }
    }

    private void HostedEditor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or
            nameof(IAssetEditorDiagnostics.Diagnostics) or
            nameof(IAssetEditorSourceDiagnostics.SourceDiagnostics))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        AssetExplorerTabViewModel? tab = _editor.SelectedTab;
        if (tab is null)
        {
            _diagnostics.ClearSource(SourceName);
            return;
        }

        IReadOnlyList<AssetValidationIssue> validationIssues =
            tab.HostedViewModel is IAssetEditorDiagnostics editorDiagnostics &&
            editorDiagnostics.Diagnostics.Count != 0
                ? editorDiagnostics.Diagnostics
                : tab.BackendEditor?.Validation.Issues ?? [];
        IReadOnlyList<EditorSourceDiagnostic> sourceDiagnostics =
            tab.HostedViewModel is IAssetEditorSourceDiagnostics editorSourceDiagnostics
                ? editorSourceDiagnostics.SourceDiagnostics
                : [];

        if (validationIssues.Count == 0 && sourceDiagnostics.Count == 0)
        {
            _diagnostics.ReplaceBySource(
                SourceName,
                [
                    new WorkbenchDiagnostic(
                        "selection-valid",
                        WorkbenchDiagnosticSeverity.Information,
                        SourceName,
                        tab.HasHostedEditor
                            ? $"No validation errors for '{tab.Title}'."
                            : $"No editor is implemented for {tab.Entry.AssetType}.")
                ]);
            return;
        }

        IEnumerable<WorkbenchDiagnostic> validationProjection =
            validationIssues.Select((issue, index) => new WorkbenchDiagnostic(
                $"{issue.FieldPath}:{index}",
                issue.Severity == AssetValidationSeverity.Error
                    ? WorkbenchDiagnosticSeverity.Error
                    : WorkbenchDiagnosticSeverity.Warning,
                SourceName,
                $"{issue.FieldPath} — {issue.Message}"));
        IEnumerable<WorkbenchDiagnostic> sourceProjection =
            sourceDiagnostics.Select((diagnostic, index) => new WorkbenchDiagnostic(
                $"source:{diagnostic.Code}:{diagnostic.Location.Start}:{diagnostic.Location.Length}:{index}",
                diagnostic.Severity == EditorSourceDiagnosticSeverity.Error
                    ? WorkbenchDiagnosticSeverity.Error
                    : WorkbenchDiagnosticSeverity.Warning,
                SourceName,
                $"{diagnostic.Code} — {diagnostic.Message}",
                diagnostic.Location));

        _diagnostics.ReplaceBySource(
            SourceName,
            validationProjection.Concat(sourceProjection));
    }

    private void Diagnostics_DiagnosticActivated(
        object? sender,
        WorkbenchDiagnosticActivatedEventArgs args)
    {
        WorkbenchDiagnostic diagnostic = args.Diagnostic;
        if (!string.Equals(diagnostic.Source, SourceName, StringComparison.Ordinal) ||
            diagnostic.Location is not { } location)
        {
            return;
        }

        if (_editor.SelectedTab?.HostedView is IEditorTextNavigator navigator)
            navigator.NavigateTo(location);
    }
}
