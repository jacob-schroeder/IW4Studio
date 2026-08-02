namespace IW4.Studio.Desktop.Rendering.WorldViewport;

/// <summary>
/// Typed collision-layer presentation state for the authoring viewport.
/// This is editor-only state; it never changes the imported ColMap or the
/// semantic document.
/// </summary>
internal enum WorldViewportCollisionDisplayMode : byte
{
    Hidden = 0,
    Overlay = 1,
    Isolated = 2
}

internal enum WorldViewportPickingDomain : byte
{
    None = 0,
    Render = 1,
    Collision = 2
}

internal readonly record struct WorldViewportCollisionDisplaySettings(
    WorldViewportCollisionDisplayMode Mode,
    bool ShowCollisionWireframe,
    bool ShowTexturedGeometry,
    bool ShowSky);

internal static class WorldViewportCollisionDisplayPolicy
{
    internal static WorldViewportCollisionDisplaySettings Resolve(
        bool overlayVisible,
        bool isolateActive)
    {
        if (isolateActive)
        {
            return new(
                WorldViewportCollisionDisplayMode.Isolated,
                ShowCollisionWireframe: true,
                ShowTexturedGeometry: false,
                ShowSky: false);
        }

        return overlayVisible
            ? new(
                WorldViewportCollisionDisplayMode.Overlay,
                ShowCollisionWireframe: true,
                ShowTexturedGeometry: true,
                ShowSky: true)
            : new(
                WorldViewportCollisionDisplayMode.Hidden,
                ShowCollisionWireframe: false,
                ShowTexturedGeometry: true,
                ShowSky: true);
    }

    internal static WorldViewportPickingDomain ResolvePickingDomain(
        bool collisionWorkspaceActive,
        bool collisionPickingActive,
        bool overlayVisible,
        bool isolateActive)
    {
        if (!collisionWorkspaceActive)
        {
            // Collision isolation hides every render-selectable layer.
            // Do not let the source-semantic picker select geometry the user
            // cannot see after switching back to the Scene workspace.
            return isolateActive
                ? WorldViewportPickingDomain.None
                : WorldViewportPickingDomain.Render;
        }

        WorldViewportCollisionDisplayMode displayMode =
            Resolve(overlayVisible, isolateActive).Mode;
        return collisionPickingActive &&
               displayMode !=
                   WorldViewportCollisionDisplayMode.Hidden
            ? WorldViewportPickingDomain.Collision
            : WorldViewportPickingDomain.None;
    }
}
