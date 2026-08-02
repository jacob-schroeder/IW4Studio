using IW4.FastFiles.Zone;
namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Failure-isolated execution of runtime lifecycle side effects. Disposing an
/// uncommitted transaction restores every participating service snapshot.
/// </summary>
public sealed class XAssetRuntimeLifecycleTransaction : IDisposable
{
    private readonly XAssetRuntimeLifecycleDispatcher _dispatcher;
    private readonly IReadOnlyList<IXAssetRuntimeStateService> _services;
    private readonly IReadOnlyList<IXAssetRuntimeStateSnapshot> _snapshots;
    private bool _completed;
    private bool _hasUnresolvedReplacement;

    internal XAssetRuntimeLifecycleTransaction(
        XAssetRuntimeLifecycleDispatcher dispatcher,
        IEnumerable<IXAssetRuntimeStateService> services)
    {
        _dispatcher = dispatcher;
        IXAssetRuntimeStateService[] copiedServices = services.ToArray();
        _services = Array.AsReadOnly(copiedServices);
        _snapshots = Array.AsReadOnly(
            copiedServices.Select(service => service.CaptureSnapshot()).ToArray());
    }

    public bool HasUnresolvedReplacement => _hasUnresolvedReplacement;

    public void ReleaseRuntimeState(XAssetReleaseContext context)
    {
        ThrowIfCompleted();
        _dispatcher.ReleaseRuntimeState(context);
    }

    public XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ThrowIfCompleted();
        XAssetReplacementDecision decision = _dispatcher.ReplaceRuntimeState(context);
        if (decision == XAssetReplacementDecision.Unresolved)
            _hasUnresolvedReplacement = true;

        return decision;
    }

    public void RetirePoolAllocation(XAssetPoolFreeContext context)
    {
        ThrowIfCompleted();
        _dispatcher.RetirePoolAllocation(context);
    }

    public void Commit()
    {
        ThrowIfCompleted();
        if (_hasUnresolvedReplacement)
        {
            throw new InvalidOperationException(
                "Cannot commit an XAsset lifecycle transaction with an unresolved replacement decision.");
        }

        _completed = true;
        _dispatcher.CompleteTransaction(this);
    }

    public void Dispose()
    {
        if (_completed)
            return;

        try
        {
            for (int index = _services.Count - 1; index >= 0; index--)
                _services[index].RestoreSnapshot(_snapshots[index]);
        }
        finally
        {
            _completed = true;
            _dispatcher.CompleteTransaction(this);
        }
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
            throw new ObjectDisposedException(nameof(XAssetRuntimeLifecycleTransaction));
    }
}
