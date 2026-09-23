namespace IW4.Streaming.Images;

public readonly record struct GfxImageStreamMipPayload(
    int Width,
    int Height,
    byte[] Payload);
