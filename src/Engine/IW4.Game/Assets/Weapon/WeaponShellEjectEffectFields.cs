using IW4.Game.Pointers;
using FxEffectDefAsset = IW4.Game.Assets.Fx.FxEffectDefAsset;

namespace IW4.Game.Assets.Weapon;

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
