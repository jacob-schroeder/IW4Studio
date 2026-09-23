namespace IW4.Runtime.Assets.Lifecycle.State;

public sealed class ComWorldRuntimeState : IComWorldRuntimeState
{
    public ComWorldRuntimeState(ComWorldRuntimeRecord state)
    {
        State = state;
    }

    public ComWorldRuntimeRecord State { get; private set; }

    public void Set(ComWorldRuntimeRecord state) => State = state;

    public IXAssetRuntimeStateSnapshot CaptureSnapshot() =>
        new ComWorldRuntimeSnapshot(State);

    public void RestoreSnapshot(IXAssetRuntimeStateSnapshot snapshot)
    {
        if (snapshot is not ComWorldRuntimeSnapshot typed)
            throw new ArgumentException("Snapshot does not belong to ComWorld runtime state.", nameof(snapshot));

        State = typed.State;
    }
}
