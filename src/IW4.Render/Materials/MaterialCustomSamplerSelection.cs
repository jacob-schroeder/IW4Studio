namespace IW4.Render.Materials;

/// <summary>
/// Exact Event20 interpretation of one authored pass's raw custom-sampler byte.
/// Unknown bits are preserved and never promoted into implicit world bindings.
/// </summary>
public readonly record struct MaterialCustomSamplerSelection
{
    private const byte KnownMask = (byte)(
        MaterialCustomSamplerFlags.ReflectionProbe |
        MaterialCustomSamplerFlags.PrimaryLightmap |
        MaterialCustomSamplerFlags.SecondaryLightmap);

    public MaterialCustomSamplerSelection(byte rawFlags)
    {
        RawFlags = rawFlags;
    }

    public byte RawFlags { get; }

    public MaterialCustomSamplerFlags Flags =>
        (MaterialCustomSamplerFlags)RawFlags;

    public byte UnknownFlags => (byte)(RawFlags & ~KnownMask);

    public bool BindsReflectionProbe =>
        (Flags & MaterialCustomSamplerFlags.ReflectionProbe) != 0;

    public bool BindsSecondaryLightmap =>
        (Flags & MaterialCustomSamplerFlags.SecondaryLightmap) != 0;

    public bool BindsPrimaryLightmap => BindsSecondaryLightmap &&
        (Flags & MaterialCustomSamplerFlags.PrimaryLightmap) != 0;

    public IEnumerable<MaterialCustomSamplerFlags> EnumerateBindingsInNativeOrder()
    {
        if (BindsReflectionProbe)
            yield return MaterialCustomSamplerFlags.ReflectionProbe;
        if (BindsSecondaryLightmap)
            yield return MaterialCustomSamplerFlags.SecondaryLightmap;
        if (BindsPrimaryLightmap)
            yield return MaterialCustomSamplerFlags.PrimaryLightmap;
    }
}
