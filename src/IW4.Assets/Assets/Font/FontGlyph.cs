using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Font;

public sealed record FontGlyph(
    ushort Letter,
    byte X0,
    byte Y0,
    byte Dx,
    byte PixelWidth,
    byte PixelHeight,
    byte Padding,
    float S0,
    float T0,
    float S1,
    float T1);
