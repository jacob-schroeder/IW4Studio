using System.Security.Cryptography;
using IW4.FastFiles.Loaders.Database;
using IW4.Studio.Documents;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IW4.Studio.Tests;

public sealed class StockFastFileRoundTripTests
{
    private const string RepositoryDirectory = "/Users/jacob/Repositories/IW4Studio";
    private const string StockFastFilesDirectory = "/Users/jacob/Repositories/MW2/fastfiles";
    private const string MapPack1Directory = "/Users/jacob/Repositories/MW2/fastfiles/mappack1";
    private const string MapPack2Directory = "/Users/jacob/Repositories/MW2/fastfiles/mappack2";
    private const string StockFastFileAllowlistEnvironmentVariable = "IW4_STOCK_FASTFILE_ALLOWLIST";
    private const string TemporaryDirectoryNamePrefix = "IW4.Studio.Tests.StockFastFileRoundTrip.";
    private const int ComparisonBufferSize = 1024 * 1024;

    public static IEnumerable<object[]> StockFastFiles =>
        DiscoverStockFastFiles().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(StockFastFiles))]
    public void Unmodified_save_as_preserves_decoded_zone_and_header_metadata(string sourcePath)
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        FileFingerprint? sourceBefore = null;

        try
        {
            sourceBefore = CaptureFingerprint(sourcePath);

            string destinationPath = Path.Combine(
                temporaryDirectory,
                Path.GetFileName(sourcePath));

            var documentService = new FastFileDocumentService();
            FastFileWorkspace workspace = documentService.Open(
                new FastFileDocumentOpenRequest(sourcePath, Isolated.Instance));
            using (var editingSession = new FastFileEditingSession(workspace))
            {
                EnsureOutputPathIsSafe(destinationPath, temporaryDirectory);
                SaveAsResult result = new TransactionalSaveAsService().SaveAs(
                    editingSession,
                    new SaveAsRequest(destinationPath, AllowOverwrite: false));

                Assert.True(
                    result.Succeeded,
                    $"Unmodified Save As failed for '{sourcePath}'.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, result.Diagnostics));
            }

            Assert.True(
                File.Exists(destinationPath),
                $"Save As reported success for '{sourcePath}', but did not create '{destinationPath}'.");
            AssertDecodedZoneAndHeaderMetadataMatch(sourcePath, destinationPath);
        }
        finally
        {
            try
            {
                if (sourceBefore is not null)
                {
                    FileFingerprint sourceAfter = CaptureFingerprint(sourcePath);
                    Assert.True(
                        sourceBefore == sourceAfter,
                        $"Stock source '{sourcePath}' changed during the round trip. " +
                        $"Before: {sourceBefore}. After: {sourceAfter}.");
                }
            }
            finally
            {
                DeleteTemporaryDirectory(temporaryDirectory);
            }
        }
    }

    private static IEnumerable<string> DiscoverStockFastFiles()
    {
        string[] paths =
        [
            .. Directory.EnumerateFiles(
                StockFastFilesDirectory,
                "*.ff",
                SearchOption.TopDirectoryOnly),
            .. Directory.EnumerateFiles(
                MapPack1Directory,
                "*.ff",
                SearchOption.TopDirectoryOnly),
            .. Directory.EnumerateFiles(
                MapPack2Directory,
                "*.ff",
                SearchOption.TopDirectoryOnly)
        ];

        string[] discoveredPaths = paths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return SelectStockFastFiles(discoveredPaths);
    }

    private static IEnumerable<string> SelectStockFastFiles(IReadOnlyList<string> discoveredPaths)
    {
        string? selector = Environment.GetEnvironmentVariable(StockFastFileAllowlistEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(selector))
            return discoveredPaths;

        string[] requestedNames = selector.Split(';', StringSplitOptions.TrimEntries);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedPaths = new List<string>(requestedNames.Length);
        foreach (string requestedName in requestedNames)
        {
            if (requestedName.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' contains an empty fastfile name.");
            }

            if (Path.GetFileName(requestedName) != requestedName ||
                requestedName.Contains('/') ||
                requestedName.Contains('\\') ||
                !string.Equals(Path.GetExtension(requestedName), ".ff", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' entry '{requestedName}' " +
                    "must be an exact .ff basename without path components.");
            }

            if (!selectedNames.Add(requestedName))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' requests '{requestedName}' more than once.");
            }

            string[] matches = discoveredPaths
                .Where(path => string.Equals(
                    Path.GetFileName(path),
                    requestedName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' requests unknown stock fastfile '{requestedName}'.");
            }

            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' entry '{requestedName}' " +
                    "matches more than one discovered stock fastfile.");
            }

            selectedPaths.Add(matches[0]);
        }

        if (selectedPaths.Count == 0)
        {
            throw new InvalidOperationException(
                $"Environment variable '{StockFastFileAllowlistEnvironmentVariable}' produced an empty fastfile selection.");
        }

        return selectedPaths.OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string CreateTemporaryDirectory()
    {
        string temporaryRoot = Path.GetTempPath();
        string temporaryDirectory = Path.Combine(
            temporaryRoot,
            $"{TemporaryDirectoryNamePrefix}{Guid.NewGuid():N}");
        ValidateOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        string resolvedTemporaryRoot = ResolveExistingDirectory(temporaryRoot);
        string proposedPhysicalDirectory = Path.Combine(
            resolvedTemporaryRoot,
            Path.GetFileName(temporaryDirectory));
        RejectProtectedPhysicalPath(proposedPhysicalDirectory);
        if (FilesystemEntryExists(temporaryDirectory))
        {
            throw new IOException(
                $"Operation-owned temporary directory path '{temporaryDirectory}' is already occupied.");
        }

        Directory.CreateDirectory(temporaryDirectory);
        ValidateExistingOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        return temporaryDirectory;
    }

    private static void DeleteTemporaryDirectory(string temporaryDirectory)
    {
        string temporaryRoot = Path.GetTempPath();
        ValidateOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        if (!FilesystemEntryExists(temporaryDirectory))
            return;

        ValidateExistingOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    private static void ValidateOperationOwnedTemporaryDirectory(
        string temporaryDirectory,
        string temporaryRoot)
    {
        string fullTemporaryDirectory =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
        string fullTemporaryRoot =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryRoot));
        string? parentDirectory = Path.GetDirectoryName(fullTemporaryDirectory);
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(parentDirectory ?? string.Empty),
                fullTemporaryRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Temporary directory '{fullTemporaryDirectory}' is not an immediate child of " +
                $"the current OS temp root '{fullTemporaryRoot}'.");
        }

        string leafName = Path.GetFileName(fullTemporaryDirectory);
        string guidPayload = leafName.StartsWith(
            TemporaryDirectoryNamePrefix,
            StringComparison.Ordinal)
            ? leafName[TemporaryDirectoryNamePrefix.Length..]
            : string.Empty;
        if (!Guid.TryParseExact(guidPayload, "N", out _))
        {
            throw new InvalidOperationException(
                $"Temporary directory '{fullTemporaryDirectory}' is not owned by this test case.");
        }

        if (IsPathWithinOrEqual(fullTemporaryDirectory, RepositoryDirectory) ||
            IsPathWithinOrEqual(fullTemporaryDirectory, StockFastFilesDirectory) ||
            IsPathWithinOrEqual(fullTemporaryDirectory, MapPack1Directory) ||
            IsPathWithinOrEqual(fullTemporaryDirectory, MapPack2Directory))
        {
            throw new InvalidOperationException(
                $"Temporary directory '{fullTemporaryDirectory}' overlaps a protected repository or stock directory.");
        }
    }

    private static void EnsureOutputPathIsSafe(string destinationPath, string temporaryDirectory)
    {
        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string fullTemporaryDirectory =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
        string resolvedTemporaryDirectory =
            ValidateExistingOperationOwnedTemporaryDirectory(
                temporaryDirectory,
                Path.GetTempPath());
        if (!IsImmediateChild(fullDestinationPath, fullTemporaryDirectory))
        {
            throw new InvalidOperationException(
                $"The Save As destination '{fullDestinationPath}' is not an immediate child of " +
                $"its operation-owned temporary directory '{fullTemporaryDirectory}'.");
        }

        string physicalDestinationPath = Path.Combine(
            resolvedTemporaryDirectory,
            Path.GetFileName(fullDestinationPath));
        RejectProtectedPhysicalPath(physicalDestinationPath);
    }

    private static string ValidateExistingOperationOwnedTemporaryDirectory(
        string temporaryDirectory,
        string temporaryRoot)
    {
        ValidateOperationOwnedTemporaryDirectory(temporaryDirectory, temporaryRoot);

        string fullTemporaryDirectory =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryDirectory));
        var directory = new DirectoryInfo(fullTemporaryDirectory);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"Operation-owned temporary directory '{fullTemporaryDirectory}' does not exist.");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Operation-owned temporary directory '{fullTemporaryDirectory}' must not be a symlink or reparse point.");
        }

        string resolvedTemporaryRoot = ResolveExistingDirectory(temporaryRoot);
        string resolvedTemporaryDirectory = ResolveExistingDirectory(fullTemporaryDirectory);
        if (!IsImmediateChild(resolvedTemporaryDirectory, resolvedTemporaryRoot))
        {
            throw new InvalidOperationException(
                $"Operation-owned temporary directory '{resolvedTemporaryDirectory}' is not an immediate " +
                $"physical child of the resolved OS temp root '{resolvedTemporaryRoot}'.");
        }

        RejectProtectedPhysicalPath(resolvedTemporaryDirectory);
        return resolvedTemporaryDirectory;
    }

    private static string ResolveExistingDirectory(string directoryPath)
    {
        string fullDirectoryPath =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        if (!Directory.Exists(fullDirectoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Directory '{fullDirectoryPath}' must exist before its physical path can be resolved.");
        }

        string root = Path.GetPathRoot(fullDirectoryPath)
            ?? throw new InvalidOperationException(
                $"Directory '{fullDirectoryPath}' has no filesystem root.");
        string currentDirectory = Path.TrimEndingDirectorySeparator(root);
        string relativePath = Path.GetRelativePath(root, fullDirectoryPath);
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            return currentDirectory;

        foreach (string component in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var componentDirectory = new DirectoryInfo(
                Path.Combine(currentDirectory, component));
            if (!componentDirectory.Exists)
            {
                throw new DirectoryNotFoundException(
                    $"Directory component '{componentDirectory.FullName}' no longer exists while resolving " +
                    $"'{fullDirectoryPath}'.");
            }

            if ((componentDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo? target = componentDirectory.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not DirectoryInfo targetDirectory || !targetDirectory.Exists)
                {
                    throw new InvalidOperationException(
                        $"Directory link '{componentDirectory.FullName}' does not resolve to an existing directory.");
                }

                currentDirectory = Path.TrimEndingDirectorySeparator(targetDirectory.FullName);
            }
            else
            {
                currentDirectory = Path.TrimEndingDirectorySeparator(componentDirectory.FullName);
            }
        }

        return currentDirectory;
    }

    private static void RejectProtectedPhysicalPath(string physicalPath)
    {
        foreach (string protectedRoot in ResolveProtectedDirectories())
        {
            if (IsPathWithinOrEqual(physicalPath, protectedRoot) ||
                IsPathWithinOrEqual(protectedRoot, physicalPath))
            {
                throw new InvalidOperationException(
                    $"Path '{physicalPath}' overlaps protected physical directory '{protectedRoot}'.");
            }
        }
    }

    private static IEnumerable<string> ResolveProtectedDirectories()
    {
        yield return ResolveExistingDirectory(RepositoryDirectory);
        yield return ResolveExistingDirectory(StockFastFilesDirectory);
        yield return ResolveExistingDirectory(MapPack1Directory);
        yield return ResolveExistingDirectory(MapPack2Directory);
    }

    private static bool IsImmediateChild(string path, string directory) =>
        string.Equals(
            GetNormalizedFullPath(Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty),
            GetNormalizedFullPath(directory),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPathWithin(string path, string directory)
    {
        string fullPath = GetNormalizedFullPath(path);
        string fullDirectory = GetNormalizedFullPath(directory);
        string directoryPrefix = fullDirectory.EndsWith(Path.DirectorySeparatorChar) ||
            fullDirectory.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        return !string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase) &&
            fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathWithinOrEqual(string path, string directory) =>
        string.Equals(
            GetNormalizedFullPath(path),
            GetNormalizedFullPath(directory),
            StringComparison.OrdinalIgnoreCase) ||
        IsPathWithin(path, directory);

    private static string GetNormalizedFullPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool FilesystemEntryExists(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        return file.Exists ||
            directory.Exists ||
            file.LinkTarget is not null ||
            directory.LinkTarget is not null;
    }

    private static FileFingerprint CaptureFingerprint(string path)
    {
        var file = new FileInfo(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ComparisonBufferSize,
            FileOptions.SequentialScan);
        string sha256 = Convert.ToHexString(SHA256.HashData(stream));
        file.Refresh();
        return new FileFingerprint(file.Length, file.LastWriteTimeUtc, sha256);
    }

    private static void AssertDecodedZoneAndHeaderMetadataMatch(
        string sourcePath,
        string candidatePath)
    {
        LoadedXZone source = LoadZone(sourcePath);
        LoadedXZone candidate = LoadZone(candidatePath);

        AssertByteSequencesMatch(
            source.ZoneBytes,
            candidate.ZoneBytes,
            $"Decoded zone mismatch: source '{sourcePath}', candidate '{candidatePath}'");

        Assert.Equal(
            source.Header.PackedStreamOffset,
            candidate.Header.PackedStreamOffset);
        Assert.Equal(
            source.Header.SerializedHeaderLength,
            candidate.Header.SerializedHeaderLength);

        ReadOnlySpan<byte> sourceHeader = source.Header.SerializedHeaderBytes.AsSpan();
        ReadOnlySpan<byte> candidateHeader = candidate.Header.SerializedHeaderBytes.AsSpan();
        Assert.True(
            sourceHeader.Length >= 8 && candidateHeader.Length >= 8,
            "DB headers must contain FileSize and MaxFileSize dwords.");
        AssertByteSequencesMatch(
            sourceHeader[..^8],
            candidateHeader[..^8],
            $"DB-header metadata mismatch: source '{sourcePath}', candidate '{candidatePath}'");
    }

    private static LoadedXZone LoadZone(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return new DbZoneLoader().DB_LoadXZone(
            bytes,
            bytes.Length,
            sourceName: path);
    }

    private static void AssertByteSequencesMatch(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> candidate,
        string description)
    {
        Assert.True(
            source.Length == candidate.Length,
            $"{description}: source is {source.Length} bytes, candidate is {candidate.Length} bytes.");

        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] != candidate[index])
            {
                throw new Xunit.Sdk.XunitException(
                    $"{description} at offset {index}: source is 0x{source[index]:X2}, " +
                    $"candidate is 0x{candidate[index]:X2}.");
            }
        }
    }

    private sealed record FileFingerprint(
        long Length,
        DateTime LastWriteTimeUtc,
        string Sha256);
}
