using System.Collections.ObjectModel;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Workbench.Tools.Diagnostics;

/// <summary>
/// Aggregates independently replaceable diagnostic snapshots into one
/// observable workbench projection.
/// </summary>
public sealed class DiagnosticsAggregator : ObservableObject, IDisposable
{
    private readonly Dictionary<string, WorkbenchDiagnostic[]> _diagnosticsBySource =
        new(StringComparer.Ordinal);
    private readonly List<string> _sourceOrder = new();
    private readonly ObservableCollection<WorkbenchDiagnostic> _entries = new();
    private readonly Dictionary<string, SourceSubscription> _subscriptions =
        new(StringComparer.Ordinal);

    private bool _disposed;
    private int _informationCount;
    private int _warningCount;
    private int _errorCount;

    public DiagnosticsAggregator()
    {
        Entries = new ReadOnlyObservableCollection<WorkbenchDiagnostic>(_entries);
    }

    public event EventHandler<WorkbenchDiagnosticActivatedEventArgs>? DiagnosticActivated;

    public ReadOnlyObservableCollection<WorkbenchDiagnostic> Entries { get; }

    public int Count => _entries.Count;

    public bool HasEntries => Count != 0;

    public int InformationCount => _informationCount;

    public int WarningCount => _warningCount;

    public int ErrorCount => _errorCount;

    public bool HasErrors => ErrorCount != 0;

    /// <summary>Raises navigation intent for one currently displayed entry.</summary>
    public void Activate(WorkbenchDiagnostic diagnostic)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!_entries.Contains(diagnostic))
            return;

        DiagnosticActivated?.Invoke(
            this,
            new WorkbenchDiagnosticActivatedEventArgs(diagnostic));
    }

    /// <summary>
    /// Atomically replaces every diagnostic owned by <paramref name="source"/>.
    /// Entry keys must be unique within that source.
    /// </summary>
    public void ReplaceBySource(
        string source,
        IEnumerable<WorkbenchDiagnostic> diagnostics)
    {
        ThrowIfDisposed();
        WorkbenchDiagnostic[] snapshot = ValidateSnapshot(source, diagnostics);
        ReplaceBySourceCore(source, snapshot);
    }

    /// <summary>
    /// Clears one source's current snapshot without detaching a subscription.
    /// A later source notification can repopulate it.
    /// </summary>
    public bool ClearSource(string source)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!_diagnosticsBySource.Remove(source))
            return false;

        _sourceOrder.Remove(source);
        ReconcileProjection();
        return true;
    }

    /// <summary>
    /// Clears all current snapshots while leaving source subscriptions active.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        if (_diagnosticsBySource.Count == 0)
            return;

        _diagnosticsBySource.Clear();
        _sourceOrder.Clear();
        ReconcileProjection();
    }

    /// <summary>
    /// Observes one source and immediately projects its current snapshot.
    /// Only one subscription per source name is active at a time.
    /// Disposing the returned registration detaches and clears that source.
    /// </summary>
    public IDisposable Subscribe(IWorkbenchDiagnosticSource source)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Source);

        WorkbenchDiagnostic[] snapshot = ValidateSnapshot(
            source.Source,
            source.CurrentDiagnostics);

        if (_subscriptions.Remove(source.Source, out SourceSubscription? previous))
            previous.Detach();

        var subscription = new SourceSubscription(this, source);
        _subscriptions.Add(source.Source, subscription);
        source.DiagnosticsChanged += subscription.HandleDiagnosticsChanged;
        ReplaceBySourceCore(source.Source, snapshot);
        return subscription;
    }

    /// <summary>
    /// Detaches and clears the source registered under <paramref name="source"/>.
    /// </summary>
    public bool Unsubscribe(string source)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!_subscriptions.TryGetValue(source, out SourceSubscription? subscription))
            return false;

        RemoveSubscription(subscription);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (SourceSubscription subscription in _subscriptions.Values)
            subscription.Detach();

        _subscriptions.Clear();
        DiagnosticActivated = null;
        _diagnosticsBySource.Clear();
        _sourceOrder.Clear();
        ReconcileProjection();
    }

    private static WorkbenchDiagnostic[] ValidateSnapshot(
        string source,
        IEnumerable<WorkbenchDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        WorkbenchDiagnostic[] snapshot = diagnostics.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorkbenchDiagnostic? diagnostic in snapshot)
        {
            if (diagnostic is null)
                throw new ArgumentException("A diagnostic snapshot cannot contain null.", nameof(diagnostics));

            if (!string.Equals(source, diagnostic.Source, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Diagnostic source '{diagnostic.Source}' does not match snapshot source '{source}'.",
                    nameof(diagnostics));
            }

            if (!keys.Add(diagnostic.Key))
            {
                throw new ArgumentException(
                    $"Diagnostic key '{diagnostic.Key}' is duplicated for source '{source}'.",
                    nameof(diagnostics));
            }
        }

        return snapshot;
    }

    private void ReplaceBySourceCore(
        string source,
        WorkbenchDiagnostic[] diagnostics)
    {
        if (!_diagnosticsBySource.TryGetValue(source, out WorkbenchDiagnostic[]? current))
        {
            _sourceOrder.Add(source);
        }
        else if (current.SequenceEqual(diagnostics))
        {
            return;
        }

        _diagnosticsBySource[source] = diagnostics;
        ReconcileProjection();
    }

    private void RefreshSubscription(SourceSubscription subscription)
    {
        if (_disposed ||
            !_subscriptions.TryGetValue(subscription.Source.Source, out SourceSubscription? current) ||
            !ReferenceEquals(current, subscription))
        {
            return;
        }

        WorkbenchDiagnostic[] snapshot = ValidateSnapshot(
            subscription.Source.Source,
            subscription.Source.CurrentDiagnostics);
        ReplaceBySourceCore(subscription.Source.Source, snapshot);
    }

    private void RemoveSubscription(SourceSubscription subscription)
    {
        string source = subscription.Source.Source;
        if (!_subscriptions.TryGetValue(source, out SourceSubscription? current) ||
            !ReferenceEquals(current, subscription))
        {
            subscription.Detach();
            return;
        }

        _subscriptions.Remove(source);
        subscription.Detach();

        if (_diagnosticsBySource.Remove(source))
        {
            _sourceOrder.Remove(source);
            ReconcileProjection();
        }
    }

    private void ReconcileProjection()
    {
        int previousCount = Count;
        int previousInformationCount = _informationCount;
        int previousWarningCount = _warningCount;
        int previousErrorCount = _errorCount;

        WorkbenchDiagnostic[] desired = _sourceOrder
            .SelectMany(source => _diagnosticsBySource[source])
            .ToArray();

        for (int desiredIndex = 0; desiredIndex < desired.Length; desiredIndex++)
        {
            WorkbenchDiagnostic next = desired[desiredIndex];
            if (desiredIndex < _entries.Count &&
                HasSameIdentity(_entries[desiredIndex], next))
            {
                if (_entries[desiredIndex] != next)
                    _entries[desiredIndex] = next;

                continue;
            }

            int existingIndex = FindIdentity(next, desiredIndex + 1);
            if (existingIndex >= 0)
            {
                _entries.Move(existingIndex, desiredIndex);
                if (_entries[desiredIndex] != next)
                    _entries[desiredIndex] = next;
            }
            else
            {
                _entries.Insert(desiredIndex, next);
            }
        }

        while (_entries.Count > desired.Length)
            _entries.RemoveAt(_entries.Count - 1);

        _informationCount = desired.Count(
            diagnostic => diagnostic.Severity == WorkbenchDiagnosticSeverity.Information);
        _warningCount = desired.Count(
            diagnostic => diagnostic.Severity == WorkbenchDiagnosticSeverity.Warning);
        _errorCount = desired.Count(
            diagnostic => diagnostic.Severity == WorkbenchDiagnosticSeverity.Error);

        RaiseSummaryChanges(
            previousCount,
            previousInformationCount,
            previousWarningCount,
            previousErrorCount);
    }

    private int FindIdentity(WorkbenchDiagnostic target, int startIndex)
    {
        for (int index = startIndex; index < _entries.Count; index++)
        {
            if (HasSameIdentity(_entries[index], target))
                return index;
        }

        return -1;
    }

    private static bool HasSameIdentity(
        WorkbenchDiagnostic left,
        WorkbenchDiagnostic right) =>
        string.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
        string.Equals(left.Key, right.Key, StringComparison.Ordinal);

    private void RaiseSummaryChanges(
        int previousCount,
        int previousInformationCount,
        int previousWarningCount,
        int previousErrorCount)
    {
        if (previousCount != Count)
            OnPropertyChanged(nameof(Count));
        if ((previousCount != 0) != HasEntries)
            OnPropertyChanged(nameof(HasEntries));
        if (previousInformationCount != InformationCount)
            OnPropertyChanged(nameof(InformationCount));
        if (previousWarningCount != WarningCount)
            OnPropertyChanged(nameof(WarningCount));
        if (previousErrorCount != ErrorCount)
        {
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class SourceSubscription : IDisposable
    {
        private DiagnosticsAggregator? _owner;

        public SourceSubscription(
            DiagnosticsAggregator owner,
            IWorkbenchDiagnosticSource source)
        {
            _owner = owner;
            Source = source;
        }

        public IWorkbenchDiagnosticSource Source { get; }

        public void HandleDiagnosticsChanged(object? sender, EventArgs args) =>
            _owner?.RefreshSubscription(this);

        public void Dispose() => _owner?.RemoveSubscription(this);

        public void Detach()
        {
            if (_owner is null)
                return;

            Source.DiagnosticsChanged -= HandleDiagnosticsChanged;
            _owner = null;
        }
    }
}
