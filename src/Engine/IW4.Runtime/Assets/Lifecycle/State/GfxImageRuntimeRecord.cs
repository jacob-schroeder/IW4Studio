namespace IW4.Runtime.Assets.Lifecycle.State;

public sealed record GfxImageRuntimeRecord(
    GfxImageRuntimeSideRecord SideRecord,
    bool IsSideRecordAuthoritative,
    uint AuxiliaryWord,
    GfxImageRuntimeHeaderState Header,
    bool StreamPart0Marked,
    bool StreamPart1Marked,
    bool StreamPart2Marked,
    bool StreamPart3Marked,
    bool CardMemoryMarked);
