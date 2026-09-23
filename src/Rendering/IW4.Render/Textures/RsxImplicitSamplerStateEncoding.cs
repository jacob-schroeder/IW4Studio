namespace IW4.Render.Textures;

/// <summary>
/// Raw sampler-state encodings recovered from the native Event20 implicit
/// texture bindings. These values are part of the renderer-facing RSX ABI.
/// </summary>
public static class RsxImplicitSamplerStateEncoding
{
    public const byte ReflectionProbe = 0x72;

    public const byte Lightmap = 0x62;
}
