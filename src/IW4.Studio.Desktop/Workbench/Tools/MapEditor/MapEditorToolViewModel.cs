using Avalonia.Threading;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor;
using IW4.Studio.MapEditor.Compilation.Import;

namespace IW4.Studio.Desktop.Workbench.Tools.MapEditor;

/// <summary>
/// Right-rail launcher for the aggregate compiled-map editor. The import runs
/// off the UI thread and remains detached from runtime assets and the shared
/// FastFile editing session.
/// </summary>
public sealed class MapEditorToolViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly FastFileWorkspace _workspace;
    private readonly FastFileEditingSession? _editingSession;
    private readonly IW4.Studio.MapEditor.MapEditorService _service;
    private MapEditorOpenResult? _result;
    private MapEditorEditingContext? _editingContext;
    private Task? _preparation;
    private string _statusHeading = "Map editor ready";
    private string _statusMessage =
        "Select this tool to resolve the compiled map bundle and prepare its detached semantic editor document.";
    private bool _isPreparing;
    private bool _canLaunch;
    private bool _disposed;

    public MapEditorToolViewModel(
        FastFileWorkspace workspace,
        IW4.Studio.MapEditor.MapEditorService? service = null)
        : this(workspace, editingSession: null, service)
    {
    }

    public MapEditorToolViewModel(
        FastFileWorkspace workspace,
        FastFileEditingSession? editingSession,
        IW4.Studio.MapEditor.MapEditorService? service = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (editingSession is not null &&
            !ReferenceEquals(editingSession.Workspace, workspace))
        {
            throw new ArgumentException(
                "The editing session does not belong to this workspace.",
                nameof(editingSession));
        }

        _workspace = workspace;
        _editingSession = editingSession;
        _service = service ?? new IW4.Studio.MapEditor.MapEditorService();
        DocumentName = Path.GetFileName(workspace.Document.Request.Path);
        TargetZoneName = workspace.Document.TargetZone.LogicalZoneName;
    }

    public event EventHandler? LaunchRequested;

    public Task? Preparation => _preparation;
    public string DocumentName { get; }
    public string TargetZoneName { get; }

    public string StatusHeading
    {
        get => _statusHeading;
        private set => SetProperty(ref _statusHeading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsPreparing
    {
        get => _isPreparing;
        private set => SetProperty(ref _isPreparing, value);
    }

    public bool CanLaunch
    {
        get => _canLaunch;
        private set => SetProperty(ref _canLaunch, value);
    }

    public string MapIdentity =>
        _result?.Session?.Bundle.MapIdentity ?? "Not resolved";

    public string ObjectCountText =>
        _result?.Session is { } session
            ? $"{session.Document.Objects.Count:N0}"
            : "—";

    public string SourceBindingCountText =>
        _result?.Session is { } session
            ? $"{session.SourceBindings.Count:N0}"
            : "—";

    public string SavePolicy =>
        _result?.Succeeded == true
            ? "Proof-gated ComMap, MapEnts, FxMap, and static-model Save As · unsupported edits fail closed"
            : "Unavailable";

    public ExistingMapImportResult? Session => _result?.Session;
    public MapEditorOpenResult? OpenResult => _result;
    public MapEditorEditingContext? EditingContext => _editingContext;

    public Task EnsurePrepared()
    {
        if (_disposed)
            return Task.CompletedTask;

        if (_preparation is not null)
            return _preparation;

        StatusHeading = "Preparing map document";
        StatusMessage =
            "Resolving the compiled map bundle and projecting detached semantic objects…";
        IsPreparing = true;
        return _preparation = PrepareAsync(_workspace, _service);
    }

    public void RequestLaunch()
    {
        if (!CanLaunch || Session is null)
            return;

        LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _editingContext?.Dispose();
        _editingContext = null;
    }

    private async Task PrepareAsync(
        FastFileWorkspace workspace,
        IW4.Studio.MapEditor.MapEditorService service)
    {
        MapEditorOpenResult result;
        try
        {
            result = await Task.Run(
                    () => service.Open(
                        workspace,
                        _editingSession?.Revision ?? 0,
                        _cancellation.Token),
                    _cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result = MapEditorOpenResult.Failure(
                MapEditorOpenStatus.Invalid,
                $"Unexpected map-editor preparation failure: {exception.Message}");
        }

        if (_disposed || _cancellation.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => ApplyResult(result));
    }

    private void ApplyResult(MapEditorOpenResult result)
    {
        if (_disposed)
            return;

        _editingContext?.Dispose();
        _result = result;
        _editingContext = result is
            { Succeeded: true, Session: { } preparedSession }
            ? new MapEditorEditingContext(preparedSession.Document)
            : null;
        IsPreparing = false;
        CanLaunch = result.Succeeded;
        if (result.Succeeded && result.Session is { } session)
        {
            StatusHeading = "Compiled map document ready";
            int diagnosticCount = result.Diagnostics
                .Distinct(StringComparer.Ordinal)
                .Count();
            StatusMessage =
                $"Imported {session.Document.Objects.Count:N0} semantic objects without mutating loaded assets." +
                (diagnosticCount == 0
                    ? string.Empty
                    : $" Review {diagnosticCount:N0} import diagnostic(s) in the editor.");
        }
        else
        {
            StatusHeading = result.Status == MapEditorOpenStatus.NotAMap
                ? "No compiled map in this fastfile"
                : "Map editor unavailable";
            StatusMessage = string.Join(
                Environment.NewLine,
                result.Diagnostics.DefaultIfEmpty(
                    "The compiled map bundle could not be resolved."));
        }

        OnPropertyChanged(nameof(MapIdentity));
        OnPropertyChanged(nameof(ObjectCountText));
        OnPropertyChanged(nameof(SourceBindingCountText));
        OnPropertyChanged(nameof(SavePolicy));
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(OpenResult));
        OnPropertyChanged(nameof(EditingContext));
    }
}
