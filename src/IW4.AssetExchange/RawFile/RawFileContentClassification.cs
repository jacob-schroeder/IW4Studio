using System.Text;

namespace IW4.AssetExchange.RawFile;

public enum RawFileContentKind
{
    Textual,
    Binary
}

public enum RawFileTextEncoding
{
    Utf8,
    Windows1252
}

public readonly record struct RawFileContentClassification(
    RawFileContentKind Kind,
    RawFileTextEncoding? TextEncoding,
    bool IsDeclaredTextExtension)
{
    public bool IsTextual => Kind == RawFileContentKind.Textual;
}

/// <summary>
/// Classifies logical RawFile bytes independently from their serialized
/// compressed/uncompressed representation.
/// </summary>
public static class RawFileContentClassifier
{
    private static readonly HashSet<string> TextExtensions = new(
        [
            ".arena",
            ".atr",
            ".cfg",
            ".csc",
            ".def",
            ".graph",
            ".gsc",
            ".news",
            ".rmb",
            ".script",
            ".shock",
            ".txt",
            ".vision"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Encoding StrictWindows1252 = CreateWindows1252();

    public static IReadOnlySet<string> DeclaredTextExtensions => TextExtensions;

    public static RawFileContentClassification Classify(
        string name,
        ReadOnlySpan<byte> logicalContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool declaredText = TextExtensions.Contains(Path.GetExtension(name));
        RawFileTextEncoding? detectedEncoding = DetectTextEncoding(
            logicalContent,
            allowControlCharacters: declaredText);
        if (declaredText || detectedEncoding is not null)
        {
            return new RawFileContentClassification(
                RawFileContentKind.Textual,
                detectedEncoding ?? RawFileTextEncoding.Utf8,
                declaredText);
        }

        return new RawFileContentClassification(
            RawFileContentKind.Binary,
            TextEncoding: null,
            IsDeclaredTextExtension: false);
    }

    private static RawFileTextEncoding? DetectTextEncoding(
        ReadOnlySpan<byte> logicalContent,
        bool allowControlCharacters)
    {
        if (logicalContent.Contains((byte)0))
            return null;

        if (TryDecodeText(logicalContent, StrictUtf8, allowControlCharacters))
            return RawFileTextEncoding.Utf8;
        if (TryDecodeText(logicalContent, StrictWindows1252, allowControlCharacters))
            return RawFileTextEncoding.Windows1252;
        return null;
    }

    public static string DecodeText(
        ReadOnlySpan<byte> logicalContent,
        RawFileTextEncoding encoding)
    {
        if (logicalContent.Contains((byte)0))
        {
            throw new InvalidDataException(
                "RawFile text cannot contain an embedded terminal null.");
        }

        // Control characters are valid encoded text, including in GSC strings.
        // IsText is only a heuristic for content without a declared text extension.
        return GetTextEncoding(encoding).GetString(logicalContent);
    }

    public static byte[] EncodeText(
        string text,
        RawFileTextEncoding preferredEncoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.IndexOf('\0') >= 0)
            throw new ArgumentException("RawFile text cannot contain an embedded terminal null.", nameof(text));

        try
        {
            return GetTextEncoding(preferredEncoding).GetBytes(text);
        }
        catch (EncoderFallbackException) when (preferredEncoding == RawFileTextEncoding.Windows1252)
        {
            return StrictUtf8.GetBytes(text);
        }
    }

    public static string GetDisplayName(RawFileTextEncoding encoding) => encoding switch
    {
        RawFileTextEncoding.Utf8 => "UTF-8",
        RawFileTextEncoding.Windows1252 => "Windows-1252",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding))
    };

    private static bool TryDecodeText(
        ReadOnlySpan<byte> content,
        Encoding encoding,
        bool allowControlCharacters)
    {
        try
        {
            string text = encoding.GetString(content);
            return allowControlCharacters || IsText(text);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsText(string text) => text.All(character =>
        character is '\t' or '\r' or '\n' or '\f' ||
        !char.IsControl(character));

    public static Encoding GetTextEncoding(RawFileTextEncoding encoding) => encoding switch
    {
        RawFileTextEncoding.Utf8 => StrictUtf8,
        RawFileTextEncoding.Windows1252 => StrictWindows1252,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding))
    };

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }
}
