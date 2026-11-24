namespace IW4.Game.Assets.XModel;

/// <summary>
/// Bits 0–2 select the LARGE block for each payload. Bits 3–4 encode the material vertex color mode.
/// </summary>
[Flags]
public enum XSurfaceStreamFlags : byte
{
    None = 0,
    Verts0InLarge = 0x01,
    Verts1InLarge = 0x02,
    TriIndicesInLarge = 0x04,

    // Stock materials use at most one of these two bits.
    GrayscaleVertexColor = (byte)XSurfaceVertexColorMode.Grayscale,
    NoVertexColor = (byte)XSurfaceVertexColorMode.NoVertexColor
}
