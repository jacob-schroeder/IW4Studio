namespace IW4.Render.Materials;

/// <summary>Canonical interpretation of the PS3 cull-enable/face tuple.</summary>
public enum MapRenderCullMode
{
    Disabled = 0,
    Front,
    Back
}

/// <summary>
/// Shared culling classification used by renderer backends and preview
/// planners so raw RSX face values are interpreted in one place.
/// </summary>
public static class MapRenderCull
{
    public static MapRenderCullMode? Resolve(MapRenderState state)
    {
        if (!state.CullEnabled)
            return MapRenderCullMode.Disabled;

        return state.CullFace switch
        {
            0x0404u => MapRenderCullMode.Front,
            0x0405u => MapRenderCullMode.Back,
            _ => null
        };
    }
}
