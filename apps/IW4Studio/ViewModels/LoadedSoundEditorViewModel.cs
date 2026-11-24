using Avalonia.Threading;
using IW4.Game.Assets.Sound;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.LoadedSounds;
using IW4.Studio.Desktop.Editors.Sound;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

internal sealed class LoadedSoundClip(LoadedSound asset, string label)
{
    internal LoadedSound Asset { get; } = asset;
    internal string Label { get; } = label;
    internal TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(asset.DurationMilliseconds);
    internal bool HasAudio => Asset.PhysicalData is { Length: > 0 };
    internal float[]? Peaks { get; set; }
    internal TimeSpan Position { get; set; }
    internal string Details => HasAudio ? $"{Duration.TotalSeconds:0.###} s   ·   {Asset.SampleRate / 1000d:0.###} kHz   ·   " +
        (Asset.ChannelCount switch { 1 => "Mono", 2 => "Stereo", var n => $"{n} channels" }) : "No audio embedded";
}

internal sealed class LoadedSoundEditorViewModel : ObservableObject, IAssetEditorStagingState, IDisposable
{
    private readonly AssetEditorSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly List<LoadedSoundClip?> _history = [];
    private LoadedSoundDraft _baseline;
    private SoundPreviewPlayer? _player;
    private LoadedSoundClip? _playing;
    private bool _disposed, _loading, _refreshQueued, _conflict;
    private string? _message;

    internal LoadedSoundEditorViewModel(AssetEditorSession session)
    {
        _session = session;
        _baseline = session.OpenDraft<LoadedSoundDraft>();
        Original = CaptureOriginal();
        RefreshAliases();
        _session.WorkspaceAssetsChanged += WorkspaceChanged;
        _timer.Tick += PlaybackTick;
    }

    internal LoadedSoundClip Original { get; private set; }
    internal LoadedSoundClip? Replacement { get; private set; }
    internal IReadOnlyList<string> AliasNames { get; private set; } = [];
    internal string Name => _session.Entry.NormalizedName ?? Original.Asset.Name ?? "Untitled sound";
    internal string Heading
    {
        get
        {
            string label = Name[(Name.LastIndexOf('/') + 1)..].Replace('_', ' ').Trim();
            return label.Length == 0 ? Name : char.ToUpperInvariant(label[0]) + label[1..];
        }
    }
    internal bool IsEditable => _session.CanEdit;
    internal bool CanImport => IsEditable && !_disposed && !_loading && !_conflict;
    internal bool CanApply => CanImport && HasUnappliedChanges;
    internal bool CanUndo => !_loading && (_history.Count != 0 || _conflict);
    internal bool IsPlaying(LoadedSoundClip? clip) => clip is not null && ReferenceEquals(clip, _playing);
    public bool HasUnappliedChanges => Replacement is not null;
    internal string Status => _conflict ? "The workspace audio changed. Discard this preview to load the current sound." :
        _message ?? (_loading ? "Reading MP3…" : !IsEditable ? "Read only" : HasUnappliedChanges ? "Replacement ready to apply" :
        _session.HasUnsavedChanges ? "Applied to workspace" : "Up to date");
    internal event Action? Changed;
    internal event Action? PlayheadChanged;

    private LoadedSoundClip CaptureOriginal()
    {
        var asset = _baseline.Capture();
        var clip = new LoadedSoundClip(asset, "Current workspace audio");
        _ = LoadPeaksAsync(clip);
        return clip;
    }

    private async Task LoadPeaksAsync(LoadedSoundClip clip)
    {
        if (clip.Asset.PhysicalData is not { Length: > 0 } bytes) return;
        try
        {
            clip.Peaks = await LoadedSoundWaveformExtractor.ExtractAsync(bytes, 512, _lifetime.Token);
            if (!_disposed && (clip == Original || clip == Replacement)) Changed?.Invoke();
        }
        catch (OperationCanceledException) when (_disposed) { }
    }

    internal async Task ImportAsync(Stream stream, string fileName)
    {
        if (!CanImport) return;
        var original = Original;
        var cancellation = _lifetime.Token;
        _loading = true;
        _message = null;
        Notify();
        try
        {
            byte[] bytes = await LoadedSoundImport.ReadMp3Async(stream, cancellation);
            var result = await Task.Run(() => LoadedSoundImport.Compile(original.Asset, bytes, 1), cancellation);
            if (_disposed || _conflict || original != Original) return;
            if (SameAudio(result.Replacement, Replacement?.Asset ?? Original.Asset))
            {
                _message = "That audio is already selected.";
                return;
            }
            StopPlayback();
            _history.Add(Replacement);
            if (_history.Count > 6) _history.RemoveAt(0);
            Replacement = SameAudio(result.Replacement, Original.Asset) ? null :
                new LoadedSoundClip(result.Replacement, fileName);
            if (Replacement is { } clip) _ = LoadPeaksAsync(clip);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception e) when (e is IOException or InvalidDataException or OverflowException or ArgumentException)
        {
            _message = e.Message;
        }
        finally
        {
            _loading = false;
            if (!_disposed) Notify();
        }
    }

    private static bool SameAudio(LoadedSound a, LoadedSound b) =>
        a.PhysicalData.AsSpan().SequenceEqual(b.PhysicalData);

    internal void Undo()
    {
        if (!CanUndo) return;
        StopPlayback();
        _message = null;
        if (_conflict)
        {
            _conflict = false;
            _history.Clear();
            Replacement = null;
            RefreshWorkspace();
        }
        else
        {
            Replacement = _history[^1];
            _history.RemoveAt(_history.Count - 1);
        }
        Notify();
    }

    internal void Apply()
    {
        if (!CanApply || Replacement is not { } pending) return;
        try
        {
            bool changed = _session.ApplyCompiledLoadedSound(Original.Asset, pending.Asset, out var issues);
            var errors = issues.Where(issue => issue.Severity == AssetValidationSeverity.Error).Select(issue => issue.Message).ToArray();
            if (errors.Length > 0)
            {
                RefreshWorkspace();
                _message = string.Join(" ", errors);
            }
            else
            {
                StopPlayback();
                Replacement = null;
                _history.Clear();
                _baseline = _session.OpenDraft<LoadedSoundDraft>();
                Original = CaptureOriginal();
                _message = changed ? null : "This audio already matches the workspace.";
            }
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _message = e.Message;
        }
        Notify();
    }

    private void WorkspaceChanged(object? sender, EventArgs e)
    {
        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (!_disposed) RefreshWorkspace();
        });
    }

    internal void RefreshWorkspace()
    {
        var current = _session.OpenDraft<LoadedSoundDraft>();
        if (!_baseline.Matches(current))
        {
            if (Replacement is not null) _conflict = true;
            else
            {
                StopPlayback();
                _history.Clear();
                _baseline = current;
                Original = CaptureOriginal();
                _message = _loading ? "The workspace audio changed. Choose the MP3 again." : null;
            }
        }
        else _conflict = false;
        RefreshAliases();
        Notify();
    }

    private void RefreshAliases() => AliasNames = _session.CaptureLoadedSoundUsages()
        .Select(row => string.IsNullOrWhiteSpace(row.AliasName) ? row.SoundName : row.AliasName)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    internal void TogglePlayback(LoadedSoundClip? clip)
    {
        if (_disposed || clip?.Asset.PhysicalData is not { Length: > 0 } bytes || !SoundPreviewPlayer.IsPlatformSupported) return;
        if (_playing == clip) { StopPlayback(); return; }
        StopPlayback();
        try
        {
            _player = new SoundPreviewPlayer(bytes);
            if (clip.Position >= clip.Duration) clip.Position = TimeSpan.Zero;
            _player.Position = clip.Position;
            _player.Play();
            _playing = clip;
            _message = null;
            _timer.Start();
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or PlatformNotSupportedException)
        {
            StopPlayback();
            _message = e.Message;
        }
        Changed?.Invoke();
    }

    internal void Seek(LoadedSoundClip? clip, TimeSpan time)
    {
        if (clip is null) return;
        clip.Position = TimeSpan.FromTicks(Math.Clamp(time.Ticks, 0L, clip.Duration.Ticks));
        if (clip == _playing && _player is not null)
        {
            if (clip.Position >= clip.Duration) StopPlayback(ended: true);
            else _player.Position = clip.Position;
        }
        PlayheadChanged?.Invoke();
    }

    private void PlaybackTick(object? sender, EventArgs e)
    {
        if (_player is null || _playing is null) return;
        if (_player.HasEnded) { StopPlayback(ended: true); return; }
        _playing.Position = _player.Position;
        PlayheadChanged?.Invoke();
    }

    internal void StopPlayback() => StopPlayback(false);

    private void StopPlayback(bool ended)
    {
        _timer.Stop();
        if (_player is null) return;
        if (_playing is { } clip) clip.Position = ended ? clip.Duration : _player.Position;
        _player.Dispose();
        _player = null;
        _playing = null;
        if (!_disposed) Changed?.Invoke();
    }

    internal void Report(string message) { _message = message; Notify(); }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasUnappliedChanges));
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.WorkspaceAssetsChanged -= WorkspaceChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _timer.Tick -= PlaybackTick;
        StopPlayback();
    }
}
