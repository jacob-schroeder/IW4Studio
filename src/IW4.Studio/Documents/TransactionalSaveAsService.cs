using IW4.FastFiles.Database;
using IW4.Linker;
using IW4.Linker.Packaging;

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
/// Implementations may delete only operation-created temporary paths.
/// </summary>
public interface ITransactionalSaveFileSystem
{
    string GetFullPath(string path);
    string ResolveExistingDirectoryPath(string path);
    string ResolveExistingFilePath(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    string CreateTemporarySiblingPath(string destinationPath);
    void WriteAllBytesAndFlushNew(string path, ReadOnlySpan<byte> bytes);
    void CommitTemporaryFile(string temporaryPath, string destinationPath, bool overwrite);
    void DeleteTemporaryFile(string path);
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

    public string CreateTemporarySiblingPath(string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidDataException("Save destination has no containing directory.");
        string name = Path.GetFileName(destinationPath);
        for (int attempt = 0; attempt != 128; attempt++)
        {
            string candidate = Path.Combine(directory, $".{name}.{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Could not allocate a unique temporary Save As sibling path.");
    }

    public void WriteAllBytesAndFlushNew(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public void CommitTemporaryFile(string temporaryPath, string destinationPath, bool overwrite)
    {
        if (File.Exists(destinationPath) && !overwrite)
            throw new IOException("Save destination already exists and overwrite was not approved.");

        // A same-directory rename is this service's atomic publication edge.
        File.Move(temporaryPath, destinationPath, overwrite);
    }

    public void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
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
/// Links and packages the session's single frozen object file, then publishes
/// it through a flushed temporary sibling and same-directory atomic move.
/// </summary>
public sealed class TransactionalSaveAsService
{
    private readonly ZoneLinker _linker;
    private readonly FastFilePackager _packager;
    private readonly ITransactionalSaveFileSystem _fileSystem;

    public TransactionalSaveAsService(
        FastFilePackager? packager = null,
        ITransactionalSaveFileSystem? fileSystem = null,
        ZoneLinker? linker = null)
    {
        _linker = linker ?? new ZoneLinker();
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
        string? temporaryPath = null;
        bool committed = false;
        try
        {
            string requestedDestinationPath = _fileSystem.GetFullPath(request.DestinationPath);
            string destinationDirectory = Path.GetDirectoryName(requestedDestinationPath)
                ?? throw new InvalidDataException("Save destination has no containing directory.");
            if (!_fileSystem.DirectoryExists(destinationDirectory))
                throw new DirectoryNotFoundException($"Save destination directory '{destinationDirectory}' does not exist.");
            string physicalDestinationDirectory =
                _fileSystem.ResolveExistingDirectoryPath(destinationDirectory);
            string destinationPath = Path.Combine(
                physicalDestinationDirectory,
                Path.GetFileName(requestedDestinationPath));
            string physicalSourcePath = _fileSystem.ResolveExistingFilePath(revision.SourcePath);
            if (string.Equals(destinationPath, physicalSourcePath, StringComparison.OrdinalIgnoreCase) ||
                (_fileSystem.FileExists(destinationPath) &&
                 string.Equals(
                     _fileSystem.ResolveExistingFilePath(destinationPath),
                     physicalSourcePath,
                     StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "Save As cannot replace the currently opened source fastfile through a physical path alias.");
            }
            if (_fileSystem.FileExists(destinationPath) && !request.AllowOverwrite)
                throw new IOException("Save destination already exists and overwrite was not approved.");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Linking, "Linking the frozen zone object."));
            ZoneLinkResult link = _linker.Link(revision.ZoneObjectFile);
            diagnostics.AddRange(link.Errors.Select(error => $"{error.Code}: {error.Message}"));
            if (!link.Succeeded || link.DecodedBytes is not { } decodedBytes)
                return new SaveAsResult(false, false, null, diagnostics);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Packaging, "Packaging the linked decoded zone."));
            FastFilePackagingPolicy policy = request.PackagingPolicy ??
                CreateSourcePreservingPackagingPolicy(revision.Header);
            FastFilePackagingResult package = _packager.Package(
                decodedBytes,
                revision.Header,
                policy);
            diagnostics.AddRange(package.Errors.Select(error => $"{error.Code}: {error.Message}"));
            if (!package.Succeeded || package.Bytes is not { } packageBytes)
                return new SaveAsResult(false, false, null, diagnostics);

            cancellationToken.ThrowIfCancellationRequested();
            temporaryPath = _fileSystem.CreateTemporarySiblingPath(destinationPath);
            progress?.Report(new(SaveAsStage.WritingTemporary, "Writing and flushing a temporary sibling candidate."));
            _fileSystem.WriteAllBytesAndFlushNew(temporaryPath, packageBytes.Span);

            if (request.CandidateValidator is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(SaveAsStage.VerifyingCandidate, "Validating the flushed candidate."));
                IReadOnlyList<string> candidateDiagnostics = request.CandidateValidator.Validate(
                    temporaryPath,
                    cancellationToken)
                    ?? throw new InvalidDataException("The Save As candidate validator returned no result.");
                diagnostics.AddRange(candidateDiagnostics.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (candidateDiagnostics.Any(value => !string.IsNullOrWhiteSpace(value)))
                    return new SaveAsResult(false, false, null, diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Committing, "Atomically committing the temporary sibling."));
            _fileSystem.CommitTemporaryFile(temporaryPath, destinationPath, request.AllowOverwrite);
            committed = true;
            temporaryPath = null;
            return new SaveAsResult(true, false, destinationPath, diagnostics);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add("Save As was cancelled before commit.");
            return new SaveAsResult(false, true, null, diagnostics);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            OverflowException)
        {
            diagnostics.Add($"{exception.GetType().Name}: {exception.Message}");
            return new SaveAsResult(false, false, null, diagnostics);
        }
        finally
        {
            if (!committed && temporaryPath is not null)
            {
                try
                {
                    _fileSystem.DeleteTemporaryFile(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Keep the primary failure; cleanup is restricted to the
                    // operation-created temporary sibling.
                }
            }
        }
    }

    private static FastFilePackagingPolicy CreateSourcePreservingPackagingPolicy(DbHeader header)
    {
        long physicalTrailerLength = header.SourceFileLength - header.FileSize;
        if (physicalTrailerLength is not 0 and not sizeof(ushort))
        {
            throw new InvalidDataException(
                "The imported fastfile has an unsupported physical trailer length.");
        }

        return new FastFilePackagingPolicy(
            FileCreationTimeRaw: header.FileCreationTimeRaw,
            MaxFileSizePolicy: FastFileMaxFileSizePolicy.AtLeastFileSize,
            EmitDoubleTerminator: physicalTrailerLength == sizeof(ushort));
    }
}
