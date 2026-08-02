namespace IW4.Render.Materials;

/// <summary>
/// Pure role-classification result.
/// </summary>
public sealed record MapRenderEditorMaterialTextureClassification(
    MapRenderEditorMaterialTextureRole Role)
{
    public bool IsKnown => Role != MapRenderEditorMaterialTextureRole.Unknown;
}
