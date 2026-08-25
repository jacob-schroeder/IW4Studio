namespace IW4.AssetExchange.SourceFormat.Image;

/// <summary>
/// One decoded RGBA8 mip level prepared for source DDS output.
/// Cubemap levels concatenate their six faces in DDS face order, while
/// three-dimensional levels concatenate all depth slices.
/// </summary>
public readonly record struct ImageSourceMipLevel(
    int Width,
    int Height,
    int Depth,
    ReadOnlyMemory<byte> RgbaBytes);
