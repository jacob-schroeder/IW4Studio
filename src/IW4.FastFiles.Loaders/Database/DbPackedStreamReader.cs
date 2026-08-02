using System.Buffers;
using IW4.FastFiles.Loaders.Compression;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Database;

public sealed class DbPackedStreamReader
{
    private const ushort ZoneBlockTerminator = 1;
    private const int FullBlockSize = 0x10000;

    public byte[] ReadZone(FastFileCursor cursor, uint fileSize)
    {
        if (fileSize > int.MaxValue)
            throw new InvalidDataException($"FileSize 0x{fileSize:X} does not fit in this reader.");

        var output = new ArrayBufferWriter<byte>();
        int packedEnd = checked((int)fileSize);
        if (packedEnd < cursor.Offset + sizeof(ushort) || packedEnd > cursor.Length)
        {
            throw new InvalidDataException(
                $"Packed stream end 0x{packedEnd:X} is outside the available file range " +
                $"0x{cursor.Offset:X}..0x{cursor.Length:X}.");
        }

        bool sawTerminator = false;
        while (cursor.Offset < packedEnd)
        {
            if (cursor.Offset > packedEnd - sizeof(ushort))
                throw new InvalidDataException("Packed stream ends in a truncated block-size word.");
            ushort blockSize = cursor.ReadUInt16();

            if (blockSize == ZoneBlockTerminator)
            {
                if (cursor.Offset != packedEnd)
                {
                    throw new InvalidDataException(
                        $"Packed stream terminator ended at 0x{cursor.Offset:X}, " +
                        $"but DB header FileSize ends at 0x{packedEnd:X}.");
                }
                sawTerminator = true;
                TryConsumeTrailingTerminatorWord(cursor);
                break;
            }

            int compressedSize = blockSize == 0 ? FullBlockSize : blockSize;
            if (compressedSize > packedEnd - cursor.Offset)
            {
                throw new InvalidDataException(
                    $"Packed block at 0x{cursor.Offset - sizeof(ushort):X} declares 0x{compressedSize:X} " +
                    $"payload byte(s) past FileSize 0x{packedEnd:X}.");
            }
            ReadOnlyMemory<byte> compressed = cursor.ReadMemory(compressedSize);

            if (blockSize == 0)
            {
                compressed.Span.CopyTo(output.GetSpan(FullBlockSize));
                output.Advance(FullBlockSize);
                continue;
            }

            try
            {
                int decompressedSize = Deflate.DecompressPs3HeaderlessZlib(
                    compressed,
                    output.GetSpan(FullBlockSize)[..FullBlockSize]);
                output.Advance(decompressedSize);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Compressed PS3 packed-zone frame at 0x{cursor.Offset - compressedSize - sizeof(ushort):X} is invalid: " +
                    exception.Message,
                    exception);
            }
        }

        if (!sawTerminator)
            throw new InvalidDataException($"Packed stream has no terminator at FileSize 0x{packedEnd:X}.");
        return output.WrittenSpan.ToArray();
    }

    private static void TryConsumeTrailingTerminatorWord(FastFileCursor cursor)
    {
        if (cursor.Remaining < sizeof(ushort))
            return;

        if (cursor.PeekUInt16() == ZoneBlockTerminator)
            cursor.Skip(sizeof(ushort));
    }
}
