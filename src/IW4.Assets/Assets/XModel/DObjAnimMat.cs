using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

public sealed record DObjAnimMat(DObjQuat Quat, Vec3 Trans, float TransWeight)
{
    public const int SerializedSize = 0x20;
}
