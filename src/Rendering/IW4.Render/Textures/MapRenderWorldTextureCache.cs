namespace IW4.Render.Textures;

/// <summary>One map-build-local cache for world-runtime texture resources.</summary>
internal sealed class MapRenderWorldTextureCache
{
    private readonly Dictionary<MapRenderWorldTextureCacheKey, Texture> _textures = [];

    internal bool TryGetValue(
        MapRenderWorldTextureCacheKey key,
        out Texture texture) => _textures.TryGetValue(key, out texture!);

    internal void Add(MapRenderWorldTextureCacheKey key, Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _textures.Add(key, texture);
    }
}
