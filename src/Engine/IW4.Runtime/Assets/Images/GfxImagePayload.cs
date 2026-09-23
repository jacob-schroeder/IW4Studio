namespace IW4.Runtime.Assets.Images;

/// <summary>
/// Backend-neutral bytes for one resolved image level. The payload retains the
/// resolver-owned storage so scene construction does not duplicate large
/// streamed texture buffers.
/// </summary>
public readonly record struct GfxImagePayload(
    int Width,
    int Height,
    IReadOnlyList<byte> Payload);
