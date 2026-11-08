using System.Buffers.Binary;
using IW4.Game.Assets.Image;
using MapConverter.Game.IW3.PC.Images;
using MapConverter.Game.IW3.PS3.Extraction;

namespace MapConverter.Game.IW3.PS3.Images;

internal sealed class Iw3Ps3ImageLibrary(string ownedNamePrefix)
{
    private readonly Dictionary<string, (byte[] Root, ReadOnlyMemory<byte> Payload)> _sources =
        new(StringComparer.Ordinal);
    private readonly Dictionary<(string Path, int RootOffset), (byte[] Root, ReadOnlyMemory<byte> Payload)>
        _sourcesByLocation = [];
    private readonly Dictionary<string, Iw3Iwi6StreamedImageCompilation> _compiled = new(StringComparer.Ordinal);

    internal void AddFastFile(string path, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        var (data, end) = Iw3Ps3FastFileCatalog.ReadDecoded(path, token);
        int delayed = checked((int)(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)) +
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20))));
        int tail = checked(end - delayed);
        int queued = 0;
        for (int offset = 36; offset <= tail - 52; offset++)
        {
            if ((offset & 0xffff) == 0) token.ThrowIfCancellationRequested();
            if (!Iw3Ps3FastFileImageCompiler.TryReadImageHeader(data, offset, tail,
                    out var header, requireStreaming: false)) continue;
            int size = checked((int)header.PayloadByteCount);
            int payload;
            if (header.IsDeferred)
            {
                payload = checked(tail + queued);
                queued = checked(queued + size);
                if (queued > delayed) throw new InvalidDataException($"Image payloads exceed '{path}' delayed tail.");
            }
            else
            {
                payload = checked(offset + 52 + (header.Name?.Length + 1 ?? 0));
                if (payload > tail - size)
                    throw new InvalidDataException($"Image at 0x{offset:X} exceeds '{path}'.");
            }
            var source = (data.AsSpan(offset, 52).ToArray(), data.AsMemory(payload, size));
            if (!_sourcesByLocation.TryAdd((path, offset), source))
                throw new InvalidDataException($"Duplicate COD4 image root at 0x{offset:X} in '{path}'.");
            if (header.Name is { } name && !name.StartsWith(','))
            {
                if (_sources.TryGetValue(name, out var existing))
                {
                    if (!existing.Payload.Span.SequenceEqual(source.Item2.Span))
                        throw new InvalidDataException($"Conflicting COD4 image '{name}' in '{path}'.");
                }
                else _sources.Add(name, source);
            }
            if (!header.IsDeferred) offset = payload + size - 1;
        }
        if (queued != delayed)
            throw new InvalidDataException($"Image definitions in '{path}' account for {queued} of {delayed} delayed bytes.");
    }

    internal GfxImageAsset ResolveAt(string path, int rootOffset, string name)
    {
        path = Path.GetFullPath(path);
        if (!_sourcesByLocation.TryGetValue((path, rootOffset), out var source))
            throw new InvalidDataException(
                $"Missing COD4 PS3 image source at decoded 0x{rootOffset:X} in '{path}'.");
        name = name.TrimStart(',');
        if (_sources.TryGetValue(name, out var existing))
        {
            if (!existing.Payload.Span.SequenceEqual(source.Payload.Span))
                throw new InvalidDataException($"Conflicting COD4 PS3 image '{name}' at 0x{rootOffset:X}.");
        }
        else _sources.Add(name, source);
        return Resolve(name);
    }

    internal GfxImageAsset Resolve(string name)
    {
        name = name.TrimStart(',');
        if (_compiled.TryGetValue(name, out var image)) return image.Image;
        if (!_sources.TryGetValue(name, out var source))
            throw new InvalidDataException($"Missing COD4 PS3 image '{name}'.");
        image = Compile(ownedNamePrefix + name, source.Root, source.Payload);
        _compiled.Add(name, image);
        return image.Image;
    }

    internal IReadOnlyList<Iw3Iwi6StreamedImageCompilation> Compiled => _compiled.Values.ToArray();

    internal static Iw3Iwi6StreamedImageCompilation Compile(
        string name, ReadOnlySpan<byte> root, ReadOnlyMemory<byte> payload)
    {
        byte format = root[4];
        uint remap = BinaryPrimitives.ReadUInt32BigEndian(root[8..]);
        ushort width = BinaryPrimitives.ReadUInt16BigEndian(root[12..]);
        ushort height = BinaryPrimitives.ReadUInt16BigEndian(root[14..]);
        ushort depth = BinaryPrimitives.ReadUInt16BigEndian(root[16..]);
        if (root[7] == 0 && root[6] == 2 && depth == 1 && root[0x2a] == 3 &&
            remap == 0x0001aae4 && format is 0x85 or 0x86 or 0x87 or 0x88)
            return Iw3Iwi6StreamedImageCompiler.CompileTopLevelFirstPs3Payload(name, format,
                root[5], width, height, payload, TextureSemantic.TwoDimensional, useSrgbReads: false);
        return new(new GfxImageAsset
        {
            Name = name, Format = format, LevelCount = root[5],
            DimensionCount = (GfxImageDimension)root[6], MultiFaceControl = root[7],
            TextureControl1 = remap, Width = width, Height = height, Depth = depth,
            MemoryLocation = (GfxImageMemoryLocation)root[0x12], MinLodControl = root[0x13],
            RenderTargetPitch = BinaryPrimitives.ReadUInt32BigEndian(root[0x14..]),
            MapType = (MapType)BinaryPrimitives.ReadUInt32BigEndian(root),
            Category = (ImageCategory)root[0x2a],
            BaseWidth = width, BaseHeight = height, BaseDepth = depth, BaseLevelCount = root[5],
            // Inline fastfile pixels belong to the zone. Cached images enter
            // IW4's independent card-memory release path.
            CardMemory = checked((uint)payload.Length), Cached = GfxImageCached.No,
            PayloadByteCount = payload.Length, PayloadBytes = payload.ToArray()
        }, []);
    }
}
