using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

public sealed class FxMaterialVisual : FxElemVisual
{
    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
}
