using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.XModel;

public sealed record XBoneInfo(Bounds Bounds, float RadiusSquared)
{
    public const int SerializedSize = 0x1c;
}
