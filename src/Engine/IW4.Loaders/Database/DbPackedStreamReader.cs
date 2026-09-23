using System.Buffers;
using IW4.Loaders.Compression;
using IW4.Runtime.Diagnostics;
using IW4.Game.IO;

namespace IW4.Loaders.Database;

public sealed class DbPackedStreamReader
{
    private const ushort ZoneBlockTerminator = 1;
    private const int FullBlockSize = 0x10000;

    public byte[] ReadZone(
        FastFileCursor cursor,
        uint declaredFileSize,
        LoadDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var output = new ArrayBufferWriter<byte>();
        int availableEnd = cursor.Length;
        if (availableEnd < cursor.Offset + sizeof(ushort))
        {
            throw new InvalidDataException(
                $"Packed stream has no complete block-size word in the available file range " +
                $"0x{cursor.Offset:X}..0x{availableEnd:X}.");
        }

        bool sawTerminator = false;
        while (cursor.Offset < availableEnd)
        {
            if (cursor.Offset > availableEnd - sizeof(ushort))
                throw new InvalidDataException("Packed stream ends in a truncated block-size word.");
            ushort blockSize = cursor.ReadUInt16();

            if (blockSize == ZoneBlockTerminator)
            {
                if ((uint)cursor.Offset != declaredFileSize)
                    diagnostics.Warn(
                        $"DB header FileSize 0x{declaredFileSize:X} does not match " +
                        $"the actual packed stream end 0x{cursor.Offset:X}.");
                sawTerminator = true;
                TryConsumeTrailingTerminatorWord(cursor);
                break;
            }

            int compressedSize = blockSize == 0 ? FullBlockSize : blockSize;
            if (compressedSize > availableEnd - cursor.Offset)
            {
                throw new InvalidDataException(
                    $"Packed block at 0x{cursor.Offset - sizeof(ushort):X} declares 0x{compressedSize:X} " +
                    $"payload byte(s) past the actual file end 0x{availableEnd:X}.");
            }
            ReadOnlyMemory<byte> compressed = cursor.ReadMemory(compressedSize);

            if (blockSize == 0)
            {
                compressed.Span.CopyTo(output.GetSpan(FullBlockSize));
                output.Advance(FullBlockSize);
                continue;
            }

            int frameOffset = cursor.Offset - compressedSize - sizeof(ushort);
            try
            {
                int decompressedSize = Deflate.DecompressPs3HeaderlessZlib(
                    compressed,
                    output.GetSpan(FullBlockSize)[..FullBlockSize],
                    out uint storedAdler,
                    out uint calculatedAdler);
                output.Advance(decompressedSize);
                if (calculatedAdler != storedAdler)
                {
                    diagnostics.Warn(
                        $"Compressed PS3 packed-zone frame at 0x{frameOffset:X} has an Adler-32 mismatch: " +
                        $"stored 0x{storedAdler:X8}, calculated 0x{calculatedAdler:X8}; " +
                        "continuing with inflated data.");
                }
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(
                    $"Compressed PS3 packed-zone frame at 0x{frameOffset:X} is invalid: " +
                    exception.Message,
                    exception);
            }
        }

        if (!sawTerminator)
        {
            diagnostics.Warn(
                $"Packed stream has no terminator in the available file range ending at 0x{availableEnd:X}; " +
                "continuing with all complete packed blocks.");
        }
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
