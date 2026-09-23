namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Identifies which native-style typed-pool allocation owns runtime side
/// state. A stable slot survives fallback promotion; a provider allocation is
/// retired after its state is copied into that stable slot.
/// </summary>
public enum XAssetRuntimeAllocationKind
{
    StableSlot,
    Provider
}
