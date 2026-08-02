using IW4.FastFiles.Zone;
using IW4.Runtime.Assets.Lifecycle.State;

namespace IW4.Runtime.Assets.Lifecycle.Policies;

/// <summary>
/// Prevents GfxWorld release while the BSP is active. Renderer detachment is a
/// higher-level prerequisite; this policy performs no GPU teardown.
/// </summary>
public sealed class GfxWorldGuardLifecyclePolicy : XAssetRuntimeLifecyclePolicyBase
{
    public const string InUseMessage = "Cannot unload bsp while it is in use";

    private static readonly IReadOnlyCollection<XAssetType> SupportedTypes =
        Array.AsReadOnly(new[] { XAssetType.GfxMap });

    private readonly IGfxWorldRuntimeState _state;
    private readonly IReadOnlyCollection<IXAssetRuntimeStateService> _stateServices;

    public GfxWorldGuardLifecyclePolicy(IGfxWorldRuntimeState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stateServices = Array.AsReadOnly(new IXAssetRuntimeStateService[] { state });
    }

    public override IReadOnlyCollection<XAssetType> AssetTypes => SupportedTypes;

    public override IReadOnlyCollection<IXAssetRuntimeStateService> StateServices =>
        _stateServices;

    public override void ValidateRelease(XAssetReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_state.IsBspInUse)
            throw new InvalidOperationException(InUseMessage);
    }

    public override void ReleaseRuntimeState(XAssetReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _state.ClearTextureState(context.SlotAddress);
    }

    public override XAssetReplacementDecision ReplaceRuntimeState(
        XAssetReplacementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        throw new InvalidOperationException(
            $"GfxMap '{context.Name}' is a native capacity-one singleton and cannot promote a fallback provider.");
    }
}
