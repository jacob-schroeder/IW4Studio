using System.IO.Compression;
using IW4.Assets.Assets.RawFile;

namespace IW4.Studio.Documents;

/// <summary>Strict logical-content boundary for RawFile editing.  It neither
/// guesses at malformed compression nor treats compressed bytes as text.</summary>
public static class RawFileContentCodec
{
    /// <summary>
    /// Decodes the exact serialized form required by strict runtime consumers.
    /// </summary>
    public static byte[] DecodeStrictSerializedContent(
        string assetName,
        RawFileAsset rawFile)
    {
        if (rawFile.CompressedLen < 0 || rawFile.Len < 0)
        {
            throw new InvalidDataException(
                $"RawFile '{assetName}' has negative serialized length metadata.");
        }

        byte[] payload = rawFile.Buffer?.ToArray() ?? [];
        if (rawFile.CompressedLen != 0)
        {
            if (payload.Length != rawFile.CompressedLen)
            {
                throw new InvalidDataException(
                    $"Compressed RawFile '{assetName}' has {payload.Length} payload bytes; expected {rawFile.CompressedLen}.");
            }

            return DecodeCompressed(payload, rawFile.Len);
        }

        if (rawFile.Buffer is null)
        {
            if (rawFile.Len != 0)
            {
                throw new InvalidDataException(
                    $"RawFile '{assetName}' has no buffer for its declared {rawFile.Len}-byte content.");
            }

            return [];
        }

        int expectedLength;
        try
        {
            expectedLength = checked(rawFile.Len + 1);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                $"RawFile '{assetName}' length cannot include its terminal null.",
                exception);
        }

        if (payload.Length != expectedLength || payload[^1] != 0)
        {
            throw new InvalidDataException(
                $"Uncompressed RawFile '{assetName}' must contain exactly len + 1 bytes ending in a terminal null.");
        }

        return payload[..rawFile.Len];
    }

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
