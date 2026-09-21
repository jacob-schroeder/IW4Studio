using Silk.NET.OpenGL;

namespace Iw4Radiant.Rendering;

internal static class SceneShaderProgram
{
    internal static uint Create(GL gl, string header, string vertexFile, string fragmentFile)
    {
        uint vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(ShaderType.VertexShader, vertexFile);
            fragment = Compile(ShaderType.FragmentShader, fragmentFile);
            program = gl.CreateProgram();
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.BindAttribLocation(program, 0, "aPosition");
            gl.BindAttribLocation(program, 1, "aNormal");
            gl.BindAttribLocation(program, 2, "aTexCoord");
            gl.BindAttribLocation(program, 3, "aColor");
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0)
                throw new InvalidOperationException($"{vertexFile}/{fragmentFile}: {gl.GetProgramInfoLog(program)}");
            return program;
        }
        catch
        {
            if (program != 0)
                gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            if (vertex != 0)
                gl.DeleteShader(vertex);
            if (fragment != 0)
                gl.DeleteShader(fragment);
        }

        uint Compile(ShaderType type, string filename)
        {
            string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shaders", filename));
            foreach (string include in new[] { "material-alpha.glsl", "ocean-waves.hlsl", "ocean-surface.hlsl" })
            {
                string directive = "#include \"" + include + "\"";
                if (source.Contains(directive, StringComparison.Ordinal))
                    source = source.Replace(directive,
                        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shaders", include)), StringComparison.Ordinal);
            }
            uint shader = gl.CreateShader(type);
            gl.ShaderSource(shader, header + source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
            if (compiled != 0)
                return shader;
            string error = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"{filename}: {error}");
        }
    }
}
