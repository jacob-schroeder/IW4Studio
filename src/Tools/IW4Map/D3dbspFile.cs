using System.Buffers.Binary;

namespace IW4Map;

internal sealed class D3dbspFile
{
    private const uint Ident = 0x50534249;
    private const uint Version = 22;
    private const int HeaderSize = 12;
    private const int ChunkDescriptorSize = 8;
    private const int MaximumChunkCount = 100;

    private D3dbspFile(IReadOnlyList<D3dbspLump> lumps)
    {
        Lumps = lumps;
    }

    public IReadOnlyList<D3dbspLump> Lumps { get; }

    public ReadOnlySpan<byte> GetRequiredData(D3dbspLumpType type) =>
        Lumps.FirstOrDefault(lump => lump.Type == type)?.Data ??
        throw new InvalidDataException($"The d3dbsp has no {FormatType(type)} chunk.");

    public ReadOnlySpan<byte> GetOptionalData(D3dbspLumpType type) =>
        Lumps.FirstOrDefault(lump => lump.Type == type)?.Data ?? [];

    public bool HasLump(D3dbspLumpType type) =>
        Lumps.Any(lump => lump.Type == type);

    public static D3dbspFile Create(
        IReadOnlyList<(D3dbspLumpType Type, byte[] Data)> lumps)
    {
        ArgumentNullException.ThrowIfNull(lumps);
        if (lumps.Count > MaximumChunkCount)
        {
            throw new ArgumentException(
                $"A version 22 d3dbsp cannot contain more than {MaximumChunkCount} chunks.",
                nameof(lumps));
        }

        var seenTypes = new HashSet<D3dbspLumpType>();
        var created = new D3dbspLump[lumps.Count];
        for (int index = 0; index < lumps.Count; index++)
        {
            (D3dbspLumpType type, byte[] data) = lumps[index];
            ArgumentNullException.ThrowIfNull(data);
            if (!seenTypes.Add(type))
            {
                throw new ArgumentException(
                    $"The d3dbsp contains duplicate {FormatType(type)} chunks.",
                    nameof(lumps));
            }

            var payload = data.ToArray();
            created[index] = new D3dbspLump(
                type,
                payload,
                new byte[GetPaddingLength(checked((uint)payload.Length))]);
        }

        return new D3dbspFile(Array.AsReadOnly(created));
    }

    public static D3dbspFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);

        Span<byte> header = stackalloc byte[HeaderSize];
        ReadExactly(stream, header, "header");

        uint ident = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (ident != Ident)
            throw new InvalidDataException("The file does not begin with the d3dbsp 'IBSP' identifier.");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (version != Version)
            throw new InvalidDataException($"Unsupported d3dbsp version {version}; IW4Map requires version {Version}.");

        uint chunkCount = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (chunkCount > MaximumChunkCount)
        {
            throw new InvalidDataException(
                $"The d3dbsp declares {chunkCount} chunks; version 22 supports at most {MaximumChunkCount}.");
        }

        var descriptors = new (D3dbspLumpType Type, uint Length)[chunkCount];
        var seenTypes = new HashSet<D3dbspLumpType>();
        Span<byte> descriptor = stackalloc byte[ChunkDescriptorSize];
        for (int index = 0; index < descriptors.Length; index++)
        {
            ReadExactly(stream, descriptor, $"chunk descriptor {index}");
            var type = (D3dbspLumpType)BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
            if (!seenTypes.Add(type))
            {
                throw new InvalidDataException(
                    $"The d3dbsp directory contains duplicate {FormatType(type)} chunks.");
            }

            descriptors[index] = (
                type,
                BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..]));
        }

        long declaredPayloadSize = 0;
        foreach ((D3dbspLumpType _, uint length) in descriptors)
            declaredPayloadSize = checked(declaredPayloadSize + length + GetPaddingLength(length));

        long remainingFileSize = stream.Length - stream.Position;
        if (declaredPayloadSize != remainingFileSize)
        {
            throw new InvalidDataException(
                $"The d3dbsp directory declares {declaredPayloadSize} payload bytes, but the file contains {remainingFileSize} bytes after the directory.");
        }

        var lumps = new D3dbspLump[descriptors.Length];
        for (int index = 0; index < descriptors.Length; index++)
        {
            (D3dbspLumpType type, uint length) = descriptors[index];
            if (length > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Chunk {index} ({FormatType(type)}) is too large for this process: {length} bytes.");
            }

            var data = new byte[(int)length];
            ReadExactly(stream, data, $"chunk {index} ({FormatType(type)}) payload");

            int paddingLength = GetPaddingLength(length);
            var padding = new byte[paddingLength];
            if (paddingLength != 0)
                ReadExactly(stream, padding, $"chunk {index} ({FormatType(type)}) padding");

            lumps[index] = new D3dbspLump(type, data, padding);
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException(
                $"The d3dbsp has {stream.Length - stream.Position} trailing bytes after its declared chunks.");
        }

        return new D3dbspFile(Array.AsReadOnly(lumps));
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Lumps.Count > MaximumChunkCount)
            throw new InvalidOperationException($"A version 22 d3dbsp cannot contain more than {MaximumChunkCount} chunks.");
        string outputPath = Path.GetFullPath(path);
        if (File.Exists(outputPath))
            throw new IOException($"Output file '{outputPath}' already exists.");
        string directory = Path.GetDirectoryName(outputPath) ??
            throw new InvalidDataException("The d3dbsp output path has no containing directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.SequentialScan))
            {
                WriteTo(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, outputPath);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private void WriteTo(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Ident);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], checked((uint)Lumps.Count));
        stream.Write(header);

        Span<byte> descriptor = stackalloc byte[ChunkDescriptorSize];
        foreach (D3dbspLump lump in Lumps)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, (uint)lump.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[4..], lump.Length);
            stream.Write(descriptor);
        }

        foreach (D3dbspLump lump in Lumps)
        {
            stream.Write(lump.Data);
            stream.Write(lump.Padding);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public long GetPayloadOffset(int index)
    {
        if ((uint)index >= (uint)Lumps.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        long offset = checked(HeaderSize + (long)ChunkDescriptorSize * Lumps.Count);
        for (int previous = 0; previous < index; previous++)
            offset = checked(offset + Lumps[previous].Length + Lumps[previous].Padding.Length);
        return offset;
    }

    private static int GetPaddingLength(uint length) => (int)((0u - length) & 3u);

    private static void ReadExactly(Stream stream, Span<byte> buffer, string description)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"The d3dbsp ended while reading {description}.", exception);
        }
    }

    private static string FormatType(D3dbspLumpType type) =>
        Enum.IsDefined(type) ? type.ToString() : $"0x{(uint)type:X8}";
}
