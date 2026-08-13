namespace IW4.Render.OpenGl;

internal readonly record struct GlRsxProgram(
    uint Handle,
    int[] SamplerDestinations,
    int[] SamplerLocations);
