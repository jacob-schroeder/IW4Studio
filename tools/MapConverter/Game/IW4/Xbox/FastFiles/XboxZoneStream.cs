using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using IW4.Game.Math;
using MapConverter.Game.IW4.Xbox.Images;

namespace MapConverter.Game.IW4.Xbox.FastFiles;

internal enum XboxBlock { Temp, Physical, Runtime, Virtual, Large, Callback }

internal static class XboxAssetType
{
    internal const int PhysPreset = 0, PhysCollmap = 1, XAnim = 2, XModel = 4, Material = 5,
        TechniqueSet = 7, Image = 8, ClipMap = 13, ComWorld = 14, GameWorldMp = 16,
        MapEnts = 17, FxWorld = 18, GfxWorld = 19, LightDef = 20, Localize = 25, Fx = 28,
        RawFile = 34, StringTable = 35;
}

internal static class XboxWire
{
    internal const uint Inline = uint.MaxValue;
    internal static void U32(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
    internal static void I32(byte[] data, int at, int value) => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(at), value);
    internal static void U16(byte[] data, int at, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), value);
    internal static void Float(byte[] data, int at, float value) => BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(at), value);
    internal static void Vector(byte[] data, int at, Vec3 value)
    {
        Float(data, at, value.X);
        Float(data, at + 4, value.Y);
        Float(data, at + 8, value.Z);
    }
    internal static uint Present(int count) => count == 0 ? 0u : Inline;
}

internal sealed record XboxAsset(int Type, string Name, Action<XboxZoneStream> Emit);

/// <summary>IW4 Xenon has a sequential source stream and six independently aligned destination blocks.</summary>
internal sealed class XboxZoneStream
{
    private readonly MemoryStream tape = new();
    private readonly int[] sizes = new int[6];
    private readonly Dictionary<uint, (int Position, int Size)> writes = [];
    private readonly Dictionary<(int Type, string Name), uint> aliases = [];
    private readonly Dictionary<string, ushort> strings = new(StringComparer.Ordinal);
    private int tempPosition;

    internal XboxZoneStream(IReadOnlyList<XboxAsset> assets, IReadOnlyList<string?> scriptStrings)
    {
        byte[] list = new byte[16];
        XboxWire.I32(list, 0, scriptStrings.Count);
        XboxWire.U32(list, 4, XboxWire.Present(scriptStrings.Count));
        XboxWire.I32(list, 8, assets.Count);
        XboxWire.U32(list, 12, XboxWire.Present(assets.Count));
        tape.Write(list);
        if (scriptStrings.Count > 0)
        {
            byte[] table = new byte[checked(scriptStrings.Count * 4)];
            for (int i = 0; i < scriptStrings.Count; i++)
                if (scriptStrings[i] is not null)
                    XboxWire.U32(table, i * 4, XboxWire.Inline);
            Write(table, 4);
            for (int i = 0; i < scriptStrings.Count; i++)
                if (scriptStrings[i] is string text)
                {
                    String(text);
                    strings.TryAdd(text, checked((ushort)i));
                }
        }

        var rows = new byte[checked(assets.Count * 8)];
        for (int i = 0; i < assets.Count; i++)
        {
            XboxWire.I32(rows, i * 8, assets[i].Type);
            XboxWire.U32(rows, i * 8 + 4, 0xFFFFFFFE);
        }
        Write(rows, 4);
        foreach (var asset in assets)
        {
            uint cell = Reserve(4, 4, XboxBlock.Virtual);
            aliases.Add((asset.Type, asset.Name.TrimStart(',')), cell);
            try
            {
                using (TempScope()) asset.Emit(this);
            }
            catch (OverflowException e)
            {
                throw new InvalidDataException($"A field in Xbox asset '{asset.Name}' exceeds its wire format.", e);
            }
        }
    }

    internal uint Asset(int type, string name) =>
        aliases.TryGetValue((type, name.TrimStart(',')), out uint alias) ? alias :
            throw new InvalidDataException($"Xbox asset {type} '{name}' must be emitted before its consumer.");

    internal ushort ScriptString(string? value) => value != null && strings.TryGetValue(value, out ushort index) ? index :
        throw new InvalidDataException($"Missing zone script string '{value}'.");

    internal IDisposable TempScope() => new TempFrame(this, tempPosition);
    private sealed class TempFrame(XboxZoneStream stream, int saved) : IDisposable
    {
        public void Dispose() => stream.tempPosition = saved;
    }

    internal uint Reserve(int size, int alignment, XboxBlock block)
    {
        if (size < 0 || alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new InvalidDataException("Invalid Xbox block allocation.");
        int index = (int)block;
        int cursor = block == XboxBlock.Temp ? tempPosition : sizes[index];
        int start = checked(cursor + alignment - 1) & -alignment;
        int end = checked(start + size);
        if (end >= 0x10000000) throw new InvalidDataException($"Xbox {block} exceeds its 28-bit address space.");
        sizes[index] = Math.Max(sizes[index], end);
        if (block == XboxBlock.Temp) tempPosition = end;
        return (((uint)index << 28) | (uint)start) + 1;
    }

    internal uint Write(byte[] bytes, int alignment = 1, XboxBlock block = XboxBlock.Virtual)
    {
        if (block is XboxBlock.Runtime or XboxBlock.Callback)
            throw new InvalidDataException($"Xbox {block} requires a reservation without source bytes.");
        uint address = Reserve(bytes.Length, alignment, block);
        if (bytes.Length != 0)
        {
            writes[address] = (checked((int)tape.Position), bytes.Length);
            tape.Write(bytes);
        }
        return address;
    }

    internal void Patch(uint address, int offset, uint value)
    {
        var (position, size) = writes[address];
        if (offset < 0 || offset > size - 4) throw new InvalidDataException("Xbox pointer fixup is outside its record.");
        XboxWire.U32(tape.GetBuffer(), checked(position + offset), value);
    }

    internal void String(string? value)
    {
        if (value == null || value.Any(c => c == '\0' || c > byte.MaxValue))
            throw new InvalidDataException("Expected a NUL-free Latin-1 string in the Xbox zone.");
        Write(Encoding.Latin1.GetBytes(value + '\0'));
    }

    internal void Save(string path, IReadOnlyList<XboxImageStream> streams)
    {
        const int slotsPerImage = 4;
        const int streamEntrySize = 12;
        int entryCount = checked(streams.Count * slotsPerImage);
        if (entryCount > 0x3800)
            throw new InvalidDataException($"Xbox fastfile image table exceeds the 0x3800-entry loader limit ({entryCount} entries).");
        byte[] header = new byte[checked(37 + entryCount * streamEntrySize)];
        "IWffu100"u8.CopyTo(header);
        XboxWire.I32(header, 8, 0x10D);
        header[12] = 1;
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(13), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        XboxWire.I32(header, 21, 1);
        XboxWire.I32(header, 25, entryCount);
        for (int i = 0; i < streams.Count; i++)
        {
            int at = 29 + i * slotsPerImage * streamEntrySize;
            XboxWire.I32(header, at, 7);
            XboxWire.U32(header, at + 4, streams[i].Start);
            XboxWire.U32(header, at + 8, streams[i].End);
        }

        byte[] zoneHeader = new byte[32];
        XboxWire.I32(zoneHeader, 0, checked((int)tape.Length));
        for (int i = 0; i < sizes.Length; i++) XboxWire.I32(zoneHeader, 8 + i * 4, sizes[i]);
        string staged = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetRandomFileName());
        try
        {
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                output.Write(header);
                using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(zoneHeader);
                    tape.WriteTo(zlib);
                }
                uint end = checked((uint)output.Length);
                byte[] fileSizes = new byte[8];
                XboxWire.U32(fileSizes, 0, end);
                XboxWire.U32(fileSizes, 4, end);
                output.Position = header.Length - 8;
                output.Write(fileSizes);
            }
            File.Move(staged, path);
        }
        finally { File.Delete(staged); }
    }
}
