namespace IW4.Render.Materials;

/// <summary>
/// Exact Event20 interpretation of one authored pass's raw custom-sampler byte.
/// Unknown bits are preserved and never promoted into implicit world bindings.
/// </summary>
public readonly record struct MapRenderWorldCustomSamplerSelection
{
    private const byte KnownMask = (byte)(
        MapRenderWorldCustomSamplerFlags.ReflectionProbe |
        MapRenderWorldCustomSamplerFlags.PrimaryLightmap |
        MapRenderWorldCustomSamplerFlags.SecondaryLightmap);

    public MapRenderWorldCustomSamplerSelection(byte rawFlags)
    {
        RawFlags = rawFlags;
    }

    public byte RawFlags { get; }

    public MapRenderWorldCustomSamplerFlags Flags =>
        (MapRenderWorldCustomSamplerFlags)RawFlags;

    public byte UnknownFlags => (byte)(RawFlags & ~KnownMask);

    public bool BindsReflectionProbe =>
        (Flags & MapRenderWorldCustomSamplerFlags.ReflectionProbe) != 0;

    public bool BindsSecondaryLightmap =>
        (Flags & MapRenderWorldCustomSamplerFlags.SecondaryLightmap) != 0;

    public bool BindsPrimaryLightmap => BindsSecondaryLightmap &&
        (Flags & MapRenderWorldCustomSamplerFlags.PrimaryLightmap) != 0;

    public IEnumerable<MapRenderWorldCustomSamplerFlags> EnumerateBindingsInNativeOrder()
    {
        if (BindsReflectionProbe)
            yield return MapRenderWorldCustomSamplerFlags.ReflectionProbe;
        if (BindsSecondaryLightmap)
            yield return MapRenderWorldCustomSamplerFlags.SecondaryLightmap;
        if (BindsPrimaryLightmap)
            yield return MapRenderWorldCustomSamplerFlags.PrimaryLightmap;
    }
}
