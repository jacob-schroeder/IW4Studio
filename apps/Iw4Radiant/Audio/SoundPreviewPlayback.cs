using System.Diagnostics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Audio;

/// <summary>Lifetime of one cue, its delayed secondary sounds, and completion chains.</summary>
internal sealed class SoundPreviewPlayback : IDisposable
{
    private const int MaxActiveParts = 24;
    private readonly AudioPreviewEngine _engine;
    private readonly SoundEmitterSettings? _chainSettings;
    private readonly List<Part> _parts;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _paused;
    private bool _disposed;

    internal SoundPreviewPlayback(AudioPreviewEngine engine, IReadOnlyList<PreparedSelection> selected,
        SoundEmitterSettings? settings, float? distance, float pan)
    {
        _engine = engine;
        // Chains clear request overrides but keep the emitter's spatial context.
        _chainSettings = settings is null ? null : new SoundEmitterSettings();
        _parts = selected.Select(selection => new Part(selection, 0)).ToList();
        if (_parts.Count > MaxActiveParts)
            throw new InvalidOperationException("Too many simultaneous sounds in this preview cue.");
        Update(distance, pan);
    }

    internal bool HasEnded => _parts.Count == 0;
    internal bool IsAudible { get; private set; }
    internal bool HasPendingDelay => _parts.Any(part => part.WaitingForDelay);
    internal bool HasPendingWork => _parts.Any(part => !part.Selection.Profile.Looping || part.WaitingForDelay);
    internal bool HasUnrecoveredChannel => _parts.Any(part => part.Selection.Profile.Channel is not (3 or 24));

    internal void Update(float? distance, float pan)
    {
        if (_disposed) return;
        if (_paused)
        {
            _clock.Start();
            _paused = false;
        }
        try
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            IsAudible = false;
            List<PreparedSelection>? chained = null;
            int existingCount = _parts.Count;
            for (int index = 0; index < existingCount; index++)
            {
                Part part = _parts[index];
                if (part.Player?.HasEnded == true)
                {
                    part.Player.Dispose();
                    part.Player = null;
                    part.Completed = true;
                    if (!part.Selection.Profile.Looping && !part.Selection.Variant.Alias.FlagBits.IsLooping &&
                        part.Selection.Variant.Chain is { } chain)
                    {
                        chained ??= [];
                        chained.AddRange(_engine.Select(chain, _chainSettings, part.Selection.Variant));
                    }
                    continue;
                }
                UpdatePart(part, distance, pan, elapsed);
            }
            _parts.RemoveAll(part => part.Completed);
            if (chained is not null)
            {
                if (_parts.Count + chained.Count > MaxActiveParts)
                    throw new InvalidOperationException("Too many simultaneous sounds in this preview cue.");
                foreach (PreparedSelection selection in chained)
                {
                    var part = new Part(selection, elapsed);
                    _parts.Add(part);
                    // Start ready chains now; their completion is checked on a later update.
                    UpdatePart(part, distance, pan, elapsed);
                }
                _parts.RemoveAll(part => part.Completed);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void UpdatePart(Part part, float? distance, float pan, double elapsed)
    {
        SoundEmitterPlayback profile = part.Selection.Profile;
        float gain = distance.HasValue ? profile.Gain(distance.Value) : profile.Volume;
        // A map one-shot outside hearing range at dispatch never starts later.
        if (!profile.Looping && !part.RangeChecked)
        {
            part.RangeChecked = true;
            if (gain <= 0 && distance.HasValue)
            {
                part.Completed = true;
                return;
            }
        }
        if (elapsed < part.ReadyAt) return;
        part.WaitingForDelay = false;
        if (profile.Looping && gain <= 0 && distance.HasValue)
        {
            part.Player?.Pause();
            return;
        }
        if (part.Player is null)
            part.Player = _engine.Play(part.Selection.Variant.Sound, profile.Looping, gain, pan, profile.Pitch);
        else
        {
            part.Player.SetVolume(gain);
            part.Player.SetPan(pan);
            part.Player.Play();
        }
        IsAudible |= gain > 0;
    }

    internal void Pause()
    {
        if (_disposed || _paused) return;
        _clock.Stop();
        _paused = true;
        foreach (Part part in _parts) part.Player?.Pause();
        IsAudible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Stop();
        foreach (Part part in _parts) part.Player?.Dispose();
        _parts.Clear();
        IsAudible = false;
    }

    private sealed class Part(PreparedSelection selection, double elapsed)
    {
        internal PreparedSelection Selection { get; } = selection;
        internal double ReadyAt { get; } = elapsed + Math.Max(0, selection.Variant.Alias.StartDelay) / 1000.0;
        internal bool WaitingForDelay = selection.Variant.Alias.StartDelay > 0;
        internal PreviewVoice? Player;
        internal bool RangeChecked;
        internal bool Completed;
    }
}
