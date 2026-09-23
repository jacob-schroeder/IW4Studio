namespace IW4.Render.OpenGl.Programs;

internal readonly record struct MapRenderOpenGlStaticModelVegetationUniforms(
    int Parameters,
    int Bounds)
{
    public bool IsReady =>
        Parameters >= 0 &&
        Bounds >= 0;
}

internal readonly record struct MapRenderOpenGlStaticModelProgramUniforms(
    MapRenderOpenGlStaticModelVegetationUniforms Vegetation)
;
