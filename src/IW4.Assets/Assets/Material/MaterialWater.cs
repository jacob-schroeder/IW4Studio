using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

public sealed class MaterialWater
{
    public const int SerializedSize = 0x48;

    public MaterialWaterWritable Writable { get; init; }
    public XPointerReference H0XPointer { get; init; }
    public XPointerReference H0YPointer { get; init; }
    public XPointerReference WTermPointer { get; init; }
    public int M { get; init; }
    public int N { get; init; }
    public float Lx { get; init; }
    public float Lz { get; init; }
    public float Gravity { get; init; }
    public float WindVelocity { get; init; }
    public MaterialVec2 WindDirection { get; init; }
    public float Amplitude { get; init; }
    public MaterialVec4 CodeConstant { get; init; }
    public XPointer<GfxImageAsset> ImagePointer { get; init; }
    public IReadOnlyList<float> H0X { get; init; } = [];
    public IReadOnlyList<float> H0Y { get; init; } = [];
    public IReadOnlyList<float> WTerm { get; init; } = [];
    public GfxImageAsset? Image { get; init; }
}
