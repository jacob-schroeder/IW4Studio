using System.Buffers.Binary;
using MapConverter.Game.IW3.PC.Assets.Materials;
using MapConverter.Game.IW3.PC.Assets.XModel;
using MapConverter.Game.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.Assets;

/// <summary>Writes the selected source assets consumed by MapConverter’s IW4 compilers.</summary>
internal static class Iw3PcAssetExporter
{
    public static void ExportSelected(Iw3PcZone zone, IReadOnlyCollection<int> recordIds,
        string outputDirectory, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(recordIds);
        var selected = new List<Iw3PcAsset>(recordIds.Count);
        var ids = new HashSet<int>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (int id in recordIds)
        {
            token.ThrowIfCancellationRequested();
            if (id < 0 || id >= zone.Assets.Count)
                throw new InvalidDataException($"Selected IW3 PC asset record {id} is outside the catalog.");
            Iw3PcAsset asset = zone.Assets[id];
            if (!ids.Add(id))
                throw new InvalidDataException($"Selected IW3 PC {asset.Type}:{asset.Name} repeats record {id}.");
            if (asset.Name.StartsWith(',') || asset.Type is not
                ("rawfile" or "mapents" or "physpreset" or "material" or "xmodel" or "image" or "techniqueset"))
                throw new NotSupportedException($"Cannot export IW3 PC {asset.Type}:{asset.Name} (record {id}).");
            string key = asset.Type + ':' + asset.Name.Replace('\\', '/').Trim().ToLowerInvariant();
            if (!identities.Add(key))
                throw new InvalidDataException($"Selected IW3 PC {asset.Type}:{asset.Name} repeats a concrete asset identity.");
            selected.Add(asset);
        }
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException($"Asset output path already exists: '{outputDirectory}'.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (Iw3PcAsset asset in selected)
            {
                token.ThrowIfCancellationRequested();
                if (asset.Type == "techniqueset") continue;
                if (asset.Type == "xmodel")
                {
                    try
                    {
                        foreach (var file in Iw3PcModelExporter.ReadFiles(zone, asset))
                            Write(file.Key, file.Value, asset);
                    }
                    catch (Exception error) when (error is InvalidDataException or NotSupportedException or OverflowException)
                    {
                        throw new InvalidDataException(
                            $"Cannot export IW3 PC {asset.Type}:{asset.Name} (record {asset.Id}): {error.Message}", error);
                    }
                    continue;
                }
                string relativePath = asset.Type switch
                {
                    "mapents" => asset.Name + ".ents",
                    "material" => Iw3PcMaterialExporter.FileName(asset.Name),
                    "image" => Iw3PcImageExporter.FileName(asset.Name),
                    "physpreset" => "physic/" + asset.Name,
                    _ => asset.Name
                };
                byte[]? bytes;
                try
                {
                    bytes = asset.Type switch
                    {
                        "image" => Iw3PcImageExporter.ReadIwi(zone, asset),
                        "material" => Iw3PcMaterialExporter.ReadJson(zone, asset),
                        "physpreset" => Iw3PcPhysPresetExporter.ReadInfoString(zone, asset),
                        _ => ReadFileBytes(zone, asset)
                    };
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or OverflowException)
                {
                    throw new InvalidDataException(
                        $"Cannot export IW3 PC {asset.Type}:{asset.Name} (record {asset.Id}): {error.Message}", error);
                }
                if (bytes != null) Write(relativePath, bytes, asset);
            }
            var techniqueExporter = new Iw3PcTechniqueExporter(zone);
            foreach (Iw3PcAsset asset in selected.Where(value => value.Type == "techniqueset")
                         .OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    techniqueExporter.Export(asset, (path, bytes) => Write(path, bytes, asset));
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or OverflowException)
                {
                    throw new InvalidDataException(
                        $"Cannot export IW3 PC {asset.Type}:{asset.Name} (record {asset.Id}): {error.Message}", error);
                }
            }
        }
        catch
        {
            try { Directory.Delete(outputDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }

        void Write(string relativePath, byte[] bytes, Iw3PcAsset asset)
        {
            token.ThrowIfCancellationRequested();
            try { ValidateRelativePath(relativePath); }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"IW3 PC {asset.Type}:{asset.Name} (record {asset.Id}): {error.Message}", error);
            }
            string path = Path.Combine(outputDirectory, relativePath);
            if (!names.Add(relativePath))
            {
                if (relativePath.StartsWith("shader_bin/", StringComparison.Ordinal) &&
                    File.Exists(path) && FileEquals(path, bytes))
                    return;
                throw new InvalidDataException(
                    $"IW3 PC {asset.Type}:{asset.Name} (record {asset.Id}) conflicts at '{relativePath}'.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
        }
    }

    private static bool FileEquals(string path, byte[] expected)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != expected.Length) return false;
        Span<byte> buffer = stackalloc byte[4096];
        int offset = 0;
        while (offset < expected.Length)
        {
            int count = Math.Min(buffer.Length, expected.Length - offset);
            stream.ReadExactly(buffer[..count]);
            if (!buffer[..count].SequenceEqual(expected.AsSpan(offset, count))) return false;
            offset += count;
        }
        return true;
    }

    public static byte[] ReadRawFile(Iw3PcZone zone, Iw3PcAsset asset)
    {
        if (asset.Type != "rawfile" || asset.Name.StartsWith(','))
            throw new ArgumentException("An owned IW3 PC RawFile is required.", nameof(asset));
        return ReadFileBytes(zone, asset);
    }

    private static byte[] ReadFileBytes(Iw3PcZone zone, Iw3PcAsset asset)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(asset.Offset +
            (asset.Type == "mapents" ? 8 : 4), 4));
        if (length < 0 || length > 256 * 1024 * 1024)
            throw new InvalidDataException($"Invalid {asset.Type} size in '{asset.Name}'.");
        int buffer = zone.Pointer(asset.Offset + (asset.Type == "mapents" ? 4 : 8));
        // OAT omits the terminating NUL from map entity text, but copies
        // precisely RawFile.len bytes from raw script buffers.
        if (asset.Type == "mapents") length = Math.Max(length - 1, 0);
        if (buffer == 0 && length != 0)
            throw new InvalidDataException($"Missing {asset.Type} buffer in '{asset.Name}'.");
        return length == 0 ? [] : zone.Bytes(buffer, length).ToArray();
    }

    private static void ValidateRelativePath(string name)
    {
        if (string.IsNullOrEmpty(name) || Path.IsPathRooted(name) || name.IndexOfAny(['\\', ':', '\0']) >= 0 ||
            name.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException($"Unsafe IW3 asset output path '{name}'.");
    }
}
