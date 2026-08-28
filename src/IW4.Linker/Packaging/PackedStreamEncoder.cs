using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using IW4.FastFiles.Database;

namespace IW4.Linker.Packaging;

internal static class PackageFormat
{
    public static void WriteUnsignedPrefix(Span<byte> destination)
    {
        if (destination.Length < DbHeader.UnsignedPrefixLength)
            throw new ArgumentException("PS3 package prefix destination is too small.", nameof(destination));

        int written = Encoding.Latin1.GetBytes(
            DbHeader.UnsignedMagic,
            destination[..DbHeader.MagicByteLength]);
        if (written != DbHeader.MagicByteLength)
            throw new InvalidDataException("PS3 package magic must occupy eight bytes.");
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.Slice(DbHeader.MagicByteLength, sizeof(uint)),
            (uint)XFileVersion.ModernWarfare2);
    }
}

internal sealed class PackedStream
{
    public PackedStream(
        byte[] bytes,
        IEnumerable<PackedStreamPage> pages)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        Pages = Array.AsReadOnly((pages ?? throw new ArgumentNullException(nameof(pages)))
            .ToArray());
    }

    public byte[] Bytes { get; }
    public IReadOnlyList<PackedStreamPage> Pages { get; }
}

internal readonly record struct PackedStreamPage(
    int EncodedOffset,
    int EncodedByteCount)
{
    public int EncodedEnd => checked(EncodedOffset + EncodedByteCount);
}

/// <summary>
/// Shared PS3 64 KiB logical-page encoder. Each compressed frame keeps the
/// zlib Adler-32 trailer after removing only CMF/FLG; an incompressible full
/// page uses the native zero-size raw marker.
/// </summary>
internal static class PackedStreamEncoder
{
    public const int LogicalPageSize = 0x10000;

    private const ushort TerminatorWord = 1;

    public static PackedStream Encode(
        ReadOnlySpan<byte> logicalBytes,
        int terminatorCount,
        CompressionLevel compressionLevel)
    {
        if (terminatorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(terminatorCount));

        using var output = new MemoryStream();
        var pages = new List<PackedStreamPage>();
        int offset = 0;
        while (offset < logicalBytes.Length)
        {
            ReadOnlySpan<byte> page = logicalBytes.Slice(
                offset,
                Math.Min(LogicalPageSize, logicalBytes.Length - offset));
            int encodedOffset = checked((int)output.Position);
            WriteLogicalPage(output, page, compressionLevel);
            int encodedEnd = checked((int)output.Position);
            pages.Add(new PackedStreamPage(
                encodedOffset,
                checked(encodedEnd - encodedOffset)));
            offset = checked(offset + page.Length);
        }

        for (int index = 0; index < terminatorCount; index++)
            WriteUInt16(output, TerminatorWord);

        return new PackedStream(output.ToArray(), pages);
    }

    private static void WriteLogicalPage(
        Stream output,
        ReadOnlySpan<byte> page,
        CompressionLevel compressionLevel)
    {
        byte[] compressed = EncodeHeaderlessZlib(page, compressionLevel);
        if (compressed.Length is > 1 and <= ushort.MaxValue)
        {
            WriteUInt16(output, checked((ushort)compressed.Length));
            output.Write(compressed);
            return;
        }
        if (page.Length == LogicalPageSize)
        {
            WriteUInt16(output, 0);
            output.Write(page);
            return;
        }
        throw new InvalidDataException(
            "A partial decoded page cannot be represented by one PS3 packed frame.");
    }

    private static byte[] EncodeHeaderlessZlib(
        ReadOnlySpan<byte> input,
        CompressionLevel compressionLevel)
    {
        using var buffer = new MemoryStream();
        using (var compressor = new ZLibStream(
                   buffer,
                   compressionLevel,
                   leaveOpen: true))
        {
            compressor.Write(input);
        }

        byte[] zlib = buffer.ToArray();
        if (zlib.Length < 6)
            throw new InvalidDataException(
                "The zlib encoder produced an invalid PS3 packed frame.");

        ushort header = BinaryPrimitives.ReadUInt16BigEndian(
            zlib.AsSpan(0, sizeof(ushort)));
        if ((zlib[0] & 0x0f) != 8 ||
            (zlib[0] >> 4) > 7 ||
            (zlib[1] & 0x20) != 0 ||
            header % 31 != 0)
        {
            throw new InvalidDataException(
                "The zlib encoder produced unsupported CMF/FLG for a PS3 packed frame.");
        }

        return zlib[sizeof(ushort)..];
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        output.Write(bytes);
    }
}
