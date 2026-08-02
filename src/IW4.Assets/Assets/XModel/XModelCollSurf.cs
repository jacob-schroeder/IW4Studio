using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XModel;

public sealed record XModelCollSurf(Bounds Bounds, int BoneIndex, int Contents, int SurfaceFlags)
{
    public const int SerializedSize = 0x24;
}
