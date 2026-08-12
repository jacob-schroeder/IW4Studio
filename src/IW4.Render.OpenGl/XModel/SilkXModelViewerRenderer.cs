using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.XModel;

/// <summary>
/// Retained OpenGL presentation for one backend-neutral XModel projection.
/// The caller owns the current context and invokes this object only while
/// that context is current.
/// </summary>
public sealed unsafe class SilkXModelViewerRenderer : IDisposable
{
    private const int VertexFloatCount = 9;

    private readonly GL _gl;
    private readonly uint _program;
    private readonly int _viewProjectionLocation;
    private readonly int _lightDirectionLocation;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _indexCount;
    private bool _disposed;

    public SilkXModelViewerRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _program = CreateProgram(VertexShaderSource, FragmentShaderSource);
        _viewProjectionLocation = RequireUniform(_program, "uViewProjection");
        _lightDirectionLocation = RequireUniform(_program, "uLightDirection");
    }

    public void Upload(XModelRenderLod? lod)
    {
        ThrowIfDisposed();
        DeleteGeometry();
        if (lod is null || lod.Surfaces.Count == 0)
            return;

        int vertexCount = checked(lod.Surfaces.Sum(surface =>
            surface.Positions.Count));
        int indexCount = checked(lod.Surfaces.Sum(surface =>
            surface.Indices.Count));
        var vertices = new float[checked(vertexCount * VertexFloatCount)];
        var indices = new uint[indexCount];
        int vertexOffset = 0;
        int indexOffset = 0;
        foreach (XModelRenderSurface surface in lod.Surfaces)
        {
            Vector3 color = MaterialColor(surface.MaterialName);
            for (int index = 0; index < surface.Positions.Count; index++)
            {
                Vector3 position = surface.Positions[index];
                Vector3 normal = surface.Normals[index];
                int destination = checked((vertexOffset + index) * VertexFloatCount);
                vertices[destination] = position.X;
                vertices[destination + 1] = position.Y;
                vertices[destination + 2] = position.Z;
                vertices[destination + 3] = normal.X;
                vertices[destination + 4] = normal.Y;
                vertices[destination + 5] = normal.Z;
                vertices[destination + 6] = color.X;
                vertices[destination + 7] = color.Y;
                vertices[destination + 8] = color.Z;
            }

            for (int index = 0; index < surface.Indices.Count; index++)
            {
                indices[indexOffset + index] = checked(
                    (uint)vertexOffset + surface.Indices[index]);
            }

            vertexOffset = checked(vertexOffset + surface.Positions.Count);
            indexOffset = checked(indexOffset + surface.Indices.Count);
        }

        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        _indexBuffer = _gl.GenBuffer();
        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (float* vertexPointer = vertices)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                checked((nuint)(vertices.Length * sizeof(float))),
                vertexPointer,
                BufferUsageARB.StaticDraw);
        }
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        fixed (uint* indexPointer = indices)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                checked((nuint)(indices.Length * sizeof(uint))),
                indexPointer,
                BufferUsageARB.StaticDraw);
        }

        const uint stride = VertexFloatCount * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(
            0,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(
            1,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(
            2,
            3,
            VertexAttribPointerType.Float,
            false,
            stride,
            (void*)(6 * sizeof(float)));
        _gl.BindVertexArray(0);
        _indexCount = checked((uint)indices.Length);
    }

    public void Render(
        int framebuffer,
        int width,
        int height,
        Matrix4x4 viewProjection,
        bool showWireframe)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0)
            return;

        _gl.BindFramebuffer(
            FramebufferTarget.Framebuffer,
            checked((uint)framebuffer));
        _gl.Viewport(0, 0, checked((uint)width), checked((uint)height));
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.ColorMask(true, true, true, true);
        _gl.Disable(EnableCap.CullFace);
        _gl.ClearColor(0.047f, 0.059f, 0.078f, 1f);
        _gl.ClearDepth(1d);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (_indexCount == 0)
            return;

        _gl.UseProgram(_program);
        Matrix4x4 matrix = viewProjection;
        _gl.UniformMatrix4(
            _viewProjectionLocation,
            1,
            false,
            (float*)&matrix);
        Vector3 light = Vector3.Normalize(new Vector3(-0.45f, 0.85f, 0.35f));
        _gl.Uniform3(
            _lightDirectionLocation,
            light.X,
            light.Y,
            light.Z);
        _gl.BindVertexArray(_vertexArray);
        _gl.PolygonMode(
            TriangleFace.FrontAndBack,
            showWireframe ? PolygonMode.Line : PolygonMode.Fill);
        _gl.DrawElements(
            PrimitiveType.Triangles,
            _indexCount,
            DrawElementsType.UnsignedInt,
            null);
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DeleteGeometry();
        _gl.DeleteProgram(_program);
        _disposed = true;
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        try
        {
            uint fragmentShader = CompileShader(
                ShaderType.FragmentShader,
                fragmentSource);
            try
            {
                uint program = _gl.CreateProgram();
                _gl.AttachShader(program, vertexShader);
                _gl.AttachShader(program, fragmentShader);
                _gl.LinkProgram(program);
                _gl.GetProgram(
                    program,
                    ProgramPropertyARB.LinkStatus,
                    out int status);
                if (status != 0)
                    return program;

                string info = _gl.GetProgramInfoLog(program);
                _gl.DeleteProgram(program);
                throw new InvalidOperationException(
                    $"XModel viewer OpenGL program link failed: {info}");
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
        _gl.GetShader(
            shader,
            ShaderParameterName.CompileStatus,
            out int status);
        if (status != 0)
            return shader;

        string info = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException(
            $"XModel viewer OpenGL {type} compile failed: {info}");
    }

    private int RequireUniform(uint program, string name)
    {
        int location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            return location;

        throw new InvalidOperationException(
            $"XModel viewer OpenGL program omitted required uniform '{name}'.");
    }

    private void DeleteGeometry()
    {
        if (_vertexArray != 0)
            _gl.DeleteVertexArray(_vertexArray);
        if (_vertexBuffer != 0)
            _gl.DeleteBuffer(_vertexBuffer);
        if (_indexBuffer != 0)
            _gl.DeleteBuffer(_indexBuffer);

        _vertexArray = 0;
        _vertexBuffer = 0;
        _indexBuffer = 0;
        _indexCount = 0;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static Vector3 MaterialColor(string materialName)
    {
        uint hash = 2166136261;
        foreach (char character in materialName)
        {
            hash ^= character;
            hash *= 16777619;
        }

        float hue = (hash % 360) / 360f;
        return FromHsv(hue, 0.52f, 0.88f);
    }

    private static Vector3 FromHsv(float hue, float saturation, float value)
    {
        float scaled = hue * 6f;
        int sector = (int)MathF.Floor(scaled) % 6;
        float fraction = scaled - MathF.Floor(scaled);
        float p = value * (1f - saturation);
        float q = value * (1f - fraction * saturation);
        float t = value * (1f - (1f - fraction) * saturation);
        return sector switch
        {
            0 => new Vector3(value, t, p),
            1 => new Vector3(q, value, p),
            2 => new Vector3(p, value, t),
            3 => new Vector3(p, q, value),
            4 => new Vector3(t, p, value),
            _ => new Vector3(value, p, q)
        };
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec3 aColor;
        uniform mat4 uViewProjection;
        out vec3 vNormal;
        out vec3 vColor;
        void main()
        {
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
            vNormal = aNormal;
            vColor = aColor;
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 vNormal;
        in vec3 vColor;
        uniform vec3 uLightDirection;
        out vec4 outColor;
        void main()
        {
            float normalLength = length(vNormal);
            vec3 normal = normalLength > 0.00001
                ? vNormal / normalLength
                : vec3(0.0, 1.0, 0.0);
            float diffuse = max(dot(normal, uLightDirection), 0.0);
            outColor = vec4(vColor * (0.28 + diffuse * 0.72), 1.0);
        }
        """;
}
