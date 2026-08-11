using IW4.FastFiles.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;

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
        DbLoadSession? loadSession = null)
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
    }

    public FastFileDocument Document { get; }
    public bool IsBlank => Document.IsBlank;
    public string SourcePath => Document.SourcePath;
    public LoadedXZone LoadedZone => Document.LoadedZone;
    public ZoneObjectFile ZoneObjectFile => Document.ZoneObjectFile;
    public ZoneLinkRequest InitialLinkRequest => Document.InitialLinkRequest;

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

        _loadSession?.Dispose();
        _disposed = true;
    }
}
