using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets.Lifecycle;

/// <summary>
/// Explicit default for asset types without managed runtime side effects.
/// It is never used in place of a registered type-specific policy.
/// </summary>
public sealed class NoRuntimeLifecyclePolicy : XAssetRuntimeLifecyclePolicyBase
{
    private readonly IReadOnlyCollection<XAssetType> _assetTypes;

    public NoRuntimeLifecyclePolicy(IEnumerable<XAssetType> assetTypes)
    {
        ArgumentNullException.ThrowIfNull(assetTypes);
        _assetTypes = Array.AsReadOnly(assetTypes.Distinct().ToArray());
    }

    public override IReadOnlyCollection<XAssetType> AssetTypes => _assetTypes;
}
