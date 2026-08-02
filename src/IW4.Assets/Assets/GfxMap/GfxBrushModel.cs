using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxBrushModel
{
    public const int SerializedSize = 0x38;

    public IReadOnlyList<float> WritableMins { get; init; } = [];
    public IReadOnlyList<float> WritableMaxs { get; init; } = [];
    public IReadOnlyList<float> BoundsMins { get; init; } = [];
    public IReadOnlyList<float> BoundsMaxs { get; init; } = [];
    // 0x30: radius.
    public float Radius { get; init; }
    // 0x34: surface-sort prefix.
    public ushort SurfaceCount { get; init; }
    // 0x36: starting surface index.
    public ushort StartSurfIndex { get; init; }
}
