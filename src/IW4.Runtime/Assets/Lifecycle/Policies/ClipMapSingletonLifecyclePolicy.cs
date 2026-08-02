using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Runtime.Assets.Lifecycle.Policies;

/// <summary>
/// Resets the ClipMap singleton during release. Pool retirement has no
/// additional managed side effect.
/// </summary>
public sealed class ClipMapSingletonLifecyclePolicy : XAssetRuntimeLifecyclePolicyBase
{
    private static readonly IReadOnlyCollection<XAssetType> SupportedTypes =
        Array.AsReadOnly(new[] { XAssetType.ColMapSp, XAssetType.ColMapMp });

    private readonly IClipMapRuntimeState _state;
    private readonly IReadOnlyCollection<IXAssetRuntimeStateService> _stateServices;

    public ClipMapSingletonLifecyclePolicy(IClipMapRuntimeState state)
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
        _state.ResetPreservingIdentity();
    }

    public override XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        throw new InvalidOperationException(
            $"{context.AssetType} '{context.Name}' is a native capacity-one singleton and cannot promote a fallback provider.");
    }
}
