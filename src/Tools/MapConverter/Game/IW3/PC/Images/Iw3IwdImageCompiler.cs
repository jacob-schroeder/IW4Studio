using System.IO.Compression;
using IW4.Assets.Assets.Image;

namespace MapConverter.Game.IW3.PC.Images;

internal sealed record Iw3IwdImageRequest(
    string ImageName,
    TextureSemantic Semantic,
    bool UseSrgbReads);

/// <summary>
/// Inventories and compiles direct images/*.iwi entries from one IW3 IWD.
/// </summary>
internal static class Iw3IwdImageCompiler
{
    private static readonly StringComparer ImageNameComparer =
        StringComparer.OrdinalIgnoreCase;

    private sealed record ImageEntry(
        string ImageName,
        ZipArchiveEntry Entry);

    internal static IReadOnlyList<Iw3Iwi6StreamedImageCompilation> Compile(
        string iwdPath,
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        Iw3IwdImageRequest[] orderedRequests = ValidateAndOrderRequests(requests);
        string fullIwdPath = ValidateIwdPath(iwdPath);
        using FileStream stream = OpenArchiveStream(fullIwdPath);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            leaveOpen: false);
        ImageEntry[] entries = InventoryEntries(archive);
        Dictionary<string, ImageEntry> entriesByName = entries.ToDictionary(
            entry => entry.ImageName,
            ImageNameComparer);

        var compiledImages = new List<Iw3Iwi6StreamedImageCompilation>(
            Math.Min(orderedRequests.Length, entries.Length));
        foreach (Iw3IwdImageRequest request in orderedRequests)
        {
            if (!entriesByName.TryGetValue(request.ImageName, out ImageEntry? entry))
                continue;

            try
            {
                byte[] iwiBytes = ReadEntryOnce(entry.Entry);
                compiledImages.Add(Iw3Iwi6StreamedImageCompiler.Compile(
                    request.ImageName,
                    iwiBytes,
                    request.Semantic,
                    request.UseSrgbReads));
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                InvalidDataException or
                NotSupportedException or
                OverflowException)
            {
                throw new InvalidDataException(
                    $"IWD entry '{entry.Entry.FullName}' could not be compiled: " +
                    exception.Message,
                    exception);
            }
        }

        return Array.AsReadOnly(compiledImages.ToArray());
    }

    private static string ValidateIwdPath(string iwdPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iwdPath);
        string fullIwdPath = Path.GetFullPath(iwdPath);
        if (!File.Exists(fullIwdPath))
            throw new FileNotFoundException("The IW3 IWD does not exist.", fullIwdPath);
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

    private static ImageEntry[] InventoryEntries(ZipArchive archive)
    {
        var canonicalImageNames = new HashSet<string>(ImageNameComparer);
        var images = new List<ImageEntry>();
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ValidateSafeEntryPath(entry.FullName);
            if (IsImagesDirectory(entry))
                continue;

            string imageName = GetDirectImageName(entry);
            if (!canonicalImageNames.Add(imageName))
            {
                throw new InvalidDataException(
                    $"IWD image name '{imageName}' occurs more than once canonically.");
            }
            images.Add(new ImageEntry(imageName, entry));
        }

        return images
            .OrderBy(value => value.ImageName, ImageNameComparer)
            .ThenBy(value => value.ImageName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsImagesDirectory(ZipArchiveEntry entry) =>
        entry.Name.Length == 0 &&
        string.Equals(entry.FullName, "images/", StringComparison.OrdinalIgnoreCase);

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
        var canonicalNames = new HashSet<string>(ImageNameComparer);
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
            .OrderBy(request => request.ImageName, ImageNameComparer)
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
