using System.Buffers.Binary;
using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace IW4.Render.Export;

public static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] WriteRgba(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(output, "IHDR"u8, ihdr);

        using var filtered = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            filtered.WriteByte(0);
            filtered.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(filtered.GetBuffer(), 0, (int)filtered.Length);

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        uint crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (byte value in type)
            crc = Update(crc, value);
        foreach (byte value in data)
            crc = Update(crc, value);
        return ~crc;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320 ^ (crc >> 1);
        return crc;
    }
}
