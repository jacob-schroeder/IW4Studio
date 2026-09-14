using IW4.Assets.Assets.Material;

namespace Iw4Radiant.Materials;

internal sealed record MaterialSource(
    string Name,
    string ImagePath,
    bool IsSky,
    MaterialSamplerState SamplerState)
{
    internal MaterialSurfaceState Surface { get; init; } = MaterialSurfaceState.Opaque;
}
