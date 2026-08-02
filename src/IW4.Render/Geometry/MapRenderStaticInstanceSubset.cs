namespace IW4.Render.Geometry;

/// <summary>
/// Compares compacted instance subsets in stable source-index space. Object
/// indices are not a legal change key: multiple source rows may intentionally
/// refer to the same object while carrying different receiver identities or
/// per-instance lighting data.
/// </summary>
internal static class MapRenderStaticInstanceSubset
{
    public static void AppendSelectedSourceIndex(
        int sourceIndex,
        bool lodVisible,
        bool isReceiverVariant,
        uint sourceSelectionGeneration,
        uint activeSelectionGeneration,
        bool baseReceiverSuppressed,
        Span<int> destination,
        ref int destinationCount)
    {
        if (sourceIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        if ((uint)destinationCount > (uint)destination.Length)
            throw new ArgumentOutOfRangeException(nameof(destinationCount));

        bool selected = lodVisible &&
            (isReceiverVariant
                ? activeSelectionGeneration != 0 &&
                  sourceSelectionGeneration == activeSelectionGeneration
                : !baseReceiverSuppressed);
        if (!selected)
            return;
        if (destinationCount == destination.Length)
        {
            throw new ArgumentException(
                "The compacted source-index destination is full.",
                nameof(destination));
        }
        destination[destinationCount++] = sourceIndex;
    }

    public static bool HasChanged(
        ReadOnlySpan<int> currentSourceIndices,
        int currentCount,
        ReadOnlySpan<int> nextSourceIndices,
        int nextCount)
    {
        if (currentCount < 0 || currentCount > currentSourceIndices.Length)
            throw new ArgumentOutOfRangeException(nameof(currentCount));
        if (nextCount < 0 || nextCount > nextSourceIndices.Length)
            throw new ArgumentOutOfRangeException(nameof(nextCount));
        if (currentCount != nextCount)
            return true;

        return !currentSourceIndices[..currentCount]
            .SequenceEqual(nextSourceIndices[..nextCount]);
    }
}
