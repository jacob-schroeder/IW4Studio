using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.ComWorld;

public sealed class ComPrimaryLight
{
    public const int SerializedSize = 0x44;

    public int Offset { get; init; }

    // 0x00..0x03: ComPrimaryLight scalar header.
    public GfxLightType Type { get; init; }
    public byte CanUseShadowMapRaw { get; init; }
    public bool CanUseShadowMap => CanUseShadowMapRaw != 0;
    public byte Exponent { get; init; }
    public byte Unused { get; init; }

    // 0x04..0x3F: primary-light cull, shadow, and light-grid parameters.
    public Vec3 Color { get; init; }
    public Vec3 Dir { get; init; }
    public Vec3 Origin { get; init; }
    public float Radius { get; init; }
    public float CosHalfFovOuter { get; init; }
    public float CosHalfFovInner { get; init; }
    public float CosHalfFovExpanded { get; init; }
    public float RotationLimit { get; init; }
    public float TranslationLimit { get; init; }

    // 0x40: XString light-definition name.
    public XPointer<string> DefNamePointer { get; init; }
    public string? DefName { get; init; }
}
