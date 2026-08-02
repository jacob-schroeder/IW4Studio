namespace IW4.Render.OpenGl;

/// <summary>
/// Guards the context, render thread, and lifetime shared by context-owned GL
/// resource caches. Domain caches retain their own allocation validation and
/// cleanup policy.
/// </summary>
internal sealed class GlResourceCacheScope
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly string _ownerThreadViolationMessage;
    private bool _disposed;

    public GlResourceCacheScope(
        string contextIdentity,
        string ownerThreadViolationMessage)
    {
        ContextIdentity = contextIdentity;
        _ownerThreadViolationMessage = ownerThreadViolationMessage;
    }

    public string ContextIdentity { get; }

    public void EnsureUsable(object owner)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, owner);
    }

    public void EnsureContextIdentity(
        string currentContextIdentity,
        string changedIdentityMessage)
    {
        if (!string.Equals(
                currentContextIdentity,
                ContextIdentity,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(changedIdentityMessage);
        }
    }

    public bool BeginDispose()
    {
        EnsureOwnerThread();
        if (_disposed)
            return false;

        _disposed = true;
        return true;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(_ownerThreadViolationMessage);
    }
}
