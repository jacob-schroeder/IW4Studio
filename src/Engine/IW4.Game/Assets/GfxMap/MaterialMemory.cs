using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class MaterialMemory
{
    public const int SerializedSize = 0x08;

    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
    public int Memory { get; init; }
}
