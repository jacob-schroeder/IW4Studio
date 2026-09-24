using System.Numerics;
using System.Text.Json;
using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets.Sound;
using IW4.Studio.Desktop.Editors.Sound;

namespace Iw4Radiant.Views;

/// <summary>Auditions the closest audible placed alias while the mapper moves the camera.</summary>
internal sealed class MapSoundPreview : IDisposable
{
    private readonly Dictionary<string, Attenuation> _profiles = new(StringComparer.Ordinal);
    private SoundPreviewPlayer? _player;
    private (string Name, Vector3 Origin)[] _emitters = [];
    private string? _sourceDirectory;
    private string? _playingName;
    private string? _failedName;
    private Vector3 _listener;
    private bool _enabled;
    private bool _suspended;
    private bool _disposed;
    private bool _sourceNoticeShown;

    internal event Action<string>? StatusChanged;

    internal void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (!enabled)
        {
            StopCurrent();
            return;
        }
        UpdateListener(_listener);
    }

    internal void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        if (suspended)
        {
            StopCurrent();
        }
        else UpdateListener(_listener);
    }

    internal void Configure(string? sourceDirectory, IReadOnlyList<(string Name, Vector3 Origin)> emitters)
    {
        if (_sourceDirectory == sourceDirectory && _emitters.SequenceEqual(emitters)) return;
        _sourceDirectory = sourceDirectory;
        _sourceNoticeShown = false;
        _emitters = emitters.ToArray();
        _profiles.Clear();
        _failedName = null;
        StopCurrent();
        if (sourceDirectory is not null)
            foreach (string name in _emitters.Select(emitter => emitter.Name).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    SndAlias? alias = new SoundAliasListExchange().Link(sourceDirectory, name)
                        .Aliases.FirstOrDefault();
                    if (alias is not null && float.IsFinite(alias.DistanceMin) &&
                        float.IsFinite(alias.DistanceMax) && alias.DistanceMin >= 0 &&
                        alias.DistanceMax > alias.DistanceMin &&
                        float.IsFinite(alias.VolumeMin) && float.IsFinite(alias.VolumeMax))
                    {
                        // The game samples between these limits on each play; a fixed midpoint
                        // keeps editor preview volume stable while the mapper navigates.
                        float volume = Math.Clamp((alias.VolumeMin + alias.VolumeMax) * 0.5f, 0f, 1f);
                        _profiles[name] = new Attenuation(alias.DistanceMin, alias.DistanceMax,
                            volume, alias.VolumeFalloffCurve);
                    }
                }
                catch (Exception exception) when (FileOperationErrors.IsExpected(exception) || exception is JsonException)
                {
                    // Other placed sounds can still be heard; the unresolved alias stays silent.
                }
            }
        UpdateListener(_listener);
    }

    internal void UpdateListener(Vector3 position)
    {
        _listener = position;
        if (!_enabled || _suspended || _disposed) return;
        if (_sourceDirectory is null)
        {
            StopCurrent();
            if (!_sourceNoticeShown)
            {
                _sourceNoticeShown = true;
                StatusChanged?.Invoke("Choose the raw sound library to hear placed sounds.");
            }
            return;
        }

        string? closest = null;
        float closestDistance = float.PositiveInfinity;
        foreach (var (name, origin) in _emitters)
        {
            if (name == _failedName || !_profiles.TryGetValue(name, out Attenuation profile)) continue;
            float distance = Vector3.DistanceSquared(position, origin);
            if (distance >= profile.MaxDistance * profile.MaxDistance || distance >= closestDistance) continue;
            closest = name;
            closestDistance = distance;
        }
        if (closest is null)
        {
            if (_playingName is not null)
            {
                StopCurrent();
                StatusChanged?.Invoke("No placed sound is within hearing range of the camera.");
            }
            return;
        }

        float gain = _profiles[closest].Gain(MathF.Sqrt(closestDistance));
        if (closest == _playingName && _player is not null)
        {
            _player.SetVolume(gain);
            return;
        }

        StopCurrent();
        if (!SoundPreviewPlayer.IsPlatformSupported)
        {
            _failedName = closest;
            StatusChanged?.Invoke(SoundPreviewPlayer.UnavailableReason ?? "Placed sound preview is unavailable.");
            return;
        }
        string? error = SoundAliasAudition.LoadAudio(_sourceDirectory, closest, out byte[] audio);
        if (error is not null)
        {
            _failedName = closest;
            StatusChanged?.Invoke($"Placed sound '{closest}': {error}");
            return;
        }

        SoundPreviewPlayer? player = null;
        try
        {
            player = new SoundPreviewPlayer(audio);
            player.SetNumberOfLoops(-1);
            player.SetVolume(gain);
            player.Play();
            _player = player;
            _playingName = closest;
            StatusChanged?.Invoke($"Hearing placed sound '{closest}'. Volume follows camera distance; one sound plays at a time.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                          InvalidOperationException or PlatformNotSupportedException or
                                          DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            player?.Dispose();
            _failedName = closest;
            StatusChanged?.Invoke($"Placed sound '{closest}': {exception.Message}");
        }
    }

    private void StopCurrent()
    {
        _player?.Dispose();
        _player = null;
        _playingName = null;
    }

    public void Dispose()
    {
        _disposed = true;
        StopCurrent();
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
                float point = (fraction - left.X) / (right.X - left.X);
                return BaseVolume * Math.Clamp(left.Y + (right.Y - left.Y) * point, 0f, 1f);
            }
            return BaseVolume * (1f - fraction);
        }
    }
}
