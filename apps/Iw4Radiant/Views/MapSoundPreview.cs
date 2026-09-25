using System.Numerics;
using Iw4Radiant.MapSource;
using IW4.Game.Assets.Sound;
using IW4.Studio.Desktop.Editors.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions placed loaded sounds using the camera as the listener.</summary>
internal sealed class MapSoundPreview : IDisposable
{
    private readonly Dictionary<string, SourceSound> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<(MapEntity Owner, int Slot), Voice> _voices = [];
    private (MapEntity Owner, int Slot, string Name, Vector3 Origin)[] _emitters = [];
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
        IReadOnlyList<(MapEntity Owner, int Slot, string Name, Vector3 Origin)> emitters)
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
            if (!current.TryGetValue(key, out var emitter) || emitter.Name != voice.Name)
            {
                voice.Player?.Dispose();
                _voices.Remove(key);
            }
        var names = _emitters.Select(emitter => emitter.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in _sources.Keys.Where(name => !names.Contains(name)).ToArray())
            _sources.Remove(name);
        if (sourceDirectory is not null)
            foreach (string name in names)
                if (!_sources.ContainsKey(name))
                {
                    var source = new SourceSound();
                    _sources.Add(name, source);
                    _ = LoadSourceAsync(sourceDirectory, name, source);
                }
        UpdatePlayback();
    }

    private async Task LoadSourceAsync(string root, string name, SourceSound source)
    {
        // Reading alias graphs and audio stays off navigation and drag callbacks.
        // The entry identity prevents a late load from reviving an old map/library.
        var loaded = await Task.Run(() =>
        {
            string? error = SoundAliasAudition.LoadAudio(root, name, out byte[] audio, out SndAlias? alias);
            Attenuation profile = default;
            if (error is null && alias is not null)
            {
                if (!float.IsFinite(alias.DistanceMin) || !float.IsFinite(alias.DistanceMax) ||
                    alias.DistanceMin < 0 || alias.DistanceMax <= alias.DistanceMin ||
                    !float.IsFinite(alias.VolumeMin) || !float.IsFinite(alias.VolumeMax))
                    error = "The alias has an invalid volume or hearing range.";
                else
                {
                    // Keep the audition repeatable; native random alias variation is separate.
                    float volume = Math.Clamp(alias.VolumeMin * 0.5f + alias.VolumeMax * 0.5f, 0f, 1f);
                    profile = new Attenuation(alias.DistanceMin, alias.DistanceMax, volume, alias.VolumeFalloffCurve);
                }
            }
            return (audio, profile, error);
        });
        if (_disposed || !_sources.TryGetValue(name, out SourceSound? current) || !ReferenceEquals(current, source))
            return;
        source.Audio = loaded.error is null ? loaded.audio : [];
        source.Profile = loaded.profile;
        source.Error = loaded.error;
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
        foreach (var (owner, slot, name, origin) in _emitters)
        {
            if (!_sources.TryGetValue(name, out SourceSound? source) || source.Loading || source.Error is not null)
                continue;
            float distance = Vector3.Distance(_listener, origin);
            float gain = float.IsFinite(distance) ? source.Profile.Gain(distance) : 0;
            var key = (owner, slot);
            _voices.TryGetValue(key, out Voice? voice);
            if (gain <= 0)
            {
                voice?.Player?.Pause();
                continue;
            }
            if (voice is null) _voices.Add(key, voice = new Voice(name));
            if (voice.Error is not null) continue;
            try
            {
                if (voice.Player is null)
                {
                    voice.Player = new SoundPreviewPlayer(source.Audio);
                    voice.Player.SetNumberOfLoops(-1);
                }
                voice.Player.SetVolume(gain);
                // Editor stereo positioning, not a reconstruction of the game's speaker mixer.
                voice.Player.SetPan(distance > 0.001f ? Vector3.Dot((origin - _listener) / distance, _right) : 0);
                voice.Player.Play();
                playing++;
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
            .Select(pair => $"{pair.Key}: {pair.Value.Error}")
            .Concat(_voices.Values.Where(voice => voice.Error is not null)
                .Select(voice => $"{voice.Name}: {voice.Error}")).Distinct(StringComparer.Ordinal).ToArray();
        int loading = _sources.Values.Count(source => source.Loading);
        string message = playing > 0 ? $"Hearing {playing} placed {(playing == 1 ? "sound" : "sounds")} · camera distance and direction."
            : _emitters.Length == 0 ? "Map sounds on · place a looping sound to hear it."
            : loading > 0 ? "Preparing placed sounds…"
            : unavailable.Length == _sources.Count ? "No placed sounds can be previewed."
            : "Map sounds on · move closer to a sound marker.";
        if (playing > 0 && loading > 0) message += $" Preparing {loading} more.";
        if (unavailable.Length > 0) message += $" {unavailable.Length} unavailable (details on hover).";
        Publish(message, unavailable.Length == 0 ? null : string.Join('\n', unavailable));
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
        internal Attenuation Profile;
        internal string? Error;
    }

    private sealed class Voice(string name)
    {
        internal string Name { get; } = name;
        internal SoundPreviewPlayer? Player;
        internal string? Error;
    }

    private readonly record struct Attenuation(float MinDistance, float MaxDistance,
        float BaseVolume, SndCurve? Curve)
    {
        internal float Gain(float distance)
        {
            if (distance <= MinDistance) return BaseVolume;
            if (distance >= MaxDistance) return 0;

            float fraction = (distance - MinDistance) / (MaxDistance - MinDistance);
            if (Curve is null || Curve.KnotCount < 2 || Curve.Knots.Count < Curve.KnotCount)
                return BaseVolume * (1f - fraction);

            for (int index = 1; index < Curve.KnotCount; index++)
            {
                SndCurveKnot left = Curve.Knots[index - 1];
                SndCurveKnot right = Curve.Knots[index];
                if (fraction > right.X) continue;
                if (!float.IsFinite(left.X) || !float.IsFinite(right.X) ||
                    !float.IsFinite(left.Y) || !float.IsFinite(right.Y) || right.X <= left.X)
                    break;
                float point = Math.Clamp((fraction - left.X) / (right.X - left.X), 0f, 1f);
                return BaseVolume * Math.Clamp(left.Y + (right.Y - left.Y) * point, 0f, 1f);
            }
            return BaseVolume * (1f - fraction);
        }
    }
}
