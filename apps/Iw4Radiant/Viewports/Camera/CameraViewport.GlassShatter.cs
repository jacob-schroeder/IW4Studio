using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport
{
    private const double ShatterStepSeconds = 1.0 / 120;
    private readonly DispatcherTimer _shatterTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private CameraGlassShatterSimulation? _shatterSimulation;
    private CancellationTokenSource? _shatterPreparation;
    private int _shatterGeneration;
    private long _shatterLastTick;
    private double _shatterAccumulator;
    private MapDocument? _shatterDocument;
    private long _shatterRevision;
    internal bool GlassShatterActive => _shatterPreparation is not null || _shatterSimulation is not null;
    internal bool GlassShatterPreparing => _shatterPreparation is not null;
    internal event Action? GlassShatterChanged;
    internal event Action<string?>? GlassShatterErrorChanged;

    internal async Task StartGlassShatterAsync(Point position)
    {
        if (_session is not { } session || CompiledPreview is not null || CanWalk?.Invoke() == false) return;
        StopGlassShatter();
        if (session.HasPlacement || FoliagePaintingEnabled || MistPaintingEnabled)
        {
            InteractionStatusChanged?.Invoke("Finish asset placement or stop painting before Shatter.");
            return;
        }
        if (!CameraGlassShatterSimulation.CanStart(session))
        {
            InteractionStatusChanged?.Invoke("Select whole, visible breakable glass world brushes for Shatter.");
            return;
        }
        StopPhysicsPlacement();
        StopWalk();
        FinishGesture(cancel: true);
        _objectMenu?.Close();
        var preparation = new CancellationTokenSource();
        _shatterPreparation = preparation;
        int generation = ++_shatterGeneration;
        MapDocument document = session.Document;
        long revision = session.ContentRevision;
        string? filePath = session.FilePath;
        object[] selected = session.Selection.Items.ToArray();
        GlassShatterErrorChanged?.Invoke(null);
        GlassShatterChanged?.Invoke();
        InteractionStatusChanged?.Invoke("Preparing Jolt glass shatter preview…");
        CameraGlassShatterSimulation? simulation = null;
        try
        {
            if (ResolveMaterial is not { } resolveMaterial)
                throw new InvalidOperationException("Load the map's material library before Shatter.");
            var (rayOrigin, rayDirection) = _navigation.PickRay(
                (float)(position.X / Math.Max(1, Bounds.Width) * 2 - 1),
                (float)(1 - position.Y / Math.Max(1, Bounds.Height) * 2),
                (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height)));
            simulation = await CameraGlassShatterSimulation.CreateAsync(session, resolveMaterial,
                rayOrigin, rayDirection, preparation.Token);
            if (!Current()) return;
            _shatterPreparation = null;
            _shatterSimulation = simulation;
            simulation = null;
            _shatterDocument = document;
            _shatterRevision = revision;
            session.Scene.SetGlassShatterPreview(_shatterSimulation.Sources, _shatterSimulation.Fragments);
            _renderer.SetPhysicsPlacementTransforms(_shatterSimulation.DrawTransforms);
            _renderer.RefreshScene();
            RequestNextFrameRendering();
            GlassShatterChanged?.Invoke();
            _shatterLastTick = Stopwatch.GetTimestamp();
            _shatterAccumulator = 0;
            _shatterTimer.Start();
            InteractionStatusChanged?.Invoke($"Jolt shatter preview: {_shatterSimulation.ShardCount} shards. Restore glass or Escape returns the intact glass; the map is unchanged.");
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
            InvalidOperationException or NotSupportedException or OverflowException or DllNotFoundException or
            EntryPointNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            if (Current())
            {
                GlassShatterErrorChanged?.Invoke($"Shatter preview failed for {filePath ?? "Untitled.map"}.{Environment.NewLine}{Environment.NewLine}{exception}");
                InteractionStatusChanged?.Invoke($"Shatter preview unavailable: {exception.Message} See Console Output for details.");
            }
        }
        finally
        {
            if (ReferenceEquals(_shatterPreparation, preparation))
            {
                _shatterPreparation = null;
                GlassShatterChanged?.Invoke();
            }
            if (simulation is not null) await Task.Run(simulation.Dispose);
            preparation.Dispose();
        }

        bool Current() => ReferenceEquals(_shatterPreparation, preparation) &&
            generation == _shatterGeneration && !preparation.IsCancellationRequested &&
            ReferenceEquals(_session, session) && ReferenceEquals(session.Document, document) &&
            session.ContentRevision == revision && session.FilePath == filePath &&
            session.Selection.Count == selected.Length && selected.All(session.Selection.Contains);
    }

    internal void StopGlassShatter()
    {
        if (_shatterPreparation is null && _shatterSimulation is null) return;
        if (_shatterPreparation is { } preparation)
        {
            ++_shatterGeneration;
            _shatterPreparation = null;
            preparation.Cancel();
        }
        _shatterTimer.Stop();
        _shatterAccumulator = 0;
        _shatterDocument = null;
        if (_shatterSimulation is not { } simulation)
        {
            GlassShatterChanged?.Invoke();
            return;
        }
        _shatterSimulation = null;
        _renderer.SetPhysicsPlacementTransforms(null);
        _session?.Scene.SetGlassShatterPreview(null);
        simulation.Dispose();
        _renderer.RefreshScene();
        RequestNextFrameRendering();
        GlassShatterChanged?.Invoke();
        InteractionStatusChanged?.Invoke("Intact glass restored. The map is unchanged.");
    }

    private void OnGlassShatterTick(object? sender, EventArgs e)
    {
        if (_session is not { } session || !ReferenceEquals(session.Document, _shatterDocument) ||
            session.ContentRevision != _shatterRevision)
        {
            StopGlassShatter();
            return;
        }
        if (_shatterSimulation is not { } simulation || !IsEffectivelyVisible ||
            !IsEffectivelyEnabled || CanWalk?.Invoke() == false)
        {
            _shatterTimer.Stop();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        _shatterAccumulator = Math.Min(_shatterAccumulator +
            Stopwatch.GetElapsedTime(_shatterLastTick, now).TotalSeconds, ShatterStepSeconds * 8);
        _shatterLastTick = now;
        try
        {
            while (_shatterAccumulator >= ShatterStepSeconds)
            {
                _shatterAccumulator -= ShatterStepSeconds;
                simulation.Step((float)ShatterStepSeconds);
            }
            if (simulation.CapturePoses()) RequestNextFrameRendering();
            if (simulation.AllSleeping) _shatterTimer.Stop();
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException or
            TypeInitializationException)
        {
            _shatterTimer.Stop();
            GlassShatterErrorChanged?.Invoke($"Shatter simulation failed.{Environment.NewLine}{Environment.NewLine}{exception}");
            InteractionStatusChanged?.Invoke($"Shatter preview stopped: {exception.Message} See Console Output for details.");
        }
    }
}
