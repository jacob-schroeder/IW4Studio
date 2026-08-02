using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class Sunflare
{
    public const int SerializedSize = 0x60;

    public uint HasValidData { get; init; }
    public XPointer<MaterialAsset> SpriteMaterialPointer { get; init; }
    public MaterialAsset? SpriteMaterial { get; init; }
    public MaterialAsset? SpriteMaterialIncomingDefinition { get; init; }
    public XPointer<MaterialAsset> FlareMaterialPointer { get; init; }
    public MaterialAsset? FlareMaterial { get; init; }
    public MaterialAsset? FlareMaterialIncomingDefinition { get; init; }
    public float SpriteSize { get; init; }
    public float FlareMinSize { get; init; }
    public float FlareMinDot { get; init; }
    public float FlareMaxSize { get; init; }
    public float FlareMaxDot { get; init; }
    public float FlareMaxAlpha { get; init; }
    public int FlareFadeInTime { get; init; }
    public int FlareFadeOutTime { get; init; }
    public float BlindMinDot { get; init; }
    public float BlindMaxDot { get; init; }
    public float BlindMaxDarken { get; init; }
    public int BlindFadeInTime { get; init; }
    public int BlindFadeOutTime { get; init; }
    public float GlareMinDot { get; init; }
    public float GlareMaxDot { get; init; }
    public float GlareMaxLighten { get; init; }
    public int GlareFadeInTime { get; init; }
    public int GlareFadeOutTime { get; init; }
    public IReadOnlyList<float> SunFxPosition { get; init; } = [];
}
