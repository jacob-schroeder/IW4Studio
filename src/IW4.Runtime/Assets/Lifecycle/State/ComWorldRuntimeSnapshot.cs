namespace IW4.Runtime.Assets.Lifecycle.State;

internal sealed record ComWorldRuntimeSnapshot(ComWorldRuntimeRecord State)
    : IXAssetRuntimeStateSnapshot;
