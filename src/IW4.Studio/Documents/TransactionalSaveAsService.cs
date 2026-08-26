using IW4.FastFiles.Database;
using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Database.Planning;
using IW4.FastFiles.Zone;
using IW4.Linker.Linking;
using IW4.Linker.Packaging;
using System.Security.Cryptography;

namespace IW4.Studio.Documents;

public enum SaveAsStage
{
    Linking,
    Packaging,
    WritingTemporary,
    VerifyingCandidate,
    Committing
}

public sealed record SaveAsProgress(SaveAsStage Stage, string Message);

/// <summary>
/// Optional post-write constraint for callers that need to inspect the
/// flushed candidate before it is published.
/// </summary>
public interface ITransactionalSaveCandidateValidator
{
    /// <summary>Returns diagnostics that reject the candidate.</summary>
    IReadOnlyList<string> Validate(
        string candidatePath,
        CancellationToken cancellationToken = default);
}

public sealed record SaveAsRequest(
    string DestinationPath,
    bool AllowOverwrite,
    FastFilePackagingPolicy? PackagingPolicy = null,
    ITransactionalSaveCandidateValidator? CandidateValidator = null);

public sealed class SaveAsResult
{
    internal SaveAsResult(
        bool succeeded,
        bool cancelled,
        string? destinationPath,
        IEnumerable<string> diagnostics)
    {
        Succeeded = succeeded;
        Cancelled = cancelled;
        DestinationPath = destinationPath;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public bool Succeeded { get; }
    public bool Cancelled { get; }
    public string? DestinationPath { get; }
    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Narrow filesystem boundary for the transactional publication protocol.
/// Save As assumes its destination directory is not concurrently mutated by a
/// hostile same-user process. Files stay open from exclusive creation through
/// validation, publication, or rollback.
/// </summary>
public interface ITransactionalSaveFileSystem
{
    string GetFullPath(string path);
    string ResolveExistingDirectoryPath(string path);
    string ResolveExistingFilePath(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    TransactionalSaveDirectory CreateTemporarySiblingDirectory(
        string destinationPath);
    TransactionalSaveFile WriteTemporaryFile(
        TransactionalSaveDirectory directory,
        string fileName,
        ReadOnlySpan<byte> bytes);
    void CommitTemporaryFile(
        TransactionalSaveFile temporaryFile,
        string destinationPath,
        bool overwrite);
    void RollbackFile(TransactionalSaveFile file);
    void DeleteTemporaryDirectory(TransactionalSaveDirectory directory);
}

/// <summary>
/// One private staging directory created beside the publication target.
/// </summary>
public sealed class TransactionalSaveDirectory
{
    internal TransactionalSaveDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public string Path { get; }
}

/// <summary>
/// An operation-created file retained open until publication or rollback.
/// </summary>
public sealed class TransactionalSaveFile : IDisposable
{
    private readonly byte[] _contentSha256;
    private FileStream? _stream;

    internal TransactionalSaveFile(
        string path,
        long length,
        ReadOnlySpan<byte> contentSha256,
        FileStream stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (contentSha256.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                $"A file identity requires exactly {SHA256.HashSizeInBytes} SHA-256 bytes.",
                nameof(contentSha256));
        }

        ArgumentNullException.ThrowIfNull(stream);
        Path = path;
        Length = length;
        _contentSha256 = contentSha256.ToArray();
        _stream = stream;
    }

    public string Path { get; private set; }
    public long Length { get; }
    public ReadOnlyMemory<byte> ContentSha256 => _contentSha256;

    internal FileStream Stream => _stream ??
        throw new ObjectDisposedException(nameof(TransactionalSaveFile));

    internal void MoveTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}

public sealed class TransactionalSaveFileSystem : ITransactionalSaveFileSystem
{
    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string ResolveExistingDirectoryPath(string path) =>
        ResolveExistingPath(path, requireDirectory: true);

    public string ResolveExistingFilePath(string path) =>
        ResolveExistingPath(path, requireDirectory: false);

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);

    public TransactionalSaveDirectory CreateTemporarySiblingDirectory(
        string destinationPath)
    {
        RejectProtectedImageFilePackageMutation(destinationPath);
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidDataException("Save destination has no containing directory.");
        string name = Path.GetFileName(destinationPath);
        for (int attempt = 0; attempt != 128; attempt++)
        {
            string candidate = Path.Combine(directory, $".{name}.{Guid.NewGuid():N}.save");
            if (Directory.Exists(candidate) || File.Exists(candidate))
                continue;

            Directory.CreateDirectory(candidate);
            return new TransactionalSaveDirectory(candidate);
        }

        throw new IOException("Could not create a unique Save As staging directory.");
    }

    public TransactionalSaveFile WriteTemporaryFile(
        TransactionalSaveDirectory directory,
        string fileName,
        ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            throw new ArgumentException("A staged file name cannot contain a directory.", nameof(fileName));
        RejectProtectedImageFilePackageMutation(fileName);

        string path = Path.Combine(directory.Path, fileName);
        byte[] contentSha256 = SHA256.HashData(bytes);
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete,
                bufferSize: 81920,
                FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            var file = new TransactionalSaveFile(
                path,
                bytes.Length,
                contentSha256,
                stream);
            stream = null;
            return file;
        }
        catch
        {
            if (stream is not null)
            {
                try
                {
                    File.Delete(path);
                }
                finally
                {
                    stream.Dispose();
                }
            }

            throw;
        }
    }

    public void CommitTemporaryFile(
        TransactionalSaveFile temporaryFile,
        string destinationPath,
        bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(temporaryFile);
        RejectProtectedImageFilePackageMutation(temporaryFile.Path);
        RejectProtectedImageFilePackageMutation(destinationPath);
        VerifyOperationCreatedFile(temporaryFile);
        if (File.Exists(destinationPath) && !overwrite)
            throw new IOException("Save destination already exists and overwrite was not approved.");

        // A same-directory rename is this service's atomic publication edge.
        File.Move(temporaryFile.Path, destinationPath, overwrite);
        temporaryFile.MoveTo(destinationPath);
    }

    public void RollbackFile(TransactionalSaveFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        RejectProtectedImageFilePackageMutation(file.Path);
        try
        {
            VerifyOperationCreatedFile(file);
            File.Delete(file.Path);
        }
        finally
        {
            file.Dispose();
        }
    }

    public void DeleteTemporaryDirectory(TransactionalSaveDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        RejectProtectedImageFilePackageMutation(directory.Path);
        Directory.Delete(directory.Path, recursive: false);
    }

    private static void RejectProtectedImageFilePackageMutation(string path)
    {
        if (TransactionalSaveAsService.IsProtectedImageFilePackagePath(path))
        {
            throw new InvalidOperationException(
                "Packages named 'imagefile*.pak' are immutable and cannot be mutated by Save As.");
        }
    }

    private static void VerifyOperationCreatedFile(TransactionalSaveFile file)
    {
        file.Stream.Flush(flushToDisk: true);
        var info = new FileInfo(file.Path);
        if (!info.Exists || info.Length != file.Length)
        {
            throw new IOException(
                $"Operation-created file '{file.Path}' no longer has its owned identity.");
        }

        using var stream = new FileStream(
            file.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete);
        byte[] actualSha256 = SHA256.HashData(stream);
        if (!CryptographicOperations.FixedTimeEquals(
                actualSha256,
                file.ContentSha256.Span))
        {
            throw new IOException(
                $"Operation-created file '{file.Path}' was replaced after creation.");
        }
    }

    private static string ResolveExistingPath(string path, bool requireDirectory)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Path '{fullPath}' has no filesystem root.");
        string currentPath = Path.TrimEndingDirectorySeparator(root);
        string relativePath = Path.GetRelativePath(root, fullPath);
        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            if (!requireDirectory)
                throw new FileNotFoundException("The filesystem root is not a file.", fullPath);
            return currentPath;
        }

        string[] components = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < components.Length; index++)
        {
            bool finalComponent = index == components.Length - 1;
            string candidate = Path.Combine(currentPath, components[index]);
            FileSystemInfo entry = finalComponent && !requireDirectory
                ? new FileInfo(candidate)
                : new DirectoryInfo(candidate);
            if (!entry.Exists)
            {
                if (finalComponent && !requireDirectory)
                    throw new FileNotFoundException($"File '{candidate}' does not exist.", candidate);
                throw new DirectoryNotFoundException($"Directory '{candidate}' does not exist.");
            }

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                FileSystemInfo? target = entry.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !target.Exists)
                    throw new IOException($"Filesystem link '{entry.FullName}' has no existing target.");
                if ((!finalComponent || requireDirectory) && target is not DirectoryInfo)
                    throw new IOException($"Filesystem link '{entry.FullName}' does not resolve to a directory.");
                if (finalComponent && !requireDirectory && target is not FileInfo)
                    throw new IOException($"Filesystem link '{entry.FullName}' does not resolve to a file.");

                currentPath = Path.TrimEndingDirectorySeparator(target.FullName);
            }
            else
            {
                currentPath = Path.TrimEndingDirectorySeparator(entry.FullName);
            }
        }

        return currentPath;
    }
}

/// <summary>
/// Canonically links one immutable semantic revision, preserves its read-only
/// imagefile references, and atomically publishes the fastfile.
/// </summary>
public sealed class TransactionalSaveAsService
{
    private readonly ZoneLinker _zoneLinker;
    private readonly FastFilePackager _packager;
    private readonly ITransactionalSaveFileSystem _fileSystem;

    public TransactionalSaveAsService(
        ZoneLinker? zoneLinker = null,
        FastFilePackager? packager = null,
        ITransactionalSaveFileSystem? fileSystem = null)
    {
        _zoneLinker = zoneLinker ?? new ZoneLinker();
        _packager = packager ?? new FastFilePackager();
        _fileSystem = fileSystem ?? new TransactionalSaveFileSystem();
    }

    public SaveAsResult SaveAs(
        FastFileEditingSession session,
        SaveAsRequest request,
        IProgress<SaveAsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        session.ThrowIfDisposed();
        FastFileSaveRevision revision = session.CaptureRevision();

        var diagnostics = new List<string>();
        TransactionalSaveDirectory? temporaryDirectory = null;
        TransactionalSaveFile? temporaryFastFile = null;
        try
        {
            string requestedDestinationPath = _fileSystem.GetFullPath(request.DestinationPath);
            if (IsProtectedImageFilePackagePath(requestedDestinationPath))
            {
                throw new InvalidOperationException(
                    "Save As cannot target an immutable imagefile*.pak package.");
            }
            string destinationDirectory = Path.GetDirectoryName(requestedDestinationPath)
                ?? throw new InvalidDataException("Save destination has no containing directory.");
            if (!_fileSystem.DirectoryExists(destinationDirectory))
                throw new DirectoryNotFoundException($"Save destination directory '{destinationDirectory}' does not exist.");
            string physicalDestinationDirectory =
                _fileSystem.ResolveExistingDirectoryPath(destinationDirectory);
            string destinationPath = Path.Combine(
                physicalDestinationDirectory,
                Path.GetFileName(requestedDestinationPath));
            if (revision.SourcePath is { } sourcePath &&
                IsSourceDestinationAlias(sourcePath, destinationPath))
            {
                throw new InvalidOperationException(
                    "Save As cannot replace the currently opened source fastfile through a physical path alias.");
            }
            if (_fileSystem.FileExists(destinationPath) && !request.AllowOverwrite)
                throw new IOException("Save destination already exists and overwrite was not approved.");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Linking,
                $"Canonically linking semantic revision {revision.Revision}."));
            ZoneLinkResult link = _zoneLinker.Link(revision.LinkRequest);
            diagnostics.AddRange(link.Errors);
            if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
                return new SaveAsResult(false, false, null, diagnostics);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Packaging,
                "Packaging the linked zone."));

            FastFilePackagingResult package = _packager.PackageGreenfield(
                decodedBytes,
                link.LanguageMask,
                link.SelectedLanguageMask,
                link.ImageStreamLanguageTables,
                request.PackagingPolicy);
            diagnostics.AddRange(package.Errors.Select(error => $"{error.Code}: {error.Message}"));
            if (!package.Succeeded || package.Bytes is not { } packageBytes)
                return new SaveAsResult(false, false, null, diagnostics);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.WritingTemporary,
                "Writing and flushing the staged fastfile candidate."));
            temporaryDirectory = _fileSystem.CreateTemporarySiblingDirectory(
                destinationPath);
            temporaryFastFile = _fileSystem.WriteTemporaryFile(
                temporaryDirectory,
                Path.GetFileName(destinationPath),
                packageBytes.Span);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.VerifyingCandidate,
                "Fresh-loading the flushed canonical candidate."));
            ValidateFreshCandidate(
                session.Workspace,
                destinationPath,
                temporaryFastFile.Path,
                cancellationToken);

            if (request.CandidateValidator is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(
                    SaveAsStage.VerifyingCandidate,
                    "Applying caller candidate constraints."));
                IReadOnlyList<string> candidateDiagnostics = request.CandidateValidator.Validate(
                    temporaryFastFile.Path,
                    cancellationToken)
                    ?? throw new InvalidDataException("The Save As candidate validator returned no result.");
                diagnostics.AddRange(candidateDiagnostics.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (candidateDiagnostics.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    RollbackPendingFiles(
                        ref temporaryFastFile,
                        ref temporaryDirectory,
                        diagnostics);
                    return new SaveAsResult(false, false, null, diagnostics);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Committing,
                "Atomically publishing the fastfile."));
            if (!session.CommitSaveIfCurrentRevision(
                    revision.Revision,
                    () =>
                    {
                        _fileSystem.CommitTemporaryFile(
                            temporaryFastFile,
                            destinationPath,
                            request.AllowOverwrite);
                    }))
            {
                diagnostics.Add(
                    $"Semantic revision {revision.Revision} became stale before publication.");
                RollbackPendingFiles(
                    ref temporaryFastFile,
                    ref temporaryDirectory,
                    diagnostics);
                return new SaveAsResult(false, false, null, diagnostics);
            }

            ReleasePublishedFile(ref temporaryFastFile, diagnostics);
            DeleteTemporaryDirectory(ref temporaryDirectory, diagnostics);
            return new SaveAsResult(true, false, destinationPath, diagnostics);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add("Save As was cancelled before commit.");
            RollbackPendingFiles(
                ref temporaryFastFile,
                ref temporaryDirectory,
                diagnostics);
            return new SaveAsResult(false, true, null, diagnostics);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            ArgumentException or
            KeyNotFoundException or
            OverflowException)
        {
            diagnostics.Add($"{exception.GetType().Name}: {exception.Message}");
            RollbackPendingFiles(
                ref temporaryFastFile,
                ref temporaryDirectory,
                diagnostics);
            return new SaveAsResult(false, false, null, diagnostics);
        }
        finally
        {
            RollbackPendingFiles(
                ref temporaryFastFile,
                ref temporaryDirectory,
                diagnostics);
        }
    }

    private static void ValidateFreshCandidate(
        FastFileWorkspace workspace,
        string destinationPath,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        cancellationToken.ThrowIfCancellationRequested();
        using var loadSession = new DbLoadSession();
        string targetZoneName = Path.GetFileNameWithoutExtension(destinationPath);
        bool validateDefaultMpLifecycle =
            !workspace.IsBlank &&
            targetZoneName.StartsWith("mp_", StringComparison.OrdinalIgnoreCase);
        LoadedXZone loadedZone;
        if (validateDefaultMpLifecycle)
        {
            string dependencyDirectory =
                FastFileDocumentService.ResolveDependencyDirectory(workspace.SourcePath);
            loadedZone = DbDefaultZoneDependencyLoader.LoadDefaultMpCandidateForValidation(
                loadSession,
                targetZoneName,
                candidatePath,
                dependencyDirectory,
                FastFileDocumentService.ResolveAdditionalDependencyDirectories(
                    dependencyDirectory));
        }
        else
        {
            loadedZone = loadSession.DB_LoadXZone(
                candidatePath,
                XZoneFlags.DB_ZONE_DEV);
            DbDefaultZoneDependencyLoader.ValidatePs3VertexShaderCapacity(
                loadSession,
                targetZoneName);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _ = loadSession.FreezeLinkAssetPool();
        cancellationToken.ThrowIfCancellationRequested();
        _ = loadedZone.FreezeLinkRoots();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void RollbackPendingFiles(
        ref TransactionalSaveFile? fastFile,
        ref TransactionalSaveDirectory? directory,
        ICollection<string> diagnostics)
    {
        RollbackFile(ref fastFile, "fastfile", diagnostics);
        DeleteTemporaryDirectory(ref directory, diagnostics);
    }

    private void RollbackFile(
        ref TransactionalSaveFile? file,
        string description,
        ICollection<string> diagnostics)
    {
        TransactionalSaveFile? ownedFile = file;
        file = null;
        if (ownedFile is null)
            return;

        try
        {
            _fileSystem.RollbackFile(ownedFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(
                $"Could not roll back the operation-created {description} '{ownedFile.Path}': {exception.Message}");
        }
    }

    private static void ReleasePublishedFile(
        ref TransactionalSaveFile? file,
        ICollection<string> diagnostics)
    {
        TransactionalSaveFile? publishedFile = file;
        file = null;
        if (publishedFile is null)
            return;

        try
        {
            publishedFile.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(
                $"Published '{publishedFile.Path}', but releasing its file handle failed: {exception.Message}");
        }
    }

    private void DeleteTemporaryDirectory(
        ref TransactionalSaveDirectory? directory,
        ICollection<string> diagnostics)
    {
        TransactionalSaveDirectory? ownedDirectory = directory;
        directory = null;
        if (ownedDirectory is null)
            return;

        try
        {
            _fileSystem.DeleteTemporaryDirectory(ownedDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(
                $"Could not remove Save As staging directory '{ownedDirectory.Path}': {exception.Message}");
        }
    }

    private bool IsSourceDestinationAlias(string sourcePath, string destinationPath)
    {
        string fullSourcePath = _fileSystem.GetFullPath(sourcePath);
        if (string.Equals(
                destinationPath,
                fullSourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_fileSystem.FileExists(fullSourcePath))
            return false;

        string physicalSourcePath = _fileSystem.ResolveExistingFilePath(fullSourcePath);
        return string.Equals(
                   destinationPath,
                   physicalSourcePath,
                   StringComparison.OrdinalIgnoreCase) ||
               (_fileSystem.FileExists(destinationPath) &&
                string.Equals(
                    _fileSystem.ResolveExistingFilePath(destinationPath),
                    physicalSourcePath,
                    StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsProtectedImageFilePackagePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        const string prefix = "imagefile";
        const string suffix = ".pak";
        string fileName = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(path));
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

}
