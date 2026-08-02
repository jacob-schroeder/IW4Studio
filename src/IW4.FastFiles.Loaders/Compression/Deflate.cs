using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace IW4.FastFiles.Loaders.Compression;

internal static class Deflate
{
    private const uint AdlerModulus = 65_521;
    // zlib's NMAX bound keeps both Adler accumulators within 32 bits while
    // reducing modulo operations from twice per byte to twice per chunk.
    private const int AdlerMaximumChunkLength = 5_552;
    private const int MaximumDecodedFrameSize = 0x10000;
    private const int AdlerTrailerSize = sizeof(uint);

    /// <summary>
    /// PS3 packed-zone compressed frames are zlib streams with only the
    /// two-byte CMF/FLG header replaced by the outer big-endian size word.
    /// The four-byte big-endian Adler-32 trailer remains inside that declared
    /// frame size.
    /// </summary>
    public static int DecompressPs3HeaderlessZlib(
        ReadOnlyMemory<byte> data,
        Span<byte> destination)
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
        uint expectedAdler = BinaryPrimitives.ReadUInt32BigEndian(
            data.Span.Slice(deflateLength, AdlerTrailerSize));
        using Stream input = OpenReadOnlyStream(data[..deflateLength]);
        using var stream = new System.IO.Compression.DeflateStream(
            input,
            System.IO.Compression.CompressionMode.Decompress);

        int decodedLength = 0;
        while (decodedLength < MaximumDecodedFrameSize)
        {
            int read = stream.Read(
                destination.Slice(
                    decodedLength,
                    MaximumDecodedFrameSize - decodedLength));
            if (read == 0)
                break;

            decodedLength += read;
        }

        Span<byte> overflowProbe = stackalloc byte[1];
        if (decodedLength == MaximumDecodedFrameSize &&
            stream.Read(overflowProbe) != 0)
        {
            throw new InvalidDataException(
                $"A PS3 packed-zone frame inflated beyond 0x{MaximumDecodedFrameSize:X} bytes; " +
                $"the native output window is 0x{MaximumDecodedFrameSize:X}.");
        }

        uint actualAdler = ComputeAdler32(destination[..decodedLength]);
        if (actualAdler != expectedAdler)
        {
            throw new InvalidDataException(
                $"PS3 packed-zone Adler-32 mismatch: stored 0x{expectedAdler:X8}, calculated 0x{actualAdler:X8}.");
        }

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

    private static Stream OpenReadOnlyStream(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment))
        {
            return new MemoryStream(
                segment.Array!,
                segment.Offset,
                segment.Count,
                writable: false,
                publiclyVisible: true);
        }

        return new ReadOnlyMemoryStream(data);
    }

    private sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => memory.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(buffer.Length, memory.Length - _position);
            if (count == 0)
                return 0;

            memory.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int ReadByte()
        {
            if (_position == memory.Length)
                return -1;

            return memory.Span[_position++];
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
