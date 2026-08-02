using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

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
