using System.Buffers.Binary;
using System.IO.Compression;

namespace MapConverter.Game.IW3.PS3.FastFiles;

internal static class Iw3Ps3FastFileFrames
{
    private const uint Iw3FastFileVersion = 1;
    private const ushort PackedStreamTerminator = 1;
    internal const int DecodedFrameSize = 0x10000;

    internal static bool IsFastFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 12,
            FileOptions.SequentialScan);
        Span<byte> prefix = stackalloc byte[12];
        return ReadUpTo(stream, prefix) == prefix.Length &&
            prefix[..8].SequenceEqual("IWffu100"u8) &&
            BinaryPrimitives.ReadUInt32BigEndian(prefix[8..]) == Iw3FastFileVersion;
    }

    /// <summary>
    /// Reads decoded frames in order. The memory passed to <paramref name="processFrame"/>
    /// is reused and is valid only until that callback returns.
    /// </summary>
    internal static void Read(
        string path,
        Action<ReadOnlyMemory<byte>> processFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processFrame);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: DecodedFrameSize,
            FileOptions.SequentialScan);
        Span<byte> magic = stackalloc byte[8];
        ReadExactly(stream, magic, "fastfile signature");
        if (!magic.SequenceEqual("IWffu100"u8))
            throw new InvalidDataException("the fastfile does not have the unsigned IW3 PS3 signature.");

        Span<byte> versionBytes = stackalloc byte[sizeof(uint)];
        ReadExactly(stream, versionBytes, "fastfile version");
        uint version = BinaryPrimitives.ReadUInt32BigEndian(versionBytes);
        if (version != Iw3FastFileVersion)
        {
            throw new InvalidDataException(
                $"unsigned fastfile version {version} is unsupported; expected {Iw3FastFileVersion}.");
        }

        byte[] encodedFrame = new byte[ushort.MaxValue];
        byte[] decodedFrame = new byte[DecodedFrameSize];
        bool sawTerminator = false;
        while (TryReadPackedSize(stream, out ushort encodedSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (encodedSize == PackedStreamTerminator)
            {
                sawTerminator = true;
                ValidateTrailingTerminator(stream);
                break;
            }

            int decodedLength;
            if (encodedSize == 0)
            {
                ReadExactly(
                    stream,
                    decodedFrame,
                    "uncompressed 0x10000-byte packed frame");
                decodedLength = DecodedFrameSize;
            }
            else
            {
                ReadExactly(
                    stream,
                    encodedFrame.AsSpan(0, encodedSize),
                    $"0x{encodedSize:X}-byte compressed packed frame");
                decodedLength = InflateFrame(
                    encodedFrame,
                    encodedSize,
                    decodedFrame);
            }

            processFrame(decodedFrame.AsMemory(0, decodedLength));
        }

        if (!sawTerminator)
            throw new InvalidDataException("the packed stream has no terminator.");
    }

    private static int InflateFrame(
        byte[] encodedFrame,
        int encodedLength,
        byte[] decodedFrame)
    {
        const int adlerTrailerSize = sizeof(uint);
        if (encodedLength <= adlerTrailerSize)
        {
            throw new InvalidDataException(
                "a compressed packed frame must contain raw Deflate data and an Adler-32 trailer.");
        }

        int deflateLength = encodedLength - adlerTrailerSize;
        uint expectedAdler = BinaryPrimitives.ReadUInt32BigEndian(
            encodedFrame.AsSpan(deflateLength, adlerTrailerSize));
        using var input = new MemoryStream(
            encodedFrame,
            index: 0,
            count: deflateLength,
            writable: false,
            publiclyVisible: true);
        using var deflate = new DeflateStream(
            input,
            CompressionMode.Decompress,
            leaveOpen: false);

        int decodedLength = 0;
        while (decodedLength < decodedFrame.Length)
        {
            int read = deflate.Read(decodedFrame.AsSpan(decodedLength));
            if (read == 0)
                break;
            decodedLength = checked(decodedLength + read);
        }

        Span<byte> overflowProbe = stackalloc byte[1];
        if (decodedLength == decodedFrame.Length &&
            deflate.Read(overflowProbe) != 0)
        {
            throw new InvalidDataException(
                "a packed frame inflated beyond its 0x10000-byte output window.");
        }
        if (decodedLength == 0)
            throw new InvalidDataException("a compressed packed frame inflated to zero bytes.");

        uint actualAdler = ComputeAdler32(decodedFrame.AsSpan(0, decodedLength));
        if (actualAdler != expectedAdler)
        {
            throw new InvalidDataException(
                $"packed-frame Adler-32 mismatch: stored 0x{expectedAdler:X8}, " +
                $"calculated 0x{actualAdler:X8}.");
        }
        return decodedLength;
    }

    private static uint ComputeAdler32(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65_521;
        const int maximumChunkLength = 5_552;
        uint a = 1;
        uint b = 0;
        while (!bytes.IsEmpty)
        {
            int chunkLength = Math.Min(bytes.Length, maximumChunkLength);
            ReadOnlySpan<byte> chunk = bytes[..chunkLength];
            for (int index = 0; index < chunk.Length; index++)
            {
                a += chunk[index];
                b += a;
            }
            a %= modulus;
            b %= modulus;
            bytes = bytes[chunkLength..];
        }
        return (b << 16) | a;
    }

    private static bool TryReadPackedSize(
        Stream stream,
        out ushort encodedSize)
    {
        int high = stream.ReadByte();
        if (high < 0)
        {
            encodedSize = 0;
            return false;
        }

        int low = stream.ReadByte();
        if (low < 0)
            throw new EndOfStreamException("the packed stream ends in a truncated size word.");
        encodedSize = checked((ushort)((high << 8) | low));
        return true;
    }

    private static void ValidateTrailingTerminator(Stream stream)
    {
        int high = stream.ReadByte();
        if (high < 0)
            return;
        int low = stream.ReadByte();
        if (low < 0)
        {
            throw new EndOfStreamException(
                "the packed stream has one trailing byte after its terminator.");
        }
        if (((high << 8) | low) != PackedStreamTerminator)
        {
            throw new InvalidDataException(
                "the packed stream contains data after its terminator.");
        }
        if (stream.ReadByte() >= 0)
        {
            throw new InvalidDataException(
                "the packed stream contains data after its optional second terminator.");
        }
    }

    private static int ReadUpTo(Stream stream, Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = stream.Read(destination[total..]);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static void ReadExactly(
        Stream stream,
        Span<byte> destination,
        string description)
    {
        int read = ReadUpTo(stream, destination);
        if (read != destination.Length)
        {
            throw new EndOfStreamException(
                $"{description} is truncated: expected {destination.Length} byte(s), read {read}.");
        }
    }
}
