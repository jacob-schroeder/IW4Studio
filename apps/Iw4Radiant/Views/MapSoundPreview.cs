using System.Numerics;
using Iw4Radiant.MapSource;
using IW4.Studio.Desktop.Editors.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions placed sounds using the camera as the listener.</summary>
internal sealed class MapSoundPreview : IDisposable
{
    private readonly Dictionary<(string Name, SoundEmitterSettings Settings), SourceSound> _sources = [];
    private readonly Dictionary<(MapEntity Owner, int Slot), Voice> _voices = [];
    private Emitter[] _emitters = [];
    private string? _sourceDirectory;
    private Vector3 _listener;
    private Vector3 _right = Vector3.UnitX;
    private bool _enabled;
    private bool _suspended;
    private bool _disposed;
    private (string Message, string? Detail)? _status;

    internal event Action<string, string?>? StatusChanged;

    internal void SetEnabled(bool enabled)
    {
        if (_enabled == enabled || _disposed) return;
        _enabled = enabled;
        if (!enabled) StopVoices();
        UpdatePlayback();
    }

    internal void SetSuspended(bool suspended)
    {
        if (_suspended == suspended || _disposed) return;
        _suspended = suspended;
        UpdatePlayback();
    }

    internal void Configure(string? sourceDirectory,
        IReadOnlyList<Emitter> emitters)
    {
        if (_disposed || (_sourceDirectory == sourceDirectory && _emitters.SequenceEqual(emitters))) return;
        if (_sourceDirectory != sourceDirectory)
        {
            StopVoices();
            _sources.Clear();
            _sourceDirectory = sourceDirectory;
        }
        _emitters = emitters.ToArray();
        var current = _emitters.ToDictionary(emitter => (emitter.Owner, emitter.Slot));
        foreach (var (key, voice) in _voices.ToArray())
            if (!current.TryGetValue(key, out var emitter) || emitter.Name != voice.Name ||
                emitter.Settings != voice.Settings || emitter.Error is not null)
            {
                voice.Player?.Dispose();
                _voices.Remove(key);
            }
        var sources = _emitters.Where(emitter => emitter.Error is null)
            .Select(emitter => (emitter.Name, emitter.Settings)).ToHashSet();
        foreach (var key in _sources.Keys.Where(key => !sources.Contains(key)).ToArray())
            _sources.Remove(key);
        if (sourceDirectory is not null)
            foreach (var key in sources)
                if (!_sources.ContainsKey(key))
                {
                    var source = new SourceSound();
                    _sources.Add(key, source);
                    _ = LoadSourceAsync(sourceDirectory, key, source);
                }
        UpdatePlayback();
    }

    private async Task LoadSourceAsync(string root, (string Name, SoundEmitterSettings Settings) key, SourceSound source)
    {
        // Reading alias graphs and audio stays off navigation and drag callbacks.
        // The entry identity prevents a late load from reviving an old map/library.
        var loaded = await Task.Run(() => SoundAliasAudition.LoadPlayback(root, key.Name, key.Settings));
        if (_disposed || !_sources.TryGetValue(key, out SourceSound? current) || !ReferenceEquals(current, source))
            return;
        source.Audio = loaded.Error is null ? loaded.Audio : [];
        source.Profile = loaded.Profile;
        source.Error = loaded.Error;
        source.Loading = false;
        UpdatePlayback();
    }

    internal void UpdateListener(Vector3 position, Vector3 right)
    {
        _listener = position;
        _right = right;
        UpdatePlayback();
    }

    private void UpdatePlayback()
    {
        if (_disposed) return;
        if (!_enabled || _suspended)
        {
            foreach (Voice voice in _voices.Values) voice.Player?.Pause();
            Publish(!_enabled ? "Map sounds off · use Sounds above the camera." : "Map sounds paused while you listen to one sound.");
            return;
        }
        if (_sourceDirectory is null)
        {
            Publish("Choose the raw sound library to hear placed sounds.");
            return;
        }
        if (!SoundPreviewPlayer.IsPlatformSupported)
        {
            Publish(SoundPreviewPlayer.UnavailableReason ?? "Placed sound preview is unavailable.");
            return;
        }

        int playing = 0;
        foreach (var emitter in _emitters)
        {
            if (emitter.Error is not null || !_sources.TryGetValue((emitter.Name, emitter.Settings), out SourceSound? source) ||
                source.Loading || source.Error is not null || source.Profile is not { } profile)
                continue;
            float distance = Vector3.Distance(_listener, emitter.Origin);
            float gain = profile.Gain(distance);
            var key = (emitter.Owner, emitter.Slot);
            _voices.TryGetValue(key, out Voice? voice);
            if (voice is null) _voices.Add(key, voice = new Voice(emitter.Name, emitter.Settings));
            if (voice.Error is not null || voice.Completed) continue;
            if (!profile.Looping && (voice.Player?.HasEnded == true || (voice.Player is null && gain <= 0)))
            {
                // A one-shot occurs once when map preview starts. Camera motion must not retrigger it.
                voice.Completed = true;
                continue;
            }
            if (gain <= 0 && profile.Looping)
            {
                voice?.Player?.Pause();
                continue;
            }
            try
            {
                if (voice.Player is null)
                {
                    voice.Player = new SoundPreviewPlayer(source.Audio);
                    voice.Player.SetNumberOfLoops(profile.Looping ? -1 : 0);
                }
                voice.Player.SetVolume(gain);
                // Editor stereo positioning, not a reconstruction of the game's speaker mixer.
                voice.Player.SetPan(distance > 0.001f ? Vector3.Dot((emitter.Origin - _listener) / distance, _right) : 0);
                voice.Player.Play();
                if (gain > 0) playing++;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                              InvalidOperationException or PlatformNotSupportedException or
                                              DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                voice.Player?.Dispose();
                voice.Player = null;
                voice.Error = exception.Message;
            }
        }

        string[] unavailable = _sources.Where(pair => pair.Value.Error is not null)
            .Select(pair => $"{pair.Key.Name}: {pair.Value.Error}")
            .Concat(_emitters.Where(emitter => emitter.Error is not null).Select(emitter => $"{emitter.Name}: {emitter.Error}"))
            .Concat(_voices.Values.Where(voice => voice.Error is not null)
                .Select(voice => $"{voice.Name}: {voice.Error}")).Distinct(StringComparer.Ordinal).ToArray();
        int loading = _sources.Values.Count(source => source.Loading);
        string message = playing > 0 ? $"Hearing {playing} placed {(playing == 1 ? "sound" : "sounds")} · camera distance and direction."
            : _emitters.Length == 0 ? "Map sounds on · place a sound to hear it."
            : loading > 0 ? "Preparing placed sounds…"
            : unavailable.Length > 0 && _sources.Values.All(source => source.Error is not null) ? "No placed sounds can be previewed."
            : _voices.Values.Any(voice => voice.Completed) ? "One-shot playback finished · toggle map sounds to replay."
            : "Map sounds on · move closer to a sound marker.";
        if (playing > 0 && loading > 0) message += $" Preparing {loading} more.";
        if (unavailable.Length > 0) message += $" {unavailable.Length} unavailable (details on hover).";
        string previewScope = _sources.Values.Any(source => source.Profile is { Channel: not (3 or 24) })
            ? "This channel's spatial behavior is not yet reproduced; preview uses editor distance and stereo positioning."
            : "Editor distance and stereo preview; game channel mixing and voice priorities are applied in-game.";
        Publish(message, unavailable.Length == 0 ? previewScope : string.Join('\n', unavailable));
    }

    private void Publish(string message, string? detail = null)
    {
        if (_status == (message, detail)) return;
        _status = (message, detail);
        StatusChanged?.Invoke(message, detail);
    }

    private void StopVoices()
    {
        foreach (Voice voice in _voices.Values) voice.Player?.Dispose();
        _voices.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        StopVoices();
        _sources.Clear();
        _emitters = [];
    }

    private sealed class SourceSound
    {
        internal bool Loading = true;
        internal byte[] Audio = [];
        internal SoundEmitterPlayback? Profile;
        internal string? Error;
    }

    private sealed class Voice(string name, SoundEmitterSettings settings)
    {
        internal string Name { get; } = name;
        internal SoundEmitterSettings Settings { get; } = settings;
        internal SoundPreviewPlayer? Player;
        internal string? Error;
        internal bool Completed;
    }

    internal sealed record Emitter(MapEntity Owner, int Slot, string Name, Vector3 Origin,
        SoundEmitterSettings Settings, string? Error)
    {
        internal static Emitter From(MapEntity owner, int slot, MapEntity entity, Vector3 origin)
        {
            string name = entity.Properties.GetValueOrDefault("soundalias", "");
            try { return new Emitter(owner, slot, name, origin, SoundEmitterSettings.Read(entity), null); }
            catch (ArgumentException exception) { return new Emitter(owner, slot, name, origin, new(), exception.Message); }
        }
    }
}
