namespace IW4.Render.Textures;

/// <summary>
/// Decodes the low 16 bits of the RSX SET_TEXTURE_CONTROL1 payload.
/// The four two-bit source and control tables are ordered A-R-G-B.
///
/// </summary>
public static class MapRenderRsxTextureSwizzleDecoder
{
    public const uint IdentityPayload = 0x0000AAE4;

    public static MapRenderRsxTextureSwizzle Decode(uint payload)
    {
        // RSX output slots are A=0, R=1, G=2, B=3. Rendering intent retains
        // the resulting swizzle in canonical R-G-B-A component order.
        return new MapRenderRsxTextureSwizzle(
            DecodeOutput(payload, 1),
            DecodeOutput(payload, 2),
            DecodeOutput(payload, 3),
            DecodeOutput(payload, 0));
    }

    private static MapRenderRsxTextureSwizzleSource DecodeOutput(
        uint payload,
        int argbOutputIndex)
    {
        uint control = (payload >> (8 + (argbOutputIndex * 2))) & 0x3u;
        return control switch
        {
            0 => MapRenderRsxTextureSwizzleSource.Zero,
            1 => MapRenderRsxTextureSwizzleSource.One,
            // Cell GCM defines 2 as REMAP. RPCS3 preserves the hardware's
            // default-to-remap behavior for the reserved value 3 as well.
            _ => DecodeSource((payload >> (argbOutputIndex * 2)) & 0x3u)
        };
    }

    private static MapRenderRsxTextureSwizzleSource DecodeSource(uint source)
    {
        return source switch
        {
            // The native source table is A-R-G-B.
            0 => MapRenderRsxTextureSwizzleSource.Alpha,
            1 => MapRenderRsxTextureSwizzleSource.Red,
            2 => MapRenderRsxTextureSwizzleSource.Green,
            3 => MapRenderRsxTextureSwizzleSource.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }
}
