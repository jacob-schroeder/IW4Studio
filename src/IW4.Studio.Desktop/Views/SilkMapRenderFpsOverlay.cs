using System.Diagnostics;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Small, self-contained OpenGL HUD drawn after the map presentation pass.
/// It intentionally uses a pixel font so the native Silk window needs no
/// additional UI or font-rendering dependency.
/// </summary>
internal sealed unsafe class SilkMapRenderFpsOverlay : IDisposable
{
    private const int GlyphColumns = 5;
    private const int GlyphRows = 7;
    private const int MarginPixels = 14;
    private const int PaddingPixels = 6;
    // The rolling FPS value can alternate around an integer boundary. Keep
    // that from turning into per-frame allocations and driver buffer uploads.
    private static readonly long MinimumTextRefreshTicks =
        Math.Max(1, Stopwatch.Frequency / 4);
    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly int _framebufferSizeLocation;
    private readonly int _colorLocation;
    private int _glyphPixelSize;
    private int _displayedFps = -1;
    private long _nextTextRefreshTimestamp;
    private int _backgroundVertexCount;
    private int _textVertexCount;
    private bool _disposed;

    public SilkMapRenderFpsOverlay(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _program = CreateProgram();
        _framebufferSizeLocation = _gl.GetUniformLocation(
            _program,
            "uFramebufferSize");
        _colorLocation = _gl.GetUniformLocation(_program, "uColor");
        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        if (_vertexArray == 0 || _vertexBuffer == 0)
        {
            Dispose();
            throw new InvalidOperationException(
                "OpenGL did not allocate the FPS overlay resources.");
        }

        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(
            0,
            2,
            VertexAttribPointerType.Float,
            normalized: false,
            2 * sizeof(float),
            (void*)0);
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Draws the latest presentation rate in the upper-left of the current
    /// native window back buffer.
    /// </summary>
    public void Render(
        double presentedFramesPerSecond,
        int framebufferWidth,
        int framebufferHeight,
        float renderScaling)
    {
        ThrowIfDisposed();
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
            return;

        int fps = double.IsFinite(presentedFramesPerSecond)
            ? Math.Clamp((int)Math.Round(presentedFramesPerSecond), 0, 999)
            : 0;
        int glyphPixelSize = Math.Max(
            3,
            (int)MathF.Round(3f * Math.Max(1f, renderScaling)));
        bool scaleChanged = glyphPixelSize != _glyphPixelSize;
        long timestamp = Stopwatch.GetTimestamp();
        if (_displayedFps < 0 ||
            scaleChanged ||
            (fps != _displayedFps &&
             timestamp >= _nextTextRefreshTimestamp))
        {
            string label = $"{fps} FPS";
            UploadGeometry(label, glyphPixelSize);
            _displayedFps = fps;
            _nextTextRefreshTimestamp = checked(
                timestamp + MinimumTextRefreshTicks);
        }

        if (_textVertexCount == 0)
            return;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.DrawBuffer(DrawBufferMode.Back);
        _gl.Viewport(
            0,
            0,
            checked((uint)framebufferWidth),
            checked((uint)framebufferHeight));
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.FramebufferSrgb);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquationSeparate(
            BlendEquationModeEXT.FuncAdd,
            BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.OneMinusSrcAlpha);
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.ColorMask(true, true, true, true);
        _gl.UseProgram(_program);
        _gl.Uniform2(
            _framebufferSizeLocation,
            (float)framebufferWidth,
            (float)framebufferHeight);
        _gl.BindVertexArray(_vertexArray);

        _gl.Uniform4(_colorLocation, 0f, 0f, 0f, 0.65f);
        _gl.DrawArrays(
            PrimitiveType.Triangles,
            0,
            checked((uint)_backgroundVertexCount));
        _gl.Uniform4(_colorLocation, 0.93f, 0.97f, 1f, 1f);
        _gl.DrawArrays(
            PrimitiveType.Triangles,
            _backgroundVertexCount,
            checked((uint)_textVertexCount));

        // Match the renderer's post-presentation handoff for every binding
        // the HUD changes. Fixed pipeline state is deliberately re-applied by
        // the renderer on its next authored pass.
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.UseProgram(0);
    }

    private void UploadGeometry(string label, int glyphPixelSize)
    {
        var vertices = new List<float>();
        int contentWidth = label.Length * (GlyphColumns + 1) * glyphPixelSize -
            glyphPixelSize;
        int contentHeight = GlyphRows * glyphPixelSize;
        int left = MarginPixels * glyphPixelSize / 3;
        int top = MarginPixels * glyphPixelSize / 3;
        int padding = PaddingPixels * glyphPixelSize / 3;
        AppendQuad(
            vertices,
            left,
            top,
            left + contentWidth + 2 * padding,
            top + contentHeight + 2 * padding);
        _backgroundVertexCount = 6;

        int x = left + padding;
        int y = top + padding;
        foreach (char character in label)
        {
            if (Glyphs.TryGetValue(character, out string[]? rows))
            {
                for (int row = 0; row < GlyphRows; row++)
                {
                    string cells = rows[row];
                    for (int column = 0; column < GlyphColumns; column++)
                    {
                        if (cells[column] != '1')
                            continue;

                        int glyphLeft = x + column * glyphPixelSize;
                        int glyphTop = y + row * glyphPixelSize;
                        AppendQuad(
                            vertices,
                            glyphLeft,
                            glyphTop,
                            glyphLeft + glyphPixelSize,
                            glyphTop + glyphPixelSize);
                    }
                }
            }

            x += (GlyphColumns + 1) * glyphPixelSize;
        }

        _textVertexCount = vertices.Count / 2 - _backgroundVertexCount;
        float[] values = vertices.ToArray();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (float* valuesPointer = values)
        {
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                checked((nuint)(values.Length * sizeof(float))),
                valuesPointer,
                BufferUsageARB.DynamicDraw);
        }

        _glyphPixelSize = glyphPixelSize;
    }

    private static void AppendQuad(
        List<float> vertices,
        float left,
        float top,
        float right,
        float bottom)
    {
        vertices.Add(left);
        vertices.Add(top);
        vertices.Add(right);
        vertices.Add(top);
        vertices.Add(right);
        vertices.Add(bottom);
        vertices.Add(left);
        vertices.Add(top);
        vertices.Add(right);
        vertices.Add(bottom);
        vertices.Add(left);
        vertices.Add(bottom);
    }

    private uint CreateProgram()
    {
        uint vertexShader = CompileShader(
            ShaderType.VertexShader,
            VertexShaderSource);
        try
        {
            uint fragmentShader = CompileShader(
                ShaderType.FragmentShader,
                FragmentShaderSource);
            try
            {
                uint program = _gl.CreateProgram();
                _gl.AttachShader(program, vertexShader);
                _gl.AttachShader(program, fragmentShader);
                _gl.LinkProgram(program);
                _gl.GetProgram(
                    program,
                    ProgramPropertyARB.LinkStatus,
                    out int linked);
                if (linked == 0)
                {
                    string info = _gl.GetProgramInfoLog(program);
                    _gl.DeleteProgram(program);
                    throw new InvalidOperationException(
                        $"OpenGL FPS overlay program link failed: {info}");
                }

                return program;
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
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled != 0)
            return shader;

        string info = _gl.GetShaderInfoLog(shader);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException(
            $"OpenGL FPS overlay {type} compilation failed: {info}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_vertexBuffer != 0)
            _gl.DeleteBuffer(_vertexBuffer);
        if (_vertexArray != 0)
            _gl.DeleteVertexArray(_vertexArray);
        if (_program != 0)
            _gl.DeleteProgram(_program);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SilkMapRenderFpsOverlay));
    }

    private static readonly IReadOnlyDictionary<char, string[]> Glyphs =
        new Dictionary<char, string[]>
        {
            ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
            ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
            ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
            ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
            ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
            ['5'] = ["11111", "10000", "10000", "11110", "00001", "00001", "11110"],
            ['6'] = ["01110", "10000", "10000", "11110", "10001", "10001", "01110"],
            ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
            ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
            ['9'] = ["01110", "10001", "10001", "01111", "00001", "00001", "01110"],
            ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
            ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
            ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
            [' '] = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"]
        };

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;

        uniform vec2 uFramebufferSize;

        void main()
        {
            vec2 normalized = aPosition / uFramebufferSize;
            gl_Position = vec4(
                normalized.x * 2.0 - 1.0,
                1.0 - normalized.y * 2.0,
                0.0,
                1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        uniform vec4 uColor;
        out vec4 oColor;

        void main()
        {
            oColor = uColor;
        }
        """;
}
