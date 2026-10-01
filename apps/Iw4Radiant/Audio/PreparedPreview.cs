using Iw4Radiant.MapSource;
using IW4.Game.Assets.Sound;

namespace Iw4Radiant.Audio;

/// <summary>Cached PCM and mutable PS3 alias-selection history; playback values are chosen per cue.</summary>
internal sealed class PreparedPreview(PreparedVariant[] variants, string? error)
{
    private readonly int[] _sequences = variants.Select(variant => variant.Alias.Sequence).ToArray();
    internal string? Error { get; } = error;
    internal long DecodedBytes { get; set; }

    internal static void Validate(IEnumerable<SndAlias> aliases)
    {
        float total = 0;
        foreach (SndAlias alias in aliases)
        {
            total += alias.Probability;
            if (!float.IsFinite(alias.Probability) || alias.Probability < 0 ||
                !float.IsFinite(total) || total > float.MaxValue / 32768)
                throw new InvalidDataException("Preview requires finite, nonnegative variant probabilities.");
            if (alias.Sequence == int.MaxValue)
                throw new InvalidDataException("Variant sequence overflow is not supported by the preview.");
            if (alias.FlagBits.UnmappedBits != 0)
                throw new InvalidDataException($"Alias flags 0x{unchecked((uint)alias.FlagBits.UnmappedBits):X8} have unrecovered behavior and are not supported by the preview.");
            if (!float.IsFinite(alias.VolumeMin) || !float.IsFinite(alias.VolumeMax) ||
                alias.VolumeMin < 0 || alias.VolumeMax < alias.VolumeMin ||
                !float.IsFinite(alias.PitchMin) || !float.IsFinite(alias.PitchMax) ||
                alias.PitchMin <= 0 || alias.PitchMax < alias.PitchMin)
                throw new InvalidDataException("Preview requires valid volume and pitch ranges.");
            if (!string.IsNullOrEmpty(alias.ChainAliasName) &&
                (alias.FlagBits.PlaybackType != 2 || alias.SoundFiles[0].Streamed is null))
                throw new InvalidDataException("Chain preview currently requires a streamed source; loaded-sample chain dispatch is not yet recovered.");
        }
    }

    internal List<PreparedSelection> Select(SoundPreviewRandom random, SoundEmitterSettings? settings,
        PreparedVariant? chainingFrom = null)
    {
        if (Error is not null) throw new InvalidDataException(Error);
        var selected = new List<PreparedSelection>();
        SelectInto(selected, random, settings, 0, chainingFrom);
        return selected;
    }

    private void SelectInto(List<PreparedSelection> selected, SoundPreviewRandom random,
        SoundEmitterSettings? settings, int depth, PreparedVariant? chainingFrom = null)
    {
        if (variants.Length == 0) throw new InvalidDataException("This alias has no variants to preview.");
        // PS3 picker 0x00261128: weighted reservoir selection, then a retry excluding the newest sequence group.
        int index = 0;
        int maximumSequence = _sequences[0];
        float total = variants[0].Alias.Probability;
        for (int candidate = 1; candidate < variants.Length; candidate++)
        {
            float weight = variants[candidate].Alias.Probability;
            total += weight;
            if (total * random.NextVariant() < weight * 32768) index = candidate;
            maximumSequence = Math.Max(maximumSequence, _sequences[candidate]);
        }
        if (variants.Length > 2 && _sequences[index] == maximumSequence)
        {
            total = 0;
            for (int candidate = 0; candidate < variants.Length; candidate++)
            {
                if (_sequences[candidate] == maximumSequence) continue;
                float weight = variants[candidate].Alias.Probability;
                total += weight;
                if (total * random.NextVariant() < weight * 32768) index = candidate;
            }
        }
        if (maximumSequence == int.MaxValue)
            throw new InvalidDataException("Variant sequence overflow is not supported by the preview.");
        _sequences[index] = maximumSequence + 1;
        PreparedVariant variant = variants[index];
        // PS3 0x0033E528 checks the newly picked alias pointer, rather than just its name.
        if (ReferenceEquals(variant, chainingFrom)) return;
        SndAlias alias = variant.Alias;
        // PS3 start 0x0033CC60 uses independent draws, including for a secondary alias.
        float volume = alias.VolumeMin + (alias.VolumeMax - alias.VolumeMin) * random.NextValue();
        float pitch = alias.PitchMin + (alias.PitchMax - alias.PitchMin) * random.NextValue();
        SoundEmitterPlayback profile = (settings ?? new SoundEmitterSettings()).Resolve(alias, volume, pitch,
            spatial: settings is not null);
        selected.Add(new PreparedSelection(variant, profile));
        if (variant.Secondary is { } secondary && depth + 1 < 11)
            secondary.SelectInto(selected, random, settings, depth + 1);
    }
}

internal sealed record PreparedVariant(SndAlias Alias, PreparedSound Sound)
{
    internal PreparedPreview? Secondary { get; set; }
    internal PreparedPreview? Chain { get; set; }
}

internal sealed record PreparedSelection(PreparedVariant Variant, SoundEmitterPlayback Profile);

/// <summary>Native selection and value RNGs with independent editor seeds, retained across preview requests.</summary>
internal sealed class SoundPreviewRandom
{
    private uint _variantSeed = (uint)Random.Shared.NextInt64(1L << 32);
    private uint _valueSeed = (uint)Random.Shared.NextInt64(1L << 32);
    private readonly uint[] _ring = new uint[32];
    private uint _feedback;

    internal SoundPreviewRandom()
    {
        // PS3 0x004AA298 initializes the value RNG with eight warmups and 32 ring entries.
        for (int i = 0; i < 8; i++) AdvanceValue();
        for (int i = 0; i < _ring.Length; i++) _ring[i] = AdvanceValue();
        _feedback = _ring[^1];
    }

    internal int NextVariant()
    {
        _variantSeed = unchecked(_variantSeed * 0x343FD + 0x269EC3);
        return (int)((_variantSeed >> 16) & 0x7FFF);
    }

    internal float NextValue()
    {
        uint next = AdvanceValue();
        int slot = (int)(_feedback & 31);
        _feedback = _ring[slot];
        _ring[slot] = next;
        // PS3 0x002594E0 converts to binary32 before scaling; rounding may produce exactly 1.
        return (float)(_feedback & 0x3FFFFFFF) * (1f / 1073741824f);
    }

    private uint AdvanceValue() => _valueSeed = unchecked(_valueSeed * 0x19660D + 0x3C6EF35F);
}
