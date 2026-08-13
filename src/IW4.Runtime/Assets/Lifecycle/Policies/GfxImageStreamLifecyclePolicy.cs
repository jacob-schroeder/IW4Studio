using IW4.FastFiles.Zone;
using IW4.Assets.Assets.Image;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Runtime.Assets.Lifecycle.Policies;

/// <summary>
/// Maintains GfxImage header, stream-part, card-memory, and side-record state
/// during release, replacement, and pool retirement.
/// </summary>
public sealed class GfxImageStreamLifecyclePolicy : XAssetRuntimeLifecyclePolicyBase
{
    private static readonly IReadOnlyCollection<XAssetType> SupportedTypes =
        Array.AsReadOnly(new[] { XAssetType.Image });

    private readonly IGfxImageRuntimeState _state;
    private readonly IReadOnlyCollection<IXAssetRuntimeStateService> _stateServices;

    public GfxImageStreamLifecyclePolicy(IGfxImageRuntimeState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stateServices = Array.AsReadOnly(new IXAssetRuntimeStateService[] { state });
    }

    public override IReadOnlyCollection<XAssetType> AssetTypes => SupportedTypes;

    public override IReadOnlyCollection<IXAssetRuntimeStateService> StateServices =>
        _stateServices;

    public override void ReleaseRuntimeState(XAssetReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ApplyRelease(context.Allocation, context.Name);
    }

    public override XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_state.TryGet(context.SourceAllocation, out GfxImageRuntimeRecord? source) ||
            !_state.TryGet(context.DestinationAllocation, out GfxImageRuntimeRecord? destination) ||
            source is null ||
            destination is null)
        {
            return XAssetReplacementDecision.Unresolved;
        }

        if (context.Mode is 1 or 3 &&
            source.Header.Cached != GfxImageCached.No &&
            destination.Header.Cached != GfxImageCached.No)
        {
            if (!source.IsSideRecordAuthoritative ||
                !destination.IsSideRecordAuthoritative)
            {
                return XAssetReplacementDecision.Unresolved;
            }

            if (source.SideRecord.ContentEquals(destination.SideRecord))
            {
                // Equal side records preserve the destination projection while
                // allowing the caller to update canonical name identity.
                return XAssetReplacementDecision.KeepDestinationWithSourceName;
            }
        }

        if (context.Mode != 0)
        {
            ApplyRelease(context.DestinationAllocation, context.Name);
            destination = GetRequired(context.DestinationAllocation, context.Name);
        }

        if (context.Mode == 2)
        {
            _state.Set(
                context.SourceAllocation,
                source with
                {
                    SideRecord = destination.SideRecord.Copy(),
                    IsSideRecordAuthoritative = destination.IsSideRecordAuthoritative,
                    AuxiliaryWord = destination.AuxiliaryWord
                });
            _state.Set(
                context.DestinationAllocation,
                destination with
                {
                    SideRecord = source.SideRecord.Copy(),
                    IsSideRecordAuthoritative = source.IsSideRecordAuthoritative,
                    AuxiliaryWord = source.AuxiliaryWord
                });
        }
        else
        {
            _state.Set(
                context.DestinationAllocation,
                destination with
                {
                    SideRecord = source.SideRecord.Copy(),
                    IsSideRecordAuthoritative = source.IsSideRecordAuthoritative,
                    AuxiliaryWord = source.AuxiliaryWord
                });
        }

        return XAssetReplacementDecision.CopySource;
    }

    public override void RetirePoolAllocation(XAssetPoolFreeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Pool retirement applies release semantics before zeroing the indexed
        // 0x50-byte side record.
        ApplyRelease(context.Allocation, context.Name);
        GfxImageRuntimeRecord current = GetRequired(context.Allocation, context.Name);
        _state.Set(
            context.Allocation,
            current with
            {
                SideRecord = GfxImageRuntimeSideRecord.Zero(),
                IsSideRecordAuthoritative = true
            });
    }

    private void ApplyRelease(
        XAssetRuntimeAllocationKey allocation,
        string assetName)
    {
        GfxImageRuntimeRecord current = GetRequired(allocation, assetName);
        GfxImageRuntimeHeaderState header = current.Header;

        if (header.Cached != GfxImageCached.No)
        {
            if (header.CardMemory != 0)
                _state.ReleaseFirstOverlappingRange(header.Pixels, header.CardMemory);

            if (header.Pixels != 0)
            {
                header = header with
                {
                    CardMemory = 0,
                    BaseWidth = 1,
                    BaseHeight = 1,
                    BaseLevelCount = 1,
                    Pixels = 0
                };
            }
        }

        _state.Set(
            allocation,
            current with
            {
                Header = header,
                StreamPart0Marked = false,
                StreamPart1Marked = false,
                StreamPart2Marked = false,
                StreamPart3Marked = false,
                CardMemoryMarked =
                    header.Cached == GfxImageCached.No &&
                    current.CardMemoryMarked
            });
    }

    private GfxImageRuntimeRecord GetRequired(
        XAssetRuntimeAllocationKey allocation,
        string assetName)
    {
        if (_state.TryGet(allocation, out GfxImageRuntimeRecord? record) && record is not null)
            return record;

        throw new InvalidOperationException(
            $"GfxImage '{assetName}' has no indexed runtime state for {allocation}.");
    }
}
