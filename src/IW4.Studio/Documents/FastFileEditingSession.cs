using IW4.FastFiles.Database;
using IW4.Linker.Model;

namespace IW4.Studio.Documents;

/// <summary>
/// Exclusively owns one workspace and its immutable Save As revision. The
/// initial linker boundary intentionally has no semantic mutation surface;
/// disposing the session also releases the loaded workspace.
/// </summary>
public sealed class FastFileEditingSession : IDisposable
{
    private readonly FastFileSaveRevision _revision;
    private bool _disposed;

    public FastFileEditingSession(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.ThrowIfDisposed();
        Workspace = workspace;
        _revision = new FastFileSaveRevision(
            workspace.SourcePath,
            workspace.LoadedZone.Header,
            workspace.ZoneObjectFile);
        workspace.ClaimEditingSession(this);
    }

    public FastFileWorkspace Workspace { get; }

    /// <summary>The only captured revision; it never changes in this no-op phase.</summary>
    public long Revision => 0;

    internal FastFileSaveRevision CaptureRevision()
    {
        ThrowIfDisposed();
        Workspace.ThrowIfDisposed();
        return _revision;
    }

    internal void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastFileEditingSession));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Workspace.DisposeEditingSession(this);
        _disposed = true;
    }
}

/// <summary>One immutable no-op save input captured when the session begins.</summary>
internal sealed record FastFileSaveRevision(
    string SourcePath,
    DbHeader Header,
    ZoneObjectFile ZoneObjectFile);
