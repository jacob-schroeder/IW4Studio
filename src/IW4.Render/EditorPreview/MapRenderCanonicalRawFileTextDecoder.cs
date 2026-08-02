using System.IO.Compression;
using System.Text;
using IW4.Assets.Assets.RawFile;

namespace IW4.Render.EditorPreview;

internal enum MapRenderCanonicalRawFileTextDecodeStatus
{
    Ready,
    MetadataInvalid,
    BufferMissing,
    DecodeFailed
}

/// <summary>
/// Exact RawFile text boundary shared by revision-owned EditorPreview script
/// consumers. A successful decode consumes the declared byte count and never
/// accepts truncated zlib output or embedded nulls.
/// </summary>
internal static class MapRenderCanonicalRawFileTextDecoder
{
    internal static bool TryDecode(
        RawFileAsset rawFile,
        int maximumByteCount,
        out string text,
        out MapRenderCanonicalRawFileTextDecodeStatus status,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(rawFile);
        if (maximumByteCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumByteCount));

        text = string.Empty;
        status = MapRenderCanonicalRawFileTextDecodeStatus.MetadataInvalid;
        detail = string.Empty;
        if (rawFile.Len <= 0 || rawFile.Len > maximumByteCount ||
            rawFile.CompressedLen < 0 ||
            rawFile.CompressedLen > maximumByteCount)
        {
            detail =
                $"RawFile lengths are outside the supported range: compressed={rawFile.CompressedLen}, uncompressed={rawFile.Len}.";
            return false;
        }

        if (rawFile.Buffer is not { } source)
        {
            status = MapRenderCanonicalRawFileTextDecodeStatus.BufferMissing;
            detail = "RawFile buffer is null.";
            return false;
        }

        byte[] decoded;
        if (rawFile.CompressedLen == 0)
        {
            int requiredLength;
            try
            {
                requiredLength = checked(rawFile.Len + 1);
            }
            catch (OverflowException)
            {
                detail =
                    "RawFile uncompressed length overflows its null-terminated storage contract.";
                return false;
            }

            if (source.Length != requiredLength || source[^1] != 0)
            {
                detail =
                    $"Raw RawFile storage is {source.Length} bytes, expected {requiredLength} with one trailing null.";
                return false;
            }

            decoded = source.AsSpan(0, rawFile.Len).ToArray();
        }
        else
        {
            if (source.Length != rawFile.CompressedLen)
            {
                detail =
                    $"Compressed RawFile storage is {source.Length} bytes, expected {rawFile.CompressedLen}.";
                return false;
            }

            decoded = new byte[rawFile.Len];
            try
            {
                using var input = new MemoryStream(source, writable: false);
                using var zlib = new ZLibStream(
                    input,
                    CompressionMode.Decompress,
                    leaveOpen: false);
                int total = 0;
                while (total < decoded.Length)
                {
                    int read = zlib.Read(
                        decoded,
                        total,
                        decoded.Length - total);
                    if (read == 0)
                        break;
                    total += read;
                }

                if (total != decoded.Length || zlib.ReadByte() != -1)
                {
                    status =
                        MapRenderCanonicalRawFileTextDecodeStatus.DecodeFailed;
                    detail =
                        $"Zlib output length did not match declared RawFile length {rawFile.Len}.";
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException)
            {
                status =
                    MapRenderCanonicalRawFileTextDecodeStatus.DecodeFailed;
                detail = $"Zlib decode failed: {exception.Message}";
                return false;
            }
        }

        int textLength = decoded.Length;
        if (textLength > 0 && decoded[^1] == 0)
            textLength--;
        if (decoded.AsSpan(0, textLength).Contains((byte)0))
        {
            status = MapRenderCanonicalRawFileTextDecodeStatus.DecodeFailed;
            detail = "Decoded RawFile text contains an embedded null byte.";
            return false;
        }

        text = Encoding.Latin1.GetString(decoded, 0, textLength);
        status = MapRenderCanonicalRawFileTextDecodeStatus.Ready;
        return true;
    }
}
