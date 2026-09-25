using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.Viewports.Camera;

namespace Iw4Radiant.Views;

internal sealed class FxPreviewWindow : Window
{
    private readonly CameraViewport _camera = new();
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _pause = new() { Content = "Pause" };
    private readonly Button _restart = new() { Content = "Replay" };
    private readonly CheckBox _repeat = new() { Content = "Repeat", VerticalAlignment = VerticalAlignment.Center };
    private bool _closed;

    internal FxPreviewWindow(string sourceDirectory, FxSoundAsset asset,
        Func<string, MaterialSource?> resolveMaterial)
    {
        if (asset.IsSound) throw new ArgumentException("FX preview requires an effect asset.", nameof(asset));
        Title = $"{asset.DisplayName} — FX preview";
        Width = 720;
        Height = 560;
        MinWidth = 420;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _camera.Session = new EditorSession();
        _camera.ResolveMaterial = resolveMaterial;
        _camera.CanAcceptModelDrop = () => false;
        _camera.PreviewLighting = false;
        _camera.FxPreviewStatusChanged += QueueStatusUpdate;
        _camera.RendererStatusChanged += (_, _) => QueueStatusUpdate();

        var resetView = new Button { Content = "Reset view" };
        _pause.Click += (_, _) =>
        {
            _camera.SetFxPreviewPaused(!_camera.IsFxPreviewPaused);
            UpdateStatus();
        };
        _restart.Click += (_, _) =>
        {
            bool finished = _camera.IsFxPreviewFinished;
            _camera.RestartFxPreview();
            if (finished) _camera.SetFxPreviewPaused(false);
            UpdateStatus();
        };
        _repeat.IsCheckedChanged += (_, _) =>
        {
            bool finished = _camera.IsFxPreviewFinished;
            _camera.SetFxPreviewRepeat(_repeat.IsChecked == true);
            if (finished && _repeat.IsChecked == true)
            {
                _camera.RestartFxPreview();
                _camera.SetFxPreviewPaused(false);
            }
            UpdateStatus();
        };
        ToolTip.SetTip(_repeat, "Play this preview again after it finishes. Map playback is unchanged.");
        resetView.Click += (_, _) => FrameEffect();
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 10),
            Children = { _pause, _restart, _repeat, resetView }
        };
        var footer = new Border
        {
            Padding = new Thickness(12, 8),
            Child = new StackPanel
            {
                Spacing = 3,
                Children = { _status, new TextBlock
                {
                    Text = "Right-drag to orbit · scroll to zoom",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Opacity = 0.7
                } }
            }
        };
        var layout = new DockPanel();
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(controls);
        layout.Children.Add(footer);
        layout.Children.Add(_camera);
        Content = layout;

        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _closed = true;
            _camera.StopFxPreview();
            _camera.ResolveMaterial = null;
            _camera.Session = null;
        };
        Opened += (_, _) =>
        {
            FrameEffect();
            _camera.RestartFxPreview();
        };

        _camera.StartFxPreview(sourceDirectory, asset.Name, Vector3.Zero, Matrix4x4.Identity);
        UpdateStatus();
    }

    private void FrameEffect()
    {
        var bounds = _camera.FxPreviewBounds ?? (-new Vector3(128), new Vector3(128));
        _camera.FrameBounds(bounds.Item1, bounds.Item2);
    }

    private void QueueStatusUpdate()
    {
        if (!_closed) Dispatcher.UIThread.Post(UpdateStatus);
    }

    private void UpdateStatus()
    {
        if (_closed) return;
        bool active = _camera.HasActiveFxPreview;
        bool finished = _camera.IsFxPreviewFinished;
        _pause.IsEnabled = active && !finished;
        _restart.IsEnabled = active;
        _repeat.IsVisible = active && !_camera.IsFxPreviewLooping;
        _pause.Content = _camera.IsFxPreviewPaused ? "Resume" : "Pause";
        _restart.Content = _camera.IsFxPreviewLooping ? "Restart" : "Replay";
        string? notice = _camera.FxPreviewNotice;
        string? cameraError = _camera.RendererError;
        if (_camera.HasRenderingError)
        {
            _status.Text = "Camera preview unavailable";
            ToolTip.SetTip(_status, cameraError);
        }
        else if (cameraError is not null &&
            cameraError.StartsWith("Camera is waiting for OpenGL", StringComparison.Ordinal))
        {
            _status.Text = "Preparing camera preview…";
            ToolTip.SetTip(_status, cameraError);
        }
        else if (!active)
        {
            _status.Text = "This FX cannot be animated in the editor preview.";
            ToolTip.SetTip(_status, notice);
        }
        else
        {
            _status.Text = finished ? "Finished · Replay to watch again" :
                _camera.IsFxPreviewPaused ? "Paused" : _camera.IsFxPreviewLooping ? "Looping" :
                _camera.IsFxPreviewRepeating ? "Repeating" : "Playing once";
            if (!string.IsNullOrWhiteSpace(notice)) _status.Text += " · Preview limited";
            ToolTip.SetTip(_status, notice);
        }
    }
}
