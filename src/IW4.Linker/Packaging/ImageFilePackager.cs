using System.IO.Compression;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using IW4.Linker.Contracts;

namespace IW4.Linker.Packaging;

/// <summary>
/// One immutable PS3 imagefile package and the ordered DB-header references
/// that address its input payloads.
/// </summary>
public sealed class ImageFilePackage
{
    private readonly byte[] _bytes;
    private readonly IReadOnlyList<ImageFileStreamReference> _references;

    internal ImageFilePackage(
        byte[] bytes,
        IEnumerable<ImageFileStreamReference> references)
    {
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        _references = Array.AsReadOnly((references ??
            throw new ArgumentNullException(nameof(references)))
            .ToArray());
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;
    public IReadOnlyList<ImageFileStreamReference> References => _references;
}

/// <summary>
/// Writes a native PS3 imagefileN.pak from ordered logical stream payloads.
/// The package uses the shared 64 KiB PS3 frame encoder; returned references
/// identify the exact physical frame range and logical offset of every input.
/// </summary>
public sealed class ImageFilePackager
{
    private const uint MaximumFileIndex = 20;
    private const int StreamPartAlignment = 0x80;

    public ImageFilePackage Package(
        uint fileIndex,
        IReadOnlyList<ReadOnlyMemory<byte>> payloads)
    {
        if (fileIndex is 0 or > MaximumFileIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fileIndex),
                $"A PS3 imagefile package index must be between 1 and {MaximumFileIndex}.");
        }
        ArgumentNullException.ThrowIfNull(payloads);
        if (payloads.Count == 0)
        {
            throw new ArgumentException(
                "An imagefile package requires at least one ordered payload.",
                nameof(payloads));
        }

        var logicalOffsets = new int?[payloads.Count];
        int logicalByteCount = 0;
        for (int index = 0; index < payloads.Count; index++)
        {
            int byteCount = payloads[index].Length;
            if (byteCount == 0)
                continue;
            if (byteCount % StreamPartAlignment != 0)
            {
                throw new ArgumentException(
                    $"Image stream payload {index} must be aligned to " +
                    $"0x{StreamPartAlignment:x} bytes.",
                    nameof(payloads));
            }

            logicalByteCount = Align(logicalByteCount, StreamPartAlignment);
            logicalOffsets[index] = logicalByteCount;
            logicalByteCount = checked(logicalByteCount + byteCount);
        }
        if (logicalByteCount == 0)
        {
            throw new ArgumentException(
                "An imagefile package requires at least one nonempty payload.",
                nameof(payloads));
        }

        int encodedLogicalByteCount = Align(
            logicalByteCount,
            PackedStreamEncoder.LogicalPageSize);
        var logicalBytes = new byte[encodedLogicalByteCount];
        for (int index = 0; index < payloads.Count; index++)
        {
            if (logicalOffsets[index] is not { } logicalOffset)
                continue;
            payloads[index].Span.CopyTo(logicalBytes.AsSpan(logicalOffset));
        }

        PackedStream packed = PackedStreamEncoder.Encode(
            logicalBytes,
            terminatorCount: 1,
            CompressionLevel.SmallestSize);
        if (packed.Pages.Count !=
            encodedLogicalByteCount / PackedStreamEncoder.LogicalPageSize)
        {
            throw new InvalidDataException(
                "The PS3 packed stream did not emit one frame per logical image page.");
        }

        int prefixByteCount = DbHeader.UnsignedPrefixLength;
        var packageBytes = new byte[checked(prefixByteCount + packed.Bytes.Length)];
        PackageFormat.WritePrefix(
            packageBytes,
            DbHeader.UnsignedMagic,
            XFileVersion.ModernWarfare2);
        packed.Bytes.CopyTo(packageBytes, prefixByteCount);

        var references = new ImageFileStreamReference[payloads.Count];
        for (int index = 0; index < payloads.Count; index++)
        {
            int byteCount = payloads[index].Length;
            if (logicalOffsets[index] is not { } logicalOffset)
            {
                references[index] = new ImageFileStreamReference(
                    new DbHeaderImageStreamEntry(0, 0, 0, 0, 0, -1),
                    byteLength: 0);
                continue;
            }

            int firstPageIndex = logicalOffset /
                PackedStreamEncoder.LogicalPageSize;
            int lastPageIndex = checked(logicalOffset + byteCount - 1) /
                PackedStreamEncoder.LogicalPageSize;
            PackedStreamPage firstPage = packed.Pages[firstPageIndex];
            PackedStreamPage lastPage = packed.Pages[lastPageIndex];
            uint streamOffset = checked((uint)logicalOffset);
            var entry = new DbHeaderImageStreamEntry(
                fileIndex,
                SourceStart: checked((uint)(prefixByteCount + firstPage.EncodedOffset)),
                SourceEnd: checked((uint)(prefixByteCount + lastPage.EncodedEnd)),
                BlockOffset: streamOffset & 0xffff,
                StreamOffset: streamOffset,
                SerializedOffset: -1);
            references[index] = new ImageFileStreamReference(entry, byteCount);
        }

        return new ImageFilePackage(packageBytes, references);
    }

    private static int Align(int value, int alignment) => checked(
        (value + alignment - 1) & -alignment);
}
