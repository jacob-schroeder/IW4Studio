namespace IW4.Render.Techniques;

/// <summary>Canonical interpretation of the PS3 cull-enable/face tuple.</summary>
public enum CullMode
{
    Disabled = 0,
    Front,
    Back
}

/// <summary>
/// Shared culling classification used by renderer backends and preview
/// planners so raw RSX face values are interpreted in one place.
/// </summary>
public static class Cull
{
    public static CullMode? Resolve(RenderState state)
    {
        if (!state.CullEnabled)
            return CullMode.Disabled;

        return state.CullFace switch
        {
            RsxCullFace.Front => CullMode.Front,
            RsxCullFace.Back => CullMode.Back,
            _ => null
        };
    }
}
