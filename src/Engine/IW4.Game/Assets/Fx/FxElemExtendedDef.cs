using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

public sealed class FxElemExtendedDef
{
    public FxElemExtendedDefKind Kind { get; init; }
    public FxTrailDef? TrailDef { get; init; }
    public FxSparkFountainDef? SparkFountainDef { get; init; }
    public byte? DefaultBytePayload { get; init; }
}
