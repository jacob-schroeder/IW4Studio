using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution.Fog;
using IW4.Runtime.Assets.Lifecycle.State;
using IW4.Runtime.Assets.Images;

namespace IW4.Render;

/// <summary>
/// Selects which backend-neutral scene channels are retained by a scene
/// build. The default preserves every channel for callers that inspect or
/// snapshot diagnostic geometry.
/// </summary>
public enum MapRenderSceneBuildProfile : byte
{
    Neutral = 0,
    InteractiveOpenGl = 1
}

/// <summary>
/// Scene-construction input. An optional atomic runtime post snapshot supplies
/// the renderer-effective frontend vision state, which may differ from authored
/// .vision rows after selection or interpolation.
/// </summary>
public readonly record struct MapRenderInput(
    RenderAssetSource AssetSource,
    GfxWorldRuntimeState GfxWorldRuntime,
    string FastFilePath,
    GfxWorldAsset? GfxMap,
    ClipMapAsset? ClipMap,
    Action<string>? Progress = null,
    MapRenderEditorPreviewAtmosphereSettings? EditorPreviewAtmosphere = null,
    MapRenderActiveFogState? EditorPreviewActiveFog = null,
    MapRenderEditorPreviewPostRuntimeSnapshot?
        EditorPreviewPostRuntimeSnapshot = null)
{
    public IGfxImagePayloadResolver ImagePayloadResolver { get; init; } =
        UnavailableGfxImagePayloadResolver.Instance;

    public MapRenderSceneBuildProfile BuildProfile { get; init; } =
        MapRenderSceneBuildProfile.Neutral;
}
