using System.Numerics;
using IW4.Formats.SourceFormat.Material;
using IW4.Game.Assets.Material;

namespace Iw4Radiant.Materials;

internal sealed record MaterialSource(
    string Name,
    string ImagePath,
    bool IsSky,
    MaterialSamplerState SamplerState)
{
    internal bool PreviewDefinitionAvailable { get; init; } = true;
    internal string TechniqueSet { get; init; } = "";
    internal bool UsesVertexColor => TechniqueSet.StartsWith("wc_", StringComparison.Ordinal);
    internal MaterialWater? Water { get; init; }
    internal OceanWaveSettings? Ocean { get; init; }
    internal string OceanFoamImagePath { get; init; } = "";
    internal bool IsWater => Water is not null;
    internal Vector4 WaterColor { get; init; }
    internal Vector4 EnvMapParms { get; init; }
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

    // Direct mp_highrise.ff readback: both w/_default_water and
    // wc/armada_water use DETAIL|WATER and the same native surface flags.
    internal int GetCollisionContents() => IsWater ? 0x08000020 : 1;

    internal int GetCollisionSurfaceFlags() => IsWater ? 0x01460020 : GetSurfaceTypeFlags();
}
