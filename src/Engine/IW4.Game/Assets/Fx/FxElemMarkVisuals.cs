using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

public sealed class FxElemMarkVisuals
{
    public const int SerializedSize = 0x08;

    public int Offset { get; init; }
    public XPointer<MaterialAsset> Material0Pointer { get; init; }
    public MaterialAsset? Material0 { get; init; }
    public XPointer<MaterialAsset> Material1Pointer { get; init; }
    public MaterialAsset? Material1 { get; init; }
}
