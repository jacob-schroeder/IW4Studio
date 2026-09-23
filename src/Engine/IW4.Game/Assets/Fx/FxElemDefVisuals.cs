using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

public sealed class FxElemDefVisuals
{
    public const int SerializedSize = 0x04;

    public int Offset { get; init; }
    public FxElemVisual? Visual { get; init; }
    public FxMaterialVisual? Material => Visual as FxMaterialVisual;
    public FxModelVisual? Model => Visual as FxModelVisual;
    public FxEffectVisual? Effect => Visual as FxEffectVisual;
    public FxSoundVisual? Sound => Visual as FxSoundVisual;
    public FxNoChildVisual? NoChild => Visual as FxNoChildVisual;
}
