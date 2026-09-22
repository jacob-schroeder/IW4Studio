namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// UI-thread owner for one disposable render-view service generation. A
/// released generation remains owned until its asynchronous disposal has been
/// observed; callers must await that disposal before creating the next one.
/// </summary>
internal sealed class RenderViewServiceOwner<TService> : IAsyncDisposable
    where TService : class, IAsyncDisposable
{
    private TService? _service;
    private Task? _disposalTask;

    internal bool IsEmpty => _service is null && _disposalTask is null;

    internal TService GetOrCreate(Func<TService> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (_disposalTask is not null)
        {
            throw new InvalidOperationException(
                "The previous render-view service must finish shutting down before a replacement is created.");
        }

        return _service ??= create()
            ?? throw new InvalidOperationException(
                "The render-view service factory returned no service.");
    }

    internal Task BeginDisposal()
    {
        if (_service is not { } service)
            return _disposalTask ?? Task.CompletedTask;
        if (_disposalTask is not null)
        {
            throw new InvalidOperationException(
                "A render-view service cannot remain active while a prior generation is shutting down.");
        }

        _service = null;
        return _disposalTask = service.DisposeAsync().AsTask();
    }

    internal async Task WaitForDisposalAsync()
    {
        Task? disposalTask = _disposalTask;
        if (disposalTask is null)
            return;

        try
        {
            await disposalTask;
        }
        finally
        {
            if (ReferenceEquals(_disposalTask, disposalTask))
                _disposalTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task disposalTask = BeginDisposal();
        try
        {
            await disposalTask;
        }
        finally
        {
            if (ReferenceEquals(_disposalTask, disposalTask))
                _disposalTask = null;
        }
    }
}

/// <summary>
/// Owns the service shared by editor-created preview windows. Preview windows
/// borrow the service; the editor begins disposal only after the last borrower
/// leaves, and every destructive transition awaits that disposal before its
/// lifecycle callback is allowed to proceed.
/// </summary>
internal sealed class SharedRenderViewServiceLifecycle<TPreview, TService>
    where TPreview : class
    where TService : class, IAsyncDisposable
{
    private readonly HashSet<TPreview> _previews = [];
    private readonly RenderViewServiceOwner<TService> _serviceOwner = new();

    internal bool IsEmpty =>
        _previews.Count == 0 && _serviceOwner.IsEmpty;

    internal Task WaitForDisposalAsync() =>
        _serviceOwner.WaitForDisposalAsync();

    internal TService GetOrCreateService(Func<TService> create) =>
        _serviceOwner.GetOrCreate(create);

    internal void TrackPreview(TPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!_previews.Add(preview))
        {
            throw new InvalidOperationException(
                "The render preview is already tracked by this editor.");
        }
    }

    internal void ReleasePreview(TPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!_previews.Remove(preview) || _previews.Count != 0)
            return;

        _serviceOwner.BeginDisposal();
    }

    internal async Task ShutdownThenProceedAsync(
        Action<TPreview> closePreview,
        Action<Exception> reportFailure,
        Func<Task> proceedAsync)
    {
        ArgumentNullException.ThrowIfNull(closePreview);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(proceedAsync);

        foreach (TPreview preview in _previews.ToArray())
        {
            try
            {
                closePreview(preview);
            }
            catch (Exception exception)
            {
                reportFailure(exception);
                throw;
            }
        }
        _previews.Clear();

        try
        {
            await _serviceOwner.DisposeAsync();
        }
        catch (Exception exception)
        {
            reportFailure(exception);
            throw;
        }

        await proceedAsync();
    }
}
