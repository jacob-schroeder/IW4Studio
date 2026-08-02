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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        RootDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(RootDirectory))
            throw new DirectoryNotFoundException($"Fastfile directory '{RootDirectory}' does not exist.");

        _entries = new Dictionary<string, DbZoneCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(RootDirectory, "*.ff", SearchOption.TopDirectoryOnly))
        {
            string fullPath = Path.GetFullPath(path);
            string zoneName = Path.GetFileNameWithoutExtension(fullPath);
            if (!_entries.TryAdd(zoneName, new DbZoneCatalogEntry(zoneName, fullPath)))
                throw new InvalidDataException($"Fastfile directory contains duplicate logical zone '{zoneName}'.");
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
