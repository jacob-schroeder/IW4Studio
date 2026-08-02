using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

public interface IXAssetRuntimeLifecyclePolicy
{
    IReadOnlyCollection<XAssetType> AssetTypes { get; }

    IReadOnlyCollection<IXAssetRuntimeStateService> StateServices { get; }

    void ValidateRelease(XAssetReleaseContext context);

    void ReleaseRuntimeState(XAssetReleaseContext context);

    XAssetReplacementDecision ReplaceRuntimeState(XAssetReplacementContext context);

    void RetirePoolAllocation(XAssetPoolFreeContext context);
}
