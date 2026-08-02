using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

public abstract class XAssetRuntimeLifecyclePolicyBase : IXAssetRuntimeLifecyclePolicy
{
    public abstract IReadOnlyCollection<XAssetType> AssetTypes { get; }

    public virtual IReadOnlyCollection<IXAssetRuntimeStateService> StateServices =>
        Array.Empty<IXAssetRuntimeStateService>();

    public virtual void ValidateRelease(XAssetReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public virtual void ReleaseRuntimeState(XAssetReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }

    public virtual XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return XAssetReplacementDecision.CopySource;
    }

    public virtual void RetirePoolAllocation(XAssetPoolFreeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
    }
}
