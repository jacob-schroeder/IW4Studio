using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Type-safe composite over managed asset lifecycle policies.
/// </summary>
public sealed class XAssetRuntimeLifecycleDispatcher
{
    private readonly Dictionary<XAssetType, IXAssetRuntimeLifecyclePolicy> _policies;
    private readonly IXAssetRuntimeLifecyclePolicy? _defaultPolicy;
    private XAssetRuntimeLifecycleTransaction? _activeTransaction;

    public XAssetRuntimeLifecycleDispatcher(
        IEnumerable<IXAssetRuntimeLifecyclePolicy> policies,
        IXAssetRuntimeLifecyclePolicy? defaultPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(policies);
        _defaultPolicy = defaultPolicy;
        _policies = new Dictionary<XAssetType, IXAssetRuntimeLifecyclePolicy>();

        foreach (IXAssetRuntimeLifecyclePolicy policy in policies)
        {
            ArgumentNullException.ThrowIfNull(policy);
            foreach (XAssetType assetType in policy.AssetTypes)
            {
                if (!_policies.TryAdd(assetType, policy))
                {
                    throw new ArgumentException(
                        $"More than one runtime lifecycle policy handles {assetType}.",
                        nameof(policies));
                }
            }
        }

        StateServices = Array.AsReadOnly(
            _policies.Values
                .Concat(_defaultPolicy is null
                    ? Array.Empty<IXAssetRuntimeLifecyclePolicy>()
                    : new[] { _defaultPolicy })
                .SelectMany(policy => policy.StateServices)
                .Distinct<IXAssetRuntimeStateService>(ReferenceEqualityComparer.Instance)
                .ToArray());
    }

    public IReadOnlyCollection<IXAssetRuntimeStateService> StateServices { get; }

    public void ValidateRelease(XAssetReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GetPolicy(context.AssetType).ValidateRelease(context);
    }

    public XAssetRuntimeLifecycleTransaction BeginTransaction()
    {
        if (_activeTransaction is not null)
        {
            throw new InvalidOperationException(
                "The XAsset runtime lifecycle dispatcher already has an active transaction.");
        }

        var transaction = new XAssetRuntimeLifecycleTransaction(this, StateServices);
        _activeTransaction = transaction;
        return transaction;
    }

    internal void ReleaseRuntimeState(XAssetReleaseContext context)
    {
        IXAssetRuntimeLifecyclePolicy policy = GetPolicy(context.AssetType);
        policy.ValidateRelease(context);
        policy.ReleaseRuntimeState(context);
    }

    internal XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context) =>
        GetPolicy(context.AssetType).ReplaceRuntimeState(context);

    internal void RetirePoolAllocation(XAssetPoolFreeContext context) =>
        GetPolicy(context.AssetType).RetirePoolAllocation(context);

    internal void CompleteTransaction(XAssetRuntimeLifecycleTransaction transaction)
    {
        if (!ReferenceEquals(_activeTransaction, transaction))
            throw new InvalidOperationException("Lifecycle transaction ownership is inconsistent.");

        _activeTransaction = null;
    }

    private IXAssetRuntimeLifecyclePolicy GetPolicy(XAssetType assetType)
    {
        if (_policies.TryGetValue(assetType, out IXAssetRuntimeLifecyclePolicy? policy))
            return policy;
        if (_defaultPolicy is not null)
            return _defaultPolicy;

        throw new InvalidOperationException(
            $"No managed runtime lifecycle policy is registered for {assetType}.");
    }
}
