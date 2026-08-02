using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxElemMarkVisuals
{
    public const int SerializedSize = 0x08;

    public int Offset { get; init; }
    public XPointer<MaterialAsset> Material0Pointer { get; init; }
    public MaterialAsset? Material0 { get; init; }
    public MaterialAsset? IncomingMaterial0 { get; init; }
    public XPointer<MaterialAsset> Material1Pointer { get; init; }
    public MaterialAsset? Material1 { get; init; }
    public MaterialAsset? IncomingMaterial1 { get; init; }
}
