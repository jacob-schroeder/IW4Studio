namespace IW4.Runtime.Assets.Lifecycle;

public interface IXAssetRuntimeStateService
{
    IXAssetRuntimeStateSnapshot CaptureSnapshot();

    void RestoreSnapshot(IXAssetRuntimeStateSnapshot snapshot);
}
