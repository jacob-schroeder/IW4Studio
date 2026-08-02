namespace IW4.Render.EditorPreview;

/// <summary>
/// Coarse host-preview ordering for one complete authored material pass group.
/// This is editor policy and does not claim PS3 scheduler ownership.
/// </summary>
public enum MapRenderEditorDrawBucket
{
    Opaque = 0,
    AlphaTest = 1,
    Translucent = 2
}
