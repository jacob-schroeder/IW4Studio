using IW4.Game.Math;
namespace IW4.Game.Assets.FxMap;

public readonly record struct FxSpatialFrame(FxQuat Quat, Vec3 Origin)
{
    public const int SerializedSize = 0x1C;
}
