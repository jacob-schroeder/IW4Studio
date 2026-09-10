using System.Security.Cryptography;
using System.Text.Json;
using MapConverter.Game.IW3.PC.Images;
using MapConverter.Game.IW3.PS3.Extraction;

namespace MapConverter.Game.IW3.PC.Extraction;

/// <summary>
/// Resolves named IW3 source assets and retains their owning fastfiles.
/// </summary>
internal sealed class Iw3PcSourceLibrary
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly HashSet<string> ExportedAssetTypes = new(StringComparer.Ordinal)
    {
        "image", "material", "xmodel", "rawfile", "mapents", "physpreset", "techniqueset",
    };

    private readonly string _nativePath;
    private readonly string _libraryDirectory;
    private readonly string _scratchDirectory;
    private readonly string? _customIwd;
    private readonly List<Source> _sources = [];
    private readonly Dictionary<(string Type, string Key), List<Node>> _providers = [];
    private readonly Dictionary<string, Closure> _closures = new(PathComparer);
    private readonly Dictionary<(string Type, string Key), Node> _selectedDefinitions = [];
    private readonly List<ExportedFile> _exportedFiles = [];
    private readonly Dictionary<string, IReadOnlyList<string>> _imageSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _soundSources = new(StringComparer.OrdinalIgnoreCase);
    private int _exportSequence;

    private Iw3PcSourceLibrary(
        string nativePath, string libraryDirectory, string scratchDirectory, string? customIwd,
        IReadOnlyList<string> iwdPaths)
    {
        _nativePath = nativePath;
        _libraryDirectory = libraryDirectory;
        _scratchDirectory = scratchDirectory;
        _customIwd = customIwd;
        IwdPaths = iwdPaths;
    }

    internal IReadOnlyList<string> IwdPaths { get; }

    internal static async Task<Iw3PcSourceLibrary> ResolveAsync(
        string nativePath,
        string mapFF,
        string loadFF,
        string? customIwd,
        string libraryDirectory,
        string scratch,
        CancellationToken cancellationToken)
    {
        nativePath = ExistingPath(nativePath, directory: false);
        mapFF = ExistingPath(mapFF, directory: false);
        loadFF = ExistingPath(loadFF, directory: false);
        libraryDirectory = ExistingPath(libraryDirectory, directory: true);
        scratch = ExistingPath(scratch, directory: true);
        customIwd = customIwd is null ? null : ExistingPath(customIwd, directory: false);
        if (PathComparer.Equals(mapFF, loadFF))
            throw new ArgumentException("Map and load source fastfiles must be different files.");

        string[] files = EnumerateLibraryFiles(libraryDirectory, cancellationToken).ToArray();
        var iwdPaths = new List<string>();
        if (customIwd is not null)
            iwdPaths.Add(customIwd);
        iwdPaths.AddRange(files.Where(path =>
            path.EndsWith(".iwd", StringComparison.OrdinalIgnoreCase) && !PathComparer.Equals(path, customIwd)));
        var library = new Iw3PcSourceLibrary(
            nativePath, libraryDirectory, scratch, customIwd, iwdPaths.AsReadOnly());

        var inputs = new[] { (Path: mapFF, Role: "map"), (Path: loadFF, Role: "load") };
        foreach ((string path, string role) in inputs.Concat(files
                     .Where(path => path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase) &&
                         !PathComparer.Equals(path, mapFF) && !PathComparer.Equals(path, loadFF))
                     .Select(path => (Path: path, Role: "library"))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string hash = await HashFileAsync(path, cancellationToken).ConfigureAwait(false);
            bool isPs3 = Iw3Ps3FastFileCatalog.IsFastFile(path);
            Catalog catalog;
            try
            {
                if (isPs3)
                {
                    catalog = Iw3Ps3FastFileCatalog.Read(path, cancellationToken);
                }
                else
                {
                    string catalogPath = Path.Combine(scratch, $"source-catalog-{library._sources.Count:D6}.json");
                    RejectExisting(catalogPath);
                    await Iw3PcExtractionBackend.RunToolAsync(
                        $"IW3 source catalog '{path}'", nativePath, scratch,
                        ["--catalog-assets", path, catalogPath], cancellationToken).ConfigureAwait(false);
                    await using FileStream stream = File.OpenRead(catalogPath);
                    catalog = await JsonSerializer.DeserializeAsync<Catalog>(stream, cancellationToken: cancellationToken)
                        .ConfigureAwait(false) ?? throw new InvalidDataException("The catalog is null.");
                }
                await VerifyHashAsync(path, hash, cancellationToken).ConfigureAwait(false);
                ValidateCatalog(catalog);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
            {
                throw new InvalidDataException($"Invalid IW3 source catalog for '{path}': {exception.Message}", exception);
            }

            var source = new Source(path, hash, role, catalog, isPs3);
            library._sources.Add(source);
            foreach (CatalogAsset asset in catalog.Assets.Where(asset => !asset.IsReference))
            {
                var key = (asset.Type, asset.Key);
                if (!library._providers.TryGetValue(key, out List<Node>? providers))
                    library._providers.Add(key, providers = []);
                providers.Add(new Node(source, asset));
            }
        }

        var errors = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Source source in library._sources.Where(source => source.Role != "library"))
            library.ResolveClosure(source, errors, cancellationToken);
        library.ResolveStreamedSounds(errors, cancellationToken);
        // World extraction still loads only the original map fastfile. A model
        // recovered from another owner would lose its source collision data.
        foreach (Node node in library._closures[mapFF].Nodes.Where(node =>
                     node.Asset.Type == "xmodel" && !PathComparer.Equals(node.Source.Path, mapFF)))
        {
            errors.Add($"Resolved xmodel:{node.Asset.Name} from '{node.Source.Path}', but map-model collision " +
                "extraction from another fastfile is not supported. Conversion cannot preserve this model's collision.");
        }
        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                $"IW3 source dependency resolution failed ({errors.Count} issue(s)):\n" +
                string.Join("\n", errors.Select(error => $"- {error}")));
        }
        return library;
    }

    internal async Task ExtractAsync(
        string rootFF, string assetDirectory, string manifestPath, CancellationToken cancellationToken)
    {
        rootFF = ExistingPath(rootFF, directory: false);
        if (!_closures.TryGetValue(rootFF, out Closure? closure))
            throw new ArgumentException($"'{rootFF}' is not a resolved map or load fastfile.", nameof(rootFF));
        Node[] ps3Definitions = OrderedNodes(closure.Nodes.Where(node => node.Source.IsPs3)).ToArray();
        if (ps3Definitions.Length != 0)
        {
            throw new NotSupportedException(
                "The source graph resolves COD4 PS3 definitions, but exporting those definitions into " +
                "the IW4 conversion path is not supported yet:\n" +
                string.Join("\n", ps3Definitions.Select(node =>
                    $"- {node.Asset.Type}:{node.Asset.Name} from '{node.Source.Path}'")));
        }
        assetDirectory = ExistingPath(assetDirectory, directory: true);
        if (Directory.EnumerateFileSystemEntries(assetDirectory).Any())
            throw new IOException($"Source extraction directory must be empty: '{assetDirectory}'.");

        foreach (IGrouping<Source, Node> group in closure.Nodes
                     .GroupBy(node => node.Source).OrderBy(group => group.Key.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Source source = group.Key;
            await VerifyHashAsync(source.Path, source.Sha256, cancellationToken).ConfigureAwait(false);
            string exportName = $"source-export-{_exportSequence++:D6}";
            string selectionPath = Path.Combine(_scratchDirectory, exportName + ".json");
            string exportDirectory = Path.Combine(_scratchDirectory, exportName);
            RejectExisting(selectionPath);
            RejectExisting(exportDirectory);
            await File.WriteAllBytesAsync(selectionPath,
                JsonSerializer.SerializeToUtf8Bytes(group.Select(node => node.Asset.Id).Order().ToArray()),
                cancellationToken).ConfigureAwait(false);
            await Iw3PcExtractionBackend.RunToolAsync(
                $"IW3 selected source extraction '{source.Path}'", _nativePath, _scratchDirectory,
                ["--extract-assets", source.Path, selectionPath, exportDirectory], cancellationToken).ConfigureAwait(false);
            await VerifyHashAsync(source.Path, source.Sha256, cancellationToken).ConfigureAwait(false);
            if (!Directory.Exists(exportDirectory))
                throw new InvalidDataException($"Source extraction did not create '{exportDirectory}'.");

            foreach (string file in EnumerateExportFiles(exportDirectory).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(exportDirectory, file);
                string destination = Path.GetFullPath(Path.Combine(assetDirectory, relativePath));
                string destinationPrefix = Path.TrimEndingDirectorySeparator(assetDirectory) + Path.DirectorySeparatorChar;
                if (!destination.StartsWith(destinationPrefix,
                        PathComparer == StringComparer.OrdinalIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Extracted source file escapes its destination: '{relativePath}'.");
                }
                ExportedFile? previous = _exportedFiles.FirstOrDefault(value =>
                    PathComparer.Equals(value.RootFastFile, rootFF) && PathComparer.Equals(value.RelativePath, relativePath));
                if (File.Exists(destination))
                {
                    if (!FilesEqual(file, destination))
                    {
                        throw new InvalidDataException(
                            $"Conflicting extracted file '{relativePath}' from '{source.Path}' and " +
                            $"'{string.Join("', '", previous?.Sources.Select(owner => owner.Path) ?? [])}'.");
                    }
                }
                else
                {
                    string directory = Path.GetDirectoryName(destination)
                        ?? throw new InvalidDataException($"Invalid source output path '{destination}'.");
                    Directory.CreateDirectory(directory);
                    File.Copy(file, destination);
                }
                if (previous is null)
                {
                    previous = new ExportedFile(rootFF, relativePath,
                        await HashFileAsync(destination, cancellationToken).ConfigureAwait(false));
                    _exportedFiles.Add(previous);
                }
                previous.Sources.Add(source);
            }
        }

        RejectExisting(manifestPath);
        Iw3ZoneManifest.Write(manifestPath, OrderedNodes(closure.Nodes)
            .DistinctBy(node => (node.Asset.Type, node.Asset.Key))
            .Select(node => new Iw3ZoneManifestEntry(node.Asset.Type, node.Asset.Name, IsReference: false)));
    }

    internal void RecordImageSources(string imageName, IReadOnlyList<string> sources)
    {
        if (_imageSources.TryGetValue(imageName, out IReadOnlyList<string>? previous))
            sources = previous.Concat(sources).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        _imageSources[imageName] = sources.ToArray();
    }

    internal byte[] SerializeProvenance() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        SourceLibrary = _libraryDirectory,
        CatalogBoundary = "Loaded named IW3 assets and their recorded dependencies; not the serialized XAsset root table. " +
            "Resolved source availability does not imply successful IW4 conversion. Streamed sounds are resolved but not converted.",
        FastFiles = _sources.Select(source => new
        {
            source.Path, source.Sha256, source.Role,
            LoadedAssetCount = source.Catalog.Assets.Length,
            source.Catalog.SkippedAssetTypes,
        }),
        IwdArchives = IwdPaths,
        Roots = _closures.Values.OrderBy(closure => closure.Root.Path, StringComparer.Ordinal).Select(closure => new
        {
            FastFile = closure.Root.Path,
            Assets = OrderedNodes(closure.Nodes).Select(node => new
            {
                node.Asset.Type, node.Asset.Name, node.Asset.Key,
                SourceFastFile = node.Source.Path, SourceSha256 = node.Source.Sha256,
                RecordId = node.Asset.Id,
                ExportSupported = CanExport(node),
                EquivalentProviders = _providers[(node.Asset.Type, node.Asset.Key)]
                    .Where(provider => SameDefinition(provider, node)).Select(provider => provider.Source.Path)
                    .Distinct(PathComparer).Order(StringComparer.Ordinal),
            }),
            Dependencies = closure.Edges.OrderBy(edge => edge.From.Asset.Type, StringComparer.Ordinal)
                .ThenBy(edge => edge.From.Asset.Key, StringComparer.Ordinal)
                .ThenBy(edge => edge.From.Source.Path, StringComparer.Ordinal)
                .ThenBy(edge => edge.To.Asset.Type, StringComparer.Ordinal)
                .ThenBy(edge => edge.To.Asset.Key, StringComparer.Ordinal)
                .ThenBy(edge => edge.To.Source.Path, StringComparer.Ordinal)
                .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
                .Select(edge => new
                {
                    From = new { FastFile = edge.From.Source.Path, RecordId = edge.From.Asset.Id },
                    To = new { FastFile = edge.To.Source.Path, RecordId = edge.To.Asset.Id },
                    edge.Kind,
                }),
            AvailableButNotExportedAssetTypes = closure.Nodes.Where(node => !CanExport(node))
                .Select(node => node.Asset.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
        }),
        ExtractedFiles = _exportedFiles.OrderBy(file => file.RootFastFile, StringComparer.Ordinal)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal).Select(file => new
            {
                file.RootFastFile, Path = file.RelativePath.Replace(Path.DirectorySeparatorChar, '/'), file.Sha256,
                Sources = file.Sources.OrderBy(source => source.Path, StringComparer.Ordinal)
                    .Select(source => new { source.Path, source.Sha256 }),
            }),
        ImagePayloads = _imageSources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new { Name = pair.Key, Sources = pair.Value }),
        StreamedSoundPayloads = _soundSources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new
            {
                Path = pair.Key, Sources = pair.Value,
                Requesters = _closures.Values.SelectMany(closure => closure.Nodes)
                    .Where(node => node.Asset.StreamedSounds.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    .Distinct().Select(node => new
                    {
                        node.Asset.Type, node.Asset.Name, SourceFastFile = node.Source.Path, RecordId = node.Asset.Id,
                    }),
            }),
    }, new JsonSerializerOptions { WriteIndented = true });

    private void ResolveClosure(Source root, SortedSet<string> errors, CancellationToken cancellationToken)
    {
        var closure = new Closure(root);
        _closures.Add(root.Path, closure);
        var pending = new Queue<(Node Node, string Chain)>();
        foreach (CatalogAsset asset in root.Catalog.Assets.Where(asset => !asset.IsReference))
            pending.Enqueue((new Node(root, asset), $"'{root.Path}' -> {asset.Type}:{asset.Name}"));
        // A reference may be a root entry without another named asset depending on it.
        foreach (CatalogAsset asset in root.Catalog.Assets.Where(asset => asset.IsReference))
        {
            string chain = $"'{root.Path}' -> {asset.Type}:{asset.Name} (reference)";
            Node? provider = ResolveNamed(root, asset.Type, asset.Key, chain, errors);
            if (provider is not null)
                pending.Enqueue((provider, chain + $" -> '{provider.Source.Path}'"));
        }

        while (pending.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Node node = item.Node;
            if (!closure.Nodes.Add(node))
                continue;
            var key = (node.Asset.Type, node.Asset.Key);
            if (_selectedDefinitions.TryGetValue(key, out Node? previous) && !SameDefinition(previous, node))
            {
                errors.Add($"Conflicting concrete definitions for {key.Type}:{key.Key}: " +
                    $"'{previous.Source.Path}' record {previous.Asset.Id} and '{node.Source.Path}' record {node.Asset.Id}; " +
                    $"requested by {item.Chain}.");
            }
            else
            {
                _selectedDefinitions.TryAdd(key, node);
            }

            foreach (int id in node.Asset.Dependencies)
            {
                CatalogAsset dependency = node.Source.Records[id];
                string chain = item.Chain + $" -> {dependency.Type}:{dependency.Name}";
                Node? target = dependency.IsReference
                    ? ResolveNamed(node.Source, dependency.Type, dependency.Key, chain, errors)
                    : new Node(node.Source, dependency);
                AddDependency(target, dependency.IsReference ? "reference-record" : "record", chain);
            }
            foreach (CatalogReference reference in node.Asset.References)
            {
                string chain = item.Chain + $" -> {reference.Type}:{reference.Name}";
                AddDependency(ResolveNamed(node.Source, reference.Type, reference.Key, chain, errors), "named-reference", chain);
            }

            void AddDependency(Node? target, string kind, string chain)
            {
                if (target is null)
                    return;
                closure.Edges.Add(new Dependency(node, target, kind));
                pending.Enqueue((target, chain + $" ['{target.Source.Path}']"));
            }
        }
    }

    private Node? ResolveNamed(Source requester, string type, string key, string chain, SortedSet<string> errors)
    {
        if (!_providers.TryGetValue((type, key), out List<Node>? candidates))
        {
            errors.Add($"Missing {type}:{key}; requested by {chain}.");
            return null;
        }
        Node[] providers = candidates.Where(node => node.Source == requester).ToArray();
        if (providers.Length == 0)
            providers = candidates.Where(node => node.Source.Role != "library").ToArray();
        if (providers.Length == 0)
            providers = candidates.ToArray();
        Node first = providers[0];
        if (providers.Any(node => !SameDefinition(first, node)))
        {
            errors.Add($"Ambiguous {type}:{key}; requested by {chain}; providers: " +
                string.Join(", ", providers.Select(node => $"'{node.Source.Path}' record {node.Asset.Id}")) + ".");
            return null;
        }
        return first;
    }

    private void ResolveStreamedSounds(SortedSet<string> errors, CancellationToken cancellationToken)
    {
        Node[] nodes = _closures.Values.SelectMany(closure => closure.Nodes).Distinct().ToArray();
        string[] requests = nodes.SelectMany(node => node.Asset.StreamedSounds)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (requests.Length == 0)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        if (_customIwd is not null)
        {
            foreach (var pair in Iw3IwdImageCompiler.ResolveFiles([_customIwd], requests))
                _soundSources.Add(pair.Key, pair.Value);
        }
        string[] remaining = requests.Where(path => !_soundSources.ContainsKey(path)).ToArray();
        if (remaining.Length != 0)
        {
            string[] stockIwds = IwdPaths.Where(path => !PathComparer.Equals(path, _customIwd)).ToArray();
            foreach (var pair in Iw3IwdImageCompiler.ResolveFiles(stockIwds, remaining))
                _soundSources.Add(pair.Key, pair.Value);
        }
        foreach (string missing in requests.Where(path => !_soundSources.ContainsKey(path)))
        {
            string requesters = string.Join(", ", nodes
                .Where(node => node.Asset.StreamedSounds.Contains(missing, StringComparer.OrdinalIgnoreCase))
                .Select(node => $"{node.Asset.Type}:{node.Asset.Name} in '{node.Source.Path}'"));
            errors.Add($"Missing streamed sound '{missing}'; requested by {requesters}.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool SameDefinition(Node left, Node right) =>
        left.Asset.Id == right.Asset.Id && left.Source.Sha256 == right.Source.Sha256;

    private static IOrderedEnumerable<Node> OrderedNodes(IEnumerable<Node> nodes) => nodes
        .OrderBy(node => node.Asset.Type, StringComparer.Ordinal)
        .ThenBy(node => node.Asset.Key, StringComparer.Ordinal)
        .ThenBy(node => node.Source.Path, StringComparer.Ordinal)
        .ThenBy(node => node.Asset.Id);

    private static bool CanExport(Node node) =>
        !node.Source.IsPs3 && ExportedAssetTypes.Contains(node.Asset.Type);

    private static void ValidateCatalog(Catalog catalog)
    {
        if (catalog.Assets is null || catalog.Assets.Length == 0 || catalog.SkippedAssetTypes is null)
            throw new InvalidDataException("The catalog must contain named assets and a skipped-type list.");
        var ids = new HashSet<int>();
        foreach (CatalogAsset asset in catalog.Assets)
        {
            if (asset is null || asset.Id < 0 || asset.Id >= catalog.Assets.Length || !ids.Add(asset.Id) ||
                !ValidIdentity(asset.Type, asset.Name, asset.Key) ||
                asset.Dependencies is null || asset.References is null || asset.StreamedSounds is null)
                throw new InvalidDataException("The catalog contains an invalid or duplicate asset record.");
            if (asset.References.Any(reference => reference is null || !ValidIdentity(reference.Type, reference.Name, reference.Key)) ||
                asset.StreamedSounds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"The catalog contains invalid references for record {asset.Id}.");
        }
        foreach (CatalogAsset asset in catalog.Assets)
        {
            if (asset.Dependencies.Any(id => !ids.Contains(id)))
                throw new InvalidDataException($"Catalog record {asset.Id} depends on an absent record.");
        }
        if (catalog.SkippedAssetTypes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The catalog contains an invalid skipped asset type.");
    }

    private static bool ValidIdentity(string type, string name, string key) =>
        Iw3ZoneManifest.IsValidEntry(type, name) && Iw3ZoneManifest.IsValidEntry(type, key);

    private static IEnumerable<string> EnumerateLibraryFiles(string root, CancellationToken cancellationToken)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(PathComparer);
        var seenDirectories = new HashSet<string>(PathComparer);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            directory = ExistingPath(directory, directory: true);
            if (!seenDirectories.Add(directory))
                continue;
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (Directory.Exists(entry))
                    pending.Push(entry);
                else if (entry.EndsWith(".ff", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".iwd", StringComparison.OrdinalIgnoreCase))
                {
                    string path = ExistingPath(entry, directory: false);
                    if (seenFiles.Add(path))
                        paths.Add(path);
                }
            }
        }
        return paths;
    }

    private static string ExistingPath(string path, bool directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!(directory ? Directory.Exists(fullPath) : File.Exists(fullPath)))
            throw new FileNotFoundException($"Source {(directory ? "directory" : "file")} does not exist: '{fullPath}'.", fullPath);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException($"Source path is not rooted: '{path}'.", nameof(path));
        string resolved = root;
        foreach (string component in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, component);
            FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            if (info.LinkTarget is not null)
            {
                resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException($"Unable to resolve source link '{resolved}'.");
            }
        }
        return resolved;
    }

    private static IEnumerable<string> EnumerateExportFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Source export contains a directory link: '{directory}'.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Source export contains a link: '{entry}'.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        if (left.Length != right.Length)
            return false;
        byte[] leftBuffer = new byte[65536];
        byte[] rightBuffer = new byte[leftBuffer.Length];
        while (left.Position < left.Length)
        {
            int count = (int)Math.Min(leftBuffer.Length, left.Length - left.Position);
            left.ReadExactly(leftBuffer.AsSpan(0, count));
            right.ReadExactly(rightBuffer.AsSpan(0, count));
            if (!leftBuffer.AsSpan(0, count).SequenceEqual(rightBuffer.AsSpan(0, count)))
                return false;
        }
        return true;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static async Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (await HashFileAsync(path, cancellationToken).ConfigureAwait(false) != expected)
            throw new InvalidDataException($"Source fastfile changed after it was cataloged: '{path}'.");
    }

    private static void RejectExisting(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException($"Source-library scratch output already exists: '{path}'.");
    }

    // Shared with the PS3 reader; these are catalog records, not engine asset models.
    internal sealed class Catalog
    {
        public required CatalogAsset[] Assets { get; init; }
        public required string[] SkippedAssetTypes { get; init; }
    }

    internal sealed class CatalogAsset
    {
        public required int Id { get; init; }
        public required string Type { get; init; }
        public required string Name { get; init; }
        public required string Key { get; init; }
        public required bool IsReference { get; init; }
        public required int[] Dependencies { get; init; }
        public required CatalogReference[] References { get; init; }
        public required string[] StreamedSounds { get; init; }
    }

    internal sealed class CatalogReference
    {
        public required string Type { get; init; }
        public required string Name { get; init; }
        public required string Key { get; init; }
    }

    private sealed class Source(string path, string sha256, string role, Catalog catalog, bool isPs3)
    {
        internal string Path { get; } = path;
        internal string Sha256 { get; } = sha256;
        internal string Role { get; } = role;
        internal Catalog Catalog { get; } = catalog;
        internal bool IsPs3 { get; } = isPs3;
        internal Dictionary<int, CatalogAsset> Records { get; } = catalog.Assets.ToDictionary(asset => asset.Id);
    }

    private sealed record Node(Source Source, CatalogAsset Asset);
    private sealed record Dependency(Node From, Node To, string Kind);

    private sealed class Closure(Source root)
    {
        internal Source Root { get; } = root;
        internal HashSet<Node> Nodes { get; } = [];
        internal HashSet<Dependency> Edges { get; } = [];
    }

    private sealed class ExportedFile(string rootFastFile, string relativePath, string sha256)
    {
        internal string RootFastFile { get; } = rootFastFile;
        internal string RelativePath { get; } = relativePath;
        internal string Sha256 { get; } = sha256;
        internal HashSet<Source> Sources { get; } = [];
    }
}
