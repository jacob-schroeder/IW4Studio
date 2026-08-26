using System.Collections.Concurrent;

namespace IW4.FastFiles.Streaming;

internal sealed class StreamPackagePathResolver
{
    private readonly ConcurrentDictionary<uint, string> _paths = [];
    private readonly string _packageDirectory;
    private readonly string _packageFilePrefix;
    private readonly string _packageDescription;

    public StreamPackagePathResolver(
        string fastFilePath,
        string packageFilePrefix,
        string packageDescription)
    {
        _packageDirectory =
            Path.GetDirectoryName(Path.GetFullPath(fastFilePath)) ??
            Environment.CurrentDirectory;
        _packageFilePrefix = packageFilePrefix;
        _packageDescription = packageDescription;
    }

    public bool TryResolve(
        uint fileIndex,
        out string packagePath,
        out string reason)
    {
        if (_paths.TryGetValue(fileIndex, out string? resolvedPath))
        {
            packagePath = resolvedPath;
            reason = string.Empty;
            return true;
        }

        string packageFileName = $"{_packageFilePrefix}{fileIndex}.pak";
        string adjacentPath = Path.Combine(
            _packageDirectory,
            packageFileName);
        string? parentDirectory = Path.GetDirectoryName(_packageDirectory);
        string? parentPath = parentDirectory is null
            ? null
            : Path.Combine(parentDirectory, packageFileName);
        string? availablePath = File.Exists(adjacentPath)
            ? adjacentPath
            : parentPath is not null && File.Exists(parentPath)
                ? parentPath
                : null;
        if (availablePath is null)
        {
            packagePath = string.Empty;
            reason = parentPath is null
                ? $"missing {_packageDescription} {adjacentPath}"
                : $"missing {_packageDescription}; checked {adjacentPath} and {parentPath}";
            return false;
        }

        packagePath = _paths.GetOrAdd(fileIndex, availablePath);
        reason = string.Empty;
        return true;
    }

    public void Clear() => _paths.Clear();
}
