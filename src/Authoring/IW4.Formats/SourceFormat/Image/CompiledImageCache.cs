using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IW4.Game.Assets.Image;

namespace IW4.Formats.SourceFormat.Image;

/// <summary>Reuses the native representation of decoded DDS source images between linker runs.</summary>
internal static class CompiledImageCache
{
    private const long MaximumEntryBytes = 512L * 1024 * 1024;
    private const long MaximumCacheBytes = 2L * 1024 * 1024 * 1024;
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IW4Studio", "compiled-images", "v1");
    private static readonly object PublicationLock = new();
    private static long _knownCacheBytes = -1;

    internal static GfxImageAsset Link(string sourceDirectory, string name,
        out IReadOnlyList<byte[]> streamParts)
    {
        var exchange = new ImageExchange();
        string source = Path.Combine(sourceDirectory, "images", name.Replace('*', '_'));
        string metadata = source + ".image.json";
        string pixels = source + ".dds";
        // Native sources already contain the target representation. Also keep the
        // original exchange's diagnostics for absent or malformed source files.
        if (!File.Exists(metadata) || exchange.ReadSourceVersion(sourceDirectory, name) != 1)
            return exchange.Link(sourceDirectory, name, out streamParts);
        if (!File.Exists(pixels)) return exchange.Link(sourceDirectory, name, out streamParts);

        string key;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            Append(hash, typeof(ImageSourceCompiler).Assembly.ManifestModule.ModuleVersionId.ToString("N"));
            Append(hash, Path.GetFullPath(sourceDirectory));
            Append(hash, name);
            AppendFile(hash, metadata);
            AppendFile(hash, pixels);
            key = Convert.ToHexString(hash.GetHashAndReset());
        }
        string entry = Path.Combine(Root, key);
        bool invalidEntry = false;
        if (File.Exists(Path.Combine(entry, "complete")))
        {
            try
            {
                if (!string.Equals(File.ReadAllText(Path.Combine(entry, "complete")), Digest(entry), StringComparison.Ordinal))
                    throw new InvalidDataException("Cached image digest does not match its contents.");
                GfxImageAsset cached = exchange.Link(entry, name, out streamParts);
                if (cached.PayloadByteCount == 0 || cached.PayloadBytes.Count == 0)
                    throw new InvalidDataException("Cached image has no inline payload.");
                return cached;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                       UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                // A partially removed or corrupted cache entry is just a miss.
                invalidEntry = true;
            }
        }

        GfxImageAsset image = exchange.Link(sourceDirectory, name, out streamParts);
        try
        {
            long length = image.PayloadBytes.Count;
            if (length == 0 || length > MaximumEntryBytes) return image;
            Directory.CreateDirectory(Root);
            string temporary = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                exchange.UnlinkNative(temporary, image,
                    streamParts.Count == 0 ? null : streamParts);
                File.WriteAllText(Path.Combine(temporary, "complete"), Digest(temporary));
                lock (PublicationLock)
                {
                    if (Directory.Exists(entry))
                    {
                        string marker = Path.Combine(entry, "complete");
                        if (invalidEntry || !File.Exists(marker) ||
                            !string.Equals(File.ReadAllText(marker), Digest(entry), StringComparison.Ordinal))
                        {
                            Directory.Delete(entry, recursive: true);
                            _knownCacheBytes = -1;
                        }
                    }
                    if (_knownCacheBytes < 0)
                        _knownCacheBytes = new DirectoryInfo(Root).GetDirectories()
                            .Where(item => !item.Name.EndsWith(".tmp", StringComparison.Ordinal))
                            .Sum(Size);
                    if (!Directory.Exists(entry))
                    {
                        Directory.Move(temporary, entry);
                        _knownCacheBytes += Size(new DirectoryInfo(entry));
                    }
                    if (_knownCacheBytes > MaximumCacheBytes)
                        foreach (DirectoryInfo directory in new DirectoryInfo(Root).GetDirectories()
                                     .Where(item => !item.Name.EndsWith(".tmp", StringComparison.Ordinal))
                                     .OrderBy(item => item.LastWriteTimeUtc))
                        {
                            if (_knownCacheBytes <= MaximumCacheBytes) break;
                            long entryBytes = Size(directory);
                            directory.Delete(recursive: true);
                            _knownCacheBytes -= entryBytes;
                        }
                }
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidDataException) { }
        catch (NotSupportedException) { }
        return image;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[128 * 1024];
        int length;
        while ((length = stream.Read(buffer)) != 0) hash.AppendData(buffer.AsSpan(0, length));
    }

    private static string Digest(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(file => Path.GetFileName(file) != "complete")
                     .Order(StringComparer.Ordinal))
        {
            Append(hash, Path.GetRelativePath(directory, file));
            AppendFile(hash, file);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static long Size(DirectoryInfo directory) =>
        directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
}
