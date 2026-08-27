using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Database.Planning;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;
using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Sound;

namespace IW4.Studio.Documents;

/// <summary>
/// An imported or blank semantic fastfile workspace. At most one editing
/// session may own it. Imported workspaces retain their loaded runtime view
/// only for current workbench consumers; the editing state is linker-owned.
/// </summary>
public sealed class FastFileWorkspace : IDisposable
{
    private readonly DbLoadSession? _loadSession;
    private FastFileEditingSession? _editingSessionOwner;
    private bool _disposed;

    internal FastFileWorkspace(
        FastFileDocument document,
        DbLoadSession? loadSession = null,
        IReadOnlyList<WorkspaceZone>? loadedZones = null,
        string? zonePlanProfileName = null,
        FastFileDependencyGraph? dependencyGraph = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsBlank != (loadSession is null))
        {
            throw new ArgumentException(
                "Only imported workspaces can retain a DB load session.",
                nameof(loadSession));
        }

        Document = document;
        _loadSession = loadSession;
        LoadedZones = Array.AsReadOnly((loadedZones ?? (document.IsBlank
            ? []
            : [new WorkspaceZone(document.LoadedZone, document.SourcePath, true, true)])).ToArray());
        ActiveZones = Array.AsReadOnly(LoadedZones.Where(zone => zone.IsActive).ToArray());
        ZonePlanProfileName = zonePlanProfileName;
        DependencyGraph = dependencyGraph ?? (document.IsBlank
            ? null
            : new FastFileDependencyGraph([new FastFileDependencyNode(
                document.SourcePath, DbDependencyRequestLoadStatus.Loaded, true)]));
        AssetCatalog = WorkspaceAssetCatalog.Create(document, LoadedZones);
    }

    public FastFileDocument Document { get; }
    public WorkspaceAssetCatalog AssetCatalog { get; }
    public bool IsBlank => Document.IsBlank;
    public string SourcePath => Document.SourcePath;
    public LoadedXZone LoadedZone => Document.LoadedZone;
    public ZoneObjectFile ZoneObjectFile => Document.ZoneObjectFile;
    public ZoneLinkRequest InitialLinkRequest => Document.InitialLinkRequest;
    public IReadOnlyList<WorkspaceZone> LoadedZones { get; }
    public IReadOnlyList<WorkspaceZone> ActiveZones { get; }
    public string? ZonePlanProfileName { get; }
    public FastFileDependencyGraph? DependencyGraph { get; }

    public bool TryGetSoundPayloadResolver(
        AssetEditorSurface surface,
        out ISoundPayloadResolver resolver,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(surface);
        WorkspaceAssetCatalogEntry entry = surface.Entry;

        if (surface.Definition is null)
        {
            resolver = UnavailableSoundPayloadResolver.Instance;
            reason = "The selected Sound has no materialized definition.";
            return false;
        }

        IEnumerable<XAssetProviderContribution> providers = LoadedZones.Count == 0
            ? []
            : LoadedZones[0].LoadResult.Context.AssetPool.Slots
                .SelectMany(slot => slot.Providers)
                .Where(provider =>
                    provider.AssetType == entry.AssetType &&
                    !provider.IsReferencePlaceholder);
        XAssetProviderContribution? owningProvider =
            providers.FirstOrDefault(provider =>
                ReferenceEquals(provider.Asset, surface.Definition)) ??
            (entry.ResolvedProvider is { } resolved
                ? providers.FirstOrDefault(provider =>
                    provider.Id.Value == resolved.ProviderId)
                : null);
        if (owningProvider is not null)
        {
            WorkspaceZone? zone = LoadedZones.FirstOrDefault(candidate =>
                candidate.LoadResult.Context.ZoneOwner == owningProvider.Owner);
            if (zone is not null)
            {
                resolver = zone.LoadResult.SoundPayloadResolver;
                reason = string.Empty;
                return true;
            }
        }

        resolver = UnavailableSoundPayloadResolver.Instance;
        reason = $"The provider zone for Sound '{entry.OriginalName ?? entry.NormalizedName ?? "<unnamed>"}' is not available.";
        return false;
    }

    internal void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastFileWorkspace));
    }

    internal void ClaimEditingSession(FastFileEditingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        if (_editingSessionOwner is not null)
        {
            throw new InvalidOperationException(
                "A fastfile workspace can be owned by only one editing session.");
        }

        _editingSessionOwner = session;
    }

    internal void DisposeEditingSession(FastFileEditingSession session)
    {
        if (!ReferenceEquals(_editingSessionOwner, session))
            throw new InvalidOperationException("The editing session does not own this workspace.");

        try
        {
            DisposeCore();
        }
        finally
        {
            _editingSessionOwner = null;
        }
    }

    public void Dispose()
    {
        if (_editingSessionOwner is not null)
        {
            throw new InvalidOperationException(
                "The workspace is owned by an editing session and must be disposed through that session.");
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        if (_disposed)
            return;

        _loadSession?.Dispose();
        _disposed = true;
    }
}
