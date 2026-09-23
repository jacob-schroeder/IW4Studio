using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.XModel;

public sealed record XSurfaceCollisionAabb(
    ushort MinsX,
    ushort MinsY,
    ushort MinsZ,
    ushort MaxsX,
    ushort MaxsY,
    ushort MaxsZ)
{
    public const int SerializedSize = 0x0c;
}
