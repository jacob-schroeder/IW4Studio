namespace IW4.Render.OpenGl.Scheduling;

/// <summary>
/// Persistent single-consumer CPU work queue with one replaceable pending
/// slot. Interactive camera updates never create an unbounded backlog: work
/// already executing is allowed to finish, while every not-yet-started request
/// is replaced by the newest exact key.
/// </summary>
internal sealed class MapRenderOpenGlLatestWorkQueue<TKey, TResult> :
    IDisposable
    where TKey : notnull
    where TResult : class
{
    private readonly object _gate = new();
    private readonly Func<TKey, CancellationToken, TResult> _producer;
    private readonly IEqualityComparer<TKey> _keyComparer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Thread _thread;
    private TKey _pendingKey = default!;
    private TKey _runningKey = default!;
    private TKey _completedKey = default!;
    private TResult? _completedResult;
    private Exception? _lastFailure;
    private TKey _failedKey = default!;
    private bool _hasPending;
    private bool _hasRunning;
    private bool _hasCompleted;
    private bool _hasFailure;
    private bool _stopping;
    private bool _disposed;

    public MapRenderOpenGlLatestWorkQueue(
        string workerName,
        Func<TKey, CancellationToken, TResult> producer,
        IEqualityComparer<TKey>? keyComparer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerName);
        _producer = producer ??
            throw new ArgumentNullException(nameof(producer));
        _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = workerName
        };
        _thread.Start();
    }

    public long SubmittedCount { get; private set; }

    public long ReplacedPendingCount { get; private set; }

    public long CompletedCount { get; private set; }

    public long FailedCount { get; private set; }

    /// <summary>
    /// Requests an exact key. Repeated requests for the completed, running, or
    /// already-pending key are free. A pending stale key is atomically replaced.
    /// </summary>
    public void Request(TKey key)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopping)
            {
                throw new ObjectDisposedException(
                    nameof(MapRenderOpenGlLatestWorkQueue<TKey, TResult>));
            }
            if ((_hasCompleted &&
                 _keyComparer.Equals(_completedKey, key)) ||
                (_hasRunning &&
                 _keyComparer.Equals(_runningKey, key)) ||
                (_hasPending &&
                 _keyComparer.Equals(_pendingKey, key)))
            {
                return;
            }

            if (_hasPending)
                ReplacedPendingCount++;
            _pendingKey = key;
            _hasPending = true;
            SubmittedCount++;
            Monitor.PulseAll(_gate);
        }
    }

    public bool TryGetExact(TKey key, out TResult? result)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_hasCompleted &&
                _keyComparer.Equals(_completedKey, key))
            {
                result = _completedResult!;
                return true;
            }

            result = null;
            return false;
        }
    }

    public bool TryTakeFailure(TKey key, out Exception? failure)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_hasFailure &&
                _keyComparer.Equals(_failedKey, key))
            {
                failure = _lastFailure;
                _hasFailure = false;
                _lastFailure = null;
                return true;
            }

            failure = null;
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _stopping = true;
            _hasPending = false;
            _shutdown.Cancel();
            Monitor.PulseAll(_gate);
        }

        _thread.Join();
        lock (_gate)
        {
            _disposed = true;
            _hasRunning = false;
            _hasCompleted = false;
            _hasFailure = false;
            _completedResult = null;
            _lastFailure = null;
            Monitor.PulseAll(_gate);
        }
        _shutdown.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            TKey key;
            lock (_gate)
            {
                while (!_stopping && !_hasPending)
                    Monitor.Wait(_gate);
                if (_stopping)
                    return;

                key = _pendingKey;
                _hasPending = false;
                _runningKey = key;
                _hasRunning = true;
            }

            TResult? result = null;
            Exception? failure = null;
            try
            {
                result = _producer(key, _shutdown.Token) ??
                    throw new InvalidOperationException(
                        "The renderer CPU work producer returned no result.");
            }
            catch (OperationCanceledException)
                when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            lock (_gate)
            {
                _hasRunning = false;
                if (_stopping)
                    return;

                if (failure is null)
                {
                    _completedKey = key;
                    _completedResult = result;
                    _hasCompleted = true;
                    CompletedCount++;
                }
                else
                {
                    _failedKey = key;
                    _lastFailure = failure;
                    _hasFailure = true;
                    FailedCount++;
                }
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
