using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec2 = IW4.Assets.Math.Vec2;
using ModelVec3 = IW4.Assets.Math.Vec3;

namespace IW4.Assets.Assets.ColMap;

public sealed class DynEntityDef
{
    public const int SerializedSize = 0x5C;

    public int Type { get; init; }
    public GfxPlacement Pose { get; init; } = new();
    public XPointer<XModelAsset> XModelPointer { get; init; }
    public XModelAsset? XModel { get; init; }
    public XModelAsset? XModelIncomingDefinition { get; init; }
    public ushort BrushModel { get; init; }
    public ushort PhysicsBrushModel { get; init; }
    public XPointer<FxEffectDefAsset> DestroyFxPointer { get; init; }
    public FxEffectDefAsset? DestroyFx { get; init; }
    public XPointer<PhysPresetAsset> PhysPresetPointer { get; init; }
    public PhysPresetAsset? PhysPreset { get; init; }
    public PhysPresetAsset? PhysPresetIncomingDefinition { get; init; }
    public int Health { get; init; }
    public PhysMass Mass { get; init; } = new();
    public int Contents { get; init; }
}
