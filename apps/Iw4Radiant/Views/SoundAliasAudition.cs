using Avalonia.Threading;
using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets.Sound;
using IW4.Studio.Desktop.Editors.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions the first language row of the first alias variant from exported PS3 sound source.</summary>
internal sealed class SoundAliasAudition : IDisposable
{
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private SoundPreviewPlayer? _player;
    private bool _disposed;

    internal SoundAliasAudition() => _playbackTimer.Tick += OnPlaybackTimerTick;

    internal event Action? PlaybackEnded;

    /// <returns>Null when playback starts, otherwise a message suitable for the browser status line.</returns>
    internal string? Play(string rawRoot, string exactAliasName)
    {
        Stop();
        if (_disposed)
            return "Sound preview is closed.";
        if (!SoundPreviewPlayer.IsPlatformSupported)
            return SoundPreviewPlayer.UnavailableReason ?? "Sound preview playback is unavailable.";

        string? loadError = LoadAudio(rawRoot, exactAliasName, out byte[] audio);
        if (loadError is not null) return loadError;

        SoundPreviewPlayer? player = null;
        try
        {
            player = new SoundPreviewPlayer(audio);
            player.Play();
            _player = player;
            _playbackTimer.Start();
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                          InvalidOperationException or PlatformNotSupportedException or
                                          DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            player?.Dispose();
            return $"Cannot preview this sound: {exception.Message}";
        }
    }

    internal void Stop()
    {
        _playbackTimer.Stop();
        SoundPreviewPlayer? player = _player;
        _player = null;
        player?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _playbackTimer.Tick -= OnPlaybackTimerTick;
    }

    internal static string? LoadAudio(string rawRoot, string exactAliasName, out byte[] audio) =>
        LoadAudio(rawRoot, exactAliasName, out audio, out _);

    internal static string? LoadAudio(string rawRoot, string exactAliasName, out byte[] audio, out SndAlias? alias)
    {
        audio = [];
        alias = null;
        if (string.IsNullOrWhiteSpace(rawRoot) || string.IsNullOrWhiteSpace(exactAliasName))
            return "Choose a sound and an exported raw sound library first.";
        try
        {
            SoundAliasListAsset asset = new SoundAliasListExchange().Link(rawRoot, exactAliasName);
            alias = asset.Aliases.FirstOrDefault();
            if (alias is null)
                return "This alias has no variants to preview.";

            SoundFile? file = alias.SoundFiles.FirstOrDefault();
            if (file is null)
                return "The first alias variant has no sound file to preview.";
            if (file.Streamed is not null)
                return "This alias uses streamed audio, which cannot be previewed from the exported loaded payload.";
            if (file.Loaded?.LoadedSound?.PhysicalData is not { Length: > 0 } data)
                return "The first alias variant has no exported loaded audio payload.";
            if (!IsMpegLayerThree(data))
                return "This alias uses an audio format that the sound preview does not support.";
            audio = data;
            return null;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is System.Text.Json.JsonException)
        {
            return $"Cannot preview this sound: {exception.Message}";
        }
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs args)
    {
        SoundPreviewPlayer? player = _player;
        if (player is null || !player.HasEnded) return;

        _playbackTimer.Stop();
        _player = null;
        player.Dispose();
        PlaybackEnded?.Invoke();
    }

    private static bool IsMpegLayerThree(ReadOnlySpan<byte> audio)
    {
        if (audio.Length < 4 || audio[0] != 0xff || (audio[1] & 0xe0) != 0xe0)
            return false;
        int version = (audio[1] >> 3) & 3;
        int layer = (audio[1] >> 1) & 3;
        int bitrate = (audio[2] >> 4) & 15;
        int sampleRate = (audio[2] >> 2) & 3;
        return version != 1 && layer == 1 && bitrate is > 0 and < 15 && sampleRate != 3;
    }
}
