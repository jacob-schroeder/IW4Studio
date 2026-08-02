using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Minimal static-model batch description used to plan editor draw grouping
/// without depending on OpenGL resources.
/// </summary>
public readonly record struct MapRenderEditorStaticPassBatch(
    int SourceOrdinal,
    int DrawGroupId,
    int PassIndex,
    int InstanceCount,
    MapRenderState State);
