namespace IW4.FastFiles.Streaming.Images;

public readonly record struct GfxImageStreamMipPayload(
    int Width,
    int Height,
    byte[] Payload);
