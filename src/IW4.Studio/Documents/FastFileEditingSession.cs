using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>
/// Exclusively owns one workspace and an immutable, revisioned semantic link
/// state. Mutable schema definitions are accepted only as transient provider
/// sources and are frozen before a revision is published.
/// </summary>
public sealed class FastFileEditingSession : IDisposable
{
    private readonly object _gate = new();
    private readonly LinkAssetPool _baseAssets;
    private LinkAssetPool _authoredAssets;
    private FastFileSaveRevision _revision;
    private bool _disposed;

    public FastFileEditingSession(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.ThrowIfDisposed();
        Workspace = workspace;
        _baseAssets = workspace.InitialLinkRequest.Assets;
        _authoredAssets = _baseAssets.WithoutProviders(
            _baseAssets.Providers.Select(provider => provider.Key));
        _revision = new FastFileSaveRevision(
            Revision: 0,
            SourcePath: workspace.Document.SourcePathOrNull,
            LinkRequest: workspace.InitialLinkRequest);
        workspace.ClaimEditingSession(this);
    }

    public FastFileWorkspace Workspace { get; }

    public long Revision
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedCore();
                return _revision.Revision;
            }
        }
    }

    /// <summary>The current immutable request snapshot.</summary>
    public ZoneLinkRequest LinkRequest
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedCore();
                return _revision.LinkRequest;
            }
        }
    }

    /// <summary>
    /// Freezes one authored provider batch, replaces every older occurrence of
    /// those logical keys, and gives the batch highest precedence.
    /// </summary>
    public void AddOrReplaceProviders(
        IEnumerable<LinkAssetProviderSource> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        LinkAssetProviderSource[] sources = providers
            .Select(source => source ?? throw new ArgumentException(
                "Provider sources cannot contain null.",
                nameof(providers)))
            .Select(source => source.AsAuthoredDetached())
            .ToArray();
        if (sources.Length == 0)
            throw new ArgumentException("At least one provider source is required.", nameof(providers));

        AssetKey[] replacedKeys = sources
            .Select(source => AssetKey.FromDefinition(source.Definition))
            .Distinct()
            .ToArray();

        lock (_gate)
        {
            ThrowIfDisposedCore();
            LinkAssetPool authoredAssets = _authoredAssets
                .WithoutProviders(replacedKeys)
                .WithHighestPrecedenceProviders(sources);
            Publish(authoredAssets, _revision.LinkRequest.Roots);
        }
    }

    /// <summary>Replaces the complete selected-root occurrence order.</summary>
    public void SetOrderedRoots(IEnumerable<LinkRoot> roots)
    {
        LinkRoot[] copied = CopyRoots(roots);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            Publish(_authoredAssets, copied);
        }
    }

    /// <summary>Removes selected root occurrences by their stable entry IDs.</summary>
    public void RemoveRoots(IEnumerable<string> entryIds)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var removedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? entryId in entryIds)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                throw new ArgumentException(
                    "Removed root entry IDs cannot be null or empty.",
                    nameof(entryIds));
            }

            removedIds.Add(entryId);
        }
        if (removedIds.Count == 0)
            throw new ArgumentException("At least one root entry ID is required.", nameof(entryIds));

        lock (_gate)
        {
            ThrowIfDisposedCore();
            LinkRoot[] roots = _revision.LinkRequest.Roots
                .Where(root => !removedIds.Contains(root.EntryId))
                .ToArray();
            if (roots.Length == _revision.LinkRequest.Roots.Count)
                throw new KeyNotFoundException("None of the requested root entry IDs exist.");
            Publish(_authoredAssets, roots);
        }
    }

    /// <summary>
    /// Deletes authored provider overrides and selected roots for the supplied
    /// logical assets. Imported/base providers remain available as dependency
    /// fallback. References from surviving providers are not rewritten.
    /// </summary>
    public void DeleteAssets(IEnumerable<AssetKey> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        AssetKey[] removedKeys = assets.Distinct().ToArray();
        if (removedKeys.Length == 0)
            throw new ArgumentException("At least one logical asset key is required.", nameof(assets));

        lock (_gate)
        {
            ThrowIfDisposedCore();
            LinkAssetPool remainingAuthored =
                _authoredAssets.WithoutProviders(removedKeys);
            var removed = new HashSet<AssetKey>(removedKeys);
            bool providerExists = _authoredAssets.Providers
                .Any(provider => removed.Contains(provider.Key));
            bool rootExists = _revision.LinkRequest.Roots
                .Any(root => root.Asset is { } key && removed.Contains(key));
            if (!providerExists && !rootExists)
                throw new KeyNotFoundException("None of the requested logical assets exist.");

            LinkRoot[] roots = _revision.LinkRequest.Roots
                .Where(root => root.Asset is not { } key || !removed.Contains(key))
                .ToArray();
            Publish(remainingAuthored, roots);
        }
    }

    internal FastFileSaveRevision CaptureRevision()
    {
        lock (_gate)
        {
            ThrowIfDisposedCore();
            Workspace.ThrowIfDisposed();
            return _revision;
        }
    }

    internal bool ExecuteIfCurrentRevision(long revision, Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            ThrowIfDisposedCore();
            Workspace.ThrowIfDisposed();
            if (_revision.Revision != revision)
                return false;

            operation();
            return true;
        }
    }

    internal void ThrowIfDisposed()
    {
        lock (_gate)
            ThrowIfDisposedCore();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            Workspace.DisposeEditingSession(this);
            _disposed = true;
        }
    }

    private static LinkRoot[] CopyRoots(IEnumerable<LinkRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return roots
            .Select(root => root ?? throw new ArgumentException(
                "Link roots cannot contain null.",
                nameof(roots)))
            .ToArray();
    }

    private void Publish(
        LinkAssetPool authoredAssets,
        IEnumerable<LinkRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(authoredAssets);
        ZoneLinkRequest previous = _revision.LinkRequest;
        LinkAssetPool assets = _baseAssets.WithHighestPrecedencePool(authoredAssets);
        var request = new ZoneLinkRequest(
            assets,
            roots,
            previous.LanguageMask,
            previous.SelectedLanguageMask);
        var revision = new FastFileSaveRevision(
            checked(_revision.Revision + 1),
            _revision.SourcePath,
            request);
        _authoredAssets = authoredAssets;
        _revision = revision;
    }

    private void ThrowIfDisposedCore()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastFileEditingSession));
    }
}

/// <summary>One immutable canonical-link revision captured by Save As.</summary>
internal sealed record FastFileSaveRevision(
    long Revision,
    string? SourcePath,
    ZoneLinkRequest LinkRequest);
