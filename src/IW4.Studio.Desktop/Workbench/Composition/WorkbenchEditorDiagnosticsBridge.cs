using System.ComponentModel;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.Workbench.Tools.Diagnostics;
using IW4.Studio.Desktop.Workbench.Tools.GscFindings;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Composition;

/// <summary>
/// Window-local adapter from the selected editor's validation contracts into
/// the field diagnostics and GSC findings tools.
/// </summary>
public sealed class WorkbenchEditorDiagnosticsBridge : IDisposable
{
    private const string SourceName = "Selected editor";

    private readonly EditorViewModel _editor;
    private readonly DiagnosticsAggregator _diagnostics;
    private readonly GscFindingsToolViewModel _gscFindings;
    private INotifyPropertyChanged? _observedHostedViewModel;
    private bool _disposed;

    public WorkbenchEditorDiagnosticsBridge(
        EditorViewModel editor,
        DiagnosticsAggregator diagnostics,
        GscFindingsToolViewModel gscFindings)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
        _gscFindings = gscFindings
            ?? throw new ArgumentNullException(nameof(gscFindings));
        _editor.PropertyChanged += Editor_PropertyChanged;
        _gscFindings.FindingActivated += GscFindings_FindingActivated;
        ObserveHostedViewModel(
            _editor.SelectedTab?.HostedViewModel as INotifyPropertyChanged);
        Refresh();
    }

    public event EventHandler? GscFindingsPresented;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _editor.PropertyChanged -= Editor_PropertyChanged;
        _gscFindings.FindingActivated -= GscFindings_FindingActivated;
        ObserveHostedViewModel(null);
        _diagnostics.ClearSource(SourceName);
        _gscFindings.Clear();
        GscFindingsPresented = null;
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
            if (_observedHostedViewModel is
                IAssetEditorSourceDiagnosticsPresentation presentation)
            {
                presentation.SourceDiagnosticsPresentationRequested -=
                    SourceDiagnostics_PresentationRequested;
            }
        }

        _observedHostedViewModel = viewModel;
        if (_observedHostedViewModel is not null)
        {
            _observedHostedViewModel.PropertyChanged +=
                HostedEditor_PropertyChanged;
            if (_observedHostedViewModel is
                IAssetEditorSourceDiagnosticsPresentation presentation)
            {
                presentation.SourceDiagnosticsPresentationRequested +=
                    SourceDiagnostics_PresentationRequested;
            }
        }
    }

    private void HostedEditor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null or
            nameof(IAssetEditorDiagnostics.Diagnostics))
        {
            RefreshValidationDiagnostics();
        }

        if (args.PropertyName is null or
            nameof(IAssetEditorSourceDiagnostics.SourceDiagnostics))
        {
            RefreshGscFindings();
        }
    }

    private void Refresh()
    {
        RefreshValidationDiagnostics();
        RefreshGscFindings();
    }

    private void RefreshValidationDiagnostics()
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

        if (validationIssues.Count == 0)
        {
            _diagnostics.ReplaceBySource(
                SourceName,
                [
                    new WorkbenchDiagnostic(
                        "selection-valid",
                        WorkbenchDiagnosticSeverity.Information,
                        SourceName,
                        tab.HasHostedEditor
                            ? $"No asset validation errors for '{tab.Title}'."
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

        _diagnostics.ReplaceBySource(SourceName, validationProjection);
    }

    private void RefreshGscFindings()
    {
        AssetExplorerTabViewModel? tab = _editor.SelectedTab;
        IReadOnlyList<EditorSourceDiagnostic> findings =
            tab?.HostedViewModel is IAssetEditorSourceDiagnostics sourceDiagnostics
                ? sourceDiagnostics.SourceDiagnostics
                : [];

        if (tab is null || findings.Count == 0)
        {
            _gscFindings.Clear();
            return;
        }

        _gscFindings.Replace(tab.Title, findings);
    }

    private void SourceDiagnostics_PresentationRequested(
        object? sender,
        EventArgs args)
    {
        RefreshGscFindings();
        if (_gscFindings.HasItems)
            GscFindingsPresented?.Invoke(this, EventArgs.Empty);
    }

    private void GscFindings_FindingActivated(
        object? sender,
        GscFindingActivatedEventArgs args)
    {
        if (_editor.SelectedTab?.HostedView is IEditorTextNavigator navigator)
            navigator.NavigateTo(args.Finding.Location);
    }
}
