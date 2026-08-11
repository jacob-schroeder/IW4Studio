using IW4.FastFiles.Loaders.Database;
using IW4.Linker.Model;

namespace IW4.Studio.Documents;

/// <summary>
/// The single-zone immutable source-layout replay workspace returned by an
/// isolated open. At most one editing session may take ownership of its loaded
/// runtime state.
/// </summary>
public sealed class FastFileWorkspace : IDisposable
{
    private readonly DbLoadSession _loadSession;
    private FastFileEditingSession? _editingSessionOwner;
    private bool _disposed;

    internal FastFileWorkspace(
        FastFileDocument document,
        DbLoadSession loadSession)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(loadSession);

        Document = document;
        _loadSession = loadSession;
    }

    public FastFileDocument Document { get; }
    public string SourcePath => Document.SourcePath;
    public LoadedXZone LoadedZone => Document.LoadedZone;
    public ZoneObjectFile ZoneObjectFile => Document.ZoneObjectFile;

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

        DisposeCore();
        _editingSessionOwner = null;
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

        _loadSession.Runtime.DB_FreeXZones(IW4.FastFiles.Zone.XZoneFlags.DB_ZONE_DEV);
        _disposed = true;
    }
}
