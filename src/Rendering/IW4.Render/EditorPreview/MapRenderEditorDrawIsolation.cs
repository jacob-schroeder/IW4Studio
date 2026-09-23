namespace IW4.Render.EditorPreview;

/// <summary>
/// Smallest source unit that must remain independently sortable in the editor
/// draw queue. Authored passes inside that unit still remain contiguous.
/// </summary>
public enum MapRenderEditorDrawIsolation
{
    MergeCompatibleGeometry = 0,
    WorldSurfacePassGroup = 1,
    StaticModelInstancePassGroup = 2
}
