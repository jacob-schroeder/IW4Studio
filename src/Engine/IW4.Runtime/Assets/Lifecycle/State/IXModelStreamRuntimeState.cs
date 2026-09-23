namespace IW4.Runtime.Assets.Lifecycle.State;

public interface IXModelStreamRuntimeState : IXAssetRuntimeStateService
{
    bool TryGet(
        XAssetRuntimeAllocationKey allocation,
        out XModelStreamRuntimeRecord record);

    void Set(
        XAssetRuntimeAllocationKey allocation,
        XModelStreamRuntimeRecord record);
}
