using System.Buffers.Binary;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets;

internal static class Iw3PcImageExporter
{
    internal static string FileName(string name) => "images/" + name.Replace('*', '_') + ".iwi";

    internal static bool HasEmbeddedPixels(Iw3PcZone zone, Iw3PcAsset asset)
    {
        int load = zone.Pointer(asset.Offset + 4);
        return load != 0 && zone.Int32(load + 12) != 0;
    }

    internal static byte[]? ReadIwi(Iw3PcZone zone, Iw3PcAsset asset)
    {
        if (!HasEmbeddedPixels(zone, asset)) return null;
        int load = zone.Pointer(asset.Offset + 4);
        int size = zone.Int32(load + 12);
        byte flags = zone.Bytes(load + 1, 1)[0];
        int format = BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(load + 8, 4));
        int width = BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(load + 2, 2));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(load + 4, 2));
        int depth = BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(load + 6, 2));
        bool volume = (flags & 8) != 0, cube = (flags & 4) != 0;
        if (cube && volume || cube && width != height)
            throw new InvalidDataException($"Invalid embedded IW3 PC image type in '{asset.Name}'.");
        if (!volume) depth = 1;
        if (volume && depth == 0)
            throw new InvalidDataException($"Invalid embedded IW3 PC volume depth in '{asset.Name}'.");
        if (width == 0 || height == 0 || size is < 0 or > 256 * 1024 * 1024)
            throw new InvalidDataException($"Invalid embedded IW3 PC image '{asset.Name}'.");
        (Iwi6Format iwiFormat, int bytesPerPixel, int bytesPerBlock) = Format(format);
        if (iwiFormat == 0)
            throw new NotSupportedException($"D3D9 image format 0x{format:X8} in '{asset.Name}' has no IWI6 representation.");
        bool mipmaps = (flags & 2) == 0;
        var levels = new List<(int Offset, int Size)>();
        int faceByteCount = 0;
        int faces = cube ? 6 : 1;
        for (int w = width, h = height, d = depth; ;
            w = Math.Max(1, w / 2), h = Math.Max(1, h / 2), d = Math.Max(1, d / 2))
        {
            int bytes = bytesPerBlock == 0 ? checked(w * h * d * bytesPerPixel) :
                checked(((w + 3) / 4) * ((h + 3) / 4) * d * bytesPerBlock);
            levels.Add((faceByteCount, bytes));
            faceByteCount = checked(faceByteCount + bytes);
            if (!mipmaps || (w == 1 && h == 1 && d == 1)) break;
        }
        int expectedSize = checked(faces * faceByteCount);
        if (expectedSize != size)
            throw new InvalidDataException($"Embedded IW3 image '{asset.Name}' has {size} bytes; expected {expectedSize}.");
        ReadOnlySpan<byte> pixels = zone.Bytes(load + 16, size);
        byte[] result = new byte[checked(Iwi6Header.Size + size)];
        var header = new Iwi6Header(iwiFormat,
            (mipmaps ? 0 : Iwi6Flags.NoMipMaps) | (cube ? Iwi6Flags.CubeMap : 0) | (volume ? Iwi6Flags.Volume : 0),
            (ushort)width, (ushort)height, (ushort)depth, new uint[4]);
        int destination = Iwi6Header.Size;
        for (int level = levels.Count - 1; level >= 0; level--)
        {
            (int offset, int length) = levels[level];
            // PC fastfiles store a full, top-first mip chain per face.
            // IWI6 stores all faces of each mip together, smallest mip first.
            for (int face = 0; face < faces; face++)
            {
                pixels.Slice(face * faceByteCount + offset, length).CopyTo(result.AsSpan(destination, length));
                destination += length;
            }
            if (level < 4)
                header.FileSizeForPicmip[level] = (uint)destination;
        }
        header.Write(result);
        return result;
    }

    // Only D3D9 formats whose byte layout is accepted by OAT's IWI6 writer.
    private static (Iwi6Format IwiFormat, int BytesPerPixel, int BytesPerBlock) Format(int format) => format switch
    {
        20 => (Iwi6Format.Rgb, 3, 0), // R8G8B8 / B8_G8_R8
        21 => (Iwi6Format.Rgba, 4, 0), // A8R8G8B8 / B8_G8_R8_A8
        28 => (Iwi6Format.Alpha, 1, 0), // A8
        50 => (Iwi6Format.Luminance, 1, 0), // L8
        51 => (Iwi6Format.LuminanceAlpha, 2, 0), // A8L8
        0x31545844 => (Iwi6Format.Dxt1, 0, 8),  // DXT1
        0x33545844 => (Iwi6Format.Dxt3, 0, 16), // DXT3
        0x35545844 => (Iwi6Format.Dxt5, 0, 16), // DXT5
        _ => (0, 0, 0)
    };
}
