using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxEffectVisual : FxElemVisual
{
    public FxEffectDefRef EffectDef { get; init; } = new();
}
