namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Coalesces callers onto one active or successful scene warmup while allowing
/// a later request to replace a completed faulted or canceled attempt.
/// </summary>
internal sealed class RetryableRenderWarmup
{
    private readonly object _gate = new();
    private Task<RenderViewSceneBuildResult>? _current;

    internal Task<RenderViewSceneBuildResult> GetOrCreate(
        Func<Task<RenderViewSceneBuildResult>> factory,
        out bool created)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate)
        {
            if (_current is not null &&
                !_current.IsFaulted &&
                !_current.IsCanceled)
            {
                created = false;
                return _current;
            }

            Task<RenderViewSceneBuildResult> replacement =
                factory() ??
                throw new InvalidOperationException(
                    "The render-warmup factory returned no task.");
            _current = replacement;
            created = true;
            return replacement;
        }
    }

    internal bool IsCurrent(
        Task<RenderViewSceneBuildResult> candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
            return ReferenceEquals(_current, candidate);
    }
}
