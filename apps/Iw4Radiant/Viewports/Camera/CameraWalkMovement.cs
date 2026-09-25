using System.Diagnostics;
using System.Numerics;
using Avalonia.Input;
using Avalonia.Threading;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraWalkMovement
{
    // Editor simulation pacing, independent of rendering and native PMove timing.
    private const double StepSeconds = 1.0 / 120;
    private const int MaximumSteps = 8;
    private readonly CameraViewport _viewport;
    private readonly CameraNavigation _navigation;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private readonly HashSet<Key> _keys = [];
    private long _lastTick;
    private double _accumulator;
    private bool _jump, _run;

    internal CameraWalkMovement(CameraViewport viewport, CameraNavigation navigation)
    {
        _viewport = viewport;
        _navigation = navigation;
        _timer.Tick += OnTick;
    }

    internal bool IsRunning => _timer.IsEnabled;

    internal void Start()
    {
        if (_timer.IsEnabled || !_viewport.CanWalkSimulate) return;
        _lastTick = Stopwatch.GetTimestamp();
        _accumulator = 0;
        _timer.Start();
        _viewport.WalkActivityChanged();
    }

    internal void KeyDown(KeyEventArgs e)
    {
        _run = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key is not (Key.W or Key.A or Key.S or Key.D or Key.Space)) return;
        if (_keys.Add(e.Key) && e.Key == Key.Space) _jump = true;
        Start();
    }

    internal void KeyUp(KeyEventArgs e)
    {
        _run = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_keys.Remove(e.Key)) e.Handled = true;
        if (e.Key is Key.LeftShift or Key.RightShift) e.Handled = true;
    }

    internal void Stop()
    {
        bool running = _timer.IsEnabled;
        _timer.Stop();
        _keys.Clear();
        _jump = false;
        _run = false;
        _accumulator = 0;
        if (running) _viewport.WalkActivityChanged();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_viewport.CanWalkSimulate)
        {
            Stop();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        _accumulator = Math.Min(_accumulator + Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds,
            StepSeconds * MaximumSteps);
        _lastTick = now;
        Vector3 right = _navigation.Right;
        Vector3 forward = Vector3.Cross(Vector3.UnitZ, right);
        Vector3 direction = right * (Pressed(Key.D) - Pressed(Key.A)) + forward * (Pressed(Key.W) - Pressed(Key.S));
        if (direction.LengthSquared() > 0) direction = Vector3.Normalize(direction);
        while (_accumulator >= StepSeconds)
        {
            bool jump = _jump;
            _jump = false;
            _accumulator -= StepSeconds;
            if (!_viewport.AdvanceWalk(direction, jump, _run, (float)StepSeconds))
            {
                Stop();
                return;
            }
        }
    }

    private int Pressed(Key key) => _keys.Contains(key) ? 1 : 0;
}
