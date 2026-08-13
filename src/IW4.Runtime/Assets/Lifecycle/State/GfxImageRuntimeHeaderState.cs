using IW4.Assets.Assets.Image;

namespace IW4.Runtime.Assets.Lifecycle.State;

/// <summary>
/// Mutable GfxImage root state consumed by image lifecycle operations.
/// </summary>
public readonly record struct GfxImageRuntimeHeaderState(
    uint CardMemory,
    ushort BaseWidth,
    ushort BaseHeight,
    ushort BaseDepth,
    byte BaseLevelCount,
    GfxImageCached Cached,
    uint Pixels);
