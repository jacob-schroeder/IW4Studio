namespace IW4.Game.Codecs.GfxMap;

/// <summary>
/// Exact PS3 single-precision gamma-to-linear transfer shared by the
/// fog and scene-light constant writers.
/// </summary>
public static class GfxColorCodec
{
    private static readonly float Threshold = Float(0x3D20E411);
    private static readonly float LowScale = Float(0x3D9E8391);
    private static readonly float Offset = Float(0x3D6147AE);
    private static readonly float HighScale = Float(0x3F72A76F);
    private static readonly float Exponent = Float(0x4019999A);

    public static float GammaToLinear(float value) =>
        value <= Threshold
            ? value * LowScale
            : MathF.Pow((value + Offset) * HighScale, Exponent);

    private static float Float(uint bits) =>
        BitConverter.Int32BitsToSingle(unchecked((int)bits));
}
