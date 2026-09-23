namespace IW4.Render.Scheduling;

/// <summary>
/// Reproduces the PS3 selector-page population mechanics. The caller supplies
/// runtime draw-method values and light-column state; this type does not infer
/// either from material appearance.
/// </summary>
public static class MapRenderDrawMethodPageProducer
{
    public const int PageCount = 13;
    public const int VariantCount = 7;
    public const int PageLength = 0x100;
    public const int TechniqueTableLength = PageCount * VariantCount;
    public const int PageStorageLength = PageCount * PageLength;
    public const int AlternateVariantDelta = 3;

    /// <summary>
    /// Transposes the active technique-table column into all thirteen selector
    /// pages. Bytes at light indices outside <paramref name="sceneLightCount"/>
    /// are preserved, matching the native bounded writer.
    /// </summary>
    /// <param name="variantSelectorByLight">
    /// Dense decoded selectors, one byte per scene light. Native backend state
    /// stores this byte at a 0x40 stride; a raw-state adapter must gather those
    /// bytes before calling this normalized operation.
    /// </param>
    /// <param name="alternateVariantBits">
    /// Dense decoded 32-bit bitset words. Native storage begins at backend
    /// +0x14BC; callers must preserve the PS3 word values when adapting bytes.
    /// </param>
    public static void Populate(
        Span<byte> destinationPages,
        ReadOnlySpan<byte> techniqueTable,
        ReadOnlySpan<byte> variantSelectorByLight,
        bool alternateVariantGateEnabled,
        ReadOnlySpan<uint> alternateVariantBits,
        int sceneLightCount)
    {
        if (destinationPages.Length != PageStorageLength)
            throw new ArgumentException($"Selector-page storage must be exactly 0x{PageStorageLength:X} bytes.", nameof(destinationPages));
        if (techniqueTable.Length != TechniqueTableLength)
            throw new ArgumentException($"Draw-method technique table must contain exactly {TechniqueTableLength} bytes.", nameof(techniqueTable));
        if ((uint)sceneLightCount > PageLength)
            throw new ArgumentOutOfRangeException(nameof(sceneLightCount));
        if (variantSelectorByLight.Length < sceneLightCount)
            throw new ArgumentException("Variant-selector storage does not cover every scene light.", nameof(variantSelectorByLight));

        int requiredBitWords = (sceneLightCount + 31) / 32;
        if (alternateVariantGateEnabled && alternateVariantBits.Length < requiredBitWords)
            throw new ArgumentException("Alternate-variant bit storage does not cover every scene light.", nameof(alternateVariantBits));

        for (int lightIndex = 0; lightIndex < sceneLightCount; lightIndex++)
        {
            int variant = variantSelectorByLight[lightIndex];
            if (alternateVariantGateEnabled &&
                (alternateVariantBits[lightIndex >> 5] & (1u << (lightIndex & 31))) != 0)
            {
                variant += AlternateVariantDelta;
            }

            if ((uint)variant >= VariantCount)
            {
                throw new InvalidDataException(
                    $"Scene light {lightIndex} selected draw-method variant {variant}; valid PS3 table columns are 0..{VariantCount - 1}.");
            }

            for (int pageIndex = 0; pageIndex < PageCount; pageIndex++)
            {
                destinationPages[(pageIndex * PageLength) + lightIndex] =
                    techniqueTable[(pageIndex * VariantCount) + variant];
            }
        }
    }

    public static ReadOnlySpan<byte> GetPage(ReadOnlySpan<byte> pages, int pageIndex)
    {
        if (pages.Length != PageStorageLength)
            throw new ArgumentException($"Selector-page storage must be exactly 0x{PageStorageLength:X} bytes.", nameof(pages));
        if ((uint)pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        return pages.Slice(pageIndex * PageLength, PageLength);
    }
}
