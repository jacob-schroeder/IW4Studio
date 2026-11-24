namespace IW4.Game.Assets.XModel;

/// <summary>
/// Material vertex color mode encoded in bits 3–4 of the PS3 XSurface flags.
/// </summary>
public enum XSurfaceVertexColorMode : byte
{
    FullColor = 0x00,
    Grayscale = 0x08,
    NoVertexColor = 0x10
}
