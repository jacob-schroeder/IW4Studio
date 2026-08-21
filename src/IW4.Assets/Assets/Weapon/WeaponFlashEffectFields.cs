using IW4.FastFiles.Pointers;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponFlashEffectFields
{
    public XPointer<FxEffectDefAsset> ViewPointer { get; init; }
    public FxEffectDefAsset? View { get; init; }
    public XPointer<FxEffectDefAsset> WorldPointer { get; init; }
    public FxEffectDefAsset? World { get; init; }
}
