using IW4.Linker.Plans;
using ShaderConvert.Game.IW3.PC.Shaders;
using ShaderConvert.Game.IW3.PC.Techniques;

namespace ShaderConvert;

/// <summary>IW3 material bindings and packed vertex inputs for PC shader conversion.</summary>
public sealed record ShaderConversionOptions
{
    public string? TechniquePath { get; init; }
    public int PassIndex { get; init; }
    public ushort SignedNormalInputMask { get; init; }
    public ushort DecodedTexCoordInputMask { get; init; }
}

/// <summary>Converts compiled Direct3D 9 bytecode into PS3 RSX Cg programs.</summary>
public static class ShaderConverter
{
    /// <param name="options">Optional IW3 technique assignments and vertex input decoding.</param>
    /// <summary>Reads the stage and shader model from compiled Direct3D 9 bytecode.</summary>
    public static ShaderCompilation ConvertBytecode(byte[] bytecode, string sourcePath = "shader.cso",
        ShaderConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bytecode);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        Iw3ShaderSource shader = Iw3PcShaderCompiler.ReadSource(bytecode, sourcePath);
        ushort normals = options?.SignedNormalInputMask ?? 0;
        ushort texCoords = options?.DecodedTexCoordInputMask ?? 0;
        if (shader.Stage != Iw3ShaderStage.Vertex && (normals != 0 || texCoords != 0))
            throw new ArgumentException("Input decode masks apply only to vertex shaders.", nameof(options));

        if (options?.TechniquePath is { } techniquePath)
        {
            Iw3TechniqueSource technique = Iw3TechniqueFormatParser.ParseTechnique(techniquePath);
            if ((uint)options.PassIndex >= (uint)technique.Passes.Count)
                throw new ArgumentOutOfRangeException(nameof(options), $"Technique '{techniquePath}' contains {technique.Passes.Count} passes.");
            Iw3TechniquePassSource pass = technique.Passes[options.PassIndex];
            Iw3ShaderSource declared = shader.Stage == Iw3ShaderStage.Vertex ? pass.VertexShader : pass.PixelShader;
            if (declared.ShaderModel != shader.ShaderModel)
                throw new InvalidDataException("The technique shader model does not match the input bytecode.");
            shader = shader with { Arguments = declared.Arguments };
        }
        else if (options is { PassIndex: not 0 })
        {
            throw new ArgumentException("Selecting a pass requires a technique path.", nameof(options));
        }

        ShaderCompilation compiled = new Iw3PcShaderCompiler().Compile(shader, bytecode, sourcePath, normals, texCoords);
        MaterialShaderVertexCommandMetrics? metrics = null;
        if (shader.Stage == Iw3ShaderStage.Vertex)
        {
            metrics = MaterialShaderVertexReservation.CalculateCommandMetrics(compiled.Program, sourcePath);
            const int budget = MaterialShaderVertexReservation.Mw2VertexCommandBudgetBytes;
            if (metrics.Value.CommandBytes > budget)
                throw new InvalidDataException($"Converted vertex shader '{sourcePath}' needs {metrics.Value.CommandBytes} command-list bytes. The MW2 SPU merger budget is {budget} bytes.");
        }
        return compiled with { VertexCommands = metrics };
    }
}
