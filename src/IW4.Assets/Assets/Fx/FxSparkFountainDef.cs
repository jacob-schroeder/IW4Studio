using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed record FxSparkFountainDef(
    float Gravity,
    float BounceFrac,
    float BounceRand,
    float SparkSpacing,
    float SparkLength,
    int SparkCount,
    float LoopTime,
    float VelMin,
    float VelMax,
    float VelConeFrac,
    float RestSpeed,
    float BoostTime,
    float BoostFactor)
{
    public const int SerializedSize = 0x34;
}
