using System.Security.Cryptography;
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
    private const string TemporaryDirectoryNamePrefix = "IW4.Studio.Tests.StockFastFileRoundTrip.";
    private const int ComparisonBufferSize = 1024 * 1024;

    public static IEnumerable<object[]> StockFastFiles =>
        DiscoverStockFastFiles().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(StockFastFiles))]
    public void Unmodified_save_as_is_byte_identical_and_preserves_stock_source(string sourcePath)
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
            AssertFilesAreByteIdentical(sourcePath, destinationPath);
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

        return paths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal);
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

    private static void AssertFilesAreByteIdentical(string sourcePath, string candidatePath)
    {
        var source = new FileInfo(sourcePath);
        var candidate = new FileInfo(candidatePath);
        Assert.True(
            source.Length == candidate.Length,
            $"Fastfile length mismatch: source '{sourcePath}' is {source.Length} bytes, " +
            $"candidate '{candidatePath}' is {candidate.Length} bytes.");

        using var sourceStream = OpenForSequentialRead(sourcePath);
        using var candidateStream = OpenForSequentialRead(candidatePath);
        byte[] sourceBuffer = new byte[ComparisonBufferSize];
        byte[] candidateBuffer = new byte[ComparisonBufferSize];
        long offset = 0;
        long remaining = source.Length;

        while (remaining != 0)
        {
            int chunkLength = (int)Math.Min(remaining, ComparisonBufferSize);
            Span<byte> sourceChunk = sourceBuffer.AsSpan(0, chunkLength);
            Span<byte> candidateChunk = candidateBuffer.AsSpan(0, chunkLength);
            sourceStream.ReadExactly(sourceChunk);
            candidateStream.ReadExactly(candidateChunk);

            if (sourceChunk.SequenceEqual(candidateChunk))
            {
                offset += chunkLength;
                remaining -= chunkLength;
                continue;
            }

            for (int index = 0; index < chunkLength; index++)
            {
                if (sourceBuffer[index] != candidateBuffer[index])
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Fastfile byte mismatch at offset {offset + index}: " +
                        $"source is 0x{sourceBuffer[index]:X2}, " +
                        $"candidate is 0x{candidateBuffer[index]:X2}.");
                }
            }

            throw new InvalidOperationException(
                "A mismatching fastfile chunk did not contain a differing byte.");
        }
    }

    private static FileStream OpenForSequentialRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ComparisonBufferSize,
            FileOptions.SequentialScan);

    private sealed record FileFingerprint(
        long Length,
        DateTime LastWriteTimeUtc,
        string Sha256);
}
