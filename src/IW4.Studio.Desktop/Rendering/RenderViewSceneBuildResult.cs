using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Render;
using IW4.Render.Resources;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Immutable result of projecting one fastfile document into the shared map
/// scene and scene-resource snapshot consumed by render frame planning.
/// </summary>
public sealed record RenderViewSceneBuildResult
{
    private RenderViewSceneBuildResult(
        RenderViewSceneBuildStatus status,
        Guid sourceDocumentId,
        LoadedXZone sourceZone,
        GfxWorldAsset? gfxWorld,
        ClipMapAsset? clipMap,
        MapRenderScene? scene,
        RenderSceneSnapshot? sceneSnapshot,
        string? nonRenderableReason)
    {
        if (sourceDocumentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceDocumentId));
        ArgumentNullException.ThrowIfNull(sourceZone);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        bool hasRenderableContent =
            gfxWorld is not null || clipMap is not null;
        if (status == RenderViewSceneBuildStatus.Renderable &&
            (!hasRenderableContent ||
             scene is null ||
             sceneSnapshot is null ||
             nonRenderableReason is not null))
        {
            throw new ArgumentException(
                "A renderable result requires map assets, a scene, and a scene snapshot only.");
        }

        if (status == RenderViewSceneBuildStatus.NoRenderableMapAssets &&
            (hasRenderableContent ||
             scene is not null ||
             sceneSnapshot is not null ||
             string.IsNullOrWhiteSpace(nonRenderableReason)))
        {
            throw new ArgumentException(
                "A non-renderable result requires a reason and cannot retain map render content.");
        }

        Status = status;
        SourceDocumentId = sourceDocumentId;
        SourceZone = sourceZone;
        GfxWorld = gfxWorld;
        ClipMap = clipMap;
        Scene = scene;
        SceneSnapshot = sceneSnapshot;
        NonRenderableReason = nonRenderableReason;
    }

    public RenderViewSceneBuildStatus Status { get; }

    public bool IsRenderable =>
        Status == RenderViewSceneBuildStatus.Renderable;

    /// <summary>
    /// Authoritative Studio document that supplied the target-zone runtime
    /// assets used to build this scene.
    /// </summary>
    public Guid SourceDocumentId { get; }

    public LoadedXZone SourceZone { get; }

    public GfxWorldAsset? GfxWorld { get; }

    public ClipMapAsset? ClipMap { get; }

    public MapRenderScene? Scene { get; }

    public RenderSceneSnapshot? SceneSnapshot { get; }

    public string? NonRenderableReason { get; }

    internal static RenderViewSceneBuildResult Renderable(
        Guid sourceDocumentId,
        LoadedXZone sourceZone,
        GfxWorldAsset? gfxWorld,
        ClipMapAsset? clipMap,
        MapRenderScene scene,
        RenderSceneSnapshot sceneSnapshot) =>
        new(
            RenderViewSceneBuildStatus.Renderable,
            sourceDocumentId,
            sourceZone,
            gfxWorld,
            clipMap,
            scene,
            sceneSnapshot,
            nonRenderableReason: null);

    internal static RenderViewSceneBuildResult NoRenderableMapAssets(
        Guid sourceDocumentId,
        LoadedXZone sourceZone,
        string reason) =>
        new(
            RenderViewSceneBuildStatus.NoRenderableMapAssets,
            sourceDocumentId,
            sourceZone,
            gfxWorld: null,
            clipMap: null,
            scene: null,
            sceneSnapshot: null,
            reason);
}
