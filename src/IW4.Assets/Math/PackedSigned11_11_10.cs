using System.Numerics;

namespace IW4.Assets.Math;

/// <summary>
/// Lossless two's-complement X11/Y11/Z10 word shared by packed placements and
/// RSX signed-normalized vertex elements. The two consumers use the same bit
/// placement but intentionally have different normalization rules.
/// </summary>
public readonly record struct PackedSigned11_11_10(uint Packed)
{
    public int X => SignExtend((int)(Packed & 0x7ffu), 11);

    public int Y => SignExtend((int)((Packed >> 11) & 0x7ffu), 11);

    public int Z => SignExtend((int)((Packed >> 22) & 0x3ffu), 10);

    /// <summary>
    /// Decodes the placement-axis convention used by GfxPackedPlacement.
    /// </summary>
    public Vector3 DecodePlacement() => new(
        X / 1023f,
        Y / 1023f,
        Z / 511f);

    /// <summary>
    /// Decodes the RSX signed-normalized vertex-element convention, including
    /// its component-width promotion before division by 32767.
    /// </summary>
    public Vector3 DecodeRsxNormalized() => new(
        (X << 5) / 32767f,
        (Y << 5) / 32767f,
        (Z << 6) / 32767f);

    private static int SignExtend(int value, int bitCount)
    {
        int shift = 32 - bitCount;
        return (value << shift) >> shift;
    }
}
