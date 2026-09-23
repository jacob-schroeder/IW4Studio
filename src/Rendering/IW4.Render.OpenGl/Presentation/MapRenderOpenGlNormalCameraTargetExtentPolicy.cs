using IW4.Render;

namespace IW4.Render.OpenGl.Presentation;

/// <summary>
/// Resolves the actual target receiving normal-camera draws. World scenes
/// render into the logical scene target before presentation; direct scenes
/// have no presentation pass and therefore draw into the physical host FBO.
/// </summary>
internal static class MapRenderOpenGlNormalCameraTargetExtentPolicy
{
    internal static MapRenderPixelExtent Resolve(
        MapRenderSurfaceExtents extents,
        bool hasPresentationSession)
    {
        if (!extents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(extents));

        return hasPresentationSession
            ? extents.SceneTarget
            : extents.HostFramebuffer;
    }
}
