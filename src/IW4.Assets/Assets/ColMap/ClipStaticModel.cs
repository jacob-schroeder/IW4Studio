using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class ClipStaticModel
{
    public const int SerializedSize = 0x4C;

    public XPointer<XModelAsset> XModelPointer { get; init; }
    public XModelAsset? XModel { get; init; }
    public XModelAsset? XModelIncomingDefinition { get; init; }
    public ModelVec3 Origin { get; init; }
    public IReadOnlyList<ModelVec3> InvScaledAxis { get; init; } = [];
    public ModelVec3 AbsMin { get; init; }
    public ModelVec3 AbsMax { get; init; }
}
