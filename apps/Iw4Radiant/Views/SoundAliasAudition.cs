using Avalonia.Threading;
using Iw4Radiant.Audio;
using Iw4Radiant.MapSource;
using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions supported alias variants from exported PS3 sound source.</summary>
internal sealed class SoundAliasAudition : IDisposable
{
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly AudioPreviewEngine _engine;
    private SoundPreviewPlayback? _player;
    private bool _disposed;
    private int _generation;

    internal SoundAliasAudition(AudioPreviewEngine engine)
    {
        _engine = engine;
        _playbackTimer.Tick += OnPlaybackTimerTick;
    }

    internal event Action<string?>? PlaybackEnded;

    /// <returns>Null when playback starts, otherwise a message suitable for the browser status line.</returns>
    internal async Task<string?> PlayAsync(string rawRoot, string exactAliasName, SoundEmitterSettings? settings = null)
    {
        Stop();
        int generation = _generation;
        if (_disposed)
            return "Sound preview is closed.";
        if (!_engine.IsSupported)
            return _engine.UnavailableReason ?? "Sound preview playback is unavailable.";

        PreparedPreview loaded = await _engine.PrepareAsync(rawRoot, exactAliasName);
        if (_disposed || generation != _generation) return null;
        if (loaded.Error is not null) return loaded.Error;

        SoundPreviewPlayback? player = null;
        try
        {
            player = _engine.Play(loaded, settings);
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
        SoundPreviewPlayback? player = _player;
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

    internal static (LoadedVariant[] Variants, string? Error) LoadVariants(string rawRoot, string exactAliasName)
    {
        if (string.IsNullOrWhiteSpace(rawRoot) || string.IsNullOrWhiteSpace(exactAliasName))
            return ([], "Choose a sound and an exported raw sound library first.");
        try
        {
            var streams = new Dictionary<StreamedSound, byte[]>();
            SoundAliasListAsset asset = new SoundAliasListExchange().Link(rawRoot, exactAliasName, bytes =>
            {
                var stream = new StreamedSound
                {
                    FileIndex = StreamedSound.NamedFileIndex,
                    Source = new StreamedSoundFileSource { StreamFileLength = bytes.Length }
                };
                streams.Add(stream, bytes);
                return stream;
            });
            if (asset.Aliases.Count == 0)
                return ([], "This alias has no variants to preview.");
            var variants = new LoadedVariant[asset.Aliases.Count];
            for (int index = 0; index < variants.Length; index++)
            {
                SndAlias alias = asset.Aliases[index];
                if (alias.SoundFiles.Count == 0)
                    return ([], $"Alias variant {index + 1} has no sound file to preview.");
                // PS3 fixup 0x0010A600 stores the row-array base; the observed playback entries read it directly.
                SoundFile file = alias.SoundFiles[0];
                byte[]? data = file.Streamed is { } stream
                    ? streams.GetValueOrDefault(stream) : file.Loaded?.LoadedSound?.PhysicalData;
                if (file.Exists != 1 || data is not { Length: > 0 })
                    return ([], $"Alias variant {index + 1} has no exported audio payload.");
                // Native streamed payloads prefix the MPEG frames with an ID3 metadata block.
                if (data.Length >= 10 && data.AsSpan(0, 3).SequenceEqual("ID3"u8))
                {
                    if ((data[6] | data[7] | data[8] | data[9]) >= 128)
                        return ([], $"Alias variant {index + 1} has an invalid ID3 metadata length.");
                    int offset = 10 + (data[6] << 21) + (data[7] << 14) + (data[8] << 7) + data[9];
                    if (data[3] == 4 && (data[5] & 0x10) != 0) offset += 10;
                    if (offset >= data.Length) return ([], $"Alias variant {index + 1} has no audio after its metadata.");
                    data = data[offset..];
                }
                if (!IsMpegLayerThree(data))
                    return ([], $"Alias variant {index + 1} uses an audio format that the sound preview does not support.");
                variants[index] = new LoadedVariant(alias, data);
            }
            return (variants, null);
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is System.Text.Json.JsonException)
        {
            return ([], $"Cannot preview this sound: {exception.Message}");
        }
    }

    internal sealed record LoadedVariant(SndAlias Alias, byte[] Audio);

    private void OnPlaybackTimerTick(object? sender, EventArgs args)
    {
        SoundPreviewPlayback? player = _player;
        if (player is null) return;
        try
        {
            player.Update(null, 0);
            if (!player.HasEnded) return;
            Stop();
            PlaybackEnded?.Invoke(null);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
            InvalidOperationException or PlatformNotSupportedException or DllNotFoundException or
            EntryPointNotFoundException or BadImageFormatException)
        {
            Stop();
            PlaybackEnded?.Invoke($"Cannot preview this sound: {exception.Message}");
        }
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
