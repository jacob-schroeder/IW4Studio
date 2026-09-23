using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class ClipStaticModel
{
    public const int SerializedSize = 0x4C;

    public XPointer<XModelAsset> XModelPointer { get; init; }
    public XModelAsset? XModel { get; init; }
    public ModelVec3 Origin { get; init; }
    public IReadOnlyList<ModelVec3> InvScaledAxis { get; init; } = [];
    public ModelBounds Bounds { get; init; } = new();
}
