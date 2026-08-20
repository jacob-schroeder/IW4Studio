using System.Runtime.InteropServices;
using System.Text;
using IW4.Render.Diagnostics;
using Silk.NET.OpenGL;
using static IW4.Render.Diagnostics.MapRenderTelemetryOverlayText;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Compact, self-contained renderer telemetry HUD drawn after presentation.
/// It intentionally uses a pixel font so the native Silk window needs no
/// additional UI or font-rendering dependency.
/// </summary>
internal sealed unsafe class SilkMapRenderFpsOverlay : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly int _framebufferSizeLocation;
    private readonly int _colorLocation;
    private readonly List<float> _geometryVertices = [];
    private int _glyphPixelSize;
    private MapRenderFrameTelemetrySnapshot? _displayedTelemetry;
    private string? _displayedText;
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
                "OpenGL did not allocate the telemetry overlay resources.");
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
    /// Draws the cached renderer snapshot in the upper-left of the current
    /// native window back buffer. A snapshot identity change occurs only at
    /// the host's low-frequency refresh, keeping ordinary frames allocation
    /// and buffer-upload free.
    /// </summary>
    public void Render(
        MapRenderFrameTelemetrySnapshot telemetry,
        double hostRenderMilliseconds,
        double hostRenderAverageMilliseconds,
        double swapMilliseconds,
        double swapAverageMilliseconds,
        int framebufferWidth,
        int framebufferHeight,
        float renderScaling)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(telemetry);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
            return;

        int glyphPixelSize =
            MapRenderTelemetryOverlayGeometry.GetGlyphPixelSize(renderScaling);
        bool scaleChanged = glyphPixelSize != _glyphPixelSize;
        if (!ReferenceEquals(telemetry, _displayedTelemetry))
        {
            string text = BuildText(
                telemetry,
                hostRenderMilliseconds,
                hostRenderAverageMilliseconds,
                swapMilliseconds,
                swapAverageMilliseconds);
            if (scaleChanged ||
                !string.Equals(text, _displayedText, StringComparison.Ordinal))
            {
                UploadGeometry(text, glyphPixelSize);
            }

            _displayedTelemetry = telemetry;
            _displayedText = text;
        }
        else if (scaleChanged && _displayedText is { } displayedText)
            UploadGeometry(displayedText, glyphPixelSize);

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

    private static string BuildText(
        MapRenderFrameTelemetrySnapshot telemetry,
        double hostRenderMilliseconds,
        double hostRenderAverageMilliseconds,
        double swapMilliseconds,
        double swapAverageMilliseconds)
    {
        var text = new StringBuilder(768);
        text.Append("FPS ");
        AppendOneDecimal(text, telemetry.PresentedFramesPerSecond);
        text.Append("  PRESENT ");
        AppendOneDecimal(text, telemetry.PresentedFrameMilliseconds.Latest);
        text.Append(" MS  HOST ");
        AppendOneDecimal(text, hostRenderMilliseconds);
        text.Append(" / AVG ");
        AppendOneDecimal(text, hostRenderAverageMilliseconds);
        text.Append(" MS  SWAP ");
        AppendOneDecimal(text, swapMilliseconds);
        text.Append(" / AVG ");
        AppendOneDecimal(text, swapAverageMilliseconds);
        text.AppendLine(" MS");

        AppendCpuAndGpuTiming(text, telemetry);

        text.Append("DRAW ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.DrawCalls));
        text.Append("  LOGICAL ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.LogicalDrawCommands));
        text.Append("  MDRAW ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.MultiDrawApiCalls));
        text.Append(" / ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.MultiDrawCommands));
        text.Append("  TRI ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.Triangles));
        text.AppendLine();

        text.Append("GL TRACKED ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.OpenGlCalls));
        text.Append("  ELIDED ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.StateShadowElidedCalls));
        text.Append("  PROG ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.ProgramChanges));
        text.Append("  VAO ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.VertexArrayChanges));
        text.Append("  TEX ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.TextureChanges));
        text.Append("  STATE ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.RenderStateChanges));
        text.Append("  UNIFORM ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.UniformUpdates));
        text.AppendLine();

        text.Append("STATIC BUNDLE  BIND ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.StaticExecutionBundleBinds));
        text.Append("  REUSE ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.StaticExecutionBundleReuses));
        text.AppendLine();

        AppendResourceTelemetry(text, telemetry);
        return text.ToString();
    }

    private void UploadGeometry(string label, int glyphPixelSize)
    {
        List<float> vertices = _geometryVertices;
        (_backgroundVertexCount, _textVertexCount) =
            MapRenderTelemetryOverlayGeometry.Write(
                label,
                glyphPixelSize,
                vertices);
        Span<float> values = CollectionsMarshal.AsSpan(vertices);
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
                        $"OpenGL telemetry overlay program link failed: {info}");
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
            $"OpenGL telemetry overlay {type} compilation failed: {info}");
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
