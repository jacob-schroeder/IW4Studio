using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxPackedPlacement
{
    public const int SerializedSize = 0x1C;

    public IReadOnlyList<float> Origin { get; init; } = [];
    public IReadOnlyList<uint> PackedAxis { get; init; } = [];
    public float Scale { get; init; }
}
