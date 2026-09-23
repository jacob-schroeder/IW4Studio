using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.XModel;

public sealed record XModelCollSurf(Bounds Bounds, int BoneIndex, int Contents, int SurfaceFlags)
{
    public const int SerializedSize = 0x24;
}
