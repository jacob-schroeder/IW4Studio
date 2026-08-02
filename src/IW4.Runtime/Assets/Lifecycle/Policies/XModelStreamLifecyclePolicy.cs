using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Runtime.Assets.Lifecycle.Policies;

/// <summary>
/// Maintains allocation-keyed XModel stream state during release,
/// replacement, and pool retirement.
/// </summary>
public sealed class XModelStreamLifecyclePolicy : XAssetRuntimeLifecyclePolicyBase
{
    private static readonly IReadOnlyCollection<XAssetType> SupportedTypes =
        Array.AsReadOnly(new[] { XAssetType.XModel });

    private readonly IXModelStreamRuntimeState _state;
    private readonly IReadOnlyCollection<IXAssetRuntimeStateService> _stateServices;

    public XModelStreamLifecyclePolicy(IXModelStreamRuntimeState state)
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
        XModelStreamRuntimeRecord current = GetRequired(context.Allocation, context.Name);
        _state.Set(context.Allocation, current with { StreamMarked = false });
    }

    public override XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        XModelStreamRuntimeRecord source = GetRequired(context.SourceAllocation, context.Name);
        XModelStreamRuntimeRecord destination = GetRequired(context.DestinationAllocation, context.Name);

        if (context.Mode == 2)
        {
            _state.Set(
                context.SourceAllocation,
                source with
                {
                    Word0 = destination.Word0,
                    Word1 = destination.Word1,
                    Word2 = destination.Word2,
                    AuxiliaryWord = destination.AuxiliaryWord
                });
            _state.Set(
                context.DestinationAllocation,
                destination with
                {
                    Word0 = source.Word0,
                    Word1 = source.Word1,
                    Word2 = source.Word2,
                    AuxiliaryWord = source.AuxiliaryWord
                });
        }
        else
        {
            _state.Set(
                context.DestinationAllocation,
                destination with
                {
                    Word0 = source.Word0,
                    Word1 = source.Word1,
                    Word2 = source.Word2,
                    AuxiliaryWord = source.AuxiliaryWord,
                    StreamMarked = true
                });
        }

        return XAssetReplacementDecision.CopySource;
    }

    public override void RetirePoolAllocation(XAssetPoolFreeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        XModelStreamRuntimeRecord current = GetRequired(context.Allocation, context.Name);

        // Pool retirement clears the first two words and the stream marker
        // after applying release semantics.
        _state.Set(
            context.Allocation,
            current with
            {
                Word0 = 0,
                Word1 = 0,
                StreamMarked = false
            });
    }

    private XModelStreamRuntimeRecord GetRequired(
        XAssetRuntimeAllocationKey allocation,
        string assetName)
    {
        if (_state.TryGet(allocation, out XModelStreamRuntimeRecord record))
            return record;

        throw new InvalidOperationException(
            $"XModel '{assetName}' has no indexed runtime state for {allocation}.");
    }
}
