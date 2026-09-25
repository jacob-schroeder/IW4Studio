using System.Numerics;
using Avalonia.Input;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Camera;

public sealed partial class CameraViewport
{
    private readonly CameraWalkMovement _walkMovement;
    private CameraWalkSimulation? _walkSimulation;
    private (Vector3 Target, float Yaw, float Pitch, float Distance) _editorPose, _walkEntryPose;
    private Vector3 _walkEntryFeet;
    private bool _walkFaulted;
    private string _walkLocation = "";

    internal bool WalkMode => _walkSimulation is not null;
    internal bool WalkPaused => !_walkMovement.IsRunning;
    internal bool WalkNeedsReset => _walkFaulted;
    internal static string WalkProfile => CameraWalkSimulation.ProfileDescription;
    internal event Action<string?>? WalkErrorChanged;
    internal Func<bool>? CanWalk { get; set; }
    internal bool CanWalkSimulate => WalkMode && !_walkFaulted && IsFocused && IsEffectivelyVisible &&
        IsEffectivelyEnabled && CanWalk?.Invoke() != false;

    internal void StartWalk()
    {
        if (WalkMode || _session is not { } session || CompiledPreview is not null || CanWalk?.Invoke() == false) return;
        if (session.HasPlacement || FoliagePaintingEnabled)
        {
            InteractionStatusChanged?.Invoke("Finish asset placement or stop Painter before entering Walk.");
            NavigationModeChanged?.Invoke();
            return;
        }
        FinishGesture(cancel: true);
        _objectMenu?.Close();
        WalkErrorChanged?.Invoke(null);
        CameraWalkSimulation? simulation = null;
        try
        {
            if (ResolveMaterial is not { } resolveMaterial)
                throw new InvalidOperationException("Load the map's material library before entering Walk.");
            simulation = CameraWalkSimulation.Create(session, resolveMaterial);
            bool spawned = false;
            float? heading = null;
            int spawnCount = 0;
            string? firstSpawnError = null;
            foreach (MapEntity entity in session.Document.Entities.Where(entity => entity.ClassName is
                         "info_player_start" or "mp_dm_spawn" or "mp_tdm_spawn" or
                         "mp_tdm_spawn_allies_start" or "mp_tdm_spawn_axis_start")
                         .OrderByDescending(entity => ReferenceEquals(entity, session.Selection.Active)))
            {
                spawnCount++;
                if (!entity.TryGetOrigin(out Vector3 feet))
                {
                    firstSpawnError ??= $"{entity.ClassName} has a missing or invalid origin.";
                    continue;
                }
                float yaw;
                try { yaw = EntityOrientation.Read(entity).Y; }
                catch (ArgumentException exception)
                {
                    firstSpawnError ??= $"{entity.ClassName}: {exception.Message}";
                    continue;
                }
                if (!simulation.TrySpawn(feet, out string spawnError))
                {
                    firstSpawnError ??= FormattableString.Invariant(
                        $"{entity.ClassName} at ({feet.X:G9}, {feet.Y:G9}, {feet.Z:G9}): {spawnError}");
                    continue;
                }
                _walkEntryFeet = feet;
                heading = yaw;
                _walkLocation = entity.ClassName;
                spawned = true;
                break;
            }
            if (!spawned)
            {
                Vector3 feet = Eye - Vector3.UnitZ * CameraWalkSimulation.EyeHeight;
                if (!simulation.TrySpawn(feet, out string error))
                {
                    string spawnSummary = spawnCount == 0
                        ? "No supported player spawn entities were found in the open source map."
                        : $"Found {spawnCount} player spawn entities, but Walk rejected every entry position.{Environment.NewLine}First rejected spawn: {firstSpawnError}";
                    throw new InvalidOperationException($"{spawnSummary}{Environment.NewLine}Camera entry also failed: {error}");
                }
                _walkEntryFeet = feet;
                _walkLocation = "camera position (no usable player spawn)";
            }
            _editorPose = _navigation.CapturePose();
            _navigation.EnterPlayerView(simulation.Eye, heading);
            _walkEntryPose = _navigation.CapturePose();
            _walkSimulation = simulation;
            simulation = null;
            _flyMode = false;
            _walkFaulted = false;
            FoliageBrushChanged?.Invoke(null);
            Focus();
            _walkMovement.Start();
            _ = LoadWalkPlayerAsync();
            NavigationModeChanged?.Invoke();
            NavigationChanged?.Invoke();
            RequestNextFrameRendering();
            InteractionStatusChanged?.Invoke($"Walk started at {_walkLocation}. Approximate standing traversal; Space jumps, R resets, Escape restores the editor camera.");
        }
        catch (Exception exception) when (IsWalkError(exception))
        {
            WalkErrorChanged?.Invoke($"Walk entry failed for {session.FilePath ?? "Untitled.map"}.{Environment.NewLine}{Environment.NewLine}{exception}");
            InteractionStatusChanged?.Invoke("Walk unavailable. The full error is in Console Output; select the text to copy it.");
            NavigationModeChanged?.Invoke();
        }
        finally { simulation?.Dispose(); }
    }

    internal void StopWalk()
    {
        if (_walkSimulation is not { } simulation) return;
        _walkSimulation = null;
        ClearWalkPlayer();
        _walkMovement.Stop();
        FinishPointerGesture(cancel: true);
        simulation.Dispose();
        _walkFaulted = false;
        _navigation.RestorePose(_editorPose);
        NavigationModeChanged?.Invoke();
        NavigationChanged?.Invoke();
        RequestNextFrameRendering();
    }

    internal void ResetWalk()
    {
        if (_walkSimulation is not { } simulation || CanWalk?.Invoke() == false) return;
        _walkMovement.Stop();
        try
        {
            if (!simulation.TrySpawn(_walkEntryFeet, out string error))
                throw new InvalidOperationException(error);
            _walkFaulted = false;
            _walkPlayerSeconds = 0;
            _navigation.RestorePose(_walkEntryPose);
            _navigation.SetEye(simulation.Eye);
            Focus();
            _walkMovement.Start();
            NavigationChanged?.Invoke();
            RequestNextFrameRendering();
            InteractionStatusChanged?.Invoke($"Walk reset to {_walkLocation}.");
        }
        catch (Exception exception) when (IsWalkError(exception)) { WalkFailed(exception); }
    }

    private bool HandleWalkKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            StopWalk();
            InteractionStatusChanged?.Invoke("Walk ended. Editor camera restored.");
        }
        else if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) != 0)
            _walkMovement.Stop();
        else if (e.Key == Key.R) ResetWalk();
        else if (e.Key == Key.Tab) return false;
        else _walkMovement.KeyDown(e);
        // Space and the editor's tool keys must never edit the map during Walk.
        e.Handled = true;
        return true;
    }

    internal bool AdvanceWalk(Vector3 direction, bool jump, float seconds)
    {
        if (_walkSimulation is not { } simulation) return false;
        try
        {
            simulation.Step(direction, jump, seconds);
            _walkPlayerSeconds += seconds;
            _navigation.SetEye(simulation.Eye);
            NavigationChanged?.Invoke();
            RequestNextFrameRendering();
            return true;
        }
        catch (Exception exception) when (IsWalkError(exception))
        {
            WalkFailed(exception);
            return false;
        }
    }

    private void WalkFailed(Exception exception)
    {
        _walkFaulted = true;
        _walkMovement.Stop();
        WalkErrorChanged?.Invoke($"Walk movement failed.{Environment.NewLine}{Environment.NewLine}{exception}");
        InteractionStatusChanged?.Invoke("Walk paused. Use Reset or Escape; the full error is in Console Output.");
        NavigationModeChanged?.Invoke();
    }

    internal void WalkActivityChanged() => NavigationModeChanged?.Invoke();

    private static bool IsWalkError(Exception exception) => IsEditError(exception) ||
        exception is NotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or TypeInitializationException;
}
