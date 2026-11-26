using IW4.Game.Assets.Image;
using MapConverter.IW3.PC.Readers;

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

    internal static IReadOnlyList<(
        Iw3Iwi6StreamedImageCompilation Compilation,
        IReadOnlyList<string> Sources)> Compile(
        IReadOnlyList<string> iwdPaths,
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(iwdPaths);
        ArgumentNullException.ThrowIfNull(requests);

        Iw3IwdImageRequest[] orderedRequests = ValidateAndOrderRequests(requests);
        IReadOnlyDictionary<string, Iw3PcIwdEntry> resolvedImages = Iw3PcIwdReader.ResolveImages(
            iwdPaths, orderedRequests.Select(request => request.ImageName));

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
                compiledImages.Add((compilation, resolvedImage.Sources));
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

    private static Iw3IwdImageRequest[] ValidateAndOrderRequests(
        IEnumerable<Iw3IwdImageRequest> requests)
    {
        var canonicalNames = new HashSet<string>(EntryNameComparer);
        var materialized = new List<Iw3IwdImageRequest>();
        foreach (Iw3IwdImageRequest request in requests)
        {
            if (request is null)
                throw new ArgumentException("An IWD image request cannot be null.", nameof(requests));
            Iw3PcIwdReader.ValidateImageName(request.ImageName, "image request");
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
}
