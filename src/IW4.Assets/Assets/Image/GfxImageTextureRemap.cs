namespace IW4.Assets.Assets.Image;

/// <summary>
/// Lossless PS3 CELL_GCM SET_TEXTURE_CONTROL1 payload. The low word selects
/// the source and operation for each sampled component. The high word is a
/// special-format remap override, not a generic flag set.
/// </summary>
public readonly record struct GfxImageTextureRemap(uint RawValue)
{
    public GfxImageTextureRemapSource AlphaSource => SourceAt(0);
    public GfxImageTextureRemapSource RedSource => SourceAt(2);
    public GfxImageTextureRemapSource GreenSource => SourceAt(4);
    public GfxImageTextureRemapSource BlueSource => SourceAt(6);

    public GfxImageTextureRemapMode AlphaMode => ModeAt(8);
    public GfxImageTextureRemapMode RedMode => ModeAt(10);
    public GfxImageTextureRemapMode GreenMode => ModeAt(12);
    public GfxImageTextureRemapMode BlueMode => ModeAt(14);

    /// <summary>
    /// Raw high word used by the RSX for special one- and two-channel
    /// formats. For those formats, zero selects XYXY expansion and any
    /// nonzero value selects XXXY. Other base formats ignore this word.
    /// </summary>
    public ushort SpecialFormatRemapOverride =>
        (ushort)(RawValue >> 16);

    public GfxImageSpecialFormatRemapOrder SpecialFormatExpansionOrder =>
        SpecialFormatRemapOverride == 0
            ? GfxImageSpecialFormatRemapOrder.Xyxy
            : GfxImageSpecialFormatRemapOrder.Xxxy;

    /// <summary>
    /// Exact bits consumed by IW4's native image-storage format key.
    /// Bits 24..31 remain preserved in <see cref="RawValue"/> but are not part
    /// of that key.
    /// </summary>
    public uint StorageFormatBits => RawValue & 0x00ff_ffff;

    /// <summary>
    /// Returns the format-aware low-word component encoding sampled by the
    /// RSX. Floating-point and special one-/two-channel formats impose
    /// hardware remaps before the ordinary A-R-G-B selector table is applied.
    /// </summary>
    public ushort EffectiveComponentEncoding(GfxImageBaseFormat baseFormat)
    {
        uint lowWord = RawValue & 0xffff;
        uint sourceByte = RawValue & 0xff;
        uint effectiveSourceByte = baseFormat switch
        {
            GfxImageBaseFormat.X32Float or
            GfxImageBaseFormat.W32Z32Y32X32Float or
            GfxImageBaseFormat.W16Z16Y16X16Float => 0xe4,

            GfxImageBaseFormat.Y16X16Float =>
                SpecialFormatExpansionOrder ==
                    GfxImageSpecialFormatRemapOrder.Xxxy
                    ? 0x56u
                    : 0x66u,

            GfxImageBaseFormat.X16 or
            GfxImageBaseFormat.Y16X16 or
            GfxImageBaseFormat.CompressedHilo8 or
            GfxImageBaseFormat.CompressedHiloS8 =>
                ExpandSpecialSourceByte(sourceByte),

            _ => sourceByte
        };

        return (ushort)((lowWord & 0xff00) | effectiveSourceByte);
    }

    private GfxImageTextureRemapSource SourceAt(int shift) =>
        (GfxImageTextureRemapSource)((RawValue >> shift) & 0x3);

    private GfxImageTextureRemapMode ModeAt(int shift) =>
        (GfxImageTextureRemapMode)((RawValue >> shift) & 0x3);

    private uint ExpandSpecialSourceByte(uint sourceByte) =>
        sourceByte switch
        {
            0xe4 => SpecialFormatExpansionOrder ==
                    GfxImageSpecialFormatRemapOrder.Xxxy
                ? 0x56u
                : 0x66u,
            0x4e => SpecialFormatExpansionOrder ==
                    GfxImageSpecialFormatRemapOrder.Xxxy
                ? 0xa9u
                : 0x99u,
            0xee => 0xaau,
            0x44 => 0x55u,
            _ => sourceByte
        };
}

/// <summary>Source component selected for a remapped output component.</summary>
public enum GfxImageTextureRemapSource : byte
{
    Alpha = 0,
    Red = 1,
    Green = 2,
    Blue = 3
}

/// <summary>Operation applied to a remapped output component.</summary>
public enum GfxImageTextureRemapMode : byte
{
    Zero = 0,
    One = 1,
    Remap = 2,
    Reserved = 3
}

/// <summary>RSX expansion order for special one- and two-channel formats.</summary>
public enum GfxImageSpecialFormatRemapOrder : byte
{
    Xyxy = 0,
    Xxxy = 1
}
