using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IW4.Studio.Desktop.Editors.Sound;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.SndCurve;

public sealed partial class SndCurveEditorView : UserControl
{
    private SndCurveEditorViewModel? _editor;
    private SoundPreviewPlayer? _tone;
    private readonly DispatcherTimer _stopTimer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private bool _refreshing;
    private int _pickerCount = -1;
    private string? _audioError;
    private string _xDisplay = "", _yDisplay = "";

    public SndCurveEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindEditor();
        Panes.SizeChanged += (_, e) => ArrangePanes(e.NewSize.Width);
        Graph.EditRequested += (_, edit) => _editor?.HandleGraphEdit(edit);
        Graph.PropertyChanged += (_, e) =>
        {
            if (!_refreshing && e.Property == SndCurveGraph.SelectedIndexProperty) _editor?.SelectPoint(Graph.SelectedIndex);
        };
        Listener.PropertyChanged += (_, e) =>
        {
            if (!_refreshing && e.Property == Slider.ValueProperty) _editor?.ListenAt(Listener.Value / 100);
        };
        WireField(PointX); WireField(PointY);
        _stopTimer.Tick += (_, _) =>
        {
            if (!IsEffectivelyVisible || _tone?.HasEnded != false) StopTone();
        };
        DetachedFromVisualTree += (_, _) => StopTone();
    }

    private void BindEditor()
    {
        if (_editor is not null) _editor.Changed -= Refresh;
        StopTone();
        _audioError = null;
        _editor = DataContext as SndCurveEditorViewModel;
        if (_editor is null) return;
        _editor.Changed += Refresh;
        Refresh();
    }

    private void ArrangePanes(double width)
    {
        bool stack = width < 640;
        if (stack == (Grid.GetRow(Inspector) == 1)) return;
        Panes.RowDefinitions = new RowDefinitions(stack ? "3*,2*" : "*");
        Panes.ColumnDefinitions = new ColumnDefinitions(stack ? "*" : "*,258");
        Panes.RowSpacing = stack ? 8 : 0;
        Grid.SetRow(Inspector, stack ? 1 : 0);
        Grid.SetColumn(Inspector, stack ? 0 : 1);
    }

    private void WireField(TextBox input)
    {
        input.LostFocus += (_, _) => CommitField(input);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) input.Text = input == PointX ? _xDisplay : _yDisplay;
            else if (e.Key == Key.Enter) CommitField(input);
            else return;
            Graph.Focus();
            e.Handled = true;
        };
    }

    private void CommitField(TextBox input)
    {
        if (_refreshing || _editor is not { CanEdit: true, SelectedIndex: >= 0 } vm) return;
        bool isX = input == PointX;
        string display = isX ? _xDisplay : _yDisplay;
        if (!input.IsEnabled || input.Text == display) return;
        var text = input.Text;
        input.Text = display;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) || !float.IsFinite(percent))
        {
            Status.Text = "Enter a percentage from 0 to 100.";
            return;
        }
        var p = vm.Points[vm.SelectedIndex];
        vm.SetPoint(isX ? percent / 100 : p.X, isX ? p.Y : percent / 100);
    }

    private void Refresh()
    {
        if (_editor is not { } vm) return;
        _refreshing = true;
        try
        {
            Heading.Text = vm.Heading;
            AssetName.Text = vm.Name;
            Status.Text = vm.Status;
            Count.Text = $"{vm.Points.Count} of {IW4.Game.Assets.Sound.SndCurve.MaxKnotCount} points";
            Hint.Text = vm.CanEdit ? "Drag a point to shape how quickly the sound fades." :
                vm.HasPreview ? "Move the listener to explore this curve." : vm.Status;
            UndoButton.IsEnabled = vm.CanUndo;
            RedoButton.IsEnabled = vm.CanRedo;
            ApplyButton.IsEnabled = vm.CanApply;
            AddButton.IsEnabled = vm.CanAdd; RemoveButton.IsEnabled = vm.CanRemove;
            AddButton.IsVisible = RemoveButton.IsVisible = !vm.CanRecover;
            RecoverButton.IsVisible = vm.CanRecover;
            Graph.IsReadOnly = !vm.CanEdit;
            Graph.Points = vm.Points;
            Graph.SelectedIndex = vm.SelectedIndex;
            Graph.ListenerDistance = vm.ListenerDistance;
            Graph.ListenerVolume = vm.ListenerVolume;
            Listener.IsEnabled = vm.HasPreview;
            Listener.Value = vm.ListenerDistance * 100d;
            DistanceReadout.Text = vm.ListenerDistance.ToString("P0");
            VolumeReadout.Text = vm.HasPreview ? vm.ListenerVolume.ToString("P0") : "—";
            Meter.Value = vm.ListenerVolume * 100d;
            if (_pickerCount != vm.Points.Count)
            {
                _pickerCount = vm.Points.Count;
                PointPicker.ItemsSource = Enumerable.Range(1, _pickerCount).Select(i => $"Point {i}").ToArray();
            }
            PointPicker.SelectedIndex = vm.SelectedIndex;
            PointX.IsEnabled = vm.CanEdit && vm.IsInteriorPoint;
            PointY.IsEnabled = vm.CanEdit && vm.SelectedIndex >= 0;
            var selected = vm.SelectedIndex >= 0 ? vm.Points[vm.SelectedIndex] : default;
            UpdateField(PointX, ref _xDisplay, selected.X, vm.SelectedIndex >= 0);
            UpdateField(PointY, ref _yDisplay, selected.Y, vm.SelectedIndex >= 0);
            if (!vm.HasPreview) StopTone();
            _tone?.SetVolume(vm.ListenerVolume);
            RefreshAudio();
        }
        finally { _refreshing = false; }
    }

    private static void UpdateField(TextBox input, ref string display, float fraction, bool hasValue)
    {
        string next = hasValue ? (fraction * 100).ToString("0.####", CultureInfo.CurrentCulture) : "";
        if (!input.IsFocused || input.Text == display) input.Text = next;
        display = next;
    }

    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) _editor?.SelectPoint(PointPicker.SelectedIndex);
    }

    private void Add(object? sender, RoutedEventArgs e) { _editor?.AddPoint(); Graph.Focus(); }
    private void Remove(object? sender, RoutedEventArgs e) { _editor?.RemovePoint(); Graph.Focus(); }
    private void Recover(object? sender, RoutedEventArgs e) => _editor?.StartLinearFade();
    private void Undo(object? sender, RoutedEventArgs e) => _editor?.Undo();
    private void Redo(object? sender, RoutedEventArgs e) => _editor?.Redo();
    private void Apply(object? sender, RoutedEventArgs e) => _editor?.Apply();

    private void ToggleTone(object? sender, RoutedEventArgs e)
    {
        if (_tone is not null) { StopTone(); return; }
        if (_editor is not { HasPreview: true } vm || !SoundPreviewPlayer.IsPlatformSupported) return;
        _audioError = null;
        try
        {
            _tone = SoundPreviewPlayer.CreateFalloffTone();
            _tone.SetVolume(vm.ListenerVolume);
            _tone.Play();
            _stopTimer.Start();
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            _audioError = error.Message;
            StopTone();
        }
        RefreshAudio();
    }

    private void StopTone()
    {
        _stopTimer.Stop();
        _tone?.Dispose();
        _tone = null;
        RefreshAudio();
    }

    private void RefreshAudio()
    {
        ToneButton.IsEnabled = SoundPreviewPlayer.IsPlatformSupported && _editor?.HasPreview == true;
        ToneButton.Content = _tone is null ? "Hear gentle tone" : "Stop tone";
        AudioNote.Text = _audioError ?? (!SoundPreviewPlayer.IsPlatformSupported ? SoundPreviewPlayer.UnavailableReason :
            _tone is null ? "The tone plays for eight seconds." : "Move the listener to hear the falloff.");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editor is null || e.Source is TextBox || e.Key != Key.Z ||
            (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) == 0) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _editor.Redo(); else _editor.Undo();
        e.Handled = true;
    }
}
