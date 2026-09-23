using IW4.Render.Geometry;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private const int BaseWorldPreflightProgressInterval = 16;
    private const int BaseWorldResourceProgressInterval = 32;

    private Action<string>? _activeLoadProgress;
    private string? _activeLoadTraceContext;
    private Action<string>? _activeProgramDriverTrace;
    private long _loadBatchTraceSequence;
    private long _loadProgramTraceSequence;
    private long _loadTextureTraceSequence;

    private bool LoadProgressEnabled => _activeLoadProgress is not null;

    private LoadProgressScope BeginLoadProgress(
        Action<string>? progress)
    {
        Action<string>? previous = _activeLoadProgress;
        string? previousContext = _activeLoadTraceContext;
        Action<string>? previousProgramTrace =
            _activeProgramDriverTrace;
        _activeLoadProgress = progress;
        _activeLoadTraceContext = null;
        _activeProgramDriverTrace = null;
        return new LoadProgressScope(
            this,
            previous,
            previousContext,
            previousProgramTrace);
    }

    private LoadTraceContextScope BeginLoadTraceContext(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        string? previous = _activeLoadTraceContext;
        _activeLoadTraceContext = previous is null
            ? context
            : $"{previous}; {context}";
        return new LoadTraceContextScope(this, previous);
    }

    private ProgramDriverTraceScope BeginProgramDriverTrace(
        Action<string>? trace)
    {
        Action<string>? previous = _activeProgramDriverTrace;
        _activeProgramDriverTrace = trace;
        return new ProgramDriverTraceScope(this, previous);
    }

    private Action<string>? CreateLoadDetailReporter(string operation)
    {
        if (!LoadProgressEnabled)
            return null;

        return message => ReportLoadDetail($"{operation}; {message}");
    }

    private void ReportLoadProgress(string message)
    {
        Action<string>? progress = _activeLoadProgress;
        if (progress is null)
            return;

        try
        {
            progress(message);
        }
        catch
        {
            // Advisory diagnostics cannot alter renderer initialization.
        }
    }

    private void ReportLoadDetail(string message)
    {
        if (!LoadProgressEnabled)
            return;

        ReportLoadProgress(_activeLoadTraceContext is { } context
            ? $"renderer detail: {message}; context={context}"
            : $"renderer detail: {message}");
    }

    private long NextLoadBatchTraceSequence() =>
        checked(++_loadBatchTraceSequence);

    private long NextLoadProgramTraceSequence() =>
        checked(++_loadProgramTraceSequence);

    private long NextLoadTextureTraceSequence() =>
        checked(++_loadTextureTraceSequence);

    private void ResetLoadTraceSequences()
    {
        _loadBatchTraceSequence = 0;
        _loadProgramTraceSequence = 0;
        _loadTextureTraceSequence = 0;
    }

    private static string DescribeWorldBatchTraceContext(
        MapRenderTexturedBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return
            $"material={QuoteLoadTraceValue(batch.Pass.MaterialName)}; " +
            $"techniqueSet={QuoteLoadTraceValue(batch.Pass.TechniquePass.TechniqueSetName)}; " +
            $"techniqueSlot={batch.Pass.TechniquePass.TechniqueSlot}; " +
            $"passIndex={batch.Pass.TechniquePass.PassIndex}; " +
            $"sceneLight={batch.SceneLightIndex}";
    }

    private static string QuoteLoadTraceValue(string? value)
    {
        if (value is null)
            return "<null>";

        return $"\"{value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)}\"";
    }

    private sealed class LoadProgressScope : IDisposable
    {
        private SilkOpenGlMapRenderer? _owner;
        private readonly Action<string>? _previous;
        private readonly string? _previousContext;
        private readonly Action<string>? _previousProgramTrace;

        internal LoadProgressScope(
            SilkOpenGlMapRenderer owner,
            Action<string>? previous,
            string? previousContext,
            Action<string>? previousProgramTrace)
        {
            _owner = owner;
            _previous = previous;
            _previousContext = previousContext;
            _previousProgramTrace = previousProgramTrace;
        }

        public void Dispose()
        {
            SilkOpenGlMapRenderer? owner = _owner;
            if (owner is null)
                return;

            _owner = null;
            owner._activeLoadProgress = _previous;
            owner._activeLoadTraceContext = _previousContext;
            owner._activeProgramDriverTrace = _previousProgramTrace;
        }
    }

    private readonly struct LoadTraceContextScope : IDisposable
    {
        private readonly SilkOpenGlMapRenderer _owner;
        private readonly string? _previous;

        internal LoadTraceContextScope(
            SilkOpenGlMapRenderer owner,
            string? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose() =>
            _owner._activeLoadTraceContext = _previous;
    }

    private readonly struct ProgramDriverTraceScope : IDisposable
    {
        private readonly SilkOpenGlMapRenderer _owner;
        private readonly Action<string>? _previous;

        internal ProgramDriverTraceScope(
            SilkOpenGlMapRenderer owner,
            Action<string>? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose() =>
            _owner._activeProgramDriverTrace = _previous;
    }
}
