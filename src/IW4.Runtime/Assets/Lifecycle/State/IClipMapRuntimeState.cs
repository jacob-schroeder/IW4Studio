namespace IW4.Runtime.Assets.Lifecycle.State;

public interface IClipMapRuntimeState : IXAssetRuntimeStateService
{
    ReadOnlyMemory<byte> Bytes { get; }

    void Replace(ReadOnlySpan<byte> bytes);

    void ResetPreservingIdentity();
}
