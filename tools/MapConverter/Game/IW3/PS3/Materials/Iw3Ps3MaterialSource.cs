using IW4.Game.Assets.Image;
using MapConverter.Game.IW3.PS3.Techniques;

namespace MapConverter.Game.IW3.PS3.Materials;

internal sealed record Iw3Ps3MaterialSource(
    string Name,
    ReadOnlyMemory<byte> Root,
    Iw3Ps3TechniqueSetSource? TechniqueSet,
    IReadOnlyList<Iw3Ps3TextureSource> Textures,
    IReadOnlyList<ReadOnlyMemory<byte>> Constants,
    IReadOnlyList<ReadOnlyMemory<byte>> States);

internal sealed record Iw3Ps3TextureSource(
    ReadOnlyMemory<byte> Root,
    GfxImageAsset? Image,
    string? ImageName,
    Iw3Ps3WaterSource? Water = null);

internal sealed record Iw3Ps3WaterSource(
    ReadOnlyMemory<byte> Root,
    ReadOnlyMemory<byte> H0X,
    ReadOnlyMemory<byte> H0Y,
    ReadOnlyMemory<byte> WTerm,
    GfxImageAsset? Image,
    string? ImageName);
