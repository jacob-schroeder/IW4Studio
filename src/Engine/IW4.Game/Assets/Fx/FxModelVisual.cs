using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

public sealed class FxModelVisual : FxElemVisual
{
    public XPointer<XModelAsset> ModelPointer { get; init; }
    public XModelAsset? Model { get; init; }
}
