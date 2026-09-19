using System.Collections.ObjectModel;
using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace MapConverter.Game.IW3.PC.Images;

internal sealed record Iw3IwdImageRequest(
    string ImageName,
    TextureSemantic Semantic,
    bool UseSrgbReads);

/// <summary>
/// Resolves source files and compiles direct images/*.iwi entries across IW3 IWDs.
/// </summary>
internal static class Iw3IwdImageCompiler
{
    private static readonly StringComparer EntryNameComparer =
        StringComparer.OrdinalIgnoreCase;

    private sealed record ImageEntry(
        string ImageName,
        ZipArchiveEntry Entry);

    internal static IReadOnlyList<(
        Iw3Iwi6StreamedImageCompilation Compilation,
        IReadOnlyList<string> Sources)> Compile(
        IReadOnlyList<string> iwdPaths,
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(iwdPaths);
        ArgumentNullException.ThrowIfNull(requests);

        Iw3IwdImageRequest[] orderedRequests = ValidateAndOrderRequests(requests);
        var resolvedImages = ResolveEntries(
            iwdPaths,
            orderedRequests.Select(request => request.ImageName),
            archive => InventoryImages(archive).Select(entry => (entry.ImageName, entry.Entry)));

        var compiledImages = new List<(
            Iw3Iwi6StreamedImageCompilation Compilation,
            IReadOnlyList<string> Sources)>(resolvedImages.Count);
        foreach (Iw3IwdImageRequest request in orderedRequests)
        {
            if (!resolvedImages.TryGetValue(request.ImageName, out var resolvedImage))
                continue;

            try
            {
                Iw3Iwi6StreamedImageCompilation compilation = Iw3Iwi6StreamedImageCompiler.Compile(
                    request.ImageName,
                    resolvedImage.Bytes,
                    request.Semantic,
                    request.UseSrgbReads);
                compiledImages.Add((compilation, resolvedImage.Sources.AsReadOnly()));
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                InvalidDataException or
                NotSupportedException or
                OverflowException)
            {
                throw new InvalidDataException(
                    $"IWD image '{request.ImageName}' from " +
                    $"'{string.Join("', '", resolvedImage.Sources)}' could not be compiled: " +
                    exception.Message,
                    exception);
            }
        }

        return Array.AsReadOnly(compiledImages.ToArray());
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ResolveFiles(
        IReadOnlyList<string> iwdPaths,
        IReadOnlyList<string> entryPaths)
    {
        ArgumentNullException.ThrowIfNull(iwdPaths);
        ArgumentNullException.ThrowIfNull(entryPaths);
        foreach (string entryPath in entryPaths)
        {
            ValidateSafeEntryPath(entryPath);
            if (entryPath.EndsWith('/'))
            {
                throw new ArgumentException(
                    $"IWD entry request '{entryPath}' identifies a directory, not a file.",
                    nameof(entryPaths));
            }
        }

        var resolvedFiles = ResolveEntries(
            iwdPaths,
            entryPaths.OrderBy(path => path, EntryNameComparer)
                .ThenBy(path => path, StringComparer.Ordinal),
            archive => InventoryFiles(archive).Select(entry => (entry.FullName, entry)));
        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(resolvedFiles.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value.Sources.AsReadOnly(),
            EntryNameComparer));
    }

    private static Dictionary<string, (byte[] Bytes, List<string> Sources)> ResolveEntries(
        IReadOnlyList<string> iwdPaths,
        IEnumerable<string> requestedNames,
        Func<ZipArchive, IEnumerable<(string Name, ZipArchiveEntry Entry)>> inventory)
    {
        var requestedEntries = new HashSet<string>(requestedNames, EntryNameComparer);
        StringComparer pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string[] fullIwdPaths = iwdPaths
            .Select(ValidateIwdPath)
            .OrderBy(path => path, pathComparer)
            .ThenBy(path => path, StringComparer.Ordinal)
            .Distinct(pathComparer)
            .ToArray();
        var resolvedEntries = new Dictionary<string, (byte[] Bytes, List<string> Sources)>(
            EntryNameComparer);
        foreach (string fullIwdPath in fullIwdPaths)
        {
            try
            {
                using FileStream stream = OpenArchiveStream(fullIwdPath);
                using var archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);
                foreach ((string name, ZipArchiveEntry entry) in inventory(archive))
                {
                    if (!requestedEntries.TryGetValue(name, out string? requestedName))
                        continue;

                    string source = $"{fullIwdPath}:{entry.FullName}";
                    byte[] bytes;
                    try
                    {
                        bytes = ReadEntryOnce(entry);
                    }
                    catch (Exception exception) when (exception is
                        ArgumentException or
                        IOException or
                        InvalidDataException or
                        NotSupportedException or
                        OverflowException)
                    {
                        throw new InvalidDataException(
                            $"IWD entry '{entry.FullName}' could not be read: " +
                            exception.Message,
                            exception);
                    }

                    if (resolvedEntries.TryGetValue(requestedName, out var resolvedEntry))
                    {
                        if (!resolvedEntry.Bytes.AsSpan().SequenceEqual(bytes))
                        {
                            throw new InvalidDataException(
                                $"IWD entry '{requestedName}' has conflicting payloads in " +
                                $"'{string.Join("', '", resolvedEntry.Sources)}' and '{source}'.");
                        }
                        if (!resolvedEntry.Sources.Contains(source, StringComparer.Ordinal))
                            resolvedEntry.Sources.Add(source);
                    }
                    else
                    {
                        resolvedEntries.Add(requestedName, (bytes, [source]));
                    }
                }
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                IOException or
                InvalidDataException or
                NotSupportedException or
                OverflowException or
                UnauthorizedAccessException)
            {
                throw new InvalidDataException(
                    $"IWD '{fullIwdPath}' could not be read: {exception.Message}",
                    exception);
            }
        }

        return resolvedEntries;
    }

    private static string ValidateIwdPath(string iwdPath)
    {
        string fullIwdPath;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(iwdPath);
            fullIwdPath = Path.GetFullPath(iwdPath);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            throw new ArgumentException(
                $"IWD path '{iwdPath}' is invalid: {exception.Message}",
                nameof(iwdPath),
                exception);
        }
        if (!File.Exists(fullIwdPath))
            throw new FileNotFoundException($"The IW3 IWD '{fullIwdPath}' does not exist.", fullIwdPath);
        return fullIwdPath;
    }

    private static FileStream OpenArchiveStream(string fullIwdPath) =>
        new(
            fullIwdPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);

    private static IEnumerable<ZipArchiveEntry> InventoryFiles(ZipArchive archive)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ValidateSafeEntryPath(entry.FullName);
            if (entry.Name.Length != 0)
                yield return entry;
        }
    }

    private static ImageEntry[] InventoryImages(ZipArchive archive)
    {
        var canonicalImageEntries = new Dictionary<string, string>(EntryNameComparer);
        var images = new List<ImageEntry>();
        foreach (ZipArchiveEntry entry in InventoryFiles(archive))
        {
            if (!entry.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase) ||
                !entry.FullName.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase))
                continue;

            string imageName;
            try
            {
                imageName = GetDirectImageName(entry);
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                InvalidDataException or
                NotSupportedException)
            {
                throw new InvalidDataException(
                    $"IWD entry '{entry.FullName}' has an invalid image path: {exception.Message}",
                    exception);
            }
            if (canonicalImageEntries.TryGetValue(imageName, out string? previousEntry))
            {
                throw new InvalidDataException(
                    $"IWD image name '{imageName}' occurs more than once canonically " +
                    $"in entries '{previousEntry}' and '{entry.FullName}'.");
            }
            canonicalImageEntries.Add(imageName, entry.FullName);
            images.Add(new ImageEntry(imageName, entry));
        }

        return images
            .OrderBy(value => value.ImageName, EntryNameComparer)
            .ThenBy(value => value.ImageName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetDirectImageName(ZipArchiveEntry entry)
    {
        const string imagesPrefix = "images/";
        string entryFullName = entry.FullName;
        if (entry.Name.Length == 0 ||
            !entryFullName.StartsWith(imagesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"IWD entry '{entryFullName}' is unsupported; only direct images/*.iwi files are allowed.");
        }

        string relativePath = entryFullName[imagesPrefix.Length..];
        if (relativePath.Contains('/') ||
            !relativePath.EndsWith(".iwi", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"IWD entry '{entryFullName}' is unsupported; only direct images/*.iwi files are allowed.");
        }

        string imageName = relativePath[..^4];
        ValidateImageName(imageName, entryFullName);
        return imageName;
    }

    private static void ValidateSafeEntryPath(string entryFullName)
    {
        if (string.IsNullOrEmpty(entryFullName) ||
            entryFullName[0] == '/' ||
            entryFullName.Contains('\\') ||
            entryFullName.Contains('\0'))
        {
            throw new InvalidDataException(
                $"IWD entry '{entryFullName}' has an unsafe path.");
        }

        string pathWithoutDirectoryMarker = entryFullName.EndsWith('/')
            ? entryFullName[..^1]
            : entryFullName;
        string[] segments = pathWithoutDirectoryMarker.Split('/');
        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".." ||
                segment.Contains(':')))
        {
            throw new InvalidDataException(
                $"IWD entry '{entryFullName}' has an unsafe path.");
        }
    }

    private static Iw3IwdImageRequest[] ValidateAndOrderRequests(
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        var canonicalNames = new HashSet<string>(EntryNameComparer);
        var materialized = new List<Iw3IwdImageRequest>();
        foreach (Iw3IwdImageRequest request in requests)
        {
            if (request is null)
                throw new ArgumentException("An IWD image request cannot be null.", nameof(requests));
            ValidateImageName(request.ImageName, "image request");
            if (!canonicalNames.Add(request.ImageName))
            {
                throw new ArgumentException(
                    $"IWD image request '{request.ImageName}' occurs more than once canonically.",
                    nameof(requests));
            }
            materialized.Add(request);
        }

        return materialized
            .OrderBy(request => request.ImageName, EntryNameComparer)
            .ThenBy(request => request.ImageName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateImageName(
        string imageName,
        string sourceDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        if (imageName is "." or ".." ||
            imageName[0] == ',' ||
            imageName.Contains('/') ||
            imageName.Contains('\\') ||
            imageName.Contains('\0') ||
            !string.Equals(imageName, imageName.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{sourceDescription} has invalid image name '{imageName}'.");
        }
        if (imageName.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{sourceDescription} image name '{imageName}' is not representable as Latin-1.");
        }
    }

    private static byte[] ReadEntryOnce(ZipArchiveEntry entry)
    {
        if (entry.Length > int.MaxValue)
        {
            throw new InvalidDataException(
                $"IWD entry '{entry.FullName}' is too large to materialize.");
        }

        var bytes = new byte[checked((int)entry.Length)];
        using Stream stream = entry.Open();
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException(
                $"IWD entry '{entry.FullName}' contains more bytes than its ZIP length.");
        }
        return bytes;
    }
}
