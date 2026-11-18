using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Physics;

public sealed class PhysGeomInfo
{
    public const int SerializedSize = 0x44;

    public XPointer<BrushWrapper> BrushWrapperPointer { get; init; }
    public BrushWrapper? BrushWrapper { get; init; }
    public int Type { get; init; }
    public IReadOnlyList<Vec3> Orientation { get; init; } = [];
    public Bounds Bounds { get; init; } = new();

    // PS3 collision dispatch at 0x2bf8a0 and 0x2bfa60 checks the wrapper first.
    // An authored wrapper acquires its pointer when linked.
    public PhysGeomShape EffectiveShape
    {
        get
        {
            if (BrushWrapper is not null || BrushWrapperPointer.Raw != 0)
                return PhysGeomShape.Brush;

            return Type == 1 ? PhysGeomShape.Box : PhysGeomShape.Cylinder;
        }
    }
}
