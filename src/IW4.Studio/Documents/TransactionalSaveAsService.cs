using IW4.FastFiles.Zone;
using IW4.FastFiles.Database;
using System.Security.Cryptography;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Emitters.Packaging;

namespace IW4.Studio.Documents;

public enum SaveAsStage
{
    Capturing,
    Validating,
    Compiling,
    Packaging,
    WritingTemporary,
    VerifyingCandidate,
    Committing,
    Acknowledging
}

public sealed record SaveAsProgress(SaveAsStage Stage, string Message);

/// <summary>
/// Optional semantic verification performed against the flushed temporary
/// fastfile before it can replace the requested destination.
/// </summary>
public interface ITransactionalSaveCandidateValidator
{
    /// <summary>
    /// Returns an empty collection when the candidate is valid. Every
    /// returned diagnostic rejects the candidate before commit.
    /// </summary>
    IReadOnlyList<string> Validate(
        string candidatePath,
        CancellationToken cancellationToken = default);
}

public sealed record SaveAsRequest(
    string DestinationPath,
    bool AllowOverwrite,
    FastFilePackagingPolicy? PackagingPolicy = null,
    long? ExpectedEditingSessionRevision = null,
    ITransactionalSaveCandidateValidator? CandidateValidator = null);

public sealed class SaveAsResult
{
    internal SaveAsResult(
        bool succeeded,
        bool cancelled,
        string? destinationPath,
        ZoneBuildValidation validation,
        IReadOnlyList<string> diagnostics)
    {
        Succeeded = succeeded;
        Cancelled = cancelled;
        DestinationPath = destinationPath;
        Validation = validation;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public bool Succeeded { get; }
    public bool Cancelled { get; }
    public string? DestinationPath { get; }
    public ZoneBuildValidation Validation { get; }
    public IReadOnlyList<string> Diagnostics { get; }

    public static SaveAsResult CancelledResult { get; } = new(
        succeeded: false,
        cancelled: true,
        destinationPath: null,
        validation: new ZoneBuildValidation([]),
        diagnostics: ["Save As was cancelled before a destination was selected."]);
}

/// <summary>Small filesystem boundary for exhaustive failure testing.  The
/// default implementation only ever deletes its own unique temporary sibling.</summary>
public interface ITransactionalSaveFileSystem
{
    string GetFullPath(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    byte[] ReadAllBytes(string path);
    string CreateTemporarySiblingPath(string destinationPath);
    void WriteAllBytesAndFlushNew(string path, ReadOnlySpan<byte> bytes);
    void CommitTemporaryFile(string temporaryPath, string destinationPath, bool overwrite);
    void DeleteTemporaryFile(string path);
}

public sealed class TransactionalSaveFileSystem : ITransactionalSaveFileSystem
{
    public string GetFullPath(string path) => Path.GetFullPath(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public string CreateTemporarySiblingPath(string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidDataException("Save destination has no containing directory.");
        string name = Path.GetFileName(destinationPath);
        for (int attempt = 0; attempt < 128; attempt++)
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
        // Same-directory rename is the atomic boundary on the supported
        // platforms.  Source/current opened files are rejected before this.
        File.Move(temporaryPath, destinationPath, overwrite);
    }

    public void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

/// <summary>
/// Application-layer Save As pipeline. It captures one revision, emits only
/// from that immutable snapshot, writes temporary siblings, publishes any
/// preserved sidecars before the primary fastfile, then acknowledges only
/// that capture. A sidecar-bearing save is fail-closed rather than
/// crash-atomic: abrupt termination can leave orphan sidecars, but never
/// intentionally publishes a new primary fastfile before all sidecars.
/// </summary>
public sealed class TransactionalSaveAsService
{
    private readonly ZoneBuildSnapshotBuilder _snapshotBuilder;
    private readonly ZoneLinker _linker;
    private readonly FastFilePackager _packager;
    private readonly ITransactionalSaveFileSystem _fileSystem;

    public TransactionalSaveAsService(
        ZoneBuildSnapshotBuilder? snapshotBuilder = null,
        FastFilePackager? packager = null,
        ITransactionalSaveFileSystem? fileSystem = null,
        ZoneLinker? linker = null)
    {
        _snapshotBuilder = snapshotBuilder ?? new ZoneBuildSnapshotBuilder();
        _linker = linker ?? new ZoneLinker();
        _packager = packager ?? new FastFilePackager();
        _fileSystem = fileSystem ?? new TransactionalSaveFileSystem();
    }

    public SaveAsResult SaveAs(
        FastFileEditingSession editingSession,
        SaveAsRequest request,
        IProgress<SaveAsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(editingSession);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        var diagnostics = new List<string>();
        ZoneBuildValidation validation = new([]);
        string? temporaryPath = null;
        var stagedSidecars = new List<StagedSidecar>();
        var committedSidecars = new List<string>();
        FastFileTransactionalSaveCaptureLease? captureLease = null;
        bool committed = false;
        try
        {
            string destinationPath = _fileSystem.GetFullPath(request.DestinationPath);
            string openedSource = _fileSystem.GetFullPath(editingSession.Workspace.TargetSource.PhysicalPath);
            if (string.Equals(destinationPath, openedSource, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save As cannot replace the currently opened source fastfile.");
            string destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidDataException("Save destination has no containing directory.");
            if (!_fileSystem.DirectoryExists(destinationDirectory))
                throw new DirectoryNotFoundException($"Save destination directory '{destinationDirectory}' does not exist.");
            if (_fileSystem.FileExists(destinationPath) && !request.AllowOverwrite)
                throw new IOException("Save destination already exists and overwrite was not approved.");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Capturing, "Capturing the current detached revision."));
            captureLease =
                editingSession.AcquireTransactionalSaveCaptureLease();
            FastFileEditingSaveSnapshot capture = captureLease.Capture;
            if (request.ExpectedEditingSessionRevision is { } expectedRevision &&
                capture.Revision != expectedRevision)
            {
                diagnostics.Add(
                    $"Editing-session revision changed from {expectedRevision} " +
                    $"to {capture.Revision}; discard the candidate and replan.");
                return new SaveAsResult(
                    false,
                    false,
                    null,
                    validation,
                    diagnostics);
            }
            ZoneBuildSnapshot snapshot = _snapshotBuilder.Capture(editingSession, capture);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Validating, "Checking every target row and transitive export requirement."));
            validation = snapshot.Validation;
            if (!validation.IsValid)
            {
                diagnostics.AddRange(validation.Errors.Select(blocker => blocker.ToString()));
                return new SaveAsResult(false, false, null, validation, diagnostics);
            }

            bool hasResourceOutputs = snapshot.ResourceOutputs.Count != 0;
            if (hasResourceOutputs &&
                _fileSystem.FileExists(destinationPath))
            {
                throw new IOException(
                    "A sidecar-bearing Save As cannot overwrite an existing " +
                    "fastfile; choose a new destination so the primary " +
                    "fastfile remains the final publication boundary.");
            }

            StagePreservedSidecars(
                snapshot,
                destinationDirectory,
                stagedSidecars);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Compiling, "Compiling the frozen decoded zone."));
            ZoneLinkResult link;
            try
            {
                ZoneLinkRequest linkRequest =
                    ZoneBuildSnapshotLinkAdapter.Create(snapshot);
                link = _linker.Link(linkRequest);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                    InvalidOperationException or
                    OverflowException or
                    ArgumentException)
            {
                var linkValidation = new ZoneBuildValidation(
                    [new ZoneBuildError(-1, "link", exception.Message)]);
                diagnostics.AddRange(
                    linkValidation.Errors.Select(blocker => blocker.ToString()));
                return new SaveAsResult(
                    false,
                    false,
                    null,
                    linkValidation,
                    diagnostics);
            }
            if (!link.Succeeded ||
                link.DecodedBytes is not { } decoded ||
                link.XFile is null)
            {
                var linkValidation = new ZoneBuildValidation(
                    link.Errors.Select(error =>
                        new ZoneBuildError(-1, "link", error)));
                diagnostics.AddRange(
                    linkValidation.Errors.Select(blocker => blocker.ToString()));
                return new SaveAsResult(
                    false,
                    false,
                    null,
                    linkValidation,
                    diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(SaveAsStage.Packaging, "Writing deterministic PS3 header and packed-stream framing in memory."));
            FastFilePackagingPolicy packagingPolicy =
                request.PackagingPolicy ??
                CreateSourcePreservingPackagingPolicy(
                    snapshot.ContainerEnvelope);
            FastFilePackagingResult package = _packager.Package(
                decoded,
                snapshot.ContainerEnvelope,
                packagingPolicy);
            diagnostics.AddRange(package.Errors.Select(error => $"{error.Code}: {error.Message}"));
            if (!package.Succeeded || package.Bytes is null)
                return new SaveAsResult(false, false, null, validation, diagnostics);

            cancellationToken.ThrowIfCancellationRequested();
            temporaryPath = _fileSystem.CreateTemporarySiblingPath(destinationPath);
            progress?.Report(new(SaveAsStage.WritingTemporary, "Writing and flushing a temporary sibling candidate."));
            _fileSystem.WriteAllBytesAndFlushNew(temporaryPath, package.Bytes.Value.Span);

            if (request.CandidateValidator is { } candidateValidator)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(
                    SaveAsStage.VerifyingCandidate,
                    "Reopening and validating the flushed candidate before commit."));
                string[] rejected = ValidateCandidate(
                    candidateValidator,
                    temporaryPath,
                    cancellationToken);
                if (rejected.Length != 0)
                {
                    diagnostics.AddRange(rejected);
                    return new SaveAsResult(
                        false,
                        false,
                        null,
                        validation,
                        diagnostics);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Committing,
                hasResourceOutputs
                    ? "Publishing preserved sidecars before the primary fastfile."
                    : "Atomically committing the temporary sibling."));
            foreach (StagedSidecar sidecar in stagedSidecars)
            {
                _fileSystem.CommitTemporaryFile(sidecar.TemporaryPath, sidecar.DestinationPath, overwrite: false);
                committedSidecars.Add(sidecar.DestinationPath);
                sidecar.IsCommitted = true;
            }
            // A sidecar-bearing candidate requires a fresh primary path. Keep
            // the final rename non-overwriting even when the caller generally
            // approved overwrite, so a path that appears during compilation
            // cannot silently replace an existing fastfile.
            _fileSystem.CommitTemporaryFile(
                temporaryPath,
                destinationPath,
                overwrite: request.AllowOverwrite && !hasResourceOutputs);
            committed = true;
            temporaryPath = null;

            progress?.Report(new(SaveAsStage.Acknowledging, "Acknowledging only the captured revision."));
            editingSession.MarkRevisionSaved(capture, new SavedDocumentState(
                destinationPath));
            return new SaveAsResult(true, false, destinationPath, validation, diagnostics);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add("Save As was cancelled before commit.");
            return new SaveAsResult(false, true, null, validation, diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            diagnostics.Add($"{exception.GetType().Name}: {exception.Message}");
            return new SaveAsResult(false, false, null, validation, diagnostics);
        }
        finally
        {
            captureLease?.Dispose();
            if (!committed)
            {
                foreach (string path in committedSidecars)
                {
                    try { _fileSystem.DeleteTemporaryFile(path); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                }
            }
            if (!committed && temporaryPath is not null)
            {
                try { _fileSystem.DeleteTemporaryFile(temporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Preserve the primary failure/cancellation while exposing
                    // failed cleanup in a future diagnostics sink.  The path
                    // is operation-owned and never a broad delete target.
                }
            }
            foreach (StagedSidecar sidecar in stagedSidecars.Where(value => !value.IsCommitted))
            {
                try { _fileSystem.DeleteTemporaryFile(sidecar.TemporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Transactional greenfield Save As. The document is frozen before any
    /// output is created, linked and packaged once, written to a temporary
    /// sibling, and atomically committed.
    /// </summary>
    public SaveAsResult SaveAs(
        NewZoneDocument document,
        SaveAsRequest request,
        IProgress<SaveAsProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);

        var diagnostics = new List<string>();
        ZoneBuildValidation validation = new([]);
        string? temporaryPath = null;
        bool committed = false;
        try
        {
            string destinationPath = _fileSystem.GetFullPath(request.DestinationPath);
            string destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidDataException("Save destination has no containing directory.");
            if (!_fileSystem.DirectoryExists(destinationDirectory))
                throw new DirectoryNotFoundException(
                    $"Save destination directory '{destinationDirectory}' does not exist.");
            if (_fileSystem.FileExists(destinationPath) && !request.AllowOverwrite)
                throw new IOException(
                    "Save destination already exists and overwrite was not approved.");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Capturing,
                "Freezing the source-independent new-zone graph."));
            ZoneLinkRequest frozen = document.FreezeRequest();

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Compiling,
                "Linking the frozen graph."));
            ZoneLinkResult link = _linker.Link(frozen);
            if (!link.Succeeded || link.DecodedBytes is null)
            {
                diagnostics.AddRange(link.Errors);
                return new SaveAsResult(false, false, null, validation, diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Packaging,
                "Packaging the decoded zone."));
            DbHeader envelope = document.CreateEnvelope(
                link.SelectedLanguageImageStreamEntries);
            FastFilePackagingPolicy packagingPolicy =
                request.PackagingPolicy ?? document.ContainerPolicy.PackagingPolicy;
            FastFilePackagingResult package = _packager.Package(
                link.DecodedBytes.Value,
                envelope,
                packagingPolicy);
            diagnostics.AddRange(package.Errors.Select(error =>
                $"{error.Code}: {error.Message}"));
            if (!package.Succeeded || package.Bytes is null)
                return new SaveAsResult(false, false, null, validation, diagnostics);

            cancellationToken.ThrowIfCancellationRequested();
            temporaryPath = _fileSystem.CreateTemporarySiblingPath(destinationPath);
            progress?.Report(new(
                SaveAsStage.WritingTemporary,
                "Writing and flushing a temporary sibling candidate."));
            _fileSystem.WriteAllBytesAndFlushNew(
                temporaryPath,
                package.Bytes.Value.Span);

            if (request.CandidateValidator is { } candidateValidator)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(
                    SaveAsStage.VerifyingCandidate,
                    "Reopening and validating the flushed candidate before commit."));
                string[] rejected = ValidateCandidate(
                    candidateValidator,
                    temporaryPath,
                    cancellationToken);
                if (rejected.Length != 0)
                {
                    diagnostics.AddRange(rejected);
                    return new SaveAsResult(
                        false,
                        false,
                        null,
                        validation,
                        diagnostics);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                SaveAsStage.Committing,
                "Atomically committing the temporary sibling."));
            _fileSystem.CommitTemporaryFile(
                temporaryPath,
                destinationPath,
                request.AllowOverwrite);
            committed = true;
            temporaryPath = null;

            progress?.Report(new(
                SaveAsStage.Acknowledging,
                "The greenfield fastfile is committed."));
            return new SaveAsResult(
                true,
                false,
                destinationPath,
                validation,
                diagnostics);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add("Save As was cancelled before commit.");
            return new SaveAsResult(false, true, null, validation, diagnostics);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException or
                ArgumentException or
                OverflowException)
        {
            diagnostics.Add($"{exception.GetType().Name}: {exception.Message}");
            return new SaveAsResult(false, false, null, validation, diagnostics);
        }
        finally
        {
            if (!committed && temporaryPath is not null)
            {
                try
                {
                    _fileSystem.DeleteTemporaryFile(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // Preserve the primary error; the operation-owned path is
                    // the only path ever considered for cleanup.
                }
            }
        }
    }

    private static FastFilePackagingPolicy CreateSourcePreservingPackagingPolicy(
        DbHeader envelope)
    {
        long physicalTrailerLength =
            envelope.SourceFileLength - envelope.FileSize;
        if (physicalTrailerLength is not 0 and not sizeof(ushort))
        {
            throw new InvalidDataException(
                "The imported fastfile has an unsupported physical trailer " +
                $"length of 0x{physicalTrailerLength:X}; expected zero or one " +
                "additional 16-bit terminator.");
        }

        return new FastFilePackagingPolicy(
            FileCreationTimeRaw: envelope.FileCreationTimeRaw,
            MaxFileSizePolicy: FastFileMaxFileSizePolicy.AtLeastFileSize,
            EmitDoubleTerminator:
                physicalTrailerLength == sizeof(ushort));
    }

    private static string[] ValidateCandidate(
        ITransactionalSaveCandidateValidator validator,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<string> candidateDiagnostics =
                validator.Validate(candidatePath, cancellationToken)
                ?? throw new InvalidDataException(
                    "The Save As candidate validator returned no result.");
            return candidateDiagnostics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => $"Candidate verification: {value}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException)
        {
            return
            [
                $"Candidate verification failed: " +
                $"{exception.GetType().Name}: {exception.Message}"
            ];
        }
    }

    private void StagePreservedSidecars(
        ZoneBuildSnapshot snapshot,
        string destinationDirectory,
        ICollection<StagedSidecar> staged)
    {
        if (snapshot.ResourceOutputs.Count == 0) return;
        string sourcePath = snapshot.SourcePhysicalPath ?? throw new InvalidDataException("A sidecar-bearing build snapshot has no opened source path.");
        string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? throw new InvalidDataException("Opened source fastfile has no containing directory.");
        foreach (ResourceOutputPlan plan in snapshot.ResourceOutputs)
        {
            string sourceSidecar = Path.Combine(sourceDirectory, plan.FileName);
            string destinationSidecar = Path.Combine(destinationDirectory, plan.FileName);
            // A package overwrite cannot be one atomic transaction with a
            // fastfile rename on every supported platform. Preserve safety by
            // requiring a new output location for sidecar-bearing candidates.
            if (_fileSystem.FileExists(destinationSidecar))
                throw new IOException($"Sidecar destination '{plan.FileName}' already exists; choose an empty output directory for this preserved-stream save.");
            byte[] bytes = _fileSystem.ReadAllBytes(sourceSidecar);
            string sha256Hex = Convert.ToHexString(SHA256.HashData(bytes));
            if (bytes.LongLength != plan.SourceLength ||
                !string.Equals(
                    sha256Hex,
                    plan.SourceSha256Hex,
                    StringComparison.Ordinal))
                throw new InvalidDataException($"Preserved sidecar '{plan.FileName}' changed since the frozen build snapshot was captured.");
            string temporary = _fileSystem.CreateTemporarySiblingPath(destinationSidecar);
            _fileSystem.WriteAllBytesAndFlushNew(temporary, bytes);
            staged.Add(new StagedSidecar(temporary, destinationSidecar));
        }
    }

    private sealed class StagedSidecar(string temporaryPath, string destinationPath)
    {
        public string TemporaryPath { get; } = temporaryPath;
        public string DestinationPath { get; } = destinationPath;
        public bool IsCommitted { get; set; }
    }
}
