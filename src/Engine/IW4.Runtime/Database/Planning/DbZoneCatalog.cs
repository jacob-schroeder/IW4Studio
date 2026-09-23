using System.Collections.ObjectModel;

namespace IW4.Runtime.Database.Planning;

/// <summary>
/// Case-insensitive catalog of physical .ff files. Dependency policy belongs
/// to a planner rather than being inferred by this catalog.
/// </summary>
public sealed class DbZoneCatalog
{
    private readonly Dictionary<string, DbZoneCatalogEntry> _entries;

    public DbZoneCatalog(string directory)
        : this(directory, [])
    {
    }

    public DbZoneCatalog(
        string directory,
        IEnumerable<string> additionalDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(additionalDirectories);

        RootDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(RootDirectory))
            throw new DirectoryNotFoundException($"Fastfile directory '{RootDirectory}' does not exist.");

        _entries = new Dictionary<string, DbZoneCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        string[] catalogDirectories =
        [
            RootDirectory,
            .. additionalDirectories
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !string.Equals(
                    path,
                    RootDirectory,
                    StringComparison.OrdinalIgnoreCase))
        ];

        foreach (string catalogDirectory in catalogDirectories)
        {
            if (!Directory.Exists(catalogDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Fastfile directory '{catalogDirectory}' does not exist.");
            }

            foreach (string path in Directory.EnumerateFiles(
                         catalogDirectory,
                         "*.ff",
                         SearchOption.TopDirectoryOnly))
            {
                string fullPath = Path.GetFullPath(path);
                string zoneName = Path.GetFileNameWithoutExtension(fullPath);
                var entry = new DbZoneCatalogEntry(zoneName, fullPath);
                if (_entries.TryAdd(zoneName, entry))
                    continue;

                throw new InvalidDataException(
                    $"Fastfile catalog contains duplicate logical zone '{zoneName}'.");
            }
        }

        Entries = new ReadOnlyCollection<DbZoneCatalogEntry>(
            _entries.Values.OrderBy(entry => entry.ZoneName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public string RootDirectory { get; }

    public IReadOnlyList<DbZoneCatalogEntry> Entries { get; }

    public bool TryGet(string zoneName, out DbZoneCatalogEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneName);
        return _entries.TryGetValue(NormalizeZoneName(zoneName), out entry!);
    }

    public string ExpectedPath(string zoneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneName);
        return Path.Combine(RootDirectory, NormalizeZoneName(zoneName) + ".ff");
    }

    public static string NormalizeZoneName(string nameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrPath);
        return Path.GetFileNameWithoutExtension(nameOrPath.Trim());
    }
}
