namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Describes whether a Studio document produced backend-neutral render-view
/// content. A document without map assets remains a valid document workspace.
/// </summary>
public enum RenderViewSceneBuildStatus
{
    Renderable = 0,
    NoRenderableMapAssets = 1,
}
