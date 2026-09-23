using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxPackedPlacement
{
    public const int SerializedSize = 0x1C;

    public IReadOnlyList<float> Origin { get; init; } = [];
    public IReadOnlyList<uint> PackedAxis { get; init; } = [];
    public float Scale { get; init; }
}
