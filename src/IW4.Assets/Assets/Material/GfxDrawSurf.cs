using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

/// <summary>
/// PS3 IW4 draw-surface routing key. The PS3 layout gives
/// <see cref="MaterialSortedIndex"/> 13 bits; correlated desktop layouts use
/// 12 bits and must not be used to unpack this value.
/// </summary>
public readonly record struct GfxDrawSurf(ulong Packed)
{
    public const int ObjectIdShift = 0;
    public const int ObjectIdMask = 0xffff;
    public const int ReflectionProbeIndexShift = 16;
    public const int ReflectionProbeIndexMask = 0xff;
    public const int HasGfxEntityIndexShift = 24;
    public const int CustomIndexShift = 25;
    public const int CustomIndexMask = 0x1f;
    public const int MaterialSortedIndexShift = 30;
    public const int MaterialSortedIndexMask = 0x1fff;
    public const int PrepassShift = 43;
    public const int PrepassMask = 0x03;
    public const int UsesHeroLightingShift = 45;
    public const int SceneLightIndexShift = 46;
    public const int SceneLightIndexMask = 0xff;
    public const ulong SceneLightIndexPackedMask =
        (ulong)SceneLightIndexMask << SceneLightIndexShift;
    public const int SurfaceTypeShift = 54;
    public const int SurfaceTypeMask = 0x0f;
    public const int PrimarySortKeyShift = 58;
    public const int PrimarySortKeyMask = 0x3f;

    public ushort ObjectId => (ushort)Extract(ObjectIdShift, ObjectIdMask);

    public byte ReflectionProbeIndex =>
        (byte)Extract(ReflectionProbeIndexShift, ReflectionProbeIndexMask);

    public bool HasGfxEntityIndex => Extract(HasGfxEntityIndexShift, 1) != 0;

    public byte CustomIndex => (byte)Extract(CustomIndexShift, CustomIndexMask);

    public int MaterialSortedIndex =>
        (int)Extract(MaterialSortedIndexShift, MaterialSortedIndexMask);

    public MaterialPrepassType Prepass =>
        (MaterialPrepassType)Extract(PrepassShift, PrepassMask);

    public bool UsesHeroLighting => Extract(UsesHeroLightingShift, 1) != 0;

    public byte SceneLightIndex =>
        (byte)Extract(SceneLightIndexShift, SceneLightIndexMask);

    public GfxDrawSurfSurfaceType SurfaceType =>
        (GfxDrawSurfSurfaceType)Extract(SurfaceTypeShift, SurfaceTypeMask);

    public MaterialSortKey PrimarySortKey =>
        (MaterialSortKey)Extract(PrimarySortKeyShift, PrimarySortKeyMask);

    public GfxDrawSurf WithSceneLightIndex(byte sceneLightIndex) =>
        new(
            (Packed & ~SceneLightIndexPackedMask) |
            ((ulong)sceneLightIndex << SceneLightIndexShift));

    private ulong Extract(int shift, int mask) =>
        (Packed >> shift) & (uint)mask;
}
