using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxElemExtendedDef
{
    public FxElemExtendedDefKind Kind { get; init; }
    public FxTrailDef? TrailDef { get; init; }
    public FxSparkFountainDef? SparkFountainDef { get; init; }
    public byte? DefaultBytePayload { get; init; }
}
