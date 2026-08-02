namespace IW4.Render.Scheduling;

/// <summary>
/// Initializes the PS3 draw-method tables. The R_InitDrawMethod name follows
/// the matching Xbox symbol; control flow and table values retain the PS3
/// layout.
/// </summary>
public static class MapRenderDrawMethodInitializer
{
    public const byte FullbrightTechnique = 4;
    public const byte StandardEmissiveTechnique = 5;
    public const byte StandardBaseTechnique = 9;
    public const byte DebugShaderTechnique = 36;
    public const byte NoneTechnique = 39;

    private static readonly byte[] BasicTechniques =
        [9, 11, 15, 19, 13, 17, 21];
    private static readonly byte[] BasicDfogTechniques =
        [10, 12, 16, 20, 14, 18, 22];
    private static readonly byte[] NoSunTechniques =
        [9, 11, 15, 19, 11, 17, 21];
    private static readonly byte[] NoSunDfogTechniques =
        [10, 12, 16, 20, 12, 18, 22];
    private static readonly byte[] NoSunLodTechniques =
        [23, 25, 15, 19, 25, 17, 21];
    private static readonly byte[] NoSunLodDfogTechniques =
        [24, 26, 16, 20, 26, 18, 22];

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
