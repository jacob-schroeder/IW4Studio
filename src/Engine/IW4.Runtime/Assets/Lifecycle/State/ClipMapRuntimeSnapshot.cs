namespace IW4.Runtime.Assets.Lifecycle.State;

internal sealed record ClipMapRuntimeSnapshot(byte[] Bytes)
    : IXAssetRuntimeStateSnapshot;
