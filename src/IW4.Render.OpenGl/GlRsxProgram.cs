using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

internal readonly record struct GlRsxVegetationUniformLocations(
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

internal readonly record struct GlRsxProgram(
    uint Handle,
    int[] SamplerDestinations,
    int[] SamplerLocations)
{
    public bool StaticModelInstancingReady { get; init; }

    public int[]? StaticModelViewRowLocations { get; init; }

    public int[]? StaticModelViewProjectionRowLocations { get; init; }

    public int StaticModelEyeOffsetLocation { get; init; } = -1;

    public GlRsxVegetationUniformLocations?
        StaticModelVegetationUniformLocations { get; init; }
}
