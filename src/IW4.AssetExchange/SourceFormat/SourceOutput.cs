using System.Text;

namespace IW4.AssetExchange.SourceFormat;

internal sealed class SourceOutput
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    private readonly string _rootDirectory;

    public SourceOutput(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        string configuredRoot = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(configuredRoot);
        var root = new DirectoryInfo(configuredRoot);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            FileSystemInfo? resolved = root.ResolveLinkTarget(
                returnFinalTarget: true);
            if (resolved is not DirectoryInfo resolvedDirectory)
            {
                throw new InvalidDataException(
                    "The configured source directory link does not resolve to a directory.");
            }

            _rootDirectory = Path.GetFullPath(resolvedDirectory.FullName);
        }
        else
        {
            _rootDirectory = configuredRoot;
        }
    }

    public IReadOnlyList<string> WriteTextBatch(
        IEnumerable<(string RelativePath, Action<TextWriter> Write)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        (string RelativePath, Action<TextWriter> Write)[] requested = files
            .ToArray();
        if (requested.Any(file => file.Write is null))
        {
            throw new ArgumentException(
                "Source output writers cannot contain null.",
                nameof(files));
        }
        if (requested.Length == 0)
            return [];

        return WriteStreamBatch(requested.Select(file =>
            (
                file.RelativePath,
                (Action<Stream>)(stream =>
                {
                    using var writer = new StreamWriter(
                        stream,
                        Utf8WithoutBom,
                        bufferSize: 1024,
                        leaveOpen: true)
                    {
                        NewLine = "\n"
                    };
                    file.Write(writer);
                }))));
    }

    public IReadOnlyList<string> WriteBinaryBatch(
        IEnumerable<(string RelativePath, Action<Stream> Write)> files) =>
        WriteStreamBatch(files);

    internal static string NormalizeOwnedAssetName(
        string? name,
        string assetType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetType);
        if (string.IsNullOrWhiteSpace(name) ||
            !string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
            name.Any(char.IsControl))
        {
            throw new InvalidDataException(
                $"{assetType} has no valid asset name.");
        }
        if (name[0] == ',')
        {
            throw new InvalidDataException(
                $"A comma-prefixed {assetType} reference has no source body to unlink.");
        }

        string normalized = name.Replace('\\', '/');
        if (normalized[0] == '/' ||
            normalized.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"{assetType} asset name '{name}' cannot be mapped to a source-relative path.");
        }

        return normalized;
    }

    internal static string NormalizeReferencedAssetName(
        string? name,
        string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (name?.StartsWith(",", StringComparison.Ordinal) == true)
            name = name[1..];
        if (string.IsNullOrWhiteSpace(name) ||
            !string.Equals(name, name.Trim(), StringComparison.Ordinal) ||
            name.Any(char.IsControl))
        {
            throw new InvalidDataException(
                $"{field} has no valid asset name.");
        }

        return name;
    }

    private IReadOnlyList<string> WriteStreamBatch(
        IEnumerable<(string RelativePath, Action<Stream> Write)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        (string RelativePath, Action<Stream> Write)[] requested = files
            .ToArray();
        if (requested.Any(file => file.Write is null))
        {
            throw new ArgumentException(
                "Source output writers cannot contain null.",
                nameof(files));
        }
        if (requested.Length == 0)
            return [];

        var paths = new HashSet<string>(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        var pending = new List<PendingOutput>(requested.Length);
        foreach ((string relativePath, Action<Stream> write) in requested)
        {
            string outputPath = Resolve(relativePath);
            if (!paths.Add(outputPath))
            {
                throw new InvalidDataException(
                    $"Source output path '{relativePath}' occurs more than once in one write batch.");
            }

            string? directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "A source output path has no parent directory.");
            }

            EnsureSafeDirectory(directory);
            string token = Guid.NewGuid().ToString("N");
            pending.Add(new PendingOutput(
                outputPath,
                Path.Combine(
                    directory,
                    $".{Path.GetFileName(outputPath)}.{token}.tmp"),
                Path.Combine(
                    directory,
                    $".{Path.GetFileName(outputPath)}.{token}.bak"),
                write));
        }

        bool committedAll = false;
        try
        {
            foreach (PendingOutput file in pending)
            {
                using var stream = new FileStream(
                    file.TemporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                file.Write(stream);
            }

            foreach (PendingOutput file in pending)
            {
                if (File.Exists(file.OutputPath))
                {
                    File.Move(file.OutputPath, file.BackupPath);
                    file.HasBackup = true;
                }

                File.Move(file.TemporaryPath, file.OutputPath);
                file.IsCommitted = true;
            }

            committedAll = true;
            return Array.AsReadOnly(
                pending.Select(file => file.OutputPath).ToArray());
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (PendingOutput file in pending.AsEnumerable().Reverse())
            {
                try
                {
                    if (file.IsCommitted && File.Exists(file.OutputPath))
                    {
                        File.Delete(file.OutputPath);
                        file.IsCommitted = false;
                    }
                    if (file.HasBackup && File.Exists(file.BackupPath))
                    {
                        File.Move(file.BackupPath, file.OutputPath, overwrite: true);
                        file.HasBackup = false;
                    }
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures.Add(rollbackFailure);
                }
            }

            if (rollbackFailures.Count != 0)
            {
                throw new AggregateException(
                    "Source output failed and one or more prior files could not be restored.",
                    [failure, .. rollbackFailures]);
            }

            throw;
        }
        finally
        {
            foreach (PendingOutput file in pending)
            {
                TryDelete(file.TemporaryPath);
                if (committedAll)
                    TryDelete(file.BackupPath);
            }
        }
    }

    private string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Source output paths must be relative.");

        string outputPath = Path.GetFullPath(
            Path.Combine(_rootDirectory, relativePath));
        string relativeToRoot = Path.GetRelativePath(
            _rootDirectory,
            outputPath);
        if (relativeToRoot.Equals(".", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeToRoot) ||
            relativeToRoot.Equals("..", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Source output path '{relativePath}' escapes the configured source directory.");
        }

        return outputPath;
    }

    private void EnsureSafeDirectory(string directory)
    {
        string relativePath = Path.GetRelativePath(_rootDirectory, directory);
        string current = _rootDirectory;
        foreach (string segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"Source output directory '{current}' is a filesystem link.");
                }

                continue;
            }

            Directory.CreateDirectory(current);
        }
    }

    private sealed class PendingOutput(
        string outputPath,
        string temporaryPath,
        string backupPath,
        Action<Stream> write)
    {
        public string OutputPath { get; } = outputPath;
        public string TemporaryPath { get; } = temporaryPath;
        public string BackupPath { get; } = backupPath;
        public Action<Stream> Write { get; } = write;
        public bool HasBackup { get; set; }
        public bool IsCommitted { get; set; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
