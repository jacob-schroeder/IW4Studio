using System.Text;

namespace IW4.Gsc.Syntax;

/// <summary>
/// Immutable GSC source plus the byte and line mappings required by the
/// native byte-oriented scanner and a UTF-16 editor.
/// </summary>
public sealed class GscSourceText
{
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly byte[] _bytes;
    private readonly int[] _byteCharacterStarts;
    private readonly int[] _byteCharacterEnds;
    private readonly int[] _lineStarts;

    public GscSourceText(string text)
        : this(text, DefaultEncoding)
    {
    }

    public GscSourceText(string text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);

        Encoding strictEncoding = (Encoding)encoding.Clone();
        strictEncoding.EncoderFallback = EncoderFallback.ExceptionFallback;

        Text = text;
        _bytes = strictEncoding.GetBytes(text);
        (_byteCharacterStarts, _byteCharacterEnds) = CreateByteCharacterMap(
            text,
            strictEncoding,
            _bytes.Length);
        _lineStarts = FindLineStarts(text);
    }

    public string Text { get; }

    public int Length => Text.Length;

    public int LineCount => _lineStarts.Length;

    internal ReadOnlySpan<byte> Bytes => _bytes;

    public GscLinePosition GetLinePosition(int offset)
    {
        if ((uint)offset > (uint)Text.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int line = Array.BinarySearch(_lineStarts, offset);
        if (line < 0)
            line = ~line - 1;

        return new GscLinePosition(line, offset - _lineStarts[line]);
    }

    public GscLinePositionSpan GetLinePositionSpan(GscTextSpan span)
    {
        ValidateSpan(span);
        return new GscLinePositionSpan(
            GetLinePosition(span.Start),
            GetLinePosition(span.End));
    }

    public string GetText(GscTextSpan span)
    {
        ValidateSpan(span);
        return Text.Substring(span.Start, span.Length);
    }

    internal GscTextSpan GetTextSpan(int byteStart, int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteStart);
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        if (byteStart > _bytes.Length || byteLength > _bytes.Length - byteStart)
            throw new ArgumentOutOfRangeException(nameof(byteLength));

        if (byteLength == 0)
        {
            int characterOffset = byteStart == _bytes.Length
                ? Text.Length
                : _byteCharacterStarts[byteStart];
            return new GscTextSpan(characterOffset, 0);
        }

        int characterStart = _byteCharacterStarts[byteStart];
        int characterEnd = _byteCharacterEnds[byteStart + byteLength - 1];
        return new GscTextSpan(characterStart, characterEnd - characterStart);
    }

    private void ValidateSpan(GscTextSpan span)
    {
        if (span.Start > Text.Length || span.Length > Text.Length - span.Start)
            throw new ArgumentOutOfRangeException(nameof(span));
    }

    private static (int[] Starts, int[] Ends) CreateByteCharacterMap(
        string text,
        Encoding encoding,
        int byteLength)
    {
        var starts = new int[byteLength];
        var ends = new int[byteLength];
        int byteOffset = 0;

        for (int characterOffset = 0; characterOffset < text.Length;)
        {
            int characterLength = char.IsHighSurrogate(text[characterOffset]) &&
                                  characterOffset + 1 < text.Length &&
                                  char.IsLowSurrogate(text[characterOffset + 1])
                ? 2
                : 1;
            int characterByteLength = encoding.GetByteCount(
                text.AsSpan(characterOffset, characterLength));

            Array.Fill(starts, characterOffset, byteOffset, characterByteLength);
            Array.Fill(
                ends,
                characterOffset + characterLength,
                byteOffset,
                characterByteLength);

            byteOffset += characterByteLength;
            characterOffset += characterLength;
        }

        if (byteOffset != byteLength)
            throw new InvalidOperationException("The GSC source byte mapping is inconsistent.");

        return (starts, ends);
    }

    private static int[] FindLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                    index++;
                starts.Add(index + 1);
            }
            else if (text[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        return [.. starts];
    }
}
