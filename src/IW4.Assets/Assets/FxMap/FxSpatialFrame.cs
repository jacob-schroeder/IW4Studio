namespace IW4.Assets.Assets.FxMap;

public readonly record struct FxSpatialFrame(FxQuat Quat, FxVec3 Origin)
{
    public const int SerializedSize = 0x1C;
}
