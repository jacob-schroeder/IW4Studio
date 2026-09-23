namespace IW4.Runtime.Assets.Lifecycle.State;

internal sealed record GfxImageRuntimeSnapshot(
    Dictionary<XAssetRuntimeAllocationKey, GfxImageRuntimeRecord> Records,
    GfxImageCardMemoryRange[] AllocatedRanges,
    GfxImageCardMemoryRange[] FreeRanges,
    bool IsCardMemoryTableDirty)
    : IXAssetRuntimeStateSnapshot;
