using System.Numerics;
using IW4.Assets.Assets.Material;

namespace Iw4Radiant.Materials;

internal sealed record MaterialSource(
    string Name,
    string ImagePath,
    bool IsSky,
    MaterialSamplerState SamplerState)
{
    internal string TechniqueSet { get; init; } = "";
    internal bool UsesVertexColor => TechniqueSet.StartsWith("wc_", StringComparison.Ordinal);
    internal MaterialSurfaceState Surface { get; init; } = MaterialSurfaceState.Opaque;
    internal MaterialGameFlags GameFlags { get; init; }
    internal MaterialSurfaceTypeBits SurfaceTypeBits { get; init; }

    internal int GetSurfaceTypeFlags()
    {
        uint bits = (uint)SurfaceTypeBits;
        if (bits == 0) return 0;
        int type = BitOperations.TrailingZeroCount(bits) + 1;
        if (!BitOperations.IsPow2(bits) || type >= (int)MaterialSurfaceType.Count)
            throw new NotSupportedException($"Material '{Name}' has no single supported collision surface type.");
        return type << 20;
    }
}
