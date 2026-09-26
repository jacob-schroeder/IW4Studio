using System.Diagnostics;
using System.Numerics;
using Avalonia.Threading;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Rendering;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport
{
    private const double PlacementStepSeconds = 1.0 / 120;
    private const int MaximumPlacementSteps = 8;
    private readonly DispatcherTimer _placementTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private CameraPrefabPlacementSimulation? _placementSimulation;
    private CancellationTokenSource? _placementPreparation;
    private int _placementGeneration, _preparingObjectCount;
    private long _placementLastTick;
    private double _placementAccumulator;
    private bool _placementFaulted;
    private bool _placementHasStarted;
    private bool _placementPreviewDepth;

    internal bool PhysicsPlacementActive => _placementSimulation is not null || _placementPreparation is not null;
    internal bool PhysicsPlacementPreparing => _placementPreparation is not null;
    internal bool PhysicsPlacementRunning => _placementTimer.IsEnabled;
    internal bool PhysicsPlacementNeedsReset => _placementFaulted;
    internal int PhysicsPlacementObjectCount => _placementSimulation?.ObjectCount ?? _preparingObjectCount;
    internal bool PhysicsPlacementHasStarted => _placementHasStarted;
    internal bool PhysicsPlacementSettled => _placementHasStarted && !_placementFaulted &&
        _placementSimulation?.AllSleeping == true;
    internal bool PhysicsPlacementHasChanges => _placementSimulation?.HasChanges == true;

    private (Vector3 Min, Vector3 Max)? PhysicsPlacementSelectionBounds()
    {
        if (_placementSimulation is not { } simulation || _session is not { } session) return null;
        (Vector3 Min, Vector3 Max)? combined = null;
        foreach (var (source, transform) in simulation.DrawTransforms)
        {
            if (session.Scene.Bounds(source) is not { } original) continue;
            var posed = SceneRenderer.TransformBounds(original, transform);
            combined = combined is { } bounds
                ? (Vector3.Min(bounds.Min, posed.Min), Vector3.Max(bounds.Max, posed.Max)) : posed;
        }
        foreach (var (clip, owner) in simulation.ClipOwners)
        {
            if (!simulation.DrawTransforms.TryGetValue(owner, out Matrix4x4 transform)) continue;
            var posed = SceneRenderer.TransformBounds(clip.GetBounds(), transform);
            combined = combined is { } bounds
                ? (Vector3.Min(bounds.Min, posed.Min), Vector3.Max(bounds.Max, posed.Max)) : posed;
        }
        return combined;
    }
    internal event Action? PhysicsPlacementChanged;

    internal async Task StartPhysicsPlacementAsync()
    {
        if (PhysicsPlacementActive || _session is not { } session || CompiledPreview is not null || CanWalk?.Invoke() == false) return;
        if (session.HasPlacement || FoliagePaintingEnabled)
        {
            InteractionStatusChanged?.Invoke("Finish asset placement or stop Painter before entering Physics placement.");
            return;
        }
        StopWalk();
        FinishGesture(cancel: true);
        _objectMenu?.Close();
        var preparation = new CancellationTokenSource();
        _placementPreparation = preparation;
        int generation = ++_placementGeneration;
        MapDocument document = session.Document;
        long revision = session.ContentRevision;
        string? filePath = session.FilePath;
        object[] selected = session.Selection.Items.ToArray();
        _preparingObjectCount = selected.OfType<MapEntity>().Count();
        PhysicsPlacementChanged?.Invoke();
        InteractionStatusChanged?.Invoke("Preparing Physics placement collision… Cancel is available while the map is prepared.");
        CameraPrefabPlacementSimulation? simulation = null;
        try
        {
            if (ResolveMaterial is not { } resolveMaterial)
                throw new InvalidOperationException("Load the map's material library before Physics placement.");
            simulation = await CameraPrefabPlacementSimulation.CreateAsync(session, resolveMaterial,
                session.Scene.ResolveModel, preparation.Token);
            if (!Current())
            {
                if (!preparation.IsCancellationRequested)
                    InteractionStatusChanged?.Invoke("Physics placement preparation ended because the map or selection changed.");
                return;
            }
            _placementPreparation = null;
            _preparingObjectCount = 0;
            session.BeginTransformPreview();
            _placementPreviewDepth = true;
            _placementSimulation = simulation;
            CameraPrefabPlacementSimulation published = simulation;
            simulation = null;
            _placementFaulted = false;
            _placementHasStarted = false;
            session.Scene.SetPhysicsPlacementPreview(_placementSimulation.Preview, _placementSimulation.ClipOwners);
            _renderer.SetPhysicsPlacementTransforms(_placementSimulation.DrawTransforms);
            _renderer.RefreshScene();
            RequestNextFrameRendering();
            PhysicsPlacementChanged?.Invoke();
            InteractionStatusChanged?.Invoke("Physics placement ready. Reset restores starting positions; Keep placement writes one undoable edit; Cancel discards the preview.");
            if (generation == _placementGeneration &&
                ReferenceEquals(_placementSimulation, published)) ResumePhysicsPlacement();
        }
        catch (OperationCanceledException) when (preparation.IsCancellationRequested) { }
        catch (Exception exception) when (IsPlacementError(exception))
        {
            if (Current() || _placementSimulation is not null &&
                generation == _placementGeneration && ReferenceEquals(_session, session))
            {
                if (_placementSimulation is not null) StopPhysicsPlacement();
                InteractionStatusChanged?.Invoke($"Physics placement unavailable: {exception.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_placementPreparation, preparation))
            {
                _placementPreparation = null;
                _preparingObjectCount = 0;
                PhysicsPlacementChanged?.Invoke();
            }
            if (simulation is not null) await Task.Run(simulation.Dispose);
            preparation.Dispose();
        }

        bool Current() => ReferenceEquals(_placementPreparation, preparation) &&
            generation == _placementGeneration && !preparation.IsCancellationRequested &&
            ReferenceEquals(_session, session) && ReferenceEquals(session.Document, document) &&
            session.ContentRevision == revision && session.FilePath == filePath &&
            session.Selection.Count == selected.Length && selected.All(session.Selection.Contains);
    }

    internal void ResumePhysicsPlacement()
    {
        if (_placementSimulation is null || _placementTimer.IsEnabled || _placementFaulted ||
            !IsEffectivelyVisible || !IsEffectivelyEnabled || CanWalk?.Invoke() == false) return;
        _placementLastTick = Stopwatch.GetTimestamp();
        _placementAccumulator = 0;
        _placementHasStarted = true;
        _placementTimer.Start();
        PhysicsPlacementChanged?.Invoke();
        InteractionStatusChanged?.Invoke("Physics placement running. Pause to inspect, Reset to retry, or Keep placement.");
    }

    internal void PausePhysicsPlacement()
    {
        if (!_placementTimer.IsEnabled) return;
        _placementTimer.Stop();
        _placementAccumulator = 0;
        PhysicsPlacementChanged?.Invoke();
        InteractionStatusChanged?.Invoke("Physics placement paused.");
    }

    internal void ResetPhysicsPlacement()
    {
        if (_placementSimulation is not { } simulation) return;
        PausePhysicsPlacement();
        try
        {
            if (simulation.Reset()) RequestNextFrameRendering();
            _placementFaulted = false;
            _placementHasStarted = false;
            PhysicsPlacementChanged?.Invoke();
            InteractionStatusChanged?.Invoke("Physics placement reset to the selected instances' entry poses.");
        }
        catch (Exception exception) when (IsPlacementError(exception)) { PhysicsPlacementFailed(exception); }
    }

    internal void ApplyPhysicsPlacement()
    {
        if (_placementSimulation is not { } simulation || _session is not { } session || _placementFaulted) return;
        _placementTimer.Stop();
        _placementSimulation = null;
        _renderer.SetPhysicsPlacementTransforms(null);
        session.Scene.SetPhysicsPlacementPreview(null);
        EndPhysicsPlacementPreview(session);
        try
        {
            bool changed = simulation.Apply(session);
            InteractionStatusChanged?.Invoke(changed ? "Physics placement kept as one undoable edit." :
                "Physics placement ended without a transform change.");
        }
        catch (Exception exception) when (IsPlacementError(exception))
        {
            InteractionStatusChanged?.Invoke($"Physics placement could not apply: {exception.Message}");
        }
        finally
        {
            simulation.Dispose();
            _placementFaulted = false;
            _renderer.RefreshScene();
            RequestNextFrameRendering();
            PhysicsPlacementChanged?.Invoke();
        }
    }

    internal void StopPhysicsPlacement()
    {
        if (_placementPreparation is { } preparation)
        {
            ++_placementGeneration;
            _placementPreparation = null;
            _preparingObjectCount = 0;
            preparation.Cancel();
            PhysicsPlacementChanged?.Invoke();
            InteractionStatusChanged?.Invoke("Physics placement preparation cancelled. The map is unchanged.");
            return;
        }
        if (_placementSimulation is not { } simulation) return;
        _placementTimer.Stop();
        _placementAccumulator = 0;
        _placementSimulation = null;
        _renderer.SetPhysicsPlacementTransforms(null);
        if (_session is { } session)
        {
            session.Scene.SetPhysicsPlacementPreview(null);
            EndPhysicsPlacementPreview(session);
        }
        simulation.Dispose();
        _placementFaulted = false;
        _placementHasStarted = false;
        _renderer.RefreshScene();
        RequestNextFrameRendering();
        PhysicsPlacementChanged?.Invoke();
        InteractionStatusChanged?.Invoke("Physics placement cancelled. The map is unchanged.");
    }

    private void OnPhysicsPlacementTick(object? sender, EventArgs e)
    {
        if (_placementSimulation is not { } simulation || !IsEffectivelyVisible || !IsEffectivelyEnabled ||
            CanWalk?.Invoke() == false)
        {
            PausePhysicsPlacement();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        _placementAccumulator = Math.Min(_placementAccumulator + Stopwatch.GetElapsedTime(_placementLastTick, now).TotalSeconds,
            PlacementStepSeconds * MaximumPlacementSteps);
        _placementLastTick = now;
        try
        {
            bool hadChanges = simulation.HasChanges;
            while (_placementAccumulator >= PlacementStepSeconds)
            {
                _placementAccumulator -= PlacementStepSeconds;
                simulation.Step((float)PlacementStepSeconds);
            }
            if (simulation.CapturePoses()) RequestNextFrameRendering();
            if (hadChanges != simulation.HasChanges) PhysicsPlacementChanged?.Invoke();
            if (simulation.AllSleeping) PausePhysicsPlacement();
        }
        catch (Exception exception) when (IsPlacementError(exception)) { PhysicsPlacementFailed(exception); }
    }

    private void EndPhysicsPlacementPreview(EditorSession session)
    {
        if (!_placementPreviewDepth) return;
        _placementPreviewDepth = false;
        session.EndTransformPreview();
    }

    private void PhysicsPlacementFailed(Exception exception)
    {
        PausePhysicsPlacement();
        _placementFaulted = true;
        PhysicsPlacementChanged?.Invoke();
        InteractionStatusChanged?.Invoke($"Physics placement paused after an error: {exception.Message} Reset or Cancel to recover.");
    }

    private static bool IsPlacementError(Exception exception) => IsWalkError(exception) ||
        exception is OverflowException or TypeInitializationException;
}
