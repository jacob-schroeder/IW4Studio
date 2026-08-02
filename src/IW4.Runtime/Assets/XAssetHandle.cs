using IW4.Assets.Zone;
using IW4.Assets.Assets;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.Assets;

/// <summary>
/// Stable reference to a canonical XAsset slot. The provider behind the slot
/// may change when a zone is retired, so callers must resolve the handle at
/// the point of use instead of retaining a provider object indefinitely.
/// </summary>
public readonly record struct XAssetHandle<TAsset>(XAssetPoolAddress Address)
    where TAsset : BaseAsset
{
    public bool IsNone => Address.RawValue == 0;

    public override string ToString() =>
        IsNone ? $"XAssetHandle<{typeof(TAsset).Name}>:<none>" :
        $"XAssetHandle<{typeof(TAsset).Name}>:{Address}";
}
