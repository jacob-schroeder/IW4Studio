namespace IW4.Studio.MapEditor.Editing.MapEntsSyntax;

/// <summary>
/// Executable-backed limits imposed while MapEnt entity text is decoded into
/// the runtime <c>SpawnVar</c> representation.
/// </summary>
public static class MapEntsRuntimeSyntaxLimits
{
    public const int MaximumDecodedTokenByteLength = 1023;
    public const int MaximumPropertyCountPerEntity = 64;
    public const int MaximumDecodedPropertyPoolByteLengthPerEntity = 2048;
}

internal readonly record struct MapEntsRuntimeTokenAnalysis(
    int DecodedByteLength,
    int? UnescapedQuoteOffset,
    bool EscapesFollowingQuote);

/// <summary>
/// Mirrors the quoted-token escape handling in <c>Com_Parse</c>: only
/// backslash-quote and backslash-backslash pairs collapse to one runtime byte.
/// </summary>
internal static class MapEntsRuntimeTokenDecoder
{
    public static MapEntsRuntimeTokenAnalysis Analyze(
        ReadOnlySpan<byte> encodedContent)
    {
        int decodedByteLength = 0;
        int? unescapedQuoteOffset = null;
        int offset = 0;
        while (offset < encodedContent.Length)
        {
            byte current = encodedContent[offset];
            if (current == (byte)'\\' &&
                offset + 1 < encodedContent.Length &&
                encodedContent[offset + 1] is (
                    (byte)'"' or
                    (byte)'\\'))
            {
                decodedByteLength++;
                offset += 2;
                continue;
            }

            if (current == (byte)'"' &&
                unescapedQuoteOffset is null)
            {
                unescapedQuoteOffset = offset;
            }

            decodedByteLength++;
            offset++;
        }

        int trailingBackslashCount = 0;
        for (int index = encodedContent.Length - 1;
             index >= 0 &&
             encodedContent[index] == (byte)'\\';
             index--)
        {
            trailingBackslashCount++;
        }

        return new MapEntsRuntimeTokenAnalysis(
            decodedByteLength,
            unescapedQuoteOffset,
            (trailingBackslashCount & 1) != 0);
    }
}
