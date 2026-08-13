namespace IW4.Assets.Assets.Image;

/// <summary>
/// Lossless PS3 CELL_GCM texture-format byte. The native format identifier
/// occupies bits 7 and 0..4; bits 5 and 6 select layout and coordinate mode.
/// </summary>
public readonly record struct GfxImageFormat(byte RawValue)
{
    public const byte BaseFormatMask = 0x9f;
    public const byte FlagsMask = 0x60;

    public GfxImageBaseFormat BaseFormat =>
        (GfxImageBaseFormat)(RawValue & BaseFormatMask);

    public GfxImageFormatFlags Flags =>
        (GfxImageFormatFlags)(RawValue & FlagsMask);

    public bool IsLinear =>
        (Flags & GfxImageFormatFlags.Linear) != 0;

    public bool UsesUnnormalizedCoordinates =>
        (Flags & GfxImageFormatFlags.UnnormalizedCoordinates) != 0;
}

/// <summary>
/// PS3 CELL_GCM texture base formats after removal of the layout and
/// coordinate-mode bits.
/// </summary>
public enum GfxImageBaseFormat : byte
{
    B8 = 0x81,
    A1R5G5B5 = 0x82,
    A4R4G4B4 = 0x83,
    R5G6B5 = 0x84,
    A8R8G8B8 = 0x85,
    CompressedDxt1 = 0x86,
    CompressedDxt23 = 0x87,
    CompressedDxt45 = 0x88,
    G8B8 = 0x8b,
    CompressedB8R8G8R8 = 0x8d,
    CompressedR8B8R8G8 = 0x8e,
    R6G5B5 = 0x8f,
    Depth24D8 = 0x90,
    Depth24D8Float = 0x91,
    Depth16 = 0x92,
    Depth16Float = 0x93,
    X16 = 0x94,
    Y16X16 = 0x95,
    R5G5B5A1 = 0x97,
    CompressedHilo8 = 0x98,
    CompressedHiloS8 = 0x99,
    W16Z16Y16X16Float = 0x9a,
    W32Z32Y32X32Float = 0x9b,
    X32Float = 0x9c,
    D1R5G5B5 = 0x9d,
    D8R8G8B8 = 0x9e,
    Y16X16Float = 0x9f
}

/// <summary>PS3 CELL_GCM texture-format modifier bits.</summary>
[Flags]
public enum GfxImageFormatFlags : byte
{
    None = 0,
    Linear = 0x20,
    UnnormalizedCoordinates = 0x40
}
