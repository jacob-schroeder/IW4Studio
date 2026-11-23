using System.Globalization;
using IW4.Game.Assets.Image;
using MapConverter.Game.IW4.Xbox.FastFiles;
using static MapConverter.Game.IW4.Xbox.FastFiles.XboxWire;

namespace MapConverter.Game.IW4.Xbox.Images;

/// <summary>One encoded, tiled Xenon mip chain and its compressed byte range in imagefile7.pak.</summary>
internal sealed record XboxImageStream(string Name, ushort Width, ushort Height, uint Format, byte Levels,
    uint PixelBytes, byte MapType, uint Start, uint End)
{
    internal static List<XboxImageStream> Read(string manifestPath)
    {
        long fileLength = new FileInfo(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, "imagefile7.pak")).Length;
        var result = new List<XboxImageStream>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint previousEnd = 0;
        foreach (string line in File.ReadLines(manifestPath))
        {
            string[] c = line.Split('\t');
            if (c.Length != 9 || string.IsNullOrWhiteSpace(c[0]) || !seen.Add(c[0]))
                throw new InvalidDataException($"Invalid Xbox image stream row '{line}'.");
            uint Number(int i, uint max = uint.MaxValue) => uint.TryParse(c[i], NumberStyles.None, CultureInfo.InvariantCulture, out uint n) && n <= max
                ? n : throw new InvalidDataException($"Invalid numeric field for Xbox image '{c[0]}'.");
            var row = new XboxImageStream(c[0], (ushort)Number(1, ushort.MaxValue), (ushort)Number(2, ushort.MaxValue), Number(3),
                (byte)Number(4, byte.MaxValue), Number(5), (byte)Number(6, byte.MaxValue), Number(7), Number(8));
            if (row.Width == 0 || row.Height == 0 || row.MapType is not (3 or 5) ||
                row.Levels is 0 or > 63 || row.PixelBytes is 0 or >= (1u << 26) ||
                row.Start != previousEnd || row.End <= row.Start || row.End > fileLength)
                throw new InvalidDataException($"Invalid Xenon texture or stream range for '{row.Name}'.");
            previousEnd = row.End;
            result.Add(row);
        }
        if (result.Count == 0 || previousEnd != fileLength)
            throw new InvalidDataException("Xbox image stream table does not cover the imagefile7.pak beside it.");
        return result;
    }

    internal void Write(XboxZoneStream zone, GfxImageAsset source)
    {
        if (MapType != (byte)source.MapType)
            throw new InvalidDataException($"Xbox texture '{Name}' has a different shape from its source image.");
        byte[] header = new byte[0x70];
        U32(header, 0x34, Format);
        header[0x38] = MapType;
        header[0x39] = (byte)source.TextureSemantic;
        header[0x3a] = (byte)source.Category;
        U16(header, 0x40, 1); U16(header, 0x42, 1); U16(header, 0x44, 1);
        header[0x46] = header[0x47] = 1;
        U16(header, 0x4c, Width); U16(header, 0x4e, Height);
        U32(header, 0x50, PixelBytes | ((uint)Levels << 26));
        U32(header, 0x6c, Inline);
        zone.Write(header, 4, XboxBlock.Temp);
        zone.String(Name);
    }
}
