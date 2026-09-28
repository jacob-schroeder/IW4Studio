using Avalonia.Threading;
using Iw4Radiant.Audio;
using Iw4Radiant.MapSource;
using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions the first language row of the first alias variant from exported PS3 sound source.</summary>
internal sealed class SoundAliasAudition : IDisposable
{
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly AudioPreviewEngine _engine;
    private PreviewVoice? _player;
    private bool _disposed;
    private int _generation;

    internal SoundAliasAudition(AudioPreviewEngine engine)
    {
        _engine = engine;
        _playbackTimer.Tick += OnPlaybackTimerTick;
    }

    internal event Action? PlaybackEnded;

    /// <returns>Null when playback starts, otherwise a message suitable for the browser status line.</returns>
    internal async Task<string?> PlayAsync(string rawRoot, string exactAliasName, SoundEmitterSettings? settings = null)
    {
        Stop();
        int generation = _generation;
        if (_disposed)
            return "Sound preview is closed.";
        if (!_engine.IsSupported)
            return _engine.UnavailableReason ?? "Sound preview playback is unavailable.";

        PreparedPreview loaded = await _engine.PrepareAsync(rawRoot, exactAliasName, settings);
        if (_disposed || generation != _generation) return null;
        if (loaded.Error is not null) return loaded.Error;

        PreviewVoice? player = null;
        try
        {
            player = _engine.Play(loaded.Sound!, loaded.Profile?.Looping == true,
                loaded.Profile?.Volume ?? 1, 0);
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
        _generation++;
        _playbackTimer.Stop();
        PreviewVoice? player = _player;
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

    private static string? LoadAudio(string rawRoot, string exactAliasName, out byte[] audio, out SndAlias? alias)
    {
        audio = [];
        alias = null;
        if (string.IsNullOrWhiteSpace(rawRoot) || string.IsNullOrWhiteSpace(exactAliasName))
            return "Choose a sound and an exported raw sound library first.";
        try
        {
            byte[]? firstStream = null;
            SoundAliasListAsset asset = new SoundAliasListExchange().Link(rawRoot, exactAliasName, bytes =>
            {
                firstStream ??= bytes;
                return new StreamedSound
                {
                    FileIndex = StreamedSound.NamedFileIndex,
                    Source = new StreamedSoundFileSource { StreamFileLength = bytes.Length }
                };
            });
            alias = asset.Aliases.FirstOrDefault();
            if (alias is null)
                return "This alias has no variants to preview.";

            SoundFile? file = alias.SoundFiles.FirstOrDefault();
            if (file is null)
                return "The first alias variant has no sound file to preview.";
            byte[]? data = file.Streamed is not null ? firstStream : file.Loaded?.LoadedSound?.PhysicalData;
            if (data is not { Length: > 0 })
                return "The first alias variant has no exported audio payload.";
            // Native streamed payloads prefix the MPEG frames with an ID3 metadata block.
            if (data.Length >= 10 && data.AsSpan(0, 3).SequenceEqual("ID3"u8))
            {
                if ((data[6] | data[7] | data[8] | data[9]) >= 128)
                    return "This sound has an invalid ID3 metadata length.";
                int offset = 10 + (data[6] << 21) + (data[7] << 14) + (data[8] << 7) + data[9];
                if (data[3] == 4 && (data[5] & 0x10) != 0) offset += 10;
                if (offset >= data.Length) return "This sound has no audio after its metadata.";
                data = data[offset..];
            }
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

    internal static (byte[] Audio, SoundEmitterPlayback? Profile, string? Error) LoadPlayback(
        string root, string name, SoundEmitterSettings? settings)
    {
        string? error = LoadAudio(root, name, out byte[] audio, out SndAlias? alias);
        if (error is not null || alias is null) return ([], null, error ?? "This sound has no alias to preview.");
        try
        {
            SoundEmitterPlayback? profile = settings?.Resolve(alias);
            return (SoundPreviewPitch.Apply(audio, profile?.Pitch ?? 1), profile, null);
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) ||
            exception is PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return ([], null, $"Cannot preview this sound: {exception.Message}");
        }
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs args)
    {
        PreviewVoice? player = _player;
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
