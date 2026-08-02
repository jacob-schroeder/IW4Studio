using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Runtime.Assets.Lifecycle.Policies;

/// <summary>
/// Resets ComWorld runtime state during release. Pool retirement has no
/// additional managed side effect.
/// </summary>
public sealed class ComWorldLifecyclePolicy : XAssetRuntimeLifecyclePolicyBase
{
    private static readonly IReadOnlyCollection<XAssetType> SupportedTypes =
        Array.AsReadOnly(new[] { XAssetType.ComMap });

    private readonly IComWorldRuntimeState _state;
    private readonly IReadOnlyCollection<IXAssetRuntimeStateService> _stateServices;

    public ComWorldLifecyclePolicy(IComWorldRuntimeState state)
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
        _state.Set(_state.State with { IsInUse = 0 });
    }

    public override XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        throw new InvalidOperationException(
            $"ComMap '{context.Name}' is a native capacity-one singleton and cannot promote a fallback provider.");
    }
}
