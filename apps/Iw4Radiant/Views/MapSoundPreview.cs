using System.Numerics;
using Avalonia.Threading;
using Iw4Radiant.Audio;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

/// <summary>Auditions placed sounds using the camera as the listener.</summary>
internal sealed class MapSoundPreview : IDisposable
{
    private readonly AudioPreviewEngine _engine;
    private readonly DispatcherTimer _completionTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Dictionary<(string Name, SoundEmitterSettings Settings), SourceSound> _sources = [];
    private IReadOnlyDictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview> _preparedSources =
        new Dictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview>();
    private string? _preparedRoot;
    private readonly Dictionary<(MapEntity Owner, int Slot), Voice> _voices = [];
    private Emitter[] _emitters = [];
    private string? _sourceDirectory;
    private int _sourceGeneration;
    private Vector3 _listener;
    private Vector3 _right = Vector3.UnitX;
    private bool _enabled;
    private bool _suspended;
    private bool _disposed;
    private (string Message, string? Detail)? _status;

    internal event Action<string, string?>? StatusChanged;

    internal MapSoundPreview(AudioPreviewEngine engine)
    {
        _engine = engine;
        _completionTimer.Tick += OnCompletionTick;
    }

    private void OnCompletionTick(object? sender, EventArgs args)
    {
        if (_disposed || !_enabled || _suspended)
        {
            _completionTimer.Stop();
            return;
        }
        bool changed = false;
        foreach (Voice voice in _voices.Values)
        {
            if (voice.Player is not { HasPendingWork: true } player) continue;
            bool audible = player.IsAudible;
            bool delayed = player.HasPendingDelay;
            bool channel = player.HasUnrecoveredChannel;
            try
            {
                player.Update(voice.Distance, voice.Pan);
                if (player.HasEnded)
                {
                    RetireVoice(voice);
                    voice.Completed = true;
                    changed = true;
                }
                else changed |= audible != player.IsAudible || delayed != player.HasPendingDelay ||
                    channel != player.HasUnrecoveredChannel;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                InvalidOperationException or PlatformNotSupportedException or DllNotFoundException or
                EntryPointNotFoundException or BadImageFormatException)
            {
                RetireVoice(voice);
                voice.Error = exception.Message;
                changed = true;
            }
        }
        _completionTimer.IsEnabled = _voices.Values.Any(voice => voice.Player?.HasPendingWork == true);
        if (changed) PublishPlaybackStatus();
    }

    internal void SetPreparedSources(string? root,
        IReadOnlyDictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview> sources)
    {
        if (_disposed) return;
        _preparedRoot = root;
        _preparedSources = root is null
            ? new Dictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview>()
            : new Dictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview>(sources);
        StopVoices();
        _sources.Clear();
        _sourceGeneration = -1;
    }

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
        int generation = _engine.Generation;
        if (_disposed || (_sourceDirectory == sourceDirectory && _sourceGeneration == generation &&
            _emitters.SequenceEqual(emitters))) return;
        if (_sourceDirectory != sourceDirectory || _sourceGeneration != generation)
        {
            StopVoices();
            _sources.Clear();
            _sourceDirectory = sourceDirectory;
            _sourceGeneration = generation;
        }
        _emitters = emitters.ToArray();
        var current = _emitters.ToDictionary(emitter => (emitter.Owner, emitter.Slot));
        foreach (var (key, voice) in _voices.ToArray())
            if (!current.TryGetValue(key, out var emitter) || emitter.Name != voice.Name ||
                emitter.Settings != voice.Settings || emitter.Error is not null)
            {
                RetireVoice(voice);
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
                    if (_preparedRoot == sourceDirectory && _preparedSources.TryGetValue(key, out PreparedPreview? ready))
                        SetSource(source, ready);
                    else if (_engine.TryGetPrepared(sourceDirectory, key.Name, out ready))
                        SetSource(source, ready);
                    else
                        _ = LoadSourceAsync(sourceDirectory, key, source);
                }
        UpdatePlayback();
    }

    private async Task LoadSourceAsync(string root, (string Name, SoundEmitterSettings Settings) key, SourceSound source)
    {
        // Reading alias graphs and audio stays off navigation and drag callbacks.
        // The entry identity prevents a late load from reviving an old map/library.
        PreparedPreview loaded = await _engine.PrepareAsync(root, key.Name);
        if (_disposed || !_sources.TryGetValue(key, out SourceSound? current) || !ReferenceEquals(current, source))
            return;
        SetSource(source, loaded);
        UpdatePlayback();
    }

    private static void SetSource(SourceSound source, PreparedPreview loaded)
    {
        source.Prepared = loaded;
        source.Error = loaded.Error;
        source.Loading = false;
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
            _completionTimer.Stop();
            foreach (Voice voice in _voices.Values)
            {
                voice.Player?.Pause();
            }
            Publish(!_enabled ? "Map sounds off · use Sounds above the camera." : "Map sounds paused while you listen to one sound.");
            return;
        }
        if (_sourceDirectory is null)
        {
            Publish("Choose the raw sound library to hear placed sounds.");
            return;
        }
        if (!_engine.IsSupported)
        {
            Publish(_engine.UnavailableReason ?? "Placed sound preview is unavailable.");
            return;
        }

        foreach (var emitter in _emitters)
        {
            if (emitter.Error is not null || !_sources.TryGetValue((emitter.Name, emitter.Settings), out SourceSound? source) ||
                source.Loading || source.Error is not null || source.Prepared is null)
                continue;
            float distance = Vector3.Distance(_listener, emitter.Origin);
            var key = (emitter.Owner, emitter.Slot);
            _voices.TryGetValue(key, out Voice? voice);
            if (voice is null) _voices.Add(key, voice = new Voice(emitter.Name, emitter.Settings));
            if (voice.Error is not null || voice.Completed) continue;
            try
            {
                // Editor stereo positioning, not a reconstruction of the game's speaker mixer.
                float pan = distance > 0.001f ? Vector3.Dot((emitter.Origin - _listener) / distance, _right) : 0;
                voice.Distance = distance;
                voice.Pan = pan;
                if (voice.Player is null)
                    voice.Player = _engine.Play(source.Prepared, emitter.Settings, distance, pan);
                else voice.Player.Update(distance, pan);
                if (voice.Player.HasEnded)
                {
                    // Rejected and finished one-shots never retrigger on camera movement.
                    RetireVoice(voice);
                    voice.Completed = true;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                              InvalidOperationException or PlatformNotSupportedException or
                                              DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                RetireVoice(voice);
                voice.Player = null;
                voice.Error = exception.Message;
            }
        }
        // Advance queued delays and completion chains while idle; ready loop-only cues need no polling.
        _completionTimer.IsEnabled = _voices.Values.Any(voice => voice.Player?.HasPendingWork == true);
        PublishPlaybackStatus();
    }

    private void PublishPlaybackStatus()
    {
        int playing = _voices.Values.Count(voice => voice.Player?.IsAudible == true);
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
            : _voices.Values.Any(voice => voice.Player?.HasPendingDelay == true) ? "Waiting for delayed sounds…"
            : _voices.Values.Any(voice => voice.Completed) ? "One-shot playback finished · toggle map sounds to replay."
            : "Map sounds on · move closer to a sound marker.";
        if (playing > 0 && loading > 0) message += $" Preparing {loading} more.";
        if (unavailable.Length > 0) message += $" {unavailable.Length} unavailable (details on hover).";
        string previewScope = _voices.Values.Any(voice => voice.Player?.HasUnrecoveredChannel == true)
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
        _completionTimer.Stop();
        foreach (Voice voice in _voices.Values) RetireVoice(voice);
        _voices.Clear();
    }

    private static void RetireVoice(Voice voice)
    {
        voice.Player?.Dispose();
        voice.Player = null;
    }

    public void Dispose()
    {
        _disposed = true;
        StopVoices();
        _completionTimer.Tick -= OnCompletionTick;
        _sources.Clear();
        _preparedSources = new Dictionary<(string Name, SoundEmitterSettings Settings), PreparedPreview>();
        _emitters = [];
    }

    private sealed class SourceSound
    {
        internal bool Loading = true;
        internal PreparedPreview? Prepared;
        internal string? Error;
    }

    private sealed class Voice(string name, SoundEmitterSettings settings)
    {
        internal string Name { get; } = name;
        internal SoundEmitterSettings Settings { get; } = settings;
        internal SoundPreviewPlayback? Player;
        internal float Distance;
        internal float Pan;
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
