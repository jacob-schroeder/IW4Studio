using System.IO.Compression;

namespace IW4.Studio.Documents;

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

}
