namespace MapConverter.Game.IW4.Xbox.Geometry;

/// <summary>Retail IW4 Xenon packed vertex words shared by world and model geometry.</summary>
internal static class XboxPackedUnitCodec
{
    internal static uint Pack(float x, float y, float z) =>
        Third(x) | (Third(y) << 10) | (Third(z) << 20);

    internal static uint Color(byte red, byte green, byte blue, byte alpha) =>
        (uint)alpha << 24 | (uint)blue << 16 | (uint)green << 8 | red;

    private static uint Third(float component)
    {
        // IW4Xenon Common::Vec3PackUnitVec -> pack32::Vec3PackUnitVecThirdBased.
        float encoded = (component - -24624.0939334638f) * 0.0001218318939208984f;
        return BitConverter.SingleToUInt32Bits(encoded) & 0x3ff;
    }
}
