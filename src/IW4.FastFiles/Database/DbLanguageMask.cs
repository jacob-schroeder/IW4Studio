namespace IW4.FastFiles.Database;

/// <summary>PS3 IW4 language-bit facts shared by header readers and writers.</summary>
public static class DbLanguageMask
{
    public const int BitCount = 15;
    public const uint SupportedBits = (1u << BitCount) - 1;

    public static bool IsSupported(uint mask) =>
        mask != 0 && (mask & ~SupportedBits) == 0;

    public static bool IsSingleLanguage(uint mask) =>
        IsSupported(mask) && (mask & (mask - 1)) == 0;
}
