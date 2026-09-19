using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using IW4.AssetExchange.SourceFormat.XAnim;
using IW4.Render.EditorPreview;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class XAnimPreviewViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PlaybackTickInterval =
        TimeSpan.FromMilliseconds(16);

    private readonly XAnimPlaybackClip? _clip;
    private readonly DispatcherTimer _playbackTimer;
    private XAnimPreviewScene? _selectedScene;
    private XAnimPreviewPose? _pose;
    private double _currentFrame;
    private double _playbackStartFrame;
    private long _playbackStartTimestamp;
    private bool _isPlaying;
    private bool _disposed;

    public XAnimPreviewViewModel(
        string? animationName,
        XAnimPlaybackClip? clip,
        IReadOnlyList<XAnimPreviewScene> scenes,
        string? previewUnavailableReason = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);

        _clip = clip;
        Name = string.IsNullOrWhiteSpace(animationName)
            ? "<unnamed XAnim>"
            : animationName;
        Scenes = Array.AsReadOnly(scenes
            .OrderByDescending(scene => scene.MatchedTrackCount)
            .ThenBy(scene => scene.UnmatchedTrackCount)
            .ThenBy(scene => scene.BoneCount)
            .ThenBy(scene => scene.ModelName, StringComparer.Ordinal)
            .ToArray());
        _selectedScene = Scenes.FirstOrDefault();
        PreviewUnavailableReason = string.IsNullOrWhiteSpace(previewUnavailableReason)
            ? "Load an XModel with matching bone names to display this animation."
            : previewUnavailableReason;

        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = PlaybackTickInterval
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        RefreshPose();
    }

    public string Name { get; }

    public IReadOnlyList<XAnimPreviewScene> Scenes { get; }

    public string PreviewUnavailableReason { get; }

    public string PreviewUnavailableTitle => _clip is null
        ? "Animation preview unavailable"
        : "A compatible XModel is required";

    public int NumFrames => _clip?.NumFrames ?? 0;

    public int BoneCount => _clip?.BoneCount ?? 0;

    public float Framerate => _clip?.Framerate ?? 0.0f;

    public double DurationSeconds => _clip?.DurationSeconds ?? 0.0;

    public bool IsLooped => _clip?.Looped == true;

    public string FrameCountText =>
        $"{NumFrames:N0} {(NumFrames == 1 ? "frame" : "frames")}";

    public string BoneCountText =>
        $"{BoneCount:N0} {(BoneCount == 1 ? "bone track" : "bone tracks")}";

    public string FramerateText => Framerate > 0.0f && float.IsFinite(Framerate)
        ? $"{Framerate:0.##} fps"
        : "Unknown rate";

    public string LoopText => IsLooped ? "Looping" : "One shot";

    public bool HasPreviewScene => SelectedScene is not null;

    public bool HasMultipleScenes => Scenes.Count > 1;

    public XAnimPreviewScene? SelectedScene
    {
        get => _selectedScene;
        set
        {
            if (ReferenceEquals(_selectedScene, value) ||
                value is not null && !Scenes.Contains(value))
            {
                return;
            }

            _selectedScene = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreviewScene));
            OnPropertyChanged(nameof(SelectedSceneSummary));
            OnPropertyChanged(nameof(PlaybackStatus));
            RefreshPose();
        }
    }

    public string SelectedSceneSummary => SelectedScene is { } scene
        ? $"{scene.MatchedTrackCount:N0} matched · {scene.UnmatchedTrackCount:N0} unmatched · {scene.BoneCount:N0} model bones"
        : "No compatible XModel skeleton";

    public XAnimPreviewPose? Pose
    {
        get => _pose;
        private set => SetProperty(ref _pose, value);
    }

    public double CurrentFrame => _currentFrame;

    public double Progress => NumFrames > 0
        ? Math.Clamp(CurrentFrame / NumFrames, 0.0, 1.0)
        : 0.0;

    public string CurrentFrameText =>
        $"Frame {CurrentFrame.ToString("0.0", CultureInfo.InvariantCulture)} / {NumFrames:N0}";

    public string TimeText =>
        $"{FormatDuration(CurrentTimeSeconds)} / {FormatDuration(DurationSeconds)}";

    public bool CanPlay =>
        !_disposed &&
        _clip is not null &&
        NumFrames > 0 &&
        Framerate > 0.0f &&
        float.IsFinite(Framerate);

    public bool CanRestart => !_disposed && _clip is not null;

    public bool IsPlaying => _isPlaying;

    public bool ShowPlayIcon => !IsPlaying;

    public bool ShowPauseIcon => IsPlaying;

    public string PlayPauseToolTip => IsPlaying
        ? "Pause animation preview"
        : "Play animation preview";

    public string PlaybackStatus
    {
        get
        {
            if (_clip is null)
                return "The XAnim data could not be decoded for preview.";
            if (!CanPlay)
                return "This XAnim has no playable frame range.";
            if (IsPlaying)
            {
                return HasPreviewScene
                    ? "Playing the animation on the selected XModel skeleton."
                    : "Playing the timeline; a compatible XModel is required to draw the skeleton.";
            }
            if (!IsLooped && CurrentFrame >= NumFrames)
                return "Playback complete. Restart or press play to preview it again.";
            if (CurrentFrame > 0.0)
                return "Playback paused.";
            return HasPreviewScene
                ? "Ready to preview the animation."
                : PreviewUnavailableReason;
        }
    }

    public void TogglePlayback()
    {
        if (IsPlaying)
        {
            PausePlayback();
            return;
        }

        if (!CanPlay)
            return;

        if (!IsLooped && CurrentFrame >= NumFrames)
            SetCurrentFrame(0.0);

        _playbackStartFrame = CurrentFrame;
        _playbackStartTimestamp = Stopwatch.GetTimestamp();
        _isPlaying = true;
        _playbackTimer.Start();
        NotifyPlaybackStateChanged();
    }

    public void PausePlayback()
    {
        if (!IsPlaying)
            return;

        UpdatePlaybackPosition();
        _playbackTimer.Stop();
        _isPlaying = false;
        NotifyPlaybackStateChanged();
    }

    public void RestartPlayback()
    {
        if (!CanRestart)
            return;

        SetCurrentFrame(0.0);
        if (IsPlaying)
        {
            _playbackStartFrame = 0.0;
            _playbackStartTimestamp = Stopwatch.GetTimestamp();
        }
        OnPropertyChanged(nameof(PlaybackStatus));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _playbackTimer.Stop();
        _playbackTimer.Tick -= PlaybackTimer_Tick;
        if (_isPlaying)
        {
            _isPlaying = false;
            NotifyPlaybackStateChanged();
        }
    }

    private double CurrentTimeSeconds => Framerate > 0.0f
        ? CurrentFrame / Framerate
        : 0.0;

    private void PlaybackTimer_Tick(object? sender, EventArgs e) =>
        UpdatePlaybackPosition();

    private void UpdatePlaybackPosition()
    {
        if (!IsPlaying || !CanPlay)
            return;

        double elapsedSeconds = Stopwatch.GetElapsedTime(
            _playbackStartTimestamp).TotalSeconds;
        double frame = _playbackStartFrame + elapsedSeconds * Framerate;
        if (IsLooped)
        {
            frame %= NumFrames;
            SetCurrentFrame(frame);
            return;
        }

        if (frame < NumFrames)
        {
            SetCurrentFrame(frame);
            return;
        }

        SetCurrentFrame(NumFrames);
        _playbackTimer.Stop();
        _isPlaying = false;
        NotifyPlaybackStateChanged();
    }

    private void SetCurrentFrame(double frame)
    {
        double clamped = Math.Clamp(frame, 0.0, NumFrames);
        if (Math.Abs(_currentFrame - clamped) < 0.0001)
            return;

        _currentFrame = clamped;
        OnPropertyChanged(nameof(CurrentFrame));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(CurrentFrameText));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(PlaybackStatus));
        RefreshPose();
    }

    private void RefreshPose() =>
        Pose = SelectedScene?.Sample((float)CurrentFrame);

    private void NotifyPlaybackStateChanged()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(ShowPlayIcon));
        OnPropertyChanged(nameof(ShowPauseIcon));
        OnPropertyChanged(nameof(PlayPauseToolTip));
        OnPropertyChanged(nameof(PlaybackStatus));
        OnPropertyChanged(nameof(CanPlay));
    }

    private static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0.0)
            return "00:00.00";
        if (seconds >= TimeSpan.MaxValue.TotalSeconds)
            return "Unbounded";

        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1.0
            ? duration.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss\.ff", CultureInfo.InvariantCulture);
    }
}
