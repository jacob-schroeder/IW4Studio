using System.Globalization;
using IW4.Game.Assets.Sound;

namespace Iw4Radiant.MapSource;

/// <summary>Per-marker overrides; absent values retain the source sound's settings.</summary>
internal sealed record SoundEmitterSettings
{
    internal bool Looping { get; init; } = true;
    internal bool HasPlaybackOverride { get; init; }
    internal float? Volume { get; init; }
    internal float? Pitch { get; init; }
    internal float? DistanceMin { get; init; }
    internal float? DistanceMax { get; init; }
    internal byte? Channel { get; init; }

    internal bool HasOverrides => HasPlaybackOverride || !Looping || Volume.HasValue || Pitch.HasValue ||
                                  DistanceMin.HasValue || DistanceMax.HasValue || Channel.HasValue;

    internal static SoundEmitterSettings Read(MapEntity entity)
    {
        string playback = entity.Properties.GetValueOrDefault("sound_playback", "");
        if (playback is not ("" or "loop" or "once"))
            throw new ArgumentException("Sound playback must be loop or once.");
        byte? channel = null;
        string channelText = entity.Properties.GetValueOrDefault("sound_channel", "");
        if (!string.IsNullOrWhiteSpace(channelText))
        {
            if (!byte.TryParse(channelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte value) || value > 63)
                throw new ArgumentException("Sound channel must be between 0 and 63.");
            channel = value;
        }
        var settings = new SoundEmitterSettings
        {
            Looping = playback != "once",
            HasPlaybackOverride = playback.Length != 0,
            Volume = ReadNumber("sound_volume"),
            Pitch = ReadNumber("sound_pitch"),
            DistanceMin = ReadNumber("sound_distance_min"),
            DistanceMax = ReadNumber("sound_distance_max"),
            Channel = channel
        };
        settings.Validate();
        return settings;

        float? ReadNumber(string key)
        {
            string text = entity.Properties.GetValueOrDefault(key, "");
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                throw new ArgumentException($"{key} must be a finite number, or empty to use the sound default.");
            return value;
        }
    }

    internal void Validate()
    {
        RequireRange(Volume, 0, 1, "Sound volume");
        RequireRange(Pitch, 0.5f, 2, "Sound pitch");
        RequireRange(DistanceMin, 0, float.MaxValue, "Inner hearing range");
        RequireRange(DistanceMax, 0, float.MaxValue, "Outer hearing range");
        if (DistanceMax == 0 || (DistanceMin.HasValue && DistanceMax.HasValue && DistanceMax <= DistanceMin))
            throw new ArgumentException("Outer hearing range must be greater than the inner range.");
        if (Channel > 63) throw new ArgumentException("Sound channel must be between 0 and 63.");
    }

    internal void WriteTo(MapEntity entity)
    {
        Validate();
        Set("sound_playback", HasPlaybackOverride || !Looping ? Looping ? "loop" : "once" : null);
        Set("sound_volume", Number(Volume));
        Set("sound_pitch", Number(Pitch));
        Set("sound_distance_min", Number(DistanceMin));
        Set("sound_distance_max", Number(DistanceMax));
        Set("sound_channel", Channel?.ToString(CultureInfo.InvariantCulture));

        void Set(string key, string? value)
        {
            if (value is null) entity.Properties.Remove(key);
            else entity.Properties[key] = value;
        }
    }

    internal SoundEmitterPlayback Resolve(SndAlias alias)
    {
        Validate();
        float min = DistanceMin ?? alias.DistanceMin;
        float max = DistanceMax ?? alias.DistanceMax;
        float volume = Volume ?? (alias.VolumeMin * 0.5f + alias.VolumeMax * 0.5f);
        float pitch = Pitch ?? (alias.PitchMin * 0.5f + alias.PitchMax * 0.5f);
        if (!float.IsFinite(min) || !float.IsFinite(max) || min < 0 || max <= min)
            throw new ArgumentException("The sound's outer hearing range must exceed its inner range, including inherited defaults.");
        if (!float.IsFinite(volume) || volume < 0 || !float.IsFinite(pitch) || pitch <= 0)
            throw new ArgumentException("The sound has invalid default volume or pitch.");
        return new SoundEmitterPlayback(Looping, Math.Clamp(volume, 0, 1), pitch, min, max,
            Channel ?? alias.FlagBits.EntityChannelIndex, alias.FlagBits.EnforcesDistanceGate, alias.VolumeFalloffCurve);
    }

    private static string? Number(float? value) => value?.ToString("G9", CultureInfo.InvariantCulture);

    private static void RequireRange(float? value, float min, float max, string label)
    {
        if (value.HasValue && (!float.IsFinite(value.Value) || value < min || value > max))
            throw new ArgumentException($"{label} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");
    }
}

internal sealed record SoundEmitterPlayback(bool Looping, float Volume, float Pitch,
    float DistanceMin, float DistanceMax, byte Channel, bool EnforcesDistanceGate, SndCurve? Curve)
{
    internal float Gain(float distance)
    {
        if (!float.IsFinite(distance) || distance >= DistanceMax || (EnforcesDistanceGate && distance < DistanceMin)) return 0;
        if (distance <= DistanceMin) return Volume;
        float fraction = (distance - DistanceMin) / (DistanceMax - DistanceMin);
        if (Curve is not null && Curve.KnotCount >= 2 && Curve.Knots.Count >= Curve.KnotCount)
            for (int index = 1; index < Curve.KnotCount; index++)
            {
                SndCurveKnot left = Curve.Knots[index - 1], right = Curve.Knots[index];
                if (fraction > right.X) continue;
                if (!float.IsFinite(left.X) || !float.IsFinite(right.X) || !float.IsFinite(left.Y) ||
                    !float.IsFinite(right.Y) || right.X <= left.X) break;
                float point = Math.Clamp((fraction - left.X) / (right.X - left.X), 0, 1);
                return Volume * Math.Clamp(left.Y + (right.Y - left.Y) * point, 0, 1);
            }
        return Volume * (1 - fraction);
    }
}
