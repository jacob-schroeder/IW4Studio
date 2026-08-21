using IW4.FastFiles.Pointers;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponShellEjectEffectFields
{
    public XPointer<FxEffectDefAsset> ViewPointer { get; init; }
    public FxEffectDefAsset? View { get; init; }
    public XPointer<FxEffectDefAsset> WorldPointer { get; init; }
    public FxEffectDefAsset? World { get; init; }
    public XPointer<FxEffectDefAsset> ViewLastShotPointer { get; init; }
    public FxEffectDefAsset? ViewLastShot { get; init; }
    public XPointer<FxEffectDefAsset> WorldLastShotPointer { get; init; }
    public FxEffectDefAsset? WorldLastShot { get; init; }
}
