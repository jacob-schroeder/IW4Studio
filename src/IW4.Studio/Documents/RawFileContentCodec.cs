using System.IO.Compression;

namespace IW4.Studio.Documents;

/// <summary>Explicit output choices for a logical RawFile edit.  Opaque
/// compressed-byte preservation remains available only for a no-edit import;
/// edited content always chooses one of these deterministic representations.</summary>
public enum RawFileCanonicalContentPolicy
{
    UncompressedBinary,
    DeterministicZlib
}

public sealed class RawFileEncodedPayload
{
    public RawFileEncodedPayload(RawFilePayloadMode mode, int compressedLength, int uncompressedLength, ReadOnlySpan<byte> serializedPayload)
    {
        Mode = mode;
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
        _serializedPayload = serializedPayload.ToArray();
    }

    private readonly byte[] _serializedPayload;
    public RawFilePayloadMode Mode { get; }
    public int CompressedLength { get; }
    public int UncompressedLength { get; }
    public byte[] GetSerializedPayloadCopy() => _serializedPayload.ToArray();
}

/// <summary>Strict logical-content boundary for RawFile editing.  It neither
/// guesses at malformed compression nor treats compressed bytes as text.</summary>
public static class RawFileContentCodec
{
    public static byte[] DecodeCompressed(ReadOnlySpan<byte> payload, int declaredUncompressedLength)
    {
        if (declaredUncompressedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(declaredUncompressedLength));
        if (payload.Length == 0)
            throw new InvalidDataException("Compressed RawFile payload is empty.");

        try
        {
            using var input = new MemoryStream(payload.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: false);
            using var output = new MemoryStream();
            Span<byte> buffer = stackalloc byte[4096];
            int read;
            while ((read = zlib.Read(buffer)) != 0)
            {
                if (output.Length + read > declaredUncompressedLength)
                {
                    throw new InvalidDataException(
                        $"Compressed RawFile inflated beyond its declared {declaredUncompressedLength}-byte logical length.");
                }
                output.Write(buffer[..read]);
            }
            byte[] content = output.ToArray();
            if (content.Length != declaredUncompressedLength)
            {
                throw new InvalidDataException(
                    $"Compressed RawFile inflated to {content.Length} bytes; expected {declaredUncompressedLength}.");
            }
            return content;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            throw new InvalidDataException("Compressed RawFile payload is not a valid zlib stream.", exception);
        }
    }

    public static RawFileEncodedPayload Encode(ReadOnlySpan<byte> logicalContent, RawFileCanonicalContentPolicy policy)
    {
        return policy switch
        {
            RawFileCanonicalContentPolicy.UncompressedBinary => EncodeUncompressed(
                logicalContent,
                RawFilePayloadMode.UncompressedBinary),
            RawFileCanonicalContentPolicy.DeterministicZlib => EncodeZlib(logicalContent),
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
    }

    /// <summary>
    /// Applies the stock RawFile storage policy to logical content. Binary
    /// content is always uncompressed. Text is compressed only when the
    /// complete zlib payload is smaller than the uncompressed content plus
    /// its required terminal null.
    /// </summary>
    public static RawFileEncodedPayload EncodeCanonical(
        ReadOnlySpan<byte> logicalContent,
        RawFileContentKind contentKind)
    {
        if (contentKind == RawFileContentKind.Binary)
        {
            return EncodeUncompressed(
                logicalContent,
                RawFilePayloadMode.UncompressedBinary);
        }
        if (contentKind != RawFileContentKind.Textual)
            throw new ArgumentOutOfRangeException(nameof(contentKind));

        if (logicalContent.Length != 0)
        {
            RawFileEncodedPayload compressed = EncodeZlib(logicalContent);
            if (compressed.CompressedLength < checked(logicalContent.Length + 1))
                return compressed;
        }

        return EncodeUncompressed(
            logicalContent,
            RawFilePayloadMode.UncompressedText);
    }

    private static RawFileEncodedPayload EncodeUncompressed(
        ReadOnlySpan<byte> logicalContent,
        RawFilePayloadMode mode)
    {
        if (mode is not (RawFilePayloadMode.UncompressedText or RawFilePayloadMode.UncompressedBinary))
            throw new ArgumentOutOfRangeException(nameof(mode));

        byte[] payload = new byte[checked(logicalContent.Length + 1)];
        logicalContent.CopyTo(payload);
        return new RawFileEncodedPayload(mode, 0, logicalContent.Length, payload);
    }

    private static RawFileEncodedPayload EncodeZlib(ReadOnlySpan<byte> logicalContent)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(logicalContent);
        byte[] payload = output.ToArray();
        if (payload.Length == 0)
            throw new InvalidDataException("Deterministic zlib encoding produced an empty payload.");
        return new RawFileEncodedPayload(RawFilePayloadMode.CompressedPayload, payload.Length, logicalContent.Length, payload);
    }
}
