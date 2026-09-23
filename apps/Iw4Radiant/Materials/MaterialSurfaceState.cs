using System.Text.Json;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;

namespace Iw4Radiant.Materials;

internal sealed record MaterialSurfaceState(GfxBlendOperation BlendOperation, GfxBlend Source, GfxBlend Destination,
    GfxAlphaTest? AlphaTest, bool DepthWrite, int SortKey)
{
    internal static MaterialSurfaceState Opaque { get; } = new(GfxBlendOperation.Disabled, GfxBlend.One, GfxBlend.Zero, null, true, 0);
    internal bool IgnoresVertexColor { get; init; }
    internal MaterialTechniqueType TechniqueType { get; init; } = MaterialTechniqueType.None;
    internal bool HasShadowMapTechnique { get; init; }
    internal GfxCullFace ShadowCullFace { get; init; }
    internal GfxAlphaTest? ShadowAlphaTest { get; init; }
    internal GfxCullFace CullFace { get; init; } = GfxCullFace.Back;
    internal GfxDepthTest? DepthTest { get; init; } = GfxDepthTest.LessThanOrEqual;
    internal GfxPolygonOffset PolygonOffset { get; init; }
    internal bool IsBlended => BlendOperation != GfxBlendOperation.Disabled;
    internal bool SupportsAlpha => BlendOperation == GfxBlendOperation.Add && Source is GfxBlend.One or GfxBlend.SourceAlpha &&
        Destination == GfxBlend.InverseSourceAlpha && !DepthWrite;

    internal static MaterialSurfaceState Read(JsonElement material)
    {
        if (!material.TryGetProperty("stateBitsEntry", out var entries) || !material.TryGetProperty("stateBits", out var states) ||
            entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() != (int)MaterialTechniqueType.Count ||
            states.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Material render-state arrays are invalid.");
        // The lit world shader emits premultiplied RGB; its Unlit preview slot
        // can use different blend factors. Select the populated color technique.
        MaterialTechniqueType technique = new[] { MaterialTechniqueType.Lit, MaterialTechniqueType.Emissive, MaterialTechniqueType.Unlit }
            .FirstOrDefault(candidate => entries[(int)candidate].GetInt32() >= 0, MaterialTechniqueType.None);
        if (technique == MaterialTechniqueType.None)
            throw new NotSupportedException("Material has no supported native color technique.");
        int slot = (int)technique;
        int index = entries[slot].GetInt32();
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
        GfxAlphaTest? alphaTest = ReadAlphaTest(Field("alphaTest"));
        int shadowIndex = entries[(int)MaterialTechniqueType.BuildShadowmapDepth].GetInt32();
        if (shadowIndex < 0) shadowIndex = entries[(int)MaterialTechniqueType.BuildShadowmapColor].GetInt32();
        if (shadowIndex >= states.GetArrayLength()) throw new InvalidDataException("Material shadow render-state entry is out of range.");
        JsonElement? shadow = shadowIndex >= 0 ? states[shadowIndex] : null;
        var result = new MaterialSurfaceState(operation, Blend(Field("srcBlendRgb")), Blend(Field("dstBlendRgb")), alphaTest,
            state.GetProperty("depthWrite").GetBoolean() && Field("depthTest") != "disabled", material.GetProperty("sortKey").GetInt32())
        {
            TechniqueType = technique,
            IgnoresVertexColor = material.GetProperty("techniqueSet").GetString()?.StartsWith("w_", StringComparison.Ordinal) == true,
            HasShadowMapTechnique = shadow.HasValue,
            ShadowCullFace = shadow is { } shadowState ? ReadCullFace(shadowState.GetProperty("cullFace").GetString() ?? "") : GfxCullFace.None,
            ShadowAlphaTest = shadow is { } alphaState ? ReadAlphaTest(alphaState.GetProperty("alphaTest").GetString() ?? "") : null,
            CullFace = ReadCullFace(Field("cullFace")),
            DepthTest = Field("depthTest") switch
            {
                "disabled" => null, "always" => GfxDepthTest.Always, "less" => GfxDepthTest.Less,
                "equal" => GfxDepthTest.Equal, "less_equal" => GfxDepthTest.LessThanOrEqual,
                var value => throw new InvalidDataException($"Unsupported material depth test '{value}'.")
            },
            PolygonOffset = Field("polygonOffset") switch
            {
                "offset0" => GfxPolygonOffset.Disabled, "offset1" => GfxPolygonOffset.Offset1,
                "offset2" => GfxPolygonOffset.Offset2, "inherit" => GfxPolygonOffset.Inherit,
                var value => throw new InvalidDataException($"Unsupported material polygon offset '{value}'.")
            }
        };
        if (technique == MaterialTechniqueType.Lit && material.TryGetProperty("techniqueSet", out var techset) &&
            techset.GetString() is { } name && (name.StartsWith("w_", StringComparison.Ordinal) || name.StartsWith("wc_", StringComparison.Ordinal)))
            foreach (MaterialTechniqueType variant in new[] { MaterialTechniqueType.LitSun, MaterialTechniqueType.LitSunShadow })
            {
                int variantIndex = entries[(int)variant].GetInt32();
                if (variantIndex < 0) continue;
                if (variantIndex >= states.GetArrayLength()) throw new InvalidDataException("Material sunlight render-state entry is out of range.");
                foreach (string field in new[] { "blendOpRgb", "srcBlendRgb", "dstBlendRgb", "alphaTest", "depthWrite", "cullFace", "depthTest", "polygonOffset" })
                    if (!JsonElement.DeepEquals(state.GetProperty(field), states[variantIndex].GetProperty(field)))
                        throw new NotSupportedException($"Material '{name}' has different {field} in Lit and {variant}; compilation requires matching sunlight surface states.");
            }
        return result;
    }

    private static GfxCullFace ReadCullFace(string value) => value switch
    {
        "none" => GfxCullFace.None, "back" => GfxCullFace.Back, "front" => GfxCullFace.Front,
        _ => throw new InvalidDataException($"Unsupported material cull face '{value}'.")
    };

    private static GfxAlphaTest? ReadAlphaTest(string value) => value switch
    {
        "disabled" => null, "gt0" => GfxAlphaTest.GreaterThanZero, "lt128" => GfxAlphaTest.LessThan128,
        "ge128" => GfxAlphaTest.GreaterThanOrEqualTo128,
        _ => throw new InvalidDataException($"Unsupported material alpha test '{value}'.")
    };

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
