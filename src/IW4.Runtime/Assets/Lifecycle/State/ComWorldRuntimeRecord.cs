namespace IW4.Runtime.Assets.Lifecycle.State;

/// <summary>
/// Exact 0x10-byte ComWorld root projection. The release callback mutates only
/// IsInUse at +0x04.
/// </summary>
public readonly record struct ComWorldRuntimeRecord(
    uint NameIdentityWord,
    int IsInUse,
    int PrimaryLightCount,
    uint PrimaryLightsWord);
