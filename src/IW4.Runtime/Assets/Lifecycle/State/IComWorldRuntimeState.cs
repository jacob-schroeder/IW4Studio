namespace IW4.Runtime.Assets.Lifecycle.State;

public interface IComWorldRuntimeState : IXAssetRuntimeStateService
{
    ComWorldRuntimeRecord State { get; }

    void Set(ComWorldRuntimeRecord state);
}
