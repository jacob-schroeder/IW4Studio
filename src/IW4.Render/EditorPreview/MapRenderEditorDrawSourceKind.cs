namespace IW4.Render.EditorPreview;

/// <summary>
/// Geometry ownership relevant to practical EditorPreview alpha ordering.
/// This is a host-preview policy and does not claim native scheduler ownership.
/// </summary>
public enum MapRenderEditorDrawSourceKind
{
    WorldSurface = 0,
    StaticModel = 1
}
