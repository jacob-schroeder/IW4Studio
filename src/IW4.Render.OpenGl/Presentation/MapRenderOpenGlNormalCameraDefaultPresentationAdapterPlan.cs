using IW4.Render.OpenGl.Targets;
using IW4.Render.EditorPreview;
using IW4.Render.SceneBuilding;
using IW4.Render.Scheduling.Lifecycle;

namespace IW4.Render.OpenGl.Presentation;

/// <summary>
/// Operational EditorPreview recipe for resolving target 2 into target 4 and
/// drawing the registered-default postfx material into the host back buffer.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan
{
    internal MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan(
        MapRenderOpenGlNormalCameraSceneTargetPlan sceneTarget,
        MapRenderWorldSceneSource source,
        MapRenderNormalCameraMaterialAssetContract feedbackReplace,
        MapRenderNormalCameraMaterialAssetContract postFx,
        MapRenderNormalCameraMaterialAssetContract postFxColor2,
        MapRenderNormalCameraMaterialAssetContract glowConsistentSetup,
        MapRenderNormalCameraMaterialAssetContract glowConsistentSetupColor2,
        MapRenderNormalCameraMaterialAssetContract glowApplyBloom,
        IReadOnlyList<MapRenderNormalCameraMaterialAssetContract>
            glowSymmetricFilters,
        MapRenderEditorPreviewEffectivePostState? effectivePost)
    {
        ArgumentNullException.ThrowIfNull(sceneTarget);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(feedbackReplace);
        ArgumentNullException.ThrowIfNull(postFx);
        ArgumentNullException.ThrowIfNull(postFxColor2);
        ArgumentNullException.ThrowIfNull(glowConsistentSetup);
        ArgumentNullException.ThrowIfNull(glowConsistentSetupColor2);
        ArgumentNullException.ThrowIfNull(glowApplyBloom);
        ArgumentNullException.ThrowIfNull(glowSymmetricFilters);

        MapRenderOpenGlNormalCameraColorTargetResourceFrame colorFrame =
            sceneTarget.Resources.ColorFrame;
        MapRenderOpenGlNormalCameraColorTargetResourceBinding scene =
            colorFrame.GetBinding(MapRenderNormalCameraTargetKind.Scene);
        MapRenderOpenGlNormalCameraColorTargetResourceBinding target3 =
            colorFrame.GetBinding(
                MapRenderNormalCameraTargetKind.ResolvedPostSun);
        MapRenderOpenGlNormalCameraColorTargetResourceBinding target4 =
            colorFrame.GetBinding(
                MapRenderNormalCameraTargetKind.ResolvedScene);
        if (!ReferenceEquals(
                scene.Resource,
                sceneTarget.Binding.Resource.ColorResource) ||
            !ReferenceEquals(target3.Resource, target4.Resource))
        {
            throw new ArgumentException(
                "Live Preview presentation requires the executed target 2 and the target-3/target-4 alias.",
                nameof(sceneTarget));
        }
        if (scene.Resource.SampleCount != 2 ||
            target4.Resource.SampleCount != 1 ||
            ReferenceEquals(scene.Resource, target4.Resource))
        {
            throw new ArgumentException(
                "Live Preview presentation requires distinct two-sample target 2 and one-sample target 4 resources.",
                nameof(sceneTarget));
        }
        if (!source.AssetLookup.HasCanonicalAssetPoolRevision(
                source.AssetPoolRevisionAtConstruction))
        {
            throw new ArgumentException(
                "Live Preview presentation requires the scene's active canonical asset revision.",
                nameof(source));
        }
        if (effectivePost is { } post &&
            (post.SourceSnapshot is not { } snapshot ||
             post.Revision.AssetPoolRevision !=
                 source.AssetPoolRevisionAtConstruction ||
             post.Revision.RuntimeRevision !=
                 snapshot.Revision))
        {
            throw new ArgumentException(
                "Live Preview effective post state must belong to the scene's canonical asset revision and its exact atomic runtime snapshot.",
                nameof(effectivePost));
        }
        if (feedbackReplace.MaterialName != "feedbackreplace" ||
            postFx.MaterialName != "postfx" ||
            postFx.CodePixelConstants.Count != 0 ||
            postFxColor2.MaterialName != "postfx_color2" ||
            !HasExactPostFxColor2Rows(postFxColor2.CodePixelConstants) ||
            feedbackReplace.StateBits0 != postFx.StateBits0 ||
            feedbackReplace.StateBits1 != postFx.StateBits1 ||
            feedbackReplace.StateBits0 != postFxColor2.StateBits0 ||
            feedbackReplace.StateBits1 != postFxColor2.StateBits1)
        {
            throw new ArgumentException(
                "Live Preview presentation requires the exact feedbackreplace and postfx material recipes.");
        }

        SceneTarget = sceneTarget;
        Source = source;
        SceneColor = scene;
        ResolvedSceneColor = target4;
        FeedbackReplace = feedbackReplace;
        PostFx = postFx;
        PostFxColor2 = postFxColor2;
        GlowConsistentSetup = glowConsistentSetup;
        GlowConsistentSetupColor2 = glowConsistentSetupColor2;
        GlowApplyBloom = glowApplyBloom;
        GlowSymmetricFilters = glowSymmetricFilters;
        EffectivePost = effectivePost;
    }

    public MapRenderOpenGlNormalCameraSceneTargetPlan SceneTarget { get; }

    public MapRenderWorldSceneSource Source { get; }

    public MapRenderOpenGlNormalCameraColorTargetResourceBinding SceneColor
        { get; }

    public MapRenderOpenGlNormalCameraColorTargetResourceBinding
        ResolvedSceneColor { get; }

    public MapRenderNormalCameraMaterialAssetContract FeedbackReplace
        { get; }

    public MapRenderNormalCameraMaterialAssetContract PostFx { get; }

    public MapRenderNormalCameraMaterialAssetContract PostFxColor2 { get; }

    public MapRenderNormalCameraMaterialAssetContract GlowConsistentSetup
        { get; }

    public MapRenderNormalCameraMaterialAssetContract
        GlowConsistentSetupColor2 { get; }

    public MapRenderNormalCameraMaterialAssetContract GlowApplyBloom { get; }

    public IReadOnlyList<MapRenderNormalCameraMaterialAssetContract>
        GlowSymmetricFilters { get; }

    /// <summary>
    /// Renderer-effective frontend/refdef and dvar state captured atomically
    /// for this immutable asset revision. Authored .vision data is not a
    /// substitute because runtime presentation may select or interpolate
    /// another row. Null fails closed to the baseline postfx copy;
    /// it does not authorize film-color or glow passes.
    /// </summary>
    public MapRenderEditorPreviewEffectivePostState? EffectivePost { get; }

    public bool UsesFilmColorManipulation =>
        EffectivePost?.SelectsPostFxColor2 == true;

    public bool UsesGlow => EffectivePost?.UsesGlow == true;

    public bool UsesGlowSetupColor2 =>
        EffectivePost?.UsesGlowSetupColor2 == true;

    public MapRenderNormalCameraMaterialAssetContract ActiveGlowSetup =>
        UsesGlowSetupColor2
            ? GlowConsistentSetupColor2
            : GlowConsistentSetup;

    public MapRenderNormalCameraMaterialAssetContract ActivePostFx =>
        UsesFilmColorManipulation ? PostFxColor2 : PostFx;

    public string ContextIdentity => SceneTarget.ContextIdentity;

    public long FrameRevision => SceneTarget.FrameRevision;

    public long AssetPoolRevision =>
        Source.AssetPoolRevisionAtConstruction;

    public int DisplayWidth =>
        SceneTarget.Resources.ColorFrame.Plan.DisplayWidth;

    public int DisplayHeight =>
        SceneTarget.Resources.ColorFrame.Plan.DisplayHeight;

    public int ResolveSourceSampleCount => SceneColor.Resource.SampleCount;

    public int ResolveDestinationSampleCount =>
        ResolvedSceneColor.Resource.SampleCount;

    public uint HostBackBufferFramebufferHandle => 0;

    public bool PreservesFogAlreadyWrittenToSceneColor => true;

    private static bool HasExactPostFxColor2Rows(
        IReadOnlyList<MapRenderNormalCameraMaterialArgumentContract> rows) =>
        rows.Count == 4 &&
        rows[0].Destination == 1 && rows[0].RawValue == 0x002e_0001u &&
        rows[1].Destination == 2 && rows[1].RawValue == 0x002f_0001u &&
        rows[2].Destination == 3 && rows[2].RawValue == 0x0030_0001u &&
        rows[3].Destination == 4 && rows[3].RawValue == 0x002d_0001u;
}

public static class
    MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlanner
{
    public static MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan
        Create(
            MapRenderOpenGlNormalCameraSceneTargetPlan sceneTarget,
            MapRenderWorldSceneSource source,
            MapRenderEditorPreviewEffectivePostState? effectivePost = null)
    {
        ArgumentNullException.ThrowIfNull(sceneTarget);
        ArgumentNullException.ThrowIfNull(source);

        MapRenderEditorPreviewNormalCameraRecipe recipe =
            MapRenderEditorPreviewNormalCameraRecipe.Current;

        return new MapRenderOpenGlNormalCameraDefaultPresentationAdapterPlan(
            sceneTarget,
            source,
            recipe.FeedbackReplace,
            recipe.PostFx,
            recipe.PostFxColor2,
            recipe.GlowConsistentSetup,
            recipe.GlowConsistentSetupColor2,
            recipe.GlowApplyBloom,
            recipe.GlowSymmetricFilters,
            effectivePost);
    }
}
