namespace IW4.AI.XModel;

/// <summary>
/// One active PS3 XModel LOD. The advisor groups peers by LodIndex and
/// observed skinning. LodRampType retains the separate authored ramp setting.
/// An unavailable measurement is represented by Single.NaN.
/// </summary>
public record LodFeatures
{
    public int LodIndex { get; init; }
    public byte LodRampType { get; init; }
    public float LodCount { get; init; }
    public float LodDistance { get; init; }
    public float TriangleCount { get; init; }
    public float VertexCount { get; init; }
    public float SurfaceCount { get; init; }
    public float BoneCount { get; init; }

    /// <summary>Vertices in the one- through four-influence blend groups.</summary>
    public float SkinnedVertexCount { get; init; }
    /// <summary>Total bone influences across those skinned vertices.</summary>
    public float SkinningInfluenceCount { get; init; }

    public float DistinctMaterialCount { get; init; }
    /// <summary>Largest material texture table used by a surface in this LOD.</summary>
    public float MaxTexturesPerMaterial { get; init; }
    /// <summary>Sum of base Lit technique passes across surfaces.</summary>
    public float LitSurfacePassCount { get; init; }
    /// <summary>Surfaces with alpha testing enabled in any base Lit pass.</summary>
    public float AlphaTestedLitSurfaceCount { get; init; }
    /// <summary>Surfaces with blending enabled in any base Lit pass.</summary>
    public float BlendedLitSurfaceCount { get; init; }
}
