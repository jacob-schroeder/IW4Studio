using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using IW4.Render.Diagnostics;
using Silk.NET.OpenGL;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Compact, self-contained renderer telemetry HUD drawn after presentation.
/// It intentionally uses a pixel font so the native Silk window needs no
/// additional UI or font-rendering dependency.
/// </summary>
internal sealed unsafe class SilkMapRenderFpsOverlay : IDisposable
{
    private const int GlyphColumns = 5;
    private const int GlyphRows = 7;
    private const int BaseGlyphPixelSize = 2;
    private const int MarginPixels = 14;
    private const int PaddingPixels = 6;
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

        int glyphPixelSize = Math.Max(
            BaseGlyphPixelSize,
            (int)MathF.Round(
                BaseGlyphPixelSize * Math.Max(1f, renderScaling)));
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

        text.Append("CPU ");
        AppendOneDecimal(text, telemetry.CpuFrameMilliseconds.Latest);
        text.Append(" / P95 ");
        AppendOneDecimal(text, telemetry.CpuFrameMilliseconds.P95);
        text.Append(" MS  GPU ");
        if (telemetry.GpuFrameMilliseconds.HasSamples)
        {
            AppendOneDecimal(text, telemetry.GpuFrameMilliseconds.Latest);
            text.Append(" / P95 ");
            AppendOneDecimal(text, telemetry.GpuFrameMilliseconds.P95);
            text.Append(" MS  AGE ");
            text.Append(GpuSampleAgeFrames(telemetry));
            text.Append(" F  READ ");
            text.Append(telemetry.LastGpuReadbackDelayFrames);
            text.AppendLine(" F");
        }
        else
        {
            text.AppendLine("NO SAMPLE");
        }

        AppendTopCpuPhases(text, telemetry.CpuPhases);
        AppendTopGpuPhases(text, telemetry.GpuPhases);

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

        text.Append("ALLOC RENDER ");
        AppendBytes(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.RenderThreadAllocatedBytes));
        text.Append("  WORKER ");
        AppendBytes(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.CpuWorkerAllocatedBytes));
        text.Append("  WAIT ");
        AppendOneDecimal(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.CpuWorkerWaitMicroseconds) /
            1000.0);
        text.Append(" MS  JOB ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.CpuWorkerJobs));
        text.Append(" HIT ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.CpuWorkerCacheHits));
        text.AppendLine();

        text.Append("TEX RES ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.TextureResidentCount));
        text.Append(' ');
        AppendBytes(
            text,
            Counter(telemetry, MapRenderFrameCounter.TextureResidentBytes));
        text.Append("  UP ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.TextureResidencyUploadCount));
        text.Append(' ');
        AppendBytes(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.TextureResidencyUploadBytes));
        text.Append("  EVICT ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.TextureResidencyEvictionCount));
        text.Append(' ');
        AppendBytes(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.TextureResidencyEvictionBytes));
        text.Append("  DEFER ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.TextureResidencyDeferredCount));
        text.AppendLine();

        text.Append("VISIBLE WORLD ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.WorldVisible));
        text.Append(" / ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.WorldCandidates));
        text.Append("  STATIC ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.StaticModelsVisible));
        text.Append(" / ");
        AppendCompactCount(
            text,
            Counter(telemetry, MapRenderFrameCounter.StaticModelCandidates));
        text.AppendLine();

        text.Append("SHADER COMPILE DELTA ");
        AppendCompactCount(
            text,
            Counter(
                telemetry,
                MapRenderFrameCounter.ShaderProgramCompilations));
        return text.ToString();
    }

    private static long GpuSampleAgeFrames(
        MapRenderFrameTelemetrySnapshot telemetry)
    {
        if (telemetry.LastPresentedFrameIndex is long presentedFrameIndex &&
            telemetry.LastGpuFrameIndex is long gpuFrameIndex &&
            presentedFrameIndex >= gpuFrameIndex)
        {
            return presentedFrameIndex - gpuFrameIndex;
        }

        return Math.Max(0, telemetry.LastGpuReadbackDelayFrames);
    }

    private static void AppendTopCpuPhases(
        StringBuilder text,
        IReadOnlyList<MapRenderCpuPhaseTelemetrySnapshot> phases)
    {
        MapRenderCpuPhaseTelemetrySnapshot? first = null;
        MapRenderCpuPhaseTelemetrySnapshot? second = null;
        for (int i = 0; i < phases.Count; i++)
        {
            MapRenderCpuPhaseTelemetrySnapshot phase = phases[i];
            if (!phase.Milliseconds.HasSamples)
                continue;

            if (first is null ||
                phase.Milliseconds.Latest > first.Milliseconds.Latest)
            {
                second = first;
                first = phase;
            }
            else if (second is null ||
                     phase.Milliseconds.Latest > second.Milliseconds.Latest)
            {
                second = phase;
            }
        }

        text.Append("CPU TOP ");
        if (first is null)
        {
            text.AppendLine("NO SAMPLE");
            return;
        }

        AppendCpuPhase(text, first.Phase);
        text.Append(' ');
        AppendOneDecimal(text, first.Milliseconds.Latest);
        text.Append(" MS");
        if (second is not null)
        {
            text.Append("  ");
            AppendCpuPhase(text, second.Phase);
            text.Append(' ');
            AppendOneDecimal(text, second.Milliseconds.Latest);
            text.Append(" MS");
        }
        text.AppendLine();
    }

    private static void AppendTopGpuPhases(
        StringBuilder text,
        IReadOnlyList<MapRenderGpuPhaseTelemetrySnapshot> phases)
    {
        MapRenderGpuPhaseTelemetrySnapshot? first = null;
        MapRenderGpuPhaseTelemetrySnapshot? second = null;
        for (int i = 0; i < phases.Count; i++)
        {
            MapRenderGpuPhaseTelemetrySnapshot phase = phases[i];
            if (!phase.Milliseconds.HasSamples)
                continue;

            if (first is null ||
                phase.Milliseconds.Latest > first.Milliseconds.Latest)
            {
                second = first;
                first = phase;
            }
            else if (second is null ||
                     phase.Milliseconds.Latest > second.Milliseconds.Latest)
            {
                second = phase;
            }
        }

        text.Append("GPU TOP ");
        if (first is null)
        {
            text.AppendLine("NO SAMPLE");
            return;
        }

        AppendGpuPhase(text, first.Phase);
        text.Append(' ');
        AppendOneDecimal(text, first.Milliseconds.Latest);
        text.Append(" MS");
        if (second is not null)
        {
            text.Append("  ");
            AppendGpuPhase(text, second.Phase);
            text.Append(' ');
            AppendOneDecimal(text, second.Milliseconds.Latest);
            text.Append(" MS");
        }
        text.AppendLine();
    }

    private static void AppendCpuPhase(
        StringBuilder text,
        MapRenderCpuPhase phase)
    {
        text.Append(phase switch
        {
            MapRenderCpuPhase.StaticResourceAdmission => "ADMISSION",
            MapRenderCpuPhase.FrameSetup => "SETUP",
            MapRenderCpuPhase.SunShadow => "SUN SHADOW",
            MapRenderCpuPhase.SceneTarget => "SCENE TARGET",
            MapRenderCpuPhase.Visibility => "VISIBILITY",
            MapRenderCpuPhase.QueueBuild => "QUEUE",
            MapRenderCpuPhase.Sky => "SKY",
            MapRenderCpuPhase.DepthPrepass => "DEPTH",
            MapRenderCpuPhase.ProcessedFloatZ => "FLOAT Z",
            MapRenderCpuPhase.WorldGeometry => "WORLD",
            MapRenderCpuPhase.StaticModels => "STATIC",
            MapRenderCpuPhase.EditorOverlay => "OVERLAY",
            MapRenderCpuPhase.Presentation => "PRESENT",
            MapRenderCpuPhase.SwapOrPresent => "SWAP",
            _ => "UNKNOWN"
        });
    }

    private static void AppendGpuPhase(
        StringBuilder text,
        MapRenderGpuPhase phase)
    {
        text.Append(phase switch
        {
            MapRenderGpuPhase.SunShadow => "SUN SHADOW",
            MapRenderGpuPhase.SceneTarget => "SCENE TARGET",
            MapRenderGpuPhase.FrameSetup => "SETUP",
            MapRenderGpuPhase.Sky => "SKY",
            MapRenderGpuPhase.Diagnostics => "DIAGNOSTICS",
            MapRenderGpuPhase.DepthPrepass => "DEPTH",
            MapRenderGpuPhase.WorldOpaque => "WORLD OPAQUE",
            MapRenderGpuPhase.WorldCutout => "WORLD CUTOUT",
            MapRenderGpuPhase.StaticOpaque => "STATIC OPAQUE",
            MapRenderGpuPhase.StaticCutout => "STATIC CUTOUT",
            MapRenderGpuPhase.ProcessedFloatZ => "FLOAT Z",
            MapRenderGpuPhase.Translucent => "TRANSLUCENT",
            MapRenderGpuPhase.Wireframe => "WIREFRAME",
            MapRenderGpuPhase.EditorOverlay => "OVERLAY",
            MapRenderGpuPhase.Presentation => "PRESENT",
            _ => "UNKNOWN"
        });
    }

    private static long Counter(
        MapRenderFrameTelemetrySnapshot telemetry,
        MapRenderFrameCounter counter)
    {
        int expectedIndex = (int)counter;
        if ((uint)expectedIndex < (uint)telemetry.Counters.Count)
        {
            MapRenderFrameCounterTelemetrySnapshot expected =
                telemetry.Counters[expectedIndex];
            if (expected.Counter == counter)
                return expected.Latest;
        }

        for (int i = 0; i < telemetry.Counters.Count; i++)
        {
            MapRenderFrameCounterTelemetrySnapshot candidate =
                telemetry.Counters[i];
            if (candidate.Counter == counter)
                return candidate.Latest;
        }

        return 0;
    }

    private static void AppendOneDecimal(StringBuilder text, double value)
    {
        if (!double.IsFinite(value) || value < 0)
            value = 0;
        text.Append(value.ToString("0.0", CultureInfo.InvariantCulture));
    }

    private static void AppendCompactCount(StringBuilder text, long value)
    {
        AppendScaledValue(text, Math.Max(0, value), 1000, includeBytes: false);
    }

    private static void AppendBytes(StringBuilder text, long value)
    {
        AppendScaledValue(text, Math.Max(0, value), 1024, includeBytes: true);
    }

    private static void AppendScaledValue(
        StringBuilder text,
        long value,
        int scale,
        bool includeBytes)
    {
        if (value < scale)
        {
            text.Append(value);
            if (includeBytes)
                text.Append('B');
            return;
        }

        double scaled = value;
        char suffix = 'K';
        scaled /= scale;
        if (scaled >= scale)
        {
            scaled /= scale;
            suffix = 'M';
            if (scaled >= scale)
            {
                scaled /= scale;
                suffix = 'G';
            }
        }

        text.Append(scaled.ToString("0.0", CultureInfo.InvariantCulture));
        text.Append(suffix);
        if (includeBytes)
            text.Append('B');
    }

    private void UploadGeometry(string label, int glyphPixelSize)
    {
        List<float> vertices = _geometryVertices;
        vertices.Clear();
        int lineCount = 1;
        int maximumLineLength = 0;
        int lineLength = 0;
        foreach (char character in label)
        {
            if (character == '\n')
            {
                maximumLineLength = Math.Max(maximumLineLength, lineLength);
                lineLength = 0;
                lineCount++;
            }
            else if (character != '\r')
            {
                lineLength++;
            }
        }

        maximumLineLength = Math.Max(maximumLineLength, lineLength);
        int contentWidth = maximumLineLength == 0
            ? 0
            : maximumLineLength * (GlyphColumns + 1) * glyphPixelSize -
              glyphPixelSize;
        int lineAdvance = (GlyphRows + 2) * glyphPixelSize;
        int contentHeight =
            GlyphRows * glyphPixelSize + (lineCount - 1) * lineAdvance;
        int left =
            MarginPixels * glyphPixelSize / BaseGlyphPixelSize;
        int top =
            MarginPixels * glyphPixelSize / BaseGlyphPixelSize;
        int padding =
            PaddingPixels * glyphPixelSize / BaseGlyphPixelSize;
        AppendQuad(
            vertices,
            left,
            top,
            left + contentWidth + 2 * padding,
            top + contentHeight + 2 * padding);
        _backgroundVertexCount = 6;

        int lineLeft = left + padding;
        int x = lineLeft;
        int y = top + padding;
        foreach (char character in label)
        {
            if (character == '\n')
            {
                x = lineLeft;
                y += lineAdvance;
                continue;
            }
            if (character == '\r')
                continue;

            if (Glyphs.TryGetValue(character, out string[]? rows))
            {
                for (int row = 0; row < GlyphRows; row++)
                {
                    string cells = rows[row];
                    int column = 0;
                    while (column < GlyphColumns)
                    {
                        while (column < GlyphColumns && cells[column] != '1')
                            column++;
                        if (column == GlyphColumns)
                            break;

                        int runStart = column;
                        while (column < GlyphColumns && cells[column] == '1')
                            column++;

                        int glyphLeft = x + runStart * glyphPixelSize;
                        int glyphTop = y + row * glyphPixelSize;
                        AppendQuad(
                            vertices,
                            glyphLeft,
                            glyphTop,
                            x + column * glyphPixelSize,
                            glyphTop + glyphPixelSize);
                    }
                }
            }

            x += (GlyphColumns + 1) * glyphPixelSize;
        }

        _textVertexCount = vertices.Count / 2 - _backgroundVertexCount;
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
            ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
            ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
            ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
            ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
            ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
            ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
            ['G'] = ["01111", "10000", "10000", "10111", "10001", "10001", "01111"],
            ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
            ['I'] = ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
            ['J'] = ["00001", "00001", "00001", "00001", "10001", "10001", "01110"],
            ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
            ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
            ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
            ['N'] = ["10001", "11001", "11001", "10101", "10011", "10011", "10001"],
            ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
            ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
            ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
            ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
            ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
            ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
            ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
            ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
            ['W'] = ["10001", "10001", "10001", "10101", "10101", "11011", "10001"],
            ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
            ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
            ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
            ['.'] = ["00000", "00000", "00000", "00000", "00000", "00110", "00110"],
            ['/'] = ["00001", "00010", "00010", "00100", "01000", "01000", "10000"],
            ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
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
