namespace IW4.Render.Textures;

/// <summary>
/// Decodes the low 16 bits of the RSX SET_TEXTURE_CONTROL1 payload.
/// The four two-bit source and control tables are ordered A-R-G-B.
///
/// </summary>
public static class RsxTextureSwizzleDecoder
{
    public const uint IdentityPayload = 0x0000AAE4;

    public static RsxTextureSwizzle Decode(uint payload)
    {
        // RSX output slots are A=0, R=1, G=2, B=3. Rendering intent retains
        // the resulting swizzle in canonical R-G-B-A component order.
        return new RsxTextureSwizzle(
            DecodeOutput(payload, 1),
            DecodeOutput(payload, 2),
            DecodeOutput(payload, 3),
            DecodeOutput(payload, 0));
    }

    private static RsxTextureSwizzleSource DecodeOutput(
        uint payload,
        int argbOutputIndex)
    {
        uint control = (payload >> (8 + (argbOutputIndex * 2))) & 0x3u;
        return control switch
        {
            0 => RsxTextureSwizzleSource.Zero,
            1 => RsxTextureSwizzleSource.One,
            // Cell GCM defines 2 as REMAP. RPCS3 preserves the hardware's
            // default-to-remap behavior for the reserved value 3 as well.
            _ => DecodeSource((payload >> (argbOutputIndex * 2)) & 0x3u)
        };
    }

    private static RsxTextureSwizzleSource DecodeSource(uint source)
    {
        return source switch
        {
            // The native source table is A-R-G-B.
            0 => RsxTextureSwizzleSource.Alpha,
            1 => RsxTextureSwizzleSource.Red,
            2 => RsxTextureSwizzleSource.Green,
            3 => RsxTextureSwizzleSource.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }
}
