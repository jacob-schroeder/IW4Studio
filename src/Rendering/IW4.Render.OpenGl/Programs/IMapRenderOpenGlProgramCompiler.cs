namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Context-bound GL compiler and handle disposer. Implementations must not
/// substitute fallback programs when exact source compilation or linking fails.
/// </summary>
public interface IMapRenderOpenGlProgramCompiler
{
    string ContextIdentity { get; }

    string LinkProfileIdentity { get; }

    MapRenderOpenGlProgramResource Compile(
        OpenGlProgramKey key,
        string vertexGlsl,
        string pixelGlsl);

    void DeleteProgram(uint programHandle);
}

internal interface IMapRenderOpenGlLinkedProgramDescriber
{
    MapRenderOpenGlProgramResource DescribeLinkedProgram(
        OpenGlProgramKey key,
        uint programHandle,
        string vertexGlsl,
        string pixelGlsl);
}
