namespace IW4.Render.Materials;

/// <summary>
/// Pure role-classification result.
/// </summary>
public sealed record EditorMaterialTextureClassification(
    EditorMaterialTextureRole Role)
{
    public bool IsKnown => Role != EditorMaterialTextureRole.Unknown;
}
