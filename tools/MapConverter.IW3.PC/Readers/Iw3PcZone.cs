using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MapConverter.IW3.PC.Assets;

namespace MapConverter.IW3.PC.Readers;

/// <summary>Decoded IW3 PC bytes with 32-bit zone pointers resolved to offsets in the file data.</summary>
public sealed partial class Iw3PcZone
{
    private readonly MemoryStream data = new();
    private readonly Dictionary<int, int> pointers = [];
    private readonly List<Iw3PcAsset> assets = [];
    private readonly List<string> scriptStrings = [];

    private Iw3PcZone() { }

    public IReadOnlyList<Iw3PcAsset> Assets => assets;
    public IReadOnlyList<string> ScriptStrings => scriptStrings;

    public ReadOnlySpan<byte> Bytes(int offset, int length)
    {
        if (length < 0 || offset < 0 || offset > data.Length - length)
            throw new InvalidDataException($"IW3 PC byte range {offset}+{length} is outside the decoded zone.");
        return data.GetBuffer().AsSpan(offset, length);
    }

    internal int Int32(int field) => BinaryPrimitives.ReadInt32LittleEndian(Bytes(field, 4));
    internal ushort UInt16(int field) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(field, 2));

    internal float[] Floats(int offset, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(Bytes(offset + i * 4, 4));
            if (!float.IsFinite(values[i]))
                throw new InvalidDataException($"Non-finite IW3 PC float at offset 0x{offset + i * 4:X}.");
        }
        return values;
    }

    public int Pointer(int fieldOffset)
    {
        if (pointers.TryGetValue(fieldOffset, out int target))
        {
            if (target < 0)
                throw new InvalidDataException($"IW3 PC runtime pointer at decoded offset 0x{fieldOffset:X} has no serialized payload.");
            return target;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(Bytes(fieldOffset, 4)) == 0)
            return 0;
        throw new InvalidDataException($"Unresolved IW3 PC pointer field at decoded offset 0x{fieldOffset:X}.");
    }

    public string String(int pointerFieldOffset)
    {
        int start = Pointer(pointerFieldOffset);
        if (start == 0) return string.Empty;
        ReadOnlySpan<byte> remaining = Bytes(start, checked((int)data.Length - start));
        int length = remaining.IndexOf((byte)0);
        if (length < 0)
            throw new InvalidDataException($"Unterminated IW3 PC string at 0x{start:X}.");
        return Encoding.Latin1.GetString(remaining[..length]);
    }

    /// <summary>Loads the little-endian, zlib-compressed IW3 PC fastfile.</summary>
    public static Iw3PcZone Load(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var file = File.OpenRead(path);
        try
        {
            Span<byte> header = stackalloc byte[12];
            file.ReadExactly(header);
            if (!header[..8].SequenceEqual("IWffu100"u8) || BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != 5)
                throw new InvalidDataException("Expected the IWffu100 version 5 container.");
            using var decoded = new ZLibStream(file, CompressionMode.Decompress);
            var zone = new Iw3PcZone();
            new Loader(zone, decoded, token).Load();
            return zone;
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or OverflowException or InsufficientExecutionStackException)
        {
            throw new InvalidDataException($"Cannot read IW3 PC fastfile '{path}': {error.Message}", error);
        }
    }

    private sealed partial class Loader(Iw3PcZone zone, Stream input, CancellationToken token)
    {
        private const int Temp = 0, Virtual = 4;
        private const long MaximumBlockMemory = 0x3C000000;
        private const uint Following = uint.MaxValue, Insert = uint.MaxValue - 1;
        private readonly int[] sizes = new int[9];
        private readonly int[] cursors = new int[9];
        private readonly List<(int Logical, int Physical, int Length)>[] ranges =
            Enumerable.Range(0, 9).Select(_ => new List<(int, int, int)>()).ToArray();
        private readonly Dictionary<uint, int> inserts = [];
        private readonly Dictionary<int, int[]> sharedDependencies = [];
        private readonly Dictionary<int, int> assetIds = [];
        private HashSet<int> dependencies = [];
        private int position;
        private long end = int.MaxValue;
        private readonly byte[] buffer = new byte[65536];

        private int Int(int field) => BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(field, 4));
        private int Short(int field) => BinaryPrimitives.ReadUInt16LittleEndian(zone.Bytes(field, 2));
        private int Byte(int field) => zone.Bytes(field, 1)[0];

        private void Ensure(int exclusiveEnd)
        {
            if (exclusiveEnd < 0 || exclusiveEnd > end)
                throw new InvalidDataException($"IW3 PC field exceeds the declared zone size at 0x{position:X}.");
            while (zone.data.Length < exclusiveEnd)
            {
                token.ThrowIfCancellationRequested();
                int count = input.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, end - zone.data.Length)));
                if (count == 0) throw new EndOfStreamException($"Zone ended before decoded offset 0x{exclusiveEnd:X}.");
                zone.data.Write(buffer, 0, count);
            }
        }

        internal void Load()
        {
            Ensure(60);
            end = 44L + (uint)Int(0);
            if (end < 60)
                throw new InvalidDataException("IW3 PC zone declares less than its required header size.");
            if (end > int.MaxValue)
                throw new InvalidDataException("IW3 PC zone exceeds the supported decoded address space.");
            long blockMemory = 0;
            for (int i = 0; i < sizes.Length; ++i)
            {
                uint size = unchecked((uint)Int(8 + i * 4));
                blockMemory += size;
                if (size > int.MaxValue || blockMemory > MaximumBlockMemory)
                    throw new InvalidDataException("IW3 PC zone block sizes exceed the OAT memory limit.");
                sizes[i] = (int)size;
            }
            position = 60;
            int strings = Int(44), roots = Int(52);
            if (strings is < 0 or > 65536 || roots < 0)
                throw new InvalidDataException("Invalid IW3 PC script string or root asset count.");
            int table = RootTable(48, strings, 4);
            for (int i = 0; i < strings; ++i)
            {
                int field = table + i * 4;
                XString(field);
                zone.scriptStrings.Add(zone.String(field));
            }

            table = RootTable(56, roots, 8);
            for (int i = 0; i < roots; ++i)
                if (Int(table + i * 8) != 24) _ = Layout(Int(table + i * 8));
            for (int i = 0; i < roots; ++i)
            {
                token.ThrowIfCancellationRequested();
                int row = table + i * 8;
                if (Int(row) != 24) Asset(row + 4, Int(row));
            }
        }

        private int RootTable(int field, int count, int stride)
        {
            uint raw = (uint)Int(field);
            if (raw == 0 && count == 0) return 0;
            if (raw != Following) throw new InvalidDataException($"Invalid IW3 PC root table pointer 0x{raw:X8} at 0x{field:X}.");
            return Take(Virtual, 4, checked(count * stride));
        }

        private int Reserve(int block, int align, int length)
        {
            int logical = checked((cursors[block] + align - 1) & -align);
            if (length < 0 || sizes[block] < logical || length > sizes[block] - logical)
                throw new InvalidDataException($"IW3 PC block {block} exceeds its {sizes[block]} bytes at offset 0x{logical:X}.");
            cursors[block] = logical + length;
            return logical;
        }

        private int Take(int block, int align, int length)
        {
            int logical = Reserve(block, align, length);
            if (block is >= 1 and <= 3) return 0;
            int start = position;
            Ensure(checked(start + length));
            position += length;
            if (block != Temp && length > 0)
            {
                var spans = ranges[block];
                if (spans.Count > 0 && spans[^1] is var last &&
                    last.Logical + last.Length == logical && last.Physical + last.Length == start)
                    spans[^1] = (last.Logical, last.Physical, last.Length + length);
                else spans.Add((logical, start, length));
            }
            return start;
        }

        private int Resolve(uint raw, bool alias)
        {
            uint address = raw - 1;
            if (alias && inserts.TryGetValue(address, out int inserted)) return inserted;
            int block = (int)(address >> 28), logical = (int)(address & 0x0FFFFFFF);
            if (block >= ranges.Length || logical >= sizes[block])
                throw new InvalidDataException($"Invalid IW3 PC zone address 0x{raw:X8}.");
            var spans = ranges[block];
            int low = 0, high = spans.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (spans[mid].Logical <= logical) low = mid + 1;
                else high = mid;
            }
            if (low == 0) throw new InvalidDataException($"IW3 PC pointer 0x{raw:X8} precedes the loaded block data.");
            var range = spans[low - 1];
            int delta = logical - range.Logical;
            if (delta >= range.Length) throw new InvalidDataException($"IW3 PC pointer 0x{raw:X8} has no loaded data.");
            int offset = range.Physical + delta;
            if (!alias) return offset;
            return zone.pointers.TryGetValue(offset, out int target) ? target : throw new InvalidDataException($"Unresolved IW3 PC alias 0x{raw:X8} at decoded offset 0x{offset:X}.");
        }

        private void XString(int field)
        {
            uint raw = (uint)Int(field);
            if (raw == 0) return;
            if (raw != Following)
            {
                zone.pointers[field] = Resolve(raw, false);
                return;
            }
            int scan = position;
            while (true)
            {
                Ensure(checked(scan + 1));
                ReadOnlySpan<byte> bytes = zone.Bytes(scan, checked((int)Math.Min(zone.data.Length, end) - scan));
                int nul = bytes.IndexOf((byte)0);
                if (nul >= 0)
                {
                    zone.pointers[field] = Take(Virtual, 1, checked(scan - position + nul + 1));
                    return;
                }
                scan += bytes.Length;
            }
        }

        // Reusable pointers in TEMP are indirect and may reserve an insertion slot in VIRTUAL.
        // Normal blocks encode the data address itself. Plain array pointers are presence flags.
        private void Data(int field, int count, int stride, int align = 4, Action<int>? visit = null,
            bool shared = false, int block = Virtual)
        {
            uint raw = (uint)Int(field);
            if (raw == 0) return;
            bool indirect = block == Temp;
            if (shared && raw != Following && !(indirect && raw == Insert))
            {
                int target = Resolve(raw, indirect);
                zone.pointers[field] = target;
                if (visit != null && sharedDependencies.TryGetValue(target, out int[]? children))
                    dependencies.UnionWith(children);
                else if (visit != null)
                    throw new InvalidDataException($"Missing IW3 PC dependency registration for pointer 0x{raw:X8} at 0x{field:X}.");
                return;
            }

            int saved = cursors[Temp];
            if (indirect && shared && raw == Insert)
            {
                int slot = Reserve(Virtual, 4, 4);
                inserts.Add(0x40000000u | (uint)slot, position);
            }
            int start = Take(block, align, checked(count * stride));
            // Runtime blocks are zero-filled by OAT and consume no compressed bytes.
            // Their data is not represented in the decoded zone address space.
            zone.pointers[field] = block is >= 1 and <= 3 ? -1 : start;
            if (visit != null)
            {
                if (block is >= 1 and <= 3)
                    throw new InvalidDataException("An IW3 PC runtime block requires unsupported nested traversal.");
                System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
                var parent = dependencies;
                if (shared) dependencies = [];
                for (int i = 0; i < count; ++i)
                {
                    token.ThrowIfCancellationRequested();
                    visit(start + i * stride);
                }
                if (shared)
                {
                    sharedDependencies[start] = dependencies.ToArray();
                    parent.UnionWith(dependencies);
                    dependencies = parent;
                }
            }
            if (indirect) cursors[Temp] = saved;
        }

        private void Asset(int field, int type)
        {
            uint raw = (uint)Int(field);
            if (raw == 0) return;
            var (name, size, nameOffset) = Layout(type);
            bool temporary = type != 32;
            if (raw != Following && !(temporary && raw == Insert))
            {
                int target = Resolve(raw, temporary);
                zone.pointers[field] = target;
                if (assetIds.TryGetValue(target, out int id)) dependencies.Add(id);
                else if (type != 8) throw new InvalidDataException($"Missing IW3 PC dependency registration for pointer 0x{raw:X8} at 0x{field:X}.");
                return;
            }
            int saved = cursors[Temp];
            if (raw == Insert)
                inserts.Add(0x40000000u | (uint)Reserve(Virtual, 4, 4), position);
            int body = Take(temporary ? Temp : Virtual, 4, size);
            zone.pointers[field] = body;
            var parent = dependencies;
            dependencies = [];
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            ReadAsset(type, body);
            string assetName = zone.String(body + nameOffset);
            if (assetName.Length > 0)
            {
                int id = zone.assets.Count;
                assetIds.Add(body, id);
                var asset = new Iw3PcAsset(id, name, assetName, body, dependencies.Order().ToArray());
                if (!assetName.StartsWith(',')) CollectNamedReferences(asset);
                zone.assets.Add(asset);
                parent.Add(id);
            }
            else if (type is not (8 or 26))
                throw new InvalidDataException($"Unnamed IW3 PC {name} at decoded offset 0x{body:X}.");
            dependencies = parent;
            if (temporary) cursors[Temp] = saved;
        }
    }
}
