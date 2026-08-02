using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxElemDef
{
    public const int SerializedSize = 0xFC;

    public int Offset { get; init; }
    public int Flags { get; init; }
    public required FxSpawnDef Spawn { get; init; }
    public required FxFloatRange SpawnRange { get; init; }
    public required FxFloatRange FadeInRange { get; init; }
    public required FxFloatRange FadeOutRange { get; init; }
    public float SpawnFrustumCullRadius { get; init; }
    public required FxIntRange SpawnDelayMsec { get; init; }
    public required FxIntRange LifeSpanMsec { get; init; }
    public IReadOnlyList<FxFloatRange> SpawnOrigin { get; init; } = [];
    public required FxFloatRange SpawnOffsetRadius { get; init; }
    public required FxFloatRange SpawnOffsetHeight { get; init; }
    public IReadOnlyList<FxFloatRange> SpawnAngles { get; init; } = [];
    public IReadOnlyList<FxFloatRange> AngularVelocity { get; init; } = [];
    public required FxFloatRange InitialRotation { get; init; }
    public required FxFloatRange Gravity { get; init; }
    public required FxFloatRange ReflectionFactor { get; init; }
    public required FxElemAtlas Atlas { get; init; }
    public FxElemType ElemType { get; init; }
    public byte VisualCount { get; init; }
    public byte VelIntervalCount { get; init; }
    public byte VisStateIntervalCount { get; init; }
    public int VelSampleCount => VelIntervalCount + 1;
    public int VisStateSampleCount => VisStateIntervalCount + 1;
    public XPointer<FxElemVelStateSample[]> VelSamplesPointer { get; init; }
    public IReadOnlyList<FxElemVelStateSample> VelSamples { get; init; } = [];
    public XPointer<FxElemVisStateSample[]> VisSamplesPointer { get; init; }
    public IReadOnlyList<FxElemVisStateSample> VisSamples { get; init; } = [];
    public FxElemDefVisuals Visuals { get; init; } = new();
    public XPointer<FxElemDefVisuals[]>? VisualArrayPointer { get; init; }
    public IReadOnlyList<FxElemDefVisuals> VisualArray { get; init; } = [];
    public XPointer<FxElemMarkVisuals[]>? MarkVisualArrayPointer { get; init; }
    public IReadOnlyList<FxElemMarkVisuals> MarkVisualArray { get; init; } = [];
    public required Bounds CollBounds { get; init; }
    public FxEffectDefRef EffectOnImpact { get; init; } = new();
    public FxEffectDefRef EffectOnDeath { get; init; } = new();
    public FxEffectDefRef EffectEmitted { get; init; } = new();
    public required FxFloatRange EmitDist { get; init; }
    public required FxFloatRange EmitDistVariance { get; init; }
    public XPointer<FxElemExtendedDef> ExtendedPointer { get; init; }
    public FxElemExtendedDef? Extended { get; init; }
    public byte SortOrder { get; init; }
    public byte LightingFrac { get; init; }
    public byte UseItemClip { get; init; }
    public byte FadeInfo { get; init; }
}
