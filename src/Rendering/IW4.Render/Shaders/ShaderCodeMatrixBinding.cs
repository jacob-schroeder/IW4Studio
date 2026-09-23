namespace IW4.Render.Shaders;

public sealed record ShaderCodeMatrixBinding(
    CodeMatrixSemantic Semantic,
    CodeMatrixTransform Transform,
    int Row);
