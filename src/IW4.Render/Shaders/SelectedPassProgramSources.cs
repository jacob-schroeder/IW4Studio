using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Shaders;

/// <summary>One provider-owned, non-mutating selected-pass source snapshot.</summary>
public sealed class SelectedPassProgramSources
{
    private readonly MaterialShaderArgumentAsset[] _arguments;

    public SelectedPassProgramSources(
        MaterialVertexDeclarationAsset? vertexDeclaration,
        ShaderProgramResolution vertexProgram,
        ShaderProgramResolution pixelProgram,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        int expectedArgumentCount)
    {
        ArgumentNullException.ThrowIfNull(vertexProgram);
        ArgumentNullException.ThrowIfNull(pixelProgram);
        ArgumentNullException.ThrowIfNull(arguments);
        if (expectedArgumentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedArgumentCount));
        if (vertexProgram.Kind != MaterialShaderKind.Vertex ||
            pixelProgram.Kind != MaterialShaderKind.Pixel)
        {
            throw new ArgumentException("Selected-pass sources require one vertex and one pixel program.");
        }

        _arguments = arguments.ToArray();
        if (_arguments.Any(argument => argument is null))
            throw new ArgumentException("Shader argument snapshots cannot contain null rows.", nameof(arguments));

        VertexDeclaration = Snapshot(vertexDeclaration);
        VertexProgram = vertexProgram;
        PixelProgram = pixelProgram;
        Arguments = Array.AsReadOnly(_arguments);
        ExpectedArgumentCount = expectedArgumentCount;
    }

    public MaterialVertexDeclarationAsset? VertexDeclaration { get; }

    public ShaderProgramResolution VertexProgram { get; }

    public ShaderProgramResolution PixelProgram { get; }

    public IReadOnlyList<MaterialShaderArgumentAsset> Arguments { get; }

    public int ExpectedArgumentCount { get; }

    public int LoadedArgumentCount => _arguments.Length;

    public bool HasCompleteArguments => LoadedArgumentCount == ExpectedArgumentCount;

    private static MaterialVertexDeclarationAsset? Snapshot(
        MaterialVertexDeclarationAsset? declaration) => declaration is null
        ? null
        : new MaterialVertexDeclarationAsset
        {
            StreamCount = declaration.StreamCount,
            HasOptionalSourceRaw = declaration.HasOptionalSourceRaw,
            Routing = Array.AsReadOnly(declaration.Routing.ToArray())
        };
}
