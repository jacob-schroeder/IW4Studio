namespace IW4.Render.Textures;

/// <summary>
/// Build-local texture cache plus the payload ownership policy selected for
/// that build. Neutral projections retain decoded RGBA compatibility payloads;
/// interactive OpenGL scenes prefer complete proven authored BC chains and
/// leave any capability fallback to the renderer that owns the GL context.
/// </summary>
internal sealed class RenderTextureCache
{
    private readonly Dictionary<
        RenderTextureCacheKey,
        Texture> _textures = [];

    internal RenderTextureCache(
        bool preferProvenAuthoredPayloads)
    {
        PreferProvenAuthoredPayloads =
            preferProvenAuthoredPayloads;
    }

    internal bool PreferProvenAuthoredPayloads { get; }

    internal bool ContainsKey(RenderTextureCacheKey key) =>
        _textures.ContainsKey(key);

    internal bool TryGetValue(
        RenderTextureCacheKey key,
        out Texture texture) =>
        _textures.TryGetValue(key, out texture!);

    internal void Add(
        RenderTextureCacheKey key,
        Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _textures.Add(key, texture);
    }
}
