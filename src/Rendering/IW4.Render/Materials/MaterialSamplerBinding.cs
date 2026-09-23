using IW4.Render.Textures;

namespace IW4.Render.Materials;

public sealed record MaterialSamplerBinding(
    MaterialSamplerIdentity Identity,
    string TextureName,
    Texture? Texture,
    UvRoute? UvRoute,
    EditorMaterialTextureRole EditorTextureRole =
        EditorMaterialTextureRole.Unknown,
    int TextureTableOrdinal = -1,
    string? ExternalResourceIdentity = null)
{
    /// <summary>
    /// True when the texture is either carried by the render scene or owned by a
    /// host resource table under an immutable key. Authored shaders consume the
    /// vertex declaration independently of their texture-unit bindings, so an
    /// editor UV projection is not part of sampler resource readiness.
    /// </summary>
    public bool IsOperationallyResolved =>
        Texture is not null ||
        !string.IsNullOrWhiteSpace(ExternalResourceIdentity);

    public string ResourceBindingIdentity =>
        Texture?.BindingIdentity ??
        ExternalResourceIdentity ??
        "MISSING";

    public string ShaderResourceIdentity =>
        ExternalResourceIdentity ?? TextureName;
}
