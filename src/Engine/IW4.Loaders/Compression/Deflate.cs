using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace IW4.Loaders.Compression;

internal static class Deflate
{
    private const uint AdlerModulus = 65_521;
    // zlib's NMAX bound keeps both Adler accumulators within 32 bits while
    // reducing modulo operations from twice per byte to twice per chunk.
    private const int AdlerMaximumChunkLength = 5_552;
    private const int MaximumDecodedFrameSize = 0x10000;
    private const int AdlerTrailerSize = sizeof(uint);
    private static readonly bool UseStrictValidation =
        AppContext.TryGetSwitch("System.IO.Compression.UseStrictValidation", out bool strict) && strict;

    /// <summary>
    /// PS3 packed-zone compressed frames are zlib streams with only the
    /// two-byte CMF/FLG header replaced by the outer big-endian size word.
    /// The four-byte big-endian Adler-32 trailer remains inside that declared
    /// frame size. The caller receives both checksum values so it can report
    /// a mismatch without discarding an otherwise decoded frame.
    /// </summary>
    public static int DecompressPs3HeaderlessZlib(
        ReadOnlySpan<byte> data,
        Span<byte> destination,
        DeflateDecoder decoder,
        out uint storedAdler,
        out uint calculatedAdler)
    {
        if (data.Length <= AdlerTrailerSize)
        {
            throw new InvalidDataException(
                "A compressed PS3 packed-zone frame must contain raw Deflate bytes plus a four-byte Adler-32 trailer.");
        }
        if (destination.Length < MaximumDecodedFrameSize)
        {
            throw new ArgumentException(
                $"The PS3 packed-zone output buffer must contain at least 0x{MaximumDecodedFrameSize:X} bytes.",
                nameof(destination));
        }

        int deflateLength = data.Length - AdlerTrailerSize;
        storedAdler = BinaryPrimitives.ReadUInt32BigEndian(data[deflateLength..]);
        decoder.Reset();
        ReadOnlySpan<byte> source = data[..deflateLength];
        int decodedLength = 0;
        Span<byte> overflowProbe = stackalloc byte[1];
        while (true)
        {
            Span<byte> output = decodedLength == MaximumDecodedFrameSize
                ? overflowProbe
                : destination[decodedLength..MaximumDecodedFrameSize];
            OperationStatus status = decoder.Decompress(
                source,
                output,
                out int consumed,
                out int written);
            source = source[consumed..];
            if (decodedLength == MaximumDecodedFrameSize && written != 0)
            {
                throw new InvalidDataException(
                    $"A PS3 packed-zone frame inflated beyond 0x{MaximumDecodedFrameSize:X} bytes; " +
                    $"the native output window is 0x{MaximumDecodedFrameSize:X}.");
            }

            decodedLength += written;
            if (status == OperationStatus.InvalidData)
                throw new InvalidDataException("The compressed PS3 packed-zone frame contains invalid Deflate data.");
            if (status == OperationStatus.Done)
                break;
            if (status == OperationStatus.NeedMoreData && source.IsEmpty)
            {
                // DeflateStream permits an incomplete final block unless strict validation is enabled.
                if (UseStrictValidation)
                    throw new InvalidDataException("The compressed PS3 packed-zone frame is truncated.");
                break;
            }
            if (consumed == 0 && written == 0)
                throw new InvalidDataException("The compressed PS3 packed-zone frame made no decoding progress.");
        }

        calculatedAdler = ComputeAdler32(destination[..decodedLength]);
        return decodedLength;
    }

    private static uint ComputeAdler32(ReadOnlySpan<byte> bytes)
    {
        uint a = 1;
        uint b = 0;
        while (!bytes.IsEmpty)
        {
            int chunkLength = Math.Min(
                bytes.Length,
                AdlerMaximumChunkLength);
            ReadOnlySpan<byte> chunk = bytes[..chunkLength];
            for (int index = 0; index < chunk.Length; index++)
            {
                a += chunk[index];
                b += a;
            }

            a %= AdlerModulus;
            b %= AdlerModulus;
            bytes = bytes[chunkLength..];
        }

        return (b << 16) | a;
    }
}
