using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.XModel;

public sealed class XRigidVertList
{
    public const int SerializedSize = 0x0c;

    public ushort BoneOffset { get; init; }
    public ushort VertCount { get; init; }
    public ushort TriOffset { get; init; }
    public ushort TriCount { get; init; }
    public XPointer<XSurfaceCollisionTree> CollisionTreePointer { get; init; }
    public XSurfaceCollisionTree? CollisionTree { get; init; }
}
