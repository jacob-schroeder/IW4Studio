namespace IW4.Render.EditorPreview;

/// <summary>
/// Identifies which native input block supplied the values copied into one
/// immutable view post state.
/// </summary>
public enum MapRenderEditorPreviewPostValueSource : byte
{
    RefDef,
    RendererTweaks
}

/// <summary>
/// Composite identity for a post snapshot. AssetPoolRevision identifies the
/// immutable scene assets; RuntimeRevision identifies one atomically captured
/// refdef/dvar state.
/// </summary>
public readonly record struct MapRenderEditorPreviewPostRevision(
    long AssetPoolRevision,
    long RuntimeRevision);

/// <summary>
/// Post fields already materialized by the native frontend/refdef path. These
/// values are deliberately distinct from the authored .vision values: the
/// frontend may interpolate or otherwise resolve them before renderer setup.
/// </summary>
public sealed record MapRenderEditorPreviewRefDefPostState(
    MapRenderEditorPreviewFilmVisionState Film,
    MapRenderEditorPreviewGlowVisionState Glow);

/// <summary>
/// Exact renderer controls used by the native glow source selector and
/// R_UsingGlow predicate.
/// </summary>
public sealed record MapRenderEditorPreviewGlowRuntimeDvars(
    bool Allowed,
    bool AllowedScriptForced,
    bool MasterEnabled,
    bool UseTweaks,
    MapRenderEditorPreviewGlowVisionState? Tweaks);

/// <summary>
/// Renderer film source selector plus the global mixer/late-material state.
/// Tweaks may be absent while UseTweaks is false because the native branch
/// does not consume them in that state.
/// </summary>
public sealed record MapRenderEditorPreviewFilmRuntimeDvars(
    bool UseTweaks,
    MapRenderEditorPreviewFilmVisionState? Tweaks,
    MapRenderEditorPreviewFilmDvarState Mixer);

public sealed record MapRenderEditorPreviewRendererPostDvars(
    bool Fullbright,
    MapRenderEditorPreviewGlowRuntimeDvars Glow,
    MapRenderEditorPreviewFilmRuntimeDvars Film)
{
    /// <summary>
    /// Default registered renderer state. Inactive tweak payloads
    /// remain unspecified instead of being invented as active view values.
    /// </summary>
    public static MapRenderEditorPreviewRendererPostDvars RegisteredDefault
        { get; } = new(
            Fullbright: false,
            Glow: new MapRenderEditorPreviewGlowRuntimeDvars(
                Allowed: true,
                AllowedScriptForced: false,
                MasterEnabled: true,
                UseTweaks: false,
                Tweaks: null),
            Film: new MapRenderEditorPreviewFilmRuntimeDvars(
                UseTweaks: false,
                Tweaks: null,
                Mixer: MapRenderEditorPreviewFilmDvarState
                    .RegisteredDefault));
}

/// <summary>
/// One atomic runtime post capture. It is resolved once before presentation;
/// render frames never poll dvars or PS3MAPI.
/// </summary>
public sealed record MapRenderEditorPreviewPostRuntimeSnapshot(
    long Revision,
    MapRenderEditorPreviewRefDefPostState RefDef,
    MapRenderEditorPreviewRendererPostDvars Dvars);

public sealed record MapRenderEditorPreviewEffectiveFilmState(
    MapRenderEditorPreviewFilmVisionState Values,
    MapRenderEditorPreviewFilmDvarState Mixer,
    MapRenderEditorPreviewPostValueSource Source)
{
    /// <summary>
    /// Selects postfx_color2 only when r_filmAltShader.current is false. Film
    /// enable does not select it.
    /// </summary>
    public bool SelectsPostFxColor2 => Mixer.SelectsPostFxColor2;
}

public sealed record MapRenderEditorPreviewEffectiveGlowState(
    MapRenderEditorPreviewGlowVisionState Values,
    MapRenderEditorPreviewPostValueSource Source,
    bool ShouldRender);

/// <summary>
/// Immutable post state consumed by one scene/runtime revision.
/// </summary>
public sealed record MapRenderEditorPreviewEffectivePostState(
    MapRenderEditorPreviewPostRevision Revision,
    MapRenderEditorPreviewPostRuntimeSnapshot SourceSnapshot,
    MapRenderEditorPreviewEffectiveFilmState Film,
    MapRenderEditorPreviewEffectiveGlowState Glow)
{
    public bool SelectsPostFxColor2 => Film.SelectsPostFxColor2;

    public bool UsesGlow => Glow.ShouldRender;

    public bool UsesGlowSetupColor2 =>
        UsesGlow && Film.Mixer.AltShader;
}

/// <summary>
/// Pure PS3-shaped evaluator for renderer-effective film and glow state.
/// </summary>
public static class MapRenderEditorPreviewEffectivePostStateEvaluator
{
    public static MapRenderEditorPreviewEffectivePostState Evaluate(
        long assetPoolRevision,
        MapRenderEditorPreviewPostRuntimeSnapshot runtime)
    {
        if (assetPoolRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(assetPoolRevision));
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime.Revision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtime),
                "Runtime post revision must be nonnegative.");
        }

        MapRenderEditorPreviewRefDefPostState refDef = runtime.RefDef ??
            throw new ArgumentException(
                "Runtime post state requires an explicit frontend/refdef snapshot.",
                nameof(runtime));
        MapRenderEditorPreviewRendererPostDvars dvars = runtime.Dvars ??
            throw new ArgumentException(
                "Runtime post state requires renderer dvars.",
                nameof(runtime));
        MapRenderEditorPreviewGlowRuntimeDvars glowDvars = dvars.Glow ??
            throw new ArgumentException(
                "Runtime post state requires renderer glow dvars.",
                nameof(runtime));
        MapRenderEditorPreviewFilmRuntimeDvars filmDvars = dvars.Film ??
            throw new ArgumentException(
                "Runtime post state requires renderer film dvars.",
                nameof(runtime));

        MapRenderEditorPreviewFilmVisionState film = filmDvars.UseTweaks
            ? filmDvars.Tweaks ?? throw new ArgumentException(
                "r_filmUseTweaks requires a complete film tweak payload.",
                nameof(runtime))
            : refDef.Film ?? throw new ArgumentException(
                "The frontend/refdef film state is absent.",
                nameof(runtime));
        MapRenderEditorPreviewGlowVisionState glow = glowDvars.UseTweaks
            ? glowDvars.Tweaks ?? throw new ArgumentException(
                "r_glowUseTweaks requires a complete glow tweak payload.",
                nameof(runtime))
            : refDef.Glow ?? throw new ArgumentException(
                "The frontend/refdef glow state is absent.",
                nameof(runtime));

        RequireValidFilm(film, nameof(runtime));
        RequireValidGlow(glow, nameof(runtime));
        MapRenderEditorPreviewFilmDvarState mixer = filmDvars.Mixer ??
            throw new ArgumentException(
                "Runtime post state requires renderer film mixer dvars.",
                nameof(runtime));
        if (!mixer.HasFiniteValues)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtime),
                "Renderer film mixer dvars must be finite.");
        }

        // Both intensity and radius must remain nonzero after the runtime
        // policy gates.
        bool shouldRenderGlow =
            (glowDvars.Allowed || glowDvars.AllowedScriptForced) &&
            glow.Enabled &&
            !dvars.Fullbright &&
            glowDvars.MasterEnabled &&
            glow.BloomIntensity != 0f &&
            glow.Radius != 0f;

        return new MapRenderEditorPreviewEffectivePostState(
            new MapRenderEditorPreviewPostRevision(
                assetPoolRevision,
                runtime.Revision),
            runtime,
            new MapRenderEditorPreviewEffectiveFilmState(
                film,
                mixer,
                filmDvars.UseTweaks
                    ? MapRenderEditorPreviewPostValueSource.RendererTweaks
                    : MapRenderEditorPreviewPostValueSource.RefDef),
            new MapRenderEditorPreviewEffectiveGlowState(
                glow,
                glowDvars.UseTweaks
                    ? MapRenderEditorPreviewPostValueSource.RendererTweaks
                    : MapRenderEditorPreviewPostValueSource.RefDef,
                shouldRenderGlow));
    }

    private static void RequireValidGlow(
        MapRenderEditorPreviewGlowVisionState glow,
        string parameterName)
    {
        if (!float.IsFinite(glow.Radius) || glow.Radius < 0f ||
            !float.IsFinite(glow.BloomCutoff) ||
            glow.BloomCutoff < 0f || glow.BloomCutoff >= 1f ||
            !float.IsFinite(glow.BloomDesaturation) ||
            !float.IsFinite(glow.BloomIntensity) ||
            glow.BloomIntensity < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Effective glow values must be finite, radius/intensity must be nonnegative, and cutoff must be in [0, 1).");
        }
    }

    private static void RequireValidFilm(
        MapRenderEditorPreviewFilmVisionState film,
        string parameterName)
    {
        if (!float.IsFinite(film.Contrast) || film.Contrast < 0f ||
            !float.IsFinite(film.Brightness) ||
            !float.IsFinite(film.Desaturation) ||
            !float.IsFinite(film.DesaturationDark) ||
            !IsFinite(film.LightTint) ||
            !IsFinite(film.MediumTint) ||
            !IsFinite(film.DarkTint))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Effective film scalar and tint values must be finite and contrast must be nonnegative.");
        }
    }

    private static bool IsFinite(System.Numerics.Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
