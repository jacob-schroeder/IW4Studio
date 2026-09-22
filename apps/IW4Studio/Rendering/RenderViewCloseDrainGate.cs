namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Coordinates a synchronous window-close boundary with one asynchronous
/// owned-service drain. Repeated close attempts remain cancelled until the
/// drain has either completed or reported its failure.
/// </summary>
internal sealed class RenderViewCloseDrainGate
{
    private Task? _drainTask;
    private bool _approved;

    internal bool HasPendingDrain => _drainTask is not null && !_approved;

    internal bool TryConsumeApproval()
    {
        if (!_approved)
            return false;

        _approved = false;
        _drainTask = null;
        return true;
    }

    internal Task BeginDrain(
        Func<Task> drainAsync,
        Action<Exception> reportFailure,
        Action retryClose)
    {
        ArgumentNullException.ThrowIfNull(drainAsync);
        ArgumentNullException.ThrowIfNull(reportFailure);
        ArgumentNullException.ThrowIfNull(retryClose);
        return _drainTask ??= DrainCoreAsync(
            drainAsync,
            reportFailure,
            retryClose);
    }

    private async Task DrainCoreAsync(
        Func<Task> drainAsync,
        Action<Exception> reportFailure,
        Action retryClose)
    {
        try
        {
            await drainAsync();
        }
        catch (Exception exception)
        {
            _approved = true;
            reportFailure(exception);
            return;
        }

        _approved = true;
        retryClose();
    }
}
