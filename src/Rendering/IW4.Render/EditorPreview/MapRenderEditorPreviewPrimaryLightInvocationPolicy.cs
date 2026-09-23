namespace IW4.Render.EditorPreview;

/// <summary>
/// Separates the view's available primary-light tweak values from the per-draw
/// hero-lighting gate in packed draw-group bit 16. The vision boolean alone
/// does not authorize applying the strengths.
/// </summary>
internal static class MapRenderEditorPreviewPrimaryLightInvocationPolicy
{
    internal static MapRenderEditorPreviewPrimaryLightVisionState? Resolve(
        MapRenderEditorPreviewPrimaryLightVisionState? vision,
        bool useHeroLighting) =>
        useHeroLighting && vision?.UseTweaks == true
            ? vision
            : null;
}
