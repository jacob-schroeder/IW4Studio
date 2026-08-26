using System.Buffers.Binary;
using System.Diagnostics;
using Avalonia.Threading;
using IW4.Assets.Assets.Sound;
using IW4.Runtime.Assets.Sound;
using IW4.Studio.Desktop.Editors;
using IW4.Studio.Desktop.Editors.Sound;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class SoundPreviewViewModel
    : ObservableObject,
      IAssetEditorProperties,
      IAssetEditorStagingState,
      IDisposable
{
    internal const int VisualizationBarCount = 120;
    private static readonly TimeSpan PlaybackTickInterval =
        TimeSpan.FromMilliseconds(50);

    private readonly ISoundPayloadResolver _soundPayloadResolver;
    private readonly string? _unavailableStreamReason;
    private readonly AssetEditorSession? _editorSession;
    private readonly DispatcherTimer _playbackTimer;
    private readonly object _variantLoadGate = new();
    private CancellationTokenSource? _variantLoadCancellation;
    private long _variantLoadRevision;
    private SoundPreviewVariantViewModel? _selectedVariant;
    private SoundPreviewMaterialization? _selectedPreview;
    private LoadedSound? _editableSelectedPayload;
    private SoundImportCandidate? _stagedImport;
    private SoundPreviewPlayer? _player;
    private IReadOnlyList<double> _liveLevels = [];
    private TimeSpan _position;
    private TimeSpan _playbackStartPosition;
    private long _playbackStartTimestamp;
    private int _lastMeterIndex = -1;
    private bool _isPlaying;
    private bool _isLoadingPreview;
    private bool _playbackFailed;
    private bool _disposed;
    private string? _playbackError;
    private string _statusMessage = string.Empty;

    public SoundPreviewViewModel(SoundAliasListAsset sound)
        : this(
            sound,
            UnavailableSoundPayloadResolver.Instance,
            "This streamed payload is not materialized by the workspace yet.")
    {
    }

    public SoundPreviewViewModel(
        SoundAliasListAsset sound,
        ISoundPayloadResolver soundPayloadResolver,
        string? unavailableStreamReason = null,
        int? initialAliasIndex = null,
        int? initialFileIndex = null,
        AssetEditorSession? editorSession = null)
        : this(
            sound,
            soundPayloadResolver,
            unavailableStreamReason,
            initialAliasIndex,
            initialFileIndex,
            onlyInitialVariant: false,
            editorSession: editorSession)
    {
    }

    private SoundPreviewViewModel(
        SoundAliasListAsset sound,
        ISoundPayloadResolver soundPayloadResolver,
        string? unavailableStreamReason,
        int? initialAliasIndex,
        int? initialFileIndex,
        bool onlyInitialVariant,
        AssetEditorSession? editorSession = null)
    {
        ArgumentNullException.ThrowIfNull(sound);
        ArgumentNullException.ThrowIfNull(soundPayloadResolver);
        _soundPayloadResolver = soundPayloadResolver;
        _unavailableStreamReason = unavailableStreamReason;
        _editorSession = editorSession;
        Name = string.IsNullOrWhiteSpace(sound.AliasName)
            ? "<unnamed sound>"
            : sound.AliasName;
        int declaredAliasCount = sound.Count;
        int loadedAliasCount = sound.Aliases.Count;
        AliasCountText = declaredAliasCount == loadedAliasCount
            ? $"{declaredAliasCount:N0} {(declaredAliasCount == 1 ? "alias" : "aliases")}"
            : $"{loadedAliasCount:N0} loaded / {declaredAliasCount:N0} declared aliases";
        Variants = Array.AsReadOnly(BuildVariants(
            sound,
            onlyInitialVariant ? initialAliasIndex : null,
            onlyInitialVariant ? initialFileIndex : null).ToArray());
        VariantCountText = $"{Variants.Count:N0} " +
            (Variants.Count == 1 ? "preview choice" : "preview choices");
        _selectedVariant = initialAliasIndex is { } aliasIndex &&
            initialFileIndex is { } fileIndex
                ? Variants.FirstOrDefault(variant =>
                    variant.AliasIndex == aliasIndex &&
                    variant.FileIndex == fileIndex)
                : null;
        _selectedVariant ??= Variants.FirstOrDefault(
                variant => variant.CanAttemptPreview)
            ?? Variants.FirstOrDefault();
        RefreshSelectedEditingState();
        ResetLiveLevels();

        _playbackTimer = new DispatcherTimer
        {
            Interval = PlaybackTickInterval
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        BeginSelectedVariantLoad();
    }

    internal static SoundPreviewViewModel CreatePackedSoundPreview(
        SoundAliasListAsset sound,
        ISoundPayloadResolver soundPayloadResolver,
        int aliasIndex,
        int fileIndex) =>
        new(
            sound,
            soundPayloadResolver,
            unavailableStreamReason: null,
            aliasIndex,
            fileIndex,
            onlyInitialVariant: true,
            editorSession: null);

    public string Name { get; }

    public IReadOnlyList<SoundPreviewVariantViewModel> Variants { get; }

    public string AliasCountText { get; }

    public string VariantCountText { get; }

    public bool HasVariantSelector => Variants.Count > 1;

    public bool CanSelectVariant => !HasUnappliedChanges;

    public bool HasEditorSession => _editorSession is not null;

    public bool CanEditSelectedPayload =>
        !_disposed &&
        _editorSession?.CanEdit == true &&
        _editableSelectedPayload is not null;

    public bool IsSelectedPayloadReadOnly => !CanEditSelectedPayload;

    public string ReadOnlyBadgeText => HasEditorSession
        ? "READ ONLY"
        : "PREVIEW ONLY";

    public bool HasUnappliedChanges => _stagedImport is not null;

    public bool CanImport => CanEditSelectedPayload;

    public bool CanApply => CanEditSelectedPayload && HasUnappliedChanges;

    public bool CanRevert =>
        _editorSession?.CanEdit == true &&
        (HasUnappliedChanges || _editorSession.HasUnsavedChanges);

    public bool CanExport =>
        !_disposed &&
        !_isLoadingPreview &&
        _selectedPreview?.PhysicalData is { Length: > 0 };

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (!SetProperty(ref _statusMessage, value))
                return;
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public SoundPreviewVariantViewModel? SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            if (ReferenceEquals(_selectedVariant, value) ||
                value is not null && !Variants.Contains(value) ||
                _stagedImport is not null)
            {
                return;
            }

            StopAndReleasePlayer();
            CancelSelectedVariantLoad();
            _selectedVariant = value;
            _selectedPreview = null;
            _playbackFailed = false;
            _playbackError = null;
            SetPosition(TimeSpan.Zero);
            ResetLiveLevels();
            OnPropertyChanged();
            RefreshSelectedEditingState();
            NotifySelectedVariantChanged();
            BeginSelectedVariantLoad();
        }
    }

    public IReadOnlyList<double> FrameGainProfile =>
        _selectedPreview?.FrameGainProfile ?? [];

    public IReadOnlyList<double> LiveLevels => _liveLevels;

    public bool HasVisualization => FrameGainProfile.Count > 0;

    public bool CanPlay =>
        !_disposed &&
        !_isLoadingPreview &&
        !_playbackFailed &&
        _selectedPreview?.HasMpegPayload == true &&
        SoundPreviewPlayer.IsPlatformSupported;

    public string FormatText =>
        _selectedPreview?.FormatText ?? SelectedVariant?.FormatText ?? "Unavailable";

    public string SampleRateText =>
        _selectedPreview?.SampleRateText ?? "Unknown";

    public string ChannelsText =>
        _selectedPreview?.ChannelsText ?? "Unknown";

    public string StoredSizeText =>
        _selectedPreview?.StoredSizeText ??
        SelectedVariant?.StoredSizeText ??
        "No data";

    public string SelectedSourceText =>
        SelectedVariant?.SourceText ?? "No sound file";

    public bool IsPlaying => _isPlaying;

    public bool ShowPlayIcon => !IsPlaying;

    public bool ShowPauseIcon => IsPlaying;

    public double Progress
    {
        get
        {
            double duration = _selectedPreview?.Duration.TotalSeconds ?? 0;
            return duration > 0
                ? Math.Clamp(_position.TotalSeconds / duration, 0, 1)
                : 0;
        }
    }

    public string PositionText => FormatDuration(_position);

    public string DurationText => FormatDuration(
        _selectedPreview?.Duration ?? TimeSpan.Zero);

    public string VisualizerCaption => IsPlaying
        ? "LIVE OUTPUT LEVEL"
        : _position > TimeSpan.Zero && Progress < 1
            ? "PAUSED OUTPUT LEVEL"
            : "MPEG FRAME GAIN PROFILE";

    public string PlaybackStatus
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_playbackError))
                return _playbackError;
            if (SelectedVariant is null)
                return "This Sound contains no alias variants.";
            if (_isLoadingPreview)
                return "Preparing the selected sound preview…";
            if (_selectedPreview is null)
                return "The selected sound preview is unavailable.";
            if (!_selectedPreview.HasMpegPayload)
                return _selectedPreview.AvailabilityMessage;
            if (!SoundPreviewPlayer.IsPlatformSupported)
                return SoundPreviewPlayer.UnavailableReason ??
                    "Native sound preview is not available on this platform.";
            if (IsPlaying)
                return "Playing the MPEG audio payload.";
            if (_position > TimeSpan.Zero && Progress < 1)
                return "Playback paused.";
            if (Progress >= 1)
                return "Playback complete. Press play to preview it again.";
            return "Ready to preview the MPEG audio payload.";
        }
    }

    public string PlayPauseToolTip => IsPlaying
        ? "Pause sound preview"
        : _isLoadingPreview
            ? "Preparing sound preview"
            : "Play sound preview";

    public string PropertySectionName => "SOUND PREVIEW";

    public IReadOnlyList<AssetEditorProperty> EditorProperties =>
        SelectedVariant is { } variant
            ?
            [
                new("Aliases", AliasCountText),
                new("Selected variant", variant.DisplayName),
                new("Source", variant.SourceText),
                new("Codec", FormatText),
                new("Duration", DurationText),
                new("Sample rate", SampleRateText),
                new("Channels", ChannelsText),
                new("Stored data", StoredSizeText)
            ]
            : [new("Aliases", AliasCountText)];

    internal bool TryCaptureImportTarget(out SoundImportTarget? target)
    {
        target = null;
        if (!CanImport ||
            SelectedVariant is not { } variant ||
            _editableSelectedPayload is not { } payload)
        {
            return false;
        }

        target = new SoundImportTarget(
            variant.AliasIndex,
            variant.FileIndex,
            payload);
        return true;
    }

    internal bool TryStageImport(
        SoundImportCandidate candidate,
        string source,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        error = null;
        string reason = "The selected payload is no longer editable.";
        LoadedSound? current = null;
        if (_editorSession is null ||
            SelectedVariant is not { } variant ||
            variant.AliasIndex != candidate.AliasIndex ||
            variant.FileIndex != candidate.FileIndex ||
            !_editorSession.TryCaptureEditableSoundPayload(
                variant.AliasIndex,
                variant.FileIndex,
                out current,
                out reason) ||
            current is null ||
            !string.Equals(
                current.Name,
                candidate.Replacement.Name,
                StringComparison.Ordinal))
        {
            error = string.IsNullOrWhiteSpace(reason)
                ? "The selected payload is no longer editable."
                : reason;
            StatusMessage = error;
            NotifyEditingStateChanged();
            return false;
        }

        StopAndReleasePlayer();
        CancelSelectedVariantLoad();
        _stagedImport = candidate;
        _editableSelectedPayload = current;
        _selectedPreview = candidate.Preview;
        _playbackFailed = false;
        _playbackError = null;
        SetPosition(TimeSpan.Zero);
        ResetLiveLevels();
        string displaySource = string.IsNullOrWhiteSpace(source)
            ? "the selected MP3"
            : source;
        StatusMessage =
            $"Imported {displaySource} for preview. Review it, then Apply to stage the change for the fastfile.";
        NotifySelectedVariantChanged();
        NotifyEditingStateChanged();
        return true;
    }

    public bool ApplyImportedPayload()
    {
        if (!CanApply ||
            _editorSession is null ||
            _stagedImport is not { } candidate)
        {
            return false;
        }

        bool applied;
        IReadOnlyList<AssetValidationIssue> issues;
        try
        {
            applied = _editorSession.ApplyCompiledSound(
                candidate.AliasIndex,
                candidate.FileIndex,
                candidate.Replacement,
                out issues);
        }
        catch (Exception exception) when (exception is
                   InvalidDataException or
                   InvalidOperationException or
                   ArgumentException or
                   OverflowException)
        {
            StatusMessage = $"Sound Apply blocked: {exception.Message}";
            NotifyEditingStateChanged();
            return false;
        }

        AssetValidationIssue? error = issues.FirstOrDefault(issue =>
            issue.Severity == AssetValidationSeverity.Error);
        if (!applied && error is not null)
        {
            StatusMessage = $"Sound Apply blocked: {error.Message}";
            NotifyEditingStateChanged();
            return false;
        }

        _stagedImport = null;
        RefreshSelectedEditingState();
        ReloadSelectedPreview();
        StatusMessage = applied
            ? "Applied the embedded MPEG payload. Save the fastfile to persist it."
            : "The imported MPEG payload already matches the applied Sound.";
        NotifyEditingStateChanged();
        return applied;
    }

    public void RevertSound()
    {
        if (!CanRevert)
            return;

        if (_stagedImport is not null)
        {
            _stagedImport = null;
            RefreshSelectedEditingState();
            ReloadSelectedPreview();
            StatusMessage = "Discarded the imported MPEG payload.";
            NotifyEditingStateChanged();
            return;
        }

        bool reverted = _editorSession?.Revert() == true;
        RefreshSelectedEditingState();
        ReloadSelectedPreview();
        StatusMessage = reverted
            ? "Reverted the Sound and embedded payload to the saved baseline."
            : "The Sound already matches its saved baseline.";
        NotifyEditingStateChanged();
    }

    internal bool TryCaptureExport(out SoundExportPayload? export)
    {
        export = null;
        if (!CanExport ||
            _selectedPreview?.PhysicalData is not { Length: > 0 } bytes)
        {
            return false;
        }

        string sourceName = _editableSelectedPayload?.Name ??
            SelectedVariant?.DisplayName ??
            Name;
        export = new SoundExportPayload(
            bytes.ToArray(),
            BuildSuggestedFileName(sourceName));
        return true;
    }

    public void ReportImportFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        StatusMessage = $"Sound import failed: {message}";
    }

    public void ReportExportFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        StatusMessage = $"Sound export failed: {message}";
    }

    public void ReportExportSuccess(string destination) =>
        StatusMessage = $"Exported the MPEG payload to {destination}.";

    public void TogglePlayback()
    {
        if (!CanPlay || _selectedPreview?.PhysicalData is not { } bytes)
            return;

        if (IsPlaying)
        {
            PausePlayback();
            return;
        }

        try
        {
            _player ??= new SoundPreviewPlayer(bytes);
            bool restart = Progress >= 1 || _player.HasEnded;
            if (restart)
            {
                _player.Restart();
                SetPosition(TimeSpan.Zero);
                ResetLiveLevels();
            }
            else
            {
                _player.Play();
            }
            _playbackStartPosition = _position;
            _playbackStartTimestamp = Stopwatch.GetTimestamp();
            SetPlaying(true);
            _playbackTimer.Start();
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   InvalidDataException or
                   IOException or
                   PlatformNotSupportedException)
        {
            ReportPlaybackFailure(exception.Message);
        }
    }

    public void PausePlayback()
    {
        if (!IsPlaying)
            return;

        UpdatePosition();
        try
        {
            _player?.Pause();
            SetPlaying(false);
            _playbackTimer.Stop();
        }
        catch (Exception exception) when (exception is
                   InvalidOperationException or
                   IOException or
                   PlatformNotSupportedException)
        {
            ReportPlaybackFailure(exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelSelectedVariantLoad();
        _playbackTimer.Stop();
        _playbackTimer.Tick -= PlaybackTimer_Tick;
        StopAndReleasePlayer();
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs args)
    {
        if (!IsPlaying || _player is null)
            return;

        UpdatePosition();
        if (!IsPlaying)
            return;

        UpdateLiveLevel(_player.ReadLevel());
    }

    private void UpdatePosition()
    {
        if (!IsPlaying || _selectedPreview is not { } preview)
            return;

        TimeSpan position = _playbackStartPosition +
            Stopwatch.GetElapsedTime(_playbackStartTimestamp);
        bool ended = _player?.HasEnded == true || position >= preview.Duration;
        SetPosition(ended ? preview.Duration : position);
        if (!ended)
            return;

        SetPlaying(false);
        _playbackTimer.Stop();
    }

    private void UpdateLiveLevel(double level)
    {
        if (_liveLevels.Count == 0)
            return;

        int index = Math.Clamp(
            (int)Math.Floor(Progress * (_liveLevels.Count - 1)),
            0,
            _liveLevels.Count - 1);
        double normalized = double.IsFinite(level)
            ? Math.Clamp(level, 0, 1)
            : 0;
        double[] updated = _liveLevels.ToArray();
        int first = Math.Clamp(_lastMeterIndex + 1, 0, index);
        for (int meterIndex = first; meterIndex <= index; meterIndex++)
            updated[meterIndex] = normalized;
        updated[index] = Math.Max(updated[index], normalized);
        _lastMeterIndex = Math.Max(_lastMeterIndex, index);
        _liveLevels = Array.AsReadOnly(updated);
        OnPropertyChanged(nameof(LiveLevels));
    }

    private void ResetLiveLevels()
    {
        _lastMeterIndex = -1;
        if (FrameGainProfile.Count == 0)
        {
            _liveLevels = [];
        }
        else
        {
            double[] levels = new double[FrameGainProfile.Count];
            Array.Fill(levels, double.NaN);
            _liveLevels = Array.AsReadOnly(levels);
        }
        OnPropertyChanged(nameof(LiveLevels));
    }

    private void SetPosition(TimeSpan position)
    {
        if (_position == position)
            return;

        _position = position;
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PlaybackStatus));
        OnPropertyChanged(nameof(VisualizerCaption));
    }

    private void SetPlaying(bool isPlaying)
    {
        if (!SetProperty(ref _isPlaying, isPlaying, nameof(IsPlaying)))
            return;

        OnPropertyChanged(nameof(ShowPlayIcon));
        OnPropertyChanged(nameof(ShowPauseIcon));
        OnPropertyChanged(nameof(PlaybackStatus));
        OnPropertyChanged(nameof(PlayPauseToolTip));
        OnPropertyChanged(nameof(VisualizerCaption));
    }

    private void StopAndReleasePlayer()
    {
        _playbackTimer?.Stop();
        SetPlaying(false);
        _player?.Dispose();
        _player = null;
    }

    private void ReportPlaybackFailure(string message)
    {
        StopAndReleasePlayer();
        _playbackFailed = true;
        _playbackError = string.IsNullOrWhiteSpace(message)
            ? "The native audio player could not start this preview."
            : $"The native audio player could not start this preview: {message}";
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(PlaybackStatus));
    }

    private void NotifySelectedVariantChanged()
    {
        OnPropertyChanged(nameof(FrameGainProfile));
        OnPropertyChanged(nameof(HasVisualization));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(SampleRateText));
        OnPropertyChanged(nameof(ChannelsText));
        OnPropertyChanged(nameof(StoredSizeText));
        OnPropertyChanged(nameof(SelectedSourceText));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(PlaybackStatus));
        OnPropertyChanged(nameof(PlayPauseToolTip));
        OnPropertyChanged(nameof(VisualizerCaption));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(EditorProperties));
    }

    private void RefreshSelectedEditingState()
    {
        _editableSelectedPayload = null;
        string reason;
        if (_editorSession is null)
        {
            reason = "This preview is read-only. Packed payloads can be exported but are not rewritten.";
        }
        else if (SelectedVariant is not { } variant)
        {
            reason = "This Sound has no selected payload.";
        }
        else if (_editorSession.TryCaptureEditableSoundPayload(
                     variant.AliasIndex,
                     variant.FileIndex,
                     out LoadedSound? payload,
                     out reason) &&
                 payload is not null)
        {
            _editableSelectedPayload = payload;
            reason = string.Empty;
        }

        StatusMessage = reason;
        NotifyEditingStateChanged();
    }

    private void ReloadSelectedPreview()
    {
        StopAndReleasePlayer();
        CancelSelectedVariantLoad();
        _selectedPreview = null;
        _playbackFailed = false;
        _playbackError = null;
        SetPosition(TimeSpan.Zero);
        ResetLiveLevels();
        NotifySelectedVariantChanged();
        BeginSelectedVariantLoad();
    }

    private void NotifyEditingStateChanged()
    {
        OnPropertyChanged(nameof(CanSelectVariant));
        OnPropertyChanged(nameof(CanEditSelectedPayload));
        OnPropertyChanged(nameof(IsSelectedPayloadReadOnly));
        OnPropertyChanged(nameof(ReadOnlyBadgeText));
        OnPropertyChanged(nameof(HasUnappliedChanges));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(CanExport));
    }

    private void BeginSelectedVariantLoad()
    {
        if (_disposed || SelectedVariant is not { } variant)
            return;

        var cancellation = new CancellationTokenSource();
        lock (_variantLoadGate)
        {
            _variantLoadCancellation?.Cancel();
            _variantLoadCancellation = cancellation;
        }

        _isLoadingPreview = true;
        NotifySelectedVariantChanged();
        long revision = Interlocked.Increment(ref _variantLoadRevision);
        LoadedSound? loadedSoundOverride = _editableSelectedPayload;
        _ = LoadSelectedVariantAsync(
            variant,
            loadedSoundOverride,
            revision,
            cancellation);
    }

    private async Task LoadSelectedVariantAsync(
        SoundPreviewVariantViewModel variant,
        LoadedSound? loadedSoundOverride,
        long revision,
        CancellationTokenSource cancellation)
    {
        CancellationToken cancellationToken = cancellation.Token;
        try
        {
            SoundPreviewMaterialization preview;
            try
            {
                preview = await Task.Run(
                        () => variant.Materialize(
                            _soundPayloadResolver,
                            _unavailableStreamReason,
                            loadedSoundOverride,
                            cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                preview = SoundPreviewMaterialization.Failed(
                    variant.FormatText,
                    variant.StoredByteCount,
                    $"The selected sound preview could not be prepared: " +
                    exception.Message);
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed ||
                    cancellationToken.IsCancellationRequested ||
                    Volatile.Read(ref _variantLoadRevision) != revision ||
                    !ReferenceEquals(SelectedVariant, variant))
                {
                    return;
                }

                _selectedPreview = preview;
                _isLoadingPreview = false;
                ResetLiveLevels();
                NotifySelectedVariantChanged();
            });
        }
        finally
        {
            lock (_variantLoadGate)
            {
                if (ReferenceEquals(_variantLoadCancellation, cancellation))
                    _variantLoadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelSelectedVariantLoad()
    {
        lock (_variantLoadGate)
        {
            _variantLoadCancellation?.Cancel();
            _variantLoadCancellation = null;
        }

        Interlocked.Increment(ref _variantLoadRevision);
        _isLoadingPreview = false;
    }

    private static IEnumerable<SoundPreviewVariantViewModel> BuildVariants(
        SoundAliasListAsset sound,
        int? onlyAliasIndex,
        int? onlyFileIndex)
    {
        if (sound.Aliases.Count == 0)
        {
            yield return SoundPreviewVariantViewModel.Create(
                sound.AliasName,
                aliasIndex: 0,
                aliasCount: 0,
                fileIndex: 0,
                soundFileCount: 0,
                soundFile: null);
            yield break;
        }

        for (int aliasIndex = 0; aliasIndex < sound.Aliases.Count; aliasIndex++)
        {
            if (onlyAliasIndex is { } selectedAliasIndex &&
                aliasIndex != selectedAliasIndex)
            {
                continue;
            }

            SndAlias alias = sound.Aliases[aliasIndex];
            if (alias.SoundFiles.Count == 0)
            {
                yield return SoundPreviewVariantViewModel.Create(
                    alias.AliasName ?? sound.AliasName,
                    aliasIndex,
                    sound.Aliases.Count,
                    fileIndex: 0,
                    soundFileCount: 0,
                    soundFile: null);
                continue;
            }

            for (int fileIndex = 0; fileIndex < alias.SoundFiles.Count; fileIndex++)
            {
                if (onlyFileIndex is { } selectedFileIndex &&
                    fileIndex != selectedFileIndex)
                {
                    continue;
                }

                yield return SoundPreviewVariantViewModel.Create(
                    alias.AliasName ?? sound.AliasName,
                    aliasIndex,
                    sound.Aliases.Count,
                    fileIndex,
                    alias.SoundFiles.Count,
                    alias.SoundFiles[fileIndex]);
            }
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "0:00.00";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
        return $"{(int)duration.TotalMinutes}:{duration.Seconds:00}.{duration.Milliseconds / 10:00}";
    }

    private static string BuildSuggestedFileName(string value)
    {
        string normalized = value.TrimStart(',').Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "sound";
        if (string.Equals(
                Path.GetExtension(fileName),
                ".mp3",
                StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        HashSet<char> invalid = [.. Path.GetInvalidFileNameChars()];
        char[] sanitized = fileName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray();
        string result = new string(sanitized).Trim();
        return string.IsNullOrWhiteSpace(result) ? "sound" : result;
    }
}

public sealed class SoundPreviewVariantViewModel
{
    private readonly SoundFile? _soundFile;

    private SoundPreviewVariantViewModel(
        int aliasIndex,
        int fileIndex,
        string displayName,
        string sourceText,
        string formatText,
        int storedByteCount,
        SoundFile? soundFile)
    {
        AliasIndex = aliasIndex;
        FileIndex = fileIndex;
        DisplayName = displayName;
        SourceText = sourceText;
        FormatText = formatText;
        StoredByteCount = storedByteCount;
        _soundFile = soundFile;
    }

    public string DisplayName { get; }

    internal int AliasIndex { get; }

    internal int FileIndex { get; }

    public string SourceText { get; }

    public string FormatText { get; }

    internal int StoredByteCount { get; }

    internal bool CanAttemptPreview =>
        _soundFile is { Exists: not 0 } soundFile &&
        (soundFile.Loaded?.LoadedSound?.PhysicalData is { Length: > 0 } ||
         soundFile.Streamed?.StreamFile is { StreamFileLength: > 0 });

    public string StoredSizeText => SoundPreviewFormat.Bytes(
        StoredByteCount);

    internal static SoundPreviewVariantViewModel Create(
        string? aliasName,
        int aliasIndex,
        int aliasCount,
        int fileIndex,
        int soundFileCount,
        SoundFile? soundFile)
    {
        string baseName = string.IsNullOrWhiteSpace(aliasName)
            ? $"Alias {aliasIndex + 1:N0}"
            : aliasName;
        string sourceText = DisplaySoundFileSource(soundFile);
        string sourceName = DisplayVariantSource(soundFile, sourceText);
        string displayName = aliasCount > 1
            ? $"Variant {aliasIndex + 1:N0} — {sourceName}"
            : soundFileCount > 1
                ? sourceName
                : baseName;
        if (soundFileCount > 1)
            displayName += $" · language row {fileIndex + 1:N0}";

        string formatText = soundFile is null || soundFile.Exists == 0
            ? "Unavailable"
            : soundFile.Type.ToString();
        int storedByteCount = soundFile?.Loaded?.LoadedSound?.PhysicalData?.Length ??
            soundFile?.Streamed?.StreamFile?.StreamFileLength ??
            0;
        return new SoundPreviewVariantViewModel(
            aliasIndex,
            fileIndex,
            displayName,
            sourceText,
            formatText,
            storedByteCount,
            soundFile);
    }

    internal SoundPreviewMaterialization Materialize(
        ISoundPayloadResolver soundPayloadResolver,
        string? unavailableStreamReason,
        LoadedSound? loadedSoundOverride,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_soundFile is null)
        {
            return SoundPreviewMaterialization.Failed(
                "Unavailable",
                0,
                "This alias has no materialized sound-file variant.");
        }
        if (_soundFile.Exists == 0)
        {
            return SoundPreviewMaterialization.Failed(
                "Unavailable",
                0,
                "This sound-file variant is marked absent by the asset.");
        }

        if ((loadedSoundOverride ?? _soundFile.Loaded?.LoadedSound) is { } loaded)
        {
            byte[]? bytes = loaded.PhysicalData;
            if (bytes is not { Length: > 0 })
            {
                return SoundPreviewMaterialization.Failed(
                    "No loaded payload",
                    0,
                    "The LoadedSound contains no materialized codec bytes.",
                    loaded.SampleRate,
                    loaded.ChannelCount);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return TryCreatePlayablePreview(
                bytes,
                loaded.SampleRate,
                loaded.ChannelCount,
                "Ready to preview the embedded MPEG audio payload.") ??
                SoundPreviewMaterialization.Failed(
                    "Unsupported loaded codec",
                    bytes.Length,
                    "This loaded codec is not supported by the sound preview yet.",
                    loaded.SampleRate,
                    loaded.ChannelCount,
                    bytes);
        }

        if (_soundFile.Streamed is { } streamed)
        {
            string reason = unavailableStreamReason ?? string.Empty;
            if (streamed.StreamFile is not null &&
                soundPayloadResolver.TryResolvePayload(
                    streamed,
                    out byte[] bytes,
                    out reason))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return TryCreatePlayablePreview(
                    bytes,
                    preferredSampleRate: 0,
                    preferredChannelCount: 0,
                    "Ready to preview the packed MPEG audio payload.") ??
                    SoundPreviewMaterialization.Failed(
                        "Unsupported streamed codec",
                        bytes.Length,
                        "This packed codec is not supported by the sound preview yet.",
                        physicalData: bytes);
            }

            return SoundPreviewMaterialization.Failed(
                _soundFile.Type.ToString(),
                streamed.StreamFile?.StreamFileLength ?? 0,
                string.IsNullOrWhiteSpace(reason)
                    ? unavailableStreamReason ??
                      "This streamed payload is not available in the workspace."
                    : $"This streamed payload is unavailable: {reason}");
        }

        return SoundPreviewMaterialization.Failed(
            "Unavailable",
            0,
            "This sound-file variant has no resolved payload.");
    }

    private static SoundPreviewMaterialization? TryCreatePlayablePreview(
        byte[] bytes,
        int preferredSampleRate,
        int preferredChannelCount,
        string availabilityMessage)
    {
        if (!MpegAudioPreview.TryAnalyze(
                bytes,
                SoundPreviewViewModel.VisualizationBarCount,
                out MpegAudioPreviewInfo? preview) ||
            preview is null)
        {
            return null;
        }

        return SoundPreviewMaterialization.Playable(
            preview.FormatName,
            preferredSampleRate > 0
                ? preferredSampleRate
                : preview.SampleRate,
            preferredChannelCount > 0
                ? preferredChannelCount
                : preview.ChannelCount,
            bytes.Length,
            preview.Duration,
            preview.Levels,
            bytes,
            availabilityMessage);
    }

    private static string DisplaySoundFileSource(SoundFile? soundFile)
    {
        if (soundFile is null)
            return "No sound file";
        if (soundFile.Loaded?.LoadedSound is { } loaded)
            return DisplayLoadedSource(loaded.Name);
        if (soundFile.Streamed is not { } streamed)
            return soundFile.Type.ToString();
        if (streamed.StreamFile is { } packed)
        {
            return $"packfile{streamed.FileIndex}.pak · " +
                $"offset {packed.StreamFileOffset:N0} · " +
                $"{packed.StreamFileLength:N0} bytes";
        }
        if (streamed.ExternalFile is { } external)
        {
            string path = Path.Combine(
                external.Directory ?? string.Empty,
                external.Filename ?? string.Empty);
            return string.IsNullOrWhiteSpace(path)
                ? "External streamed sound"
                : path;
        }

        return $"Stream package {streamed.FileIndex:N0}";
    }

    private static string DisplayVariantSource(
        SoundFile? soundFile,
        string sourceText)
    {
        if (soundFile?.Streamed?.StreamFile is not null)
            return $"packfile{soundFile.Streamed.FileIndex}.pak";

        string normalized = sourceText.TrimStart(',').Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(fileName)
            ? sourceText
            : fileName;
    }

    private static string DisplayLoadedSource(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name, "null", StringComparison.OrdinalIgnoreCase))
        {
            return "Inline LoadedSound";
        }

        string displayName = name.TrimStart(',');
        return string.IsNullOrWhiteSpace(displayName)
            ? "Inline LoadedSound"
            : displayName;
    }

}

internal sealed record SoundImportTarget(
    int AliasIndex,
    int FileIndex,
    LoadedSound Template);

internal sealed record SoundImportCandidate(
    int AliasIndex,
    int FileIndex,
    LoadedSound Replacement,
    SoundPreviewMaterialization Preview)
{
    internal static SoundImportCandidate Compile(
        SoundImportTarget target,
        ReadOnlyMemory<byte> sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!MpegAudioPreview.TryAnalyze(
                sourceBytes.Span,
                SoundPreviewViewModel.VisualizationBarCount,
                out MpegAudioPreviewInfo? analysis) ||
            analysis is null)
        {
            throw new InvalidDataException(
                "The file is not a supported contiguous MPEG Layer III stream.");
        }

        if (analysis.AudioByteCount <= 0 || analysis.FrameOffsets.Count == 0)
            throw new InvalidDataException("The MP3 contains no MPEG audio frames.");
        if (analysis.FrameOffsets.Count > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "The MP3 contains too many MPEG frames for an IW4 LoadedSound.");
        }

        long durationMilliseconds = checked(
            analysis.TotalSamples * 1000L / analysis.SampleRate);
        if (durationMilliseconds is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "Embedded IW4 sounds must be between 1 ms and 65.535 seconds long.");
        }
        if (analysis.SampleRate > ushort.MaxValue ||
            analysis.ChannelCount > ushort.MaxValue)
        {
            throw new InvalidDataException(
                "The MP3 sample layout cannot be represented by an IW4 LoadedSound.");
        }

        byte[] physicalData = sourceBytes.Slice(
            analysis.AudioStartOffset,
            analysis.AudioByteCount).ToArray();
        byte[] seekTable = new byte[checked(
            analysis.FrameOffsets.Count * sizeof(uint))];
        for (int index = 0; index < analysis.FrameOffsets.Count; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                seekTable.AsSpan(index * sizeof(uint), sizeof(uint)),
                checked((uint)analysis.FrameOffsets[index]));
        }

        LoadedSound template = target.Template;
        var replacement = new LoadedSound
        {
            Offset = template.Offset,
            RuntimeAddress = template.RuntimeAddress,
            NamePointer = template.NamePointer,
            Name = template.Name,
            PhysicalDataByteCount = physicalData.Length,
            FrameCount = checked((ushort)durationMilliseconds),
            ChannelCount = checked((ushort)analysis.ChannelCount),
            SampleRate = checked((ushort)analysis.SampleRate),
            Pad0E = template.Pad0E,
            Pad10 = template.Pad10,
            SeekTableCount = checked((ushort)analysis.FrameOffsets.Count),
            SeekTablePointer = template.SeekTablePointer,
            SeekTable = seekTable,
            PhysicalDataPointer = template.PhysicalDataPointer,
            PhysicalData = physicalData
        };
        SoundPreviewMaterialization preview = SoundPreviewMaterialization.Playable(
            analysis.FormatName,
            analysis.SampleRate,
            analysis.ChannelCount,
            physicalData.Length,
            analysis.Duration,
            analysis.Levels,
            physicalData,
            "Ready to preview the imported MPEG audio payload.");
        return new SoundImportCandidate(
            target.AliasIndex,
            target.FileIndex,
            replacement,
            preview);
    }
}

internal sealed record SoundExportPayload(
    byte[] Bytes,
    string SuggestedFileName);

internal sealed class SoundPreviewMaterialization
{
    private SoundPreviewMaterialization(
        string formatText,
        int sampleRate,
        int channelCount,
        int storedByteCount,
        TimeSpan duration,
        IReadOnlyList<double> frameGainProfile,
        byte[]? physicalData,
        string availabilityMessage)
    {
        FormatText = formatText;
        SampleRate = sampleRate;
        ChannelCount = channelCount;
        StoredByteCount = storedByteCount;
        Duration = duration;
        FrameGainProfile = frameGainProfile;
        PhysicalData = physicalData;
        AvailabilityMessage = availabilityMessage;
    }

    public string FormatText { get; }

    private int SampleRate { get; }

    private int ChannelCount { get; }

    private int StoredByteCount { get; }

    internal TimeSpan Duration { get; }

    internal IReadOnlyList<double> FrameGainProfile { get; }

    internal byte[]? PhysicalData { get; }

    internal string AvailabilityMessage { get; }

    internal bool HasMpegPayload => PhysicalData is not null &&
        Duration > TimeSpan.Zero &&
        FrameGainProfile.Count > 0;

    public string SampleRateText => SampleRate > 0
        ? $"{SampleRate / 1000d:0.###} kHz"
        : "Unknown";

    public string ChannelsText => ChannelCount switch
    {
        1 => "Mono",
        2 => "Stereo",
        > 2 => $"{ChannelCount:N0} channels",
        _ => "Unknown"
    };

    public string StoredSizeText => SoundPreviewFormat.Bytes(StoredByteCount);

    internal static SoundPreviewMaterialization Playable(
        string formatText,
        int sampleRate,
        int channelCount,
        int storedByteCount,
        TimeSpan duration,
        IReadOnlyList<double> frameGainProfile,
        byte[] physicalData,
        string availabilityMessage) =>
        new(
            formatText,
            sampleRate,
            channelCount,
            storedByteCount,
            duration,
            frameGainProfile,
            physicalData,
            availabilityMessage);

    internal static SoundPreviewMaterialization Failed(
        string formatText,
        int storedByteCount,
        string availabilityMessage,
        int sampleRate = 0,
        int channelCount = 0,
        byte[]? physicalData = null) =>
        new(
            formatText,
            sampleRate,
            channelCount,
            storedByteCount,
            TimeSpan.Zero,
            [],
            physicalData,
            availabilityMessage);
}

internal static class SoundPreviewFormat
{
    public static string Bytes(int byteCount)
    {
        if (byteCount <= 0)
            return "No data";
        if (byteCount < 1024)
            return $"{byteCount:N0} bytes";
        return $"{byteCount / 1024d:N1} KiB";
    }
}
