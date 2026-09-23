namespace IW4.Render.Textures;

/// <summary>
/// Platform-neutral slot identity for one entry in a PS3 runtime GfxTexture
/// array. Descriptor equality never changes this {kind, ordinal} identity.
/// </summary>
public readonly record struct MapRenderWorldRuntimeTextureIdentity
{
    public MapRenderWorldRuntimeTextureIdentity(
        MapRenderWorldRuntimeTextureKind kind,
        byte ordinal)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Kind = kind;
        Ordinal = ordinal;
    }

    public MapRenderWorldRuntimeTextureKind Kind { get; }

    public byte Ordinal { get; }

    public int SamplerDestination => Kind switch
    {
        MapRenderWorldRuntimeTextureKind.ReflectionProbe => 1,
        MapRenderWorldRuntimeTextureKind.SecondaryLightmap => 3,
        MapRenderWorldRuntimeTextureKind.PrimaryLightmap => 2,
        _ => throw new InvalidOperationException("Unknown world runtime texture kind.")
    };

    public override string ToString() => $"{Kind}[{Ordinal}]";
}
