using Silk.NET.OpenGL;
using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Exact GLSL compile/link path for an active Silk.NET OpenGL context.
/// Shader objects are temporary; the returned program handle remains owned by
/// the context-local cache until that cache is disposed.
/// </summary>
public sealed class SilkMapRenderOpenGlProgramCompiler :
    IMapRenderOpenGlProgramCompiler,
    IMapRenderOpenGlLinkedProgramDescriber
{
    private readonly GL _gl;
    private readonly bool _requestRetrievableBinary;

    public SilkMapRenderOpenGlProgramCompiler(
        GL gl,
        string contextIdentity,
        string linkProfileIdentity,
        bool requestRetrievableBinary = false)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkProfileIdentity);
        _gl = gl;
        _requestRetrievableBinary = requestRetrievableBinary;
        ContextIdentity = contextIdentity;
        LinkProfileIdentity = linkProfileIdentity;
    }

    public string ContextIdentity { get; }

    public string LinkProfileIdentity { get; }

    public MapRenderOpenGlProgramResource Compile(
        OpenGlProgramKey key,
        string vertexGlsl,
        string pixelGlsl)
    {
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        if (!key.MatchesSourcesForCompilerProfile(
                vertexGlsl,
                pixelGlsl,
                LinkProfileIdentity))
        {
            throw new ArgumentException(
                "OpenGL program key does not match the exact sources and compiler link profile.",
                nameof(key));
        }

        uint handle = CreateProgram(vertexGlsl, pixelGlsl);
        try
        {
            return DescribeLinkedProgram(
                key,
                handle,
                vertexGlsl,
                pixelGlsl);
        }
        catch
        {
            _gl.DeleteProgram(handle);
            throw;
        }
    }

    public MapRenderOpenGlProgramResource DescribeLinkedProgram(
        OpenGlProgramKey key,
        uint programHandle,
        string vertexGlsl,
        string pixelGlsl)
    {
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        if (programHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(programHandle));
        if (!key.MatchesSourcesForCompilerProfile(
                vertexGlsl,
                pixelGlsl,
                LinkProfileIdentity))
        {
            throw new ArgumentException(
                "OpenGL program key does not match the exact sources and compiler link profile.",
                nameof(key));
        }

        ProgramUniformLocations locations =
            QueryActiveUniformLocations(programHandle);
        return new MapRenderOpenGlProgramResource(
            key,
            programHandle,
            OpenGlProgramKey.HashExactText(vertexGlsl),
            OpenGlProgramKey.HashExactText(pixelGlsl),
            locations.Samplers,
            locations.VertexConstants,
            locations.CodePixelConstants);
    }

    public void DeleteProgram(uint programHandle)
    {
        if (programHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(programHandle));

        _gl.DeleteProgram(programHandle);
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        try
        {
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);
            try
            {
                uint program = _gl.CreateProgram();
                _gl.AttachShader(program, vertexShader);
                _gl.AttachShader(program, fragmentShader);
                if (_requestRetrievableBinary)
                {
                    _gl.ProgramParameter(
                        program,
                        ProgramParameterPName.BinaryRetrievableHint,
                        1);
                }
                _gl.LinkProgram(program);
                _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
                if (status != 0)
                    return program;

                string info = _gl.GetProgramInfoLog(program);
                _gl.DeleteProgram(program);
                throw new InvalidOperationException($"OpenGL program link failed: {info}");
            }
            finally
            {
                _gl.DeleteShader(fragmentShader);
            }
        }
        finally
        {
            _gl.DeleteShader(vertexShader);
        }
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != 0)
            return shader;

        string info = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException($"OpenGL {type} compile failed: {info}");
    }

    private ProgramUniformLocations QueryActiveUniformLocations(uint handle)
    {
        _gl.GetProgram(
            handle,
            ProgramPropertyARB.ActiveUniforms,
            out int activeUniformCount);
        if (activeUniformCount < 0)
        {
            throw new InvalidOperationException(
                "OpenGL returned a negative active-uniform count.");
        }

        var samplers = new Dictionary<int, int>();
        var vertexConstants = new Dictionary<int, int>();
        var codePixelConstants = new Dictionary<int, int>();
        for (uint activeIndex = 0;
             activeIndex < checked((uint)activeUniformCount);
             activeIndex++)
        {
            string activeName = _gl.GetActiveUniform(
                handle,
                activeIndex,
                out int activeSize,
                out _);
            MapRenderOpenGlTrackedActiveUniform[] tracked =
                MapRenderOpenGlActiveUniformDiscovery.Expand(
                    activeName,
                    activeSize);
            foreach (MapRenderOpenGlTrackedActiveUniform uniform in tracked)
            {
                int location = _gl.GetUniformLocation(
                    handle,
                    uniform.QueryName);
                if (location < 0)
                    continue;

                Dictionary<int, int> destination = uniform.Kind switch
                {
                    MapRenderOpenGlTrackedUniformKind.Sampler => samplers,
                    MapRenderOpenGlTrackedUniformKind.VertexConstant =>
                        vertexConstants,
                    MapRenderOpenGlTrackedUniformKind.CodePixelConstant =>
                        codePixelConstants,
                    _ => throw new ArgumentOutOfRangeException()
                };
                if (destination.TryGetValue(
                        uniform.Destination,
                        out int existingLocation))
                {
                    if (existingLocation != location)
                    {
                        throw new InvalidOperationException(
                            $"OpenGL reported conflicting locations for active uniform '{uniform.QueryName}'.");
                    }
                    continue;
                }
                destination.Add(uniform.Destination, location);
            }
        }

        return new ProgramUniformLocations(
            samplers,
            vertexConstants,
            codePixelConstants);
    }

    private sealed record ProgramUniformLocations(
        IReadOnlyDictionary<int, int> Samplers,
        IReadOnlyDictionary<int, int> VertexConstants,
        IReadOnlyDictionary<int, int> CodePixelConstants);
}

internal enum MapRenderOpenGlTrackedUniformKind : byte
{
    Sampler = 0,
    VertexConstant = 1,
    CodePixelConstant = 2
}

internal readonly record struct MapRenderOpenGlTrackedActiveUniform(
    MapRenderOpenGlTrackedUniformKind Kind,
    int Destination,
    string QueryName);

internal static class MapRenderOpenGlActiveUniformDiscovery
{
    private const int RsxSamplerCount = 16;
    private const string SamplerPrefix = "rsxSampler";
    private const string VertexConstantArray = "rsxVertexConst";
    private const string CodePixelConstantArray =
        OpenGlCodePixelConstantUniformLayout.ArrayName;

    internal static MapRenderOpenGlTrackedActiveUniform[] Expand(
        string activeName,
        int activeSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeName);
        if (activeSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(activeSize));

        if (TryParseSampler(activeName, out int samplerDestination))
        {
            if (activeSize != 1)
            {
                throw new InvalidOperationException(
                    $"Scalar sampler uniform '{activeName}' reported array size {activeSize}.");
            }
            return
            [
                new(
                    MapRenderOpenGlTrackedUniformKind.Sampler,
                    samplerDestination,
                    activeName)
            ];
        }

        if (TryParseArrayElement(
                activeName,
                VertexConstantArray,
                out int firstVertexConstant))
        {
            return ExpandArray(
                MapRenderOpenGlTrackedUniformKind.VertexConstant,
                VertexConstantArray,
                firstVertexConstant,
                activeSize,
                RsxVertexConstantLayout.Count);
        }

        if (TryParseArrayElement(
                activeName,
                CodePixelConstantArray,
                out int firstCodePixelConstant))
        {
            return ExpandArray(
                MapRenderOpenGlTrackedUniformKind.CodePixelConstant,
                CodePixelConstantArray,
                firstCodePixelConstant,
                activeSize,
                OpenGlCodePixelConstantUniformLayout.Count);
        }

        return [];
    }

    private static bool TryParseSampler(
        string name,
        out int destination)
    {
        destination = -1;
        if (!name.StartsWith(SamplerPrefix, StringComparison.Ordinal) ||
            !int.TryParse(
                name.AsSpan(SamplerPrefix.Length),
                out int parsed))
        {
            return false;
        }
        if ((uint)parsed >= RsxSamplerCount)
        {
            throw new InvalidOperationException(
                $"Active RSX sampler destination {parsed} is outside 0..{RsxSamplerCount - 1}.");
        }

        destination = parsed;
        return true;
    }

    private static bool TryParseArrayElement(
        string name,
        string arrayName,
        out int firstElement)
    {
        firstElement = 0;
        if (string.Equals(name, arrayName, StringComparison.Ordinal))
            return true;
        if (!name.StartsWith(arrayName, StringComparison.Ordinal) ||
            name.Length <= arrayName.Length + 2 ||
            name[arrayName.Length] != '[' ||
            name[^1] != ']' ||
            !int.TryParse(
                name.AsSpan(
                    arrayName.Length + 1,
                    name.Length - arrayName.Length - 2),
                out int parsed))
        {
            return false;
        }

        firstElement = parsed;
        return true;
    }

    private static MapRenderOpenGlTrackedActiveUniform[] ExpandArray(
        MapRenderOpenGlTrackedUniformKind kind,
        string arrayName,
        int firstElement,
        int activeSize,
        int capacity)
    {
        if (firstElement < 0 ||
            firstElement >= capacity ||
            activeSize > capacity - firstElement)
        {
            throw new InvalidOperationException(
                $"Active uniform range '{arrayName}[{firstElement}]' size {activeSize} is outside 0..{capacity - 1}.");
        }

        var result =
            new MapRenderOpenGlTrackedActiveUniform[activeSize];
        for (int offset = 0; offset < activeSize; offset++)
        {
            int destination = checked(firstElement + offset);
            result[offset] = new(
                kind,
                destination,
                $"{arrayName}[{destination}]");
        }
        return result;
    }
}
