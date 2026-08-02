using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

public sealed record XBoneInfo(Bounds Bounds, float RadiusSquared)
{
    public const int SerializedSize = 0x1c;
}
