using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace IW4.Studio.Desktop.Editors.PhysPreset;

public sealed partial class PhysPresetMotionPreview : UserControl
{
    public static readonly StyledProperty<double> BounceProperty =
        AvaloniaProperty.Register<PhysPresetMotionPreview, double>(nameof(Bounce));

    public static readonly StyledProperty<double> FrictionProperty =
        AvaloniaProperty.Register<PhysPresetMotionPreview, double>(nameof(Friction));

    public static readonly StyledProperty<bool> InfiniteFrictionProperty =
        AvaloniaProperty.Register<PhysPresetMotionPreview, bool>(nameof(InfiniteFriction));

    private const int DurationSeconds = 4;
    private const int SamplesPerSecond = 120;
    private readonly DispatcherTimer _clock = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };
    private bool _initialized;
    private bool _updatingPlayhead;
    private bool _slide;
    private double _time;
    private double _playbackStartTime;
    private long _playbackStartTimestamp;

    public PhysPresetMotionPreview()
    {
        InitializeComponent();
        Playhead.Maximum = DurationSeconds;
        Playhead.ValueChanged += (_, args) =>
        {
            if (!_updatingPlayhead)
                StopAt(args.NewValue);
        };
        DropScenario.IsCheckedChanged += (_, _) =>
        {
            if (DropScenario.IsChecked == true)
                SelectScenario(slide: false);
        };
        SlideScenario.IsCheckedChanged += (_, _) =>
        {
            if (SlideScenario.IsChecked == true)
                SelectScenario(slide: true);
        };
        ShowTrajectory.IsCheckedChanged += (_, _) =>
            Stage.SetShowTrajectory(ShowTrajectory.IsChecked == true);
        PlaybackButton.Click += (_, _) => TogglePlayback();
        RewindButton.Click += (_, _) => StopAt(0);
        _clock.Tick += (_, _) => AdvancePlayback();
        _initialized = true;
        ResetSimulation();
    }

    public double Bounce
    {
        get => GetValue(BounceProperty);
        set => SetValue(BounceProperty, value);
    }

    public double Friction
    {
        get => GetValue(FrictionProperty);
        set => SetValue(FrictionProperty, value);
    }

    public bool InfiniteFriction
    {
        get => GetValue(InfiniteFrictionProperty);
        set => SetValue(InfiniteFrictionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_initialized && (change.Property == BounceProperty ||
                             change.Property == FrictionProperty ||
                             change.Property == InfiniteFrictionProperty))
        {
            ResetSimulation();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopAt(_time);
        base.OnDetachedFromVisualTree(e);
    }

    private void SelectScenario(bool slide)
    {
        _slide = slide;
        ResetSimulation();
    }

    private void ResetSimulation()
    {
        const double step = 1.0 / SamplesPerSecond;
        const double gravity = 7.2;
        double bounce = double.IsFinite(Bounce) ? Math.Clamp(Bounce, 0, 1) : 0;
        double friction = double.IsFinite(Friction) ? Math.Clamp(Friction, 0, 8) : 0;
        bool infiniteFriction = InfiniteFriction;
        bool grounded = _slide;
        double distance = 0;
        double height = _slide ? 0 : 1.3;
        double forwardSpeed = _slide ? 3 : 1.7;
        double verticalSpeed = 0;
        var samples = new Point[DurationSeconds * SamplesPerSecond + 1];
        samples[0] = new Point(distance, height);

        // Use the same launch and timestep for each run so draft values can be compared.
        for (int index = 1; index < samples.Length; index++)
        {
            if (grounded)
                forwardSpeed = infiniteFriction ? 0 : Math.Max(0, forwardSpeed - gravity * friction * step);
            else
            {
                verticalSpeed -= gravity * step;
                height += verticalSpeed * step;
            }

            distance += forwardSpeed * step;
            if (!grounded && height <= 0)
            {
                height = 0;
                verticalSpeed = -verticalSpeed * bounce;
                forwardSpeed *= infiniteFriction ? 0 : 1 - Math.Min(0.9, friction * 0.4);
                grounded = verticalSpeed < 0.13;
            }

            samples[index] = new Point(distance, height);
        }

        Stage.SetSamples(samples);
        StopAt(0);
    }

    private void TogglePlayback()
    {
        if (_clock.IsEnabled)
        {
            StopAt(_time);
            return;
        }

        _playbackStartTime = _time >= DurationSeconds ? 0 : _time;
        _playbackStartTimestamp = Stopwatch.GetTimestamp();
        _clock.Start();
        SetTime(_playbackStartTime);
    }

    private void AdvancePlayback()
    {
        double time = _playbackStartTime + Stopwatch.GetElapsedTime(_playbackStartTimestamp).TotalSeconds;
        if (time >= DurationSeconds)
            StopAt(DurationSeconds);
        else
            SetTime(time);
    }

    private void StopAt(double time)
    {
        _clock.Stop();
        SetTime(time);
    }

    private void SetTime(double time)
    {
        _time = time;
        _updatingPlayhead = true;
        Playhead.Value = time;
        _updatingPlayhead = false;
        TimeReadout.Text = $"{time:0.00} s";
        Stage.SetFrame(time * SamplesPerSecond);

        string scenario = _slide ? "slide" : "drop";
        PlaybackButton.Content = _clock.IsEnabled ? "Pause" : time switch
        {
            >= DurationSeconds => $"Replay {scenario}",
            > 0 => "Resume",
            _ => $"Run {scenario}"
        };
    }
}
