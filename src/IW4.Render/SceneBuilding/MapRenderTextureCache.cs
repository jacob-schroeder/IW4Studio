using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Scene-build texture cache plus the payload ownership policy selected for
/// that build. Neutral scenes retain decoded RGBA compatibility payloads;
/// interactive OpenGL scenes prefer complete proven authored BC chains and
/// leave any capability fallback to the renderer that owns the GL context.
/// </summary>
internal sealed class MapRenderTextureCache
{
    private readonly Dictionary<
        MapRenderTextureCacheKey,
        MapRenderTexture> _textures = [];

    internal MapRenderTextureCache(
        bool preferProvenAuthoredPayloads)
    {
        PreferProvenAuthoredPayloads =
            preferProvenAuthoredPayloads;
    }

    internal bool PreferProvenAuthoredPayloads { get; }

    internal bool ContainsKey(MapRenderTextureCacheKey key) =>
        _textures.ContainsKey(key);

    internal bool TryGetValue(
        MapRenderTextureCacheKey key,
        out MapRenderTexture texture) =>
        _textures.TryGetValue(key, out texture!);

    internal void Add(
        MapRenderTextureCacheKey key,
        MapRenderTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _textures.Add(key, texture);
    }
}
