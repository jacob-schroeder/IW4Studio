namespace IW4.Render.OpenGl.Programs;

internal readonly record struct MapRenderOpenGlStaticModelVegetationUniforms(
    int WindEnabled,
    int Time,
    int Amplitude,
    int AngularFrequency,
    int SpatialFrequency,
    int LocalMinimumHeight,
    int LocalHeightRange)
{
    public bool IsReady =>
        WindEnabled >= 0 &&
        Time >= 0 &&
        Amplitude >= 0 &&
        AngularFrequency >= 0 &&
        SpatialFrequency >= 0 &&
        LocalMinimumHeight >= 0 &&
        LocalHeightRange >= 0;
}

internal readonly record struct MapRenderOpenGlStaticModelProgramUniforms(
    int[] ViewRowLocations,
    int[] ViewProjectionRowLocations,
    int EyeOffsetLocation,
    MapRenderOpenGlStaticModelVegetationUniforms Vegetation)
{
    public bool HasFrameRows =>
        ViewRowLocations.Length == 4 &&
        ViewProjectionRowLocations.Length == 4;
}
