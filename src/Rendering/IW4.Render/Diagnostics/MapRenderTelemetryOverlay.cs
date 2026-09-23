using System.Globalization;
using System.Text;

namespace IW4.Render.Diagnostics;

/// <summary>
/// Backend-neutral telemetry text formatting primitives shared by native map
/// presentation overlays.
/// </summary>
internal static class MapRenderTelemetryOverlayText
{
    internal static long GpuSampleAgeFrames(
        MapRenderFrameTelemetrySnapshot telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        if (telemetry.LastPresentedFrameIndex is long presentedFrameIndex &&
            telemetry.LastGpuFrameIndex is long gpuFrameIndex &&
            presentedFrameIndex >= gpuFrameIndex)
        {
            return presentedFrameIndex - gpuFrameIndex;
        }

        return Math.Max(0, telemetry.LastGpuReadbackDelayFrames);
    }

    internal static void AppendCpuAndGpuTiming(
        StringBuilder text,
        MapRenderFrameTelemetrySnapshot telemetry)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(telemetry);
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
    }

    internal static void AppendResourceTelemetry(
        StringBuilder text,
        MapRenderFrameTelemetrySnapshot telemetry)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(telemetry);
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
    }

    internal static void AppendTopCpuPhases(
        StringBuilder text,
        IReadOnlyList<MapRenderCpuPhaseTelemetrySnapshot> phases)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(phases);
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

    internal static void AppendTopGpuPhases(
        StringBuilder text,
        IReadOnlyList<MapRenderGpuPhaseTelemetrySnapshot> phases)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(phases);
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

    internal static long Counter(
        MapRenderFrameTelemetrySnapshot telemetry,
        MapRenderFrameCounter counter)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
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

    internal static double LatestCpuPhase(
        MapRenderFrameTelemetrySnapshot telemetry,
        MapRenderCpuPhase phase)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        for (int i = 0; i < telemetry.CpuPhases.Count; i++)
        {
            MapRenderCpuPhaseTelemetrySnapshot value = telemetry.CpuPhases[i];
            if (value.Phase == phase)
                return value.Milliseconds.Latest;
        }

        return 0.0;
    }

    internal static void AppendOneDecimal(StringBuilder text, double value)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!double.IsFinite(value) || value < 0)
            value = 0;
        text.Append(value.ToString("0.0", CultureInfo.InvariantCulture));
    }

    internal static void AppendCompactCount(StringBuilder text, long value)
    {
        ArgumentNullException.ThrowIfNull(text);
        AppendScaledValue(text, Math.Max(0, value), 1000, includeBytes: false);
    }

    internal static void AppendBytes(StringBuilder text, long value)
    {
        ArgumentNullException.ThrowIfNull(text);
        AppendScaledValue(text, Math.Max(0, value), 1024, includeBytes: true);
    }

    private static void AppendCpuPhase(StringBuilder text, MapRenderCpuPhase phase)
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

    private static void AppendGpuPhase(StringBuilder text, MapRenderGpuPhase phase)
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
}

/// <summary>
/// Generates upper-left pixel-font HUD geometry independently of a graphics
/// API. Vertices are pairs of physical framebuffer pixel coordinates.
/// </summary>
internal static class MapRenderTelemetryOverlayGeometry
{
    private const int GlyphColumns = 5;
    private const int GlyphRows = 7;
    private const int BaseGlyphPixelSize = 2;
    private const int MarginPixels = 14;
    private const int PaddingPixels = 6;

    internal static int GetGlyphPixelSize(float renderScaling) =>
        Math.Max(
            BaseGlyphPixelSize,
            (int)MathF.Round(
                BaseGlyphPixelSize * Math.Max(1f, renderScaling)));

    internal static (int BackgroundVertexCount, int TextVertexCount) Write(
        string label,
        int glyphPixelSize,
        List<float> vertices)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(vertices);
        if (glyphPixelSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(glyphPixelSize));

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
        int left = MarginPixels * glyphPixelSize / BaseGlyphPixelSize;
        int top = MarginPixels * glyphPixelSize / BaseGlyphPixelSize;
        int padding = PaddingPixels * glyphPixelSize / BaseGlyphPixelSize;
        AppendQuad(
            vertices,
            left,
            top,
            left + contentWidth + 2 * padding,
            top + contentHeight + 2 * padding);
        const int backgroundVertexCount = 6;

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

        return (backgroundVertexCount, vertices.Count / 2 - backgroundVertexCount);
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
}
