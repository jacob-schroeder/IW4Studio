using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Execution;

/// <summary>
/// Renderer-owned resources that can satisfy a translated authored sampler.
/// Unknown is diagnostic-only and never satisfies a requirement.
/// </summary>
public enum ShaderRuntimeSamplerResourceKind
{
    Unknown = 0,
    SunShadowAtlas = 1,
    ModelLightingAtlas = 2,
    ProcessedFloatZ = 3,
    LightAttenuation = 4,
    SpotShadowAtlas = 5
}

/// <summary>The publication rule attached to one runtime sampler.</summary>
public enum ShaderRuntimeSamplerRequirementStatus
{
    Unknown = 0,
    SameRevisionAtlasRequired = 1,
    ImmutableSceneAtlasRequired = 2,
    SameRevisionTextureRequired = 3,
    ImmutableSceneTextureRequired = 4
}

/// <summary>
/// Immutable requirement retained by the scene contract. It describes the
/// authored destination/resource semantics and the exact runtime publication
/// needed before drawing; it does not itself indicate that the resource exists.
/// </summary>
public sealed record ShaderRuntimeSamplerRequirement(
    int ArgumentIndex,
    ushort Destination,
    MaterialTextureSource CodeSamplerArgument,
    ShaderRuntimeSamplerResourceKind ResourceKind,
    ShaderRuntimeSamplerRequirementStatus Status,
    string ResourceIdentity);

/// <summary>State of one renderer-side runtime sampler publication.</summary>
public enum ShaderRuntimeSamplerBindingStatus
{
    Deferred = 0,
    Ready = 1,
    Failed = 2
}

/// <summary>
/// Renderer-facing publication token. Revision is the three-view/shadow-atlas
/// frame revision, not a texture allocation generation.
/// </summary>
public sealed record ShaderRuntimeSamplerBinding(
    ushort Destination,
    ShaderRuntimeSamplerResourceKind ResourceKind,
    long Revision,
    ShaderRuntimeSamplerBindingStatus Status);
