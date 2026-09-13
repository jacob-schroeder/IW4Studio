using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;

namespace Iw4Radiant.Viewports.Camera;

internal sealed class CameraFlyMovement
{
    private readonly CameraViewport _viewport;
    private readonly CameraNavigation _navigation;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly HashSet<Key> _keys = [];
    private long _lastTick;
    private bool _fast;

    internal CameraFlyMovement(CameraViewport viewport, CameraNavigation navigation)
    {
        _viewport = viewport;
        _navigation = navigation;
        _timer.Tick += OnTick;
    }

    internal bool KeyDown(KeyEventArgs e, bool canMove)
    {
        _fast = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key is not (Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)) return false;
        e.Handled = true;
        if (!canMove)
        {
            Stop();
            return true;
        }
        _keys.Add(e.Key);
        if (!_timer.IsEnabled)
        {
            _lastTick = Stopwatch.GetTimestamp();
            _timer.Start();
        }
        return true;
    }

    internal void KeyUp(KeyEventArgs e)
    {
        _fast = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!_keys.Remove(e.Key)) return;
        if (_keys.Count == 0) _timer.Stop();
        e.Handled = true;
    }

    internal void Stop()
    {
        _timer.Stop();
        _keys.Clear();
        _fast = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_viewport.FlyMode || !_viewport.IsFocused || !_viewport.IsEffectivelyVisible || !_viewport.IsEffectivelyEnabled)
        {
            Stop();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        float elapsed = (float)Math.Min(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds, 0.05);
        _lastTick = now;
        float right = Pressed(Key.D) - Pressed(Key.A);
        float forward = Pressed(Key.W) - Pressed(Key.S);
        float up = Pressed(Key.E) - Pressed(Key.Q);
        float length = MathF.Sqrt(right * right + forward * forward + up * up);
        if (length == 0) return;
        float step = elapsed * (_fast ? 1024 : 256) / length;
        _navigation.MoveLocal(right * step, forward * step, up * step);
        _viewport.RequestNextFrameRendering();
    }

    private int Pressed(Key key) => _keys.Contains(key) ? 1 : 0;
}
