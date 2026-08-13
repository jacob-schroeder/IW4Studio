using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Scheduling;

/// <summary>
/// Initializes the PS3 draw-method tables. The R_InitDrawMethod name follows
/// the matching Xbox symbol; control flow and table values retain the PS3
/// layout.
/// </summary>
public static class MapRenderDrawMethodInitializer
{
    public const byte FullbrightTechnique =
        (byte)MaterialTechniqueType.Unlit;
    public const byte StandardEmissiveTechnique =
        (byte)MaterialTechniqueType.Emissive;
    public const byte StandardBaseTechnique =
        (byte)MaterialTechniqueType.Lit;
    public const byte DebugShaderTechnique =
        (byte)MaterialTechniqueType.DebugNormals;
    public const byte NoneTechnique =
        (byte)MaterialTechniqueType.None;

    private static readonly byte[] BasicTechniques =
        [
            (byte)MaterialTechniqueType.Lit,
            (byte)MaterialTechniqueType.LitSun,
            (byte)MaterialTechniqueType.LitSpot,
            (byte)MaterialTechniqueType.LitOmni,
            (byte)MaterialTechniqueType.LitSunShadow,
            (byte)MaterialTechniqueType.LitSpotShadow,
            (byte)MaterialTechniqueType.LitOmniShadow
        ];
    private static readonly byte[] BasicDfogTechniques =
        [
            (byte)MaterialTechniqueType.LitDfog,
            (byte)MaterialTechniqueType.LitSunDfog,
            (byte)MaterialTechniqueType.LitSpotDfog,
            (byte)MaterialTechniqueType.LitOmniDfog,
            (byte)MaterialTechniqueType.LitSunShadowDfog,
            (byte)MaterialTechniqueType.LitSpotShadowDfog,
            (byte)MaterialTechniqueType.LitOmniShadowDfog
        ];
    private static readonly byte[] NoSunTechniques =
        [
            (byte)MaterialTechniqueType.Lit,
            (byte)MaterialTechniqueType.LitSun,
            (byte)MaterialTechniqueType.LitSpot,
            (byte)MaterialTechniqueType.LitOmni,
            (byte)MaterialTechniqueType.LitSun,
            (byte)MaterialTechniqueType.LitSpotShadow,
            (byte)MaterialTechniqueType.LitOmniShadow
        ];
    private static readonly byte[] NoSunDfogTechniques =
        [
            (byte)MaterialTechniqueType.LitDfog,
            (byte)MaterialTechniqueType.LitSunDfog,
            (byte)MaterialTechniqueType.LitSpotDfog,
            (byte)MaterialTechniqueType.LitOmniDfog,
            (byte)MaterialTechniqueType.LitSunDfog,
            (byte)MaterialTechniqueType.LitSpotShadowDfog,
            (byte)MaterialTechniqueType.LitOmniShadowDfog
        ];
    private static readonly byte[] NoSunLodTechniques =
        [
            (byte)MaterialTechniqueType.LitInstanced,
            (byte)MaterialTechniqueType.LitInstancedSun,
            (byte)MaterialTechniqueType.LitSpot,
            (byte)MaterialTechniqueType.LitOmni,
            (byte)MaterialTechniqueType.LitInstancedSun,
            (byte)MaterialTechniqueType.LitSpotShadow,
            (byte)MaterialTechniqueType.LitOmniShadow
        ];
    private static readonly byte[] NoSunLodDfogTechniques =
        [
            (byte)MaterialTechniqueType.LitInstancedDfog,
            (byte)MaterialTechniqueType.LitInstancedSunDfog,
            (byte)MaterialTechniqueType.LitSpotDfog,
            (byte)MaterialTechniqueType.LitOmniDfog,
            (byte)MaterialTechniqueType.LitInstancedSunDfog,
            (byte)MaterialTechniqueType.LitSpotShadowDfog,
            (byte)MaterialTechniqueType.LitOmniShadowDfog
        ];

    public static MapRenderDrawMethod Initialize(
        MapRenderDrawMethodSettings settings)
    {
        if (settings.FullbrightEnabled)
        {
            return CreateFilled(
                MapRenderDrawSceneMode.Fullbright,
                FullbrightTechnique,
                FullbrightTechnique,
                FullbrightTechnique);
        }

        if (settings.DebugShaderValue != 0)
        {
            return CreateFilled(
                MapRenderDrawSceneMode.DebugShader,
                DebugShaderTechnique,
                DebugShaderTechnique,
                DebugShaderTechnique);
        }

        byte[] basic = settings.UseSunDirFog
            ? BasicDfogTechniques
            : BasicTechniques;
        byte[] noSun =
            (settings.LodShadersEnabled, settings.UseSunDirFog) switch
            {
                (true, true) => NoSunLodDfogTechniques,
                (true, false) => NoSunLodTechniques,
                (false, true) => NoSunDfogTechniques,
                (false, false) => NoSunTechniques
            };

        var table = new byte[
            MapRenderDrawMethodPageProducer.TechniqueTableLength];
        for (int pageIndex = 0;
             pageIndex < MapRenderDrawMethodPageProducer.PageCount;
             pageIndex++)
        {
            ReadOnlySpan<byte> row = pageIndex switch
            {
                1 or 3 => noSun,
                >= 10 =>
                [
                    NoneTechnique,
                    NoneTechnique,
                    NoneTechnique,
                    NoneTechnique,
                    NoneTechnique,
                    NoneTechnique,
                    NoneTechnique
                ],
                _ => basic
            };
            row.CopyTo(table.AsSpan(
                pageIndex * MapRenderDrawMethodPageProducer.VariantCount));
        }

        return new MapRenderDrawMethod(
            MapRenderDrawSceneMode.Standard,
            StandardBaseTechnique,
            StandardEmissiveTechnique,
            table);
    }

    private static MapRenderDrawMethod CreateFilled(
        MapRenderDrawSceneMode drawScene,
        int baseTechnique,
        int emissiveTechnique,
        byte tableTechnique)
    {
        var table = new byte[
            MapRenderDrawMethodPageProducer.TechniqueTableLength];
        Array.Fill(table, tableTechnique);
        return new MapRenderDrawMethod(
            drawScene,
            baseTechnique,
            emissiveTechnique,
            table);
    }
}
