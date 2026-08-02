using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Physics;

public sealed class PhysGeomInfo
{
    public const int SerializedSize = 0x44;

    public XPointer<BrushWrapper> BrushWrapperPointer { get; init; }
    public BrushWrapper? BrushWrapper { get; init; }
    public int Type { get; init; }
    public IReadOnlyList<Vec3> Orientation { get; init; } = [];
    public Bounds Bounds { get; init; } = new();
}
