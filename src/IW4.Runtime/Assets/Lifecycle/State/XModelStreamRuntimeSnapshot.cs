namespace IW4.Runtime.Assets.Lifecycle.State;

internal sealed record XModelStreamRuntimeSnapshot(
    Dictionary<XAssetRuntimeAllocationKey, XModelStreamRuntimeRecord> Records)
    : IXAssetRuntimeStateSnapshot;
