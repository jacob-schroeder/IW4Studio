using IW4.Assets.Assets.Material;

namespace MapConverter.Game.IW3.PC.Techniques;

/// <summary>
/// Maps the PS3 IW4 material-technique table to its source IW3 slots. IW3 has
/// no DFOG slots, so each DFOG slot intentionally shares its base technique.
/// PS3 uses depth shadow maps and stock PS3 technique sets leave the desktop
/// color-shadow-map slot empty.
/// PS3 slots 23..26 select lower-detail lit/sun passes, not IW3 instanced
/// geometry. Reuse the base source passes and their material state rows.
/// Source-only instanced, shadow-cookie, and instanced debug slots have no
/// PS3 IW4 counterpart.
/// </summary>
internal static class Iw3TechniqueSlotMapping
{
    internal static IReadOnlyList<Iw3TechniqueSlot?> Iw4Slots { get; } =
        CreateIw4Slots();

    private static IReadOnlyList<Iw3TechniqueSlot?> CreateIw4Slots()
    {
        Iw3TechniqueSlot?[] slots =
        [
            Iw3TechniqueSlot.DepthPrepass,
            Iw3TechniqueSlot.BuildFloatZ,
            Iw3TechniqueSlot.BuildShadowmapDepth,
            null,
            Iw3TechniqueSlot.Unlit,
            Iw3TechniqueSlot.Emissive,
            Iw3TechniqueSlot.Emissive,
            Iw3TechniqueSlot.EmissiveShadow,
            Iw3TechniqueSlot.EmissiveShadow,
            Iw3TechniqueSlot.Lit,
            Iw3TechniqueSlot.Lit,
            Iw3TechniqueSlot.LitSun,
            Iw3TechniqueSlot.LitSun,
            Iw3TechniqueSlot.LitSunShadow,
            Iw3TechniqueSlot.LitSunShadow,
            Iw3TechniqueSlot.LitSpot,
            Iw3TechniqueSlot.LitSpot,
            Iw3TechniqueSlot.LitSpotShadow,
            Iw3TechniqueSlot.LitSpotShadow,
            Iw3TechniqueSlot.LitOmni,
            Iw3TechniqueSlot.LitOmni,
            Iw3TechniqueSlot.LitOmniShadow,
            Iw3TechniqueSlot.LitOmniShadow,
            Iw3TechniqueSlot.Lit,
            Iw3TechniqueSlot.Lit,
            Iw3TechniqueSlot.LitSun,
            Iw3TechniqueSlot.LitSun,
            Iw3TechniqueSlot.LightSpot,
            Iw3TechniqueSlot.LightOmni,
            Iw3TechniqueSlot.LightSpotShadow,
            Iw3TechniqueSlot.FakeLightNormal,
            Iw3TechniqueSlot.FakeLightView,
            Iw3TechniqueSlot.SunlightPreview,
            Iw3TechniqueSlot.CaseTexture,
            Iw3TechniqueSlot.WireframeSolid,
            Iw3TechniqueSlot.WireframeShaded,
            Iw3TechniqueSlot.DebugBumpmap
        ];
        if (slots.Length != MaterialAsset.TechniqueSlotCount)
        {
            throw new InvalidOperationException(
                "The IW3-to-IW4 material technique-slot map is incomplete.");
        }

        return Array.AsReadOnly(slots);
    }
}
