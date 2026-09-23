using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using ModelBounds = IW4.Game.Math.Bounds;
using ModelVec2 = IW4.Game.Math.Vec2;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Game.Assets.ColMap;

public sealed class DynEntityDef
{
    public const int SerializedSize = 0x5C;

    public int Type { get; init; }
    public GfxPlacement Pose { get; init; } = new();
    public XPointer<XModelAsset> XModelPointer { get; init; }
    public XModelAsset? XModel { get; init; }
    public ushort BrushModel { get; init; }
    public ushort PhysicsBrushModel { get; init; }
    public XPointer<FxEffectDefAsset> DestroyFxPointer { get; init; }
    public FxEffectDefAsset? DestroyFx { get; init; }
    public XPointer<PhysPresetAsset> PhysPresetPointer { get; init; }
    public PhysPresetAsset? PhysPreset { get; init; }
    public int Health { get; init; }
    public PhysMass Mass { get; init; } = new();
    public int Contents { get; init; }
}
