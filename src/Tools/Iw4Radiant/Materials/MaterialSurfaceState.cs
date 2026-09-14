using System.Text.Json;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace Iw4Radiant.Materials;

internal sealed record MaterialSurfaceState(GfxBlendOperation BlendOperation, GfxBlend Source, GfxBlend Destination,
    GfxAlphaTest? AlphaTest, bool DepthWrite, int SortKey)
{
    internal static MaterialSurfaceState Opaque { get; } = new(GfxBlendOperation.Disabled, GfxBlend.One, GfxBlend.Zero, null, true, 0);
    internal bool IsBlended => BlendOperation != GfxBlendOperation.Disabled;
    internal bool SupportsAlpha => BlendOperation == GfxBlendOperation.Add && Source == GfxBlend.SourceAlpha &&
        Destination == GfxBlend.InverseSourceAlpha && !DepthWrite;

    internal static MaterialSurfaceState Read(JsonElement material)
    {
        if (!material.TryGetProperty("stateBitsEntry", out var entries) || !material.TryGetProperty("stateBits", out var states))
            return Opaque;
        int slot = (int)MaterialTechniqueType.Unlit;
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() <= slot || states.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Material render-state arrays are invalid.");
        int index = entries[slot].GetInt32();
        if (index < 0) return Opaque;
        if (index >= states.GetArrayLength()) throw new InvalidDataException("Material render-state entry is out of range.");
        JsonElement state = states[index];
        string Field(string key) => state.GetProperty(key).GetString() ?? throw new InvalidDataException($"Missing material {key}.");
        GfxBlendOperation operation = Field("blendOpRgb") switch
        {
            "disabled" => GfxBlendOperation.Disabled, "add" => GfxBlendOperation.Add,
            "subtract" => GfxBlendOperation.Subtract, "revsubtract" => GfxBlendOperation.ReverseSubtract,
            "min" => GfxBlendOperation.Minimum, "max" => GfxBlendOperation.Maximum,
            var value => throw new InvalidDataException($"Unsupported material blend operation '{value}'.")
        };
        GfxAlphaTest? alphaTest = Field("alphaTest") switch
        {
            "disabled" => null, "gt0" => GfxAlphaTest.GreaterThanZero, "lt128" => GfxAlphaTest.LessThan128,
            "ge128" => GfxAlphaTest.GreaterThanOrEqualTo128,
            var value => throw new InvalidDataException($"Unsupported material alpha test '{value}'.")
        };
        return new(operation, Blend(Field("srcBlendRgb")), Blend(Field("dstBlendRgb")), alphaTest,
            state.GetProperty("depthWrite").GetBoolean(), material.TryGetProperty("sortKey", out var sort) ? sort.GetInt32() : 0);
    }

    private static GfxBlend Blend(string value) => value switch
    {
        "disabled" => GfxBlend.Disabled, "zero" => GfxBlend.Zero, "one" => GfxBlend.One,
        "srccolor" => GfxBlend.SourceColor, "invsrccolor" => GfxBlend.InverseSourceColor,
        "srcalpha" => GfxBlend.SourceAlpha, "invsrcalpha" => GfxBlend.InverseSourceAlpha,
        "destalpha" => GfxBlend.DestinationAlpha, "invdestalpha" => GfxBlend.InverseDestinationAlpha,
        "destcolor" => GfxBlend.DestinationColor, "invdestcolor" => GfxBlend.InverseDestinationColor,
        _ => throw new InvalidDataException($"Unsupported material blend factor '{value}'.")
    };
}
