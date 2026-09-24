using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions the first language row of the first alias variant from exported PS3 sound source.</summary>
internal sealed class SoundAliasAudition : IDisposable
{
    private readonly object _gate = new();
    private Process? _process;
    private string? _temporaryPath;
    private bool _disposed;

    // Raised on the process exit thread. Callers updating Avalonia controls must dispatch to the UI thread.
    internal event Action<string?>? PlaybackEnded;

    internal bool IsPlaying
    {
        get { lock (_gate) return _process is not null; }
    }

    /// <returns>Null when playback starts, otherwise a message suitable for the browser status line.</returns>
    internal string? Play(string rawRoot, string exactAliasName)
    {
        Stop();
        if (IsPlaying)
            return "The current sound preview could not be stopped.";
        if (_disposed)
            return "Sound preview is closed.";
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/afplay"))
            return "Sound preview requires macOS afplay.";

        string? loadError = LoadAudio(rawRoot, exactAliasName, out byte[] audio);
        if (loadError is not null) return loadError;

        string? temporaryPath = null;
        Process? process = null;
        bool started = false;
        try
        {
            temporaryPath = Path.Combine(Path.GetTempPath(), $"iw4-sound-{Guid.NewGuid():N}.mp3");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(audio);

            var start = new ProcessStartInfo("/usr/bin/afplay")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(temporaryPath);
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            lock (_gate)
            {
                if (_disposed)
                    return "Sound preview is closed.";
                _process = process;
                _temporaryPath = temporaryPath;
                process.Exited += OnProcessExited;
                if (!process.Start())
                    throw new InvalidOperationException("afplay did not start.");
                started = true;
            }
            return null;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) ||
                                          exception is Win32Exception or JsonException)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _temporaryPath = null;
                }
            }
            return $"Cannot preview this sound: {exception.Message}";
        }
        finally
        {
            if (!started && process is not null)
            {
                process.Exited -= OnProcessExited;
                process.Dispose();
            }
            if (!started && temporaryPath is not null)
                DeleteTemporaryFile(temporaryPath);
        }
    }

    internal void Stop()
    {
        string? path;
        lock (_gate)
        {
            Process? process = _process;
            if (process is null)
                return;
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { return; }

            path = _temporaryPath;
            _process = null;
            _temporaryPath = null;
            process.Exited -= OnProcessExited;
            process.Dispose();
        }
        if (path is not null)
            DeleteTemporaryFile(path);
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }

    internal static string? LoadAudio(string rawRoot, string exactAliasName, out byte[] audio)
    {
        audio = [];
        if (string.IsNullOrWhiteSpace(rawRoot) || string.IsNullOrWhiteSpace(exactAliasName))
            return "Choose a sound and an exported raw sound library first.";
        try
        {
            SoundAliasListAsset asset = new SoundAliasListExchange().Link(rawRoot, exactAliasName);
            SoundFile? file = asset.Aliases.FirstOrDefault()?.SoundFiles.FirstOrDefault();
            if (file?.Loaded?.LoadedSound?.PhysicalData is not { Length: > 0 } data)
                return "This alias has no exported loaded audio in its first language row. Streamed audio cannot be previewed.";
            if (!IsMpegLayerThree(data))
                return "This alias uses an audio format that the sound preview does not support.";
            audio = data;
            return null;
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is JsonException)
        {
            return $"Cannot preview this sound: {exception.Message}";
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        if (sender is not Process process)
            return;

        string? path;
        Action<string?>? ended;
        string? error;
        lock (_gate)
        {
            if (!ReferenceEquals(_process, process))
                return;
            path = _temporaryPath;
            _process = null;
            _temporaryPath = null;
            process.Exited -= OnProcessExited;
            error = process.ExitCode == 0 ? null : "The system audio player could not play this sound.";
            ended = PlaybackEnded;
        }
        process.Dispose();
        if (path is not null)
            DeleteTemporaryFile(path);
        ended?.Invoke(error);
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

    private static void DeleteTemporaryFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
