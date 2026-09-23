namespace IW4.Runtime.Assets.Lifecycle.State;

public interface IGfxImageRuntimeState : IXAssetRuntimeStateService
{
    IReadOnlyList<GfxImageCardMemoryRange> AllocatedRanges { get; }

    IReadOnlyList<GfxImageCardMemoryRange> FreeRanges { get; }

    bool IsCardMemoryTableDirty { get; }

    bool TryGet(
        XAssetRuntimeAllocationKey allocation,
        out GfxImageRuntimeRecord? record);

    void Set(
        XAssetRuntimeAllocationKey allocation,
        GfxImageRuntimeRecord record);

    void AddAllocatedRange(GfxImageCardMemoryRange range);

    void AddFreeRange(GfxImageCardMemoryRange range);

    bool ReleaseFirstOverlappingRange(uint start, uint length);
}
