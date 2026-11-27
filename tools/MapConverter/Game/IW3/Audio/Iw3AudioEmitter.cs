using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets;
using IW4.Game.Assets.Sound;
using IW4.Linker.Contracts;
using IW4.Linker.Packaging;
using MapConverter.Game.IW3.Rules;

namespace MapConverter.Game.IW3.Audio;

internal static class Iw3AudioEmitter
{
    internal static (IReadOnlyList<BaseAsset> Assets, byte[] SoundPackage) Emit(
        string bootstrapDirectory, string ownedPrefix, IReadOnlySet<string> ambientAliases,
        IReadOnlySet<string> mapOwnedAliases, IReadOnlySet<string> sharedAliases,
        IReadOnlyDictionary<string, Iw3SourceSound> captured,
        Func<Iw3SourceSoundRow, byte[]> readPayload, CancellationToken token)
    {
        var exchange = new SoundAliasListExchange();
        var donors = new[] { "ambient_airport_tarmac_l", "ambient_airport_tarmac_r", "emt_fly_loop" }
            .ToDictionary(name => name, name => exchange.Link(bootstrapDirectory, name).Aliases[0],
                StringComparer.Ordinal);
        SndCurve stockCurve = donors["ambient_airport_tarmac_l"].VolumeFalloffCurve ??
            throw new InvalidDataException("Native ambient donor has no falloff curve.");
        if (donors.Values.Any(donor => donor.VolumeFalloffCurve?.Filename != stockCurve.Filename))
            throw new InvalidDataException("Native audio donors use different falloff curves.");
        var mapCurve = new SndCurve
        {
            Filename = SoundRules.OwnedName(ownedPrefix, stockCurve.Filename ??
                throw new InvalidDataException("Native falloff curve has no name.")),
            KnotCount = stockCurve.KnotCount,
            Padding = stockCurve.Padding,
            Knots = stockCurve.Knots
        };
        var package = new SoundFilePackager();
        var definitions = new List<BaseAsset> { mapCurve };
        foreach (Iw3SourceSound sound in captured.Values.OrderBy(sound => sound.Name, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            bool ambient = SoundRules.IsAmbientAlias(sound.Name, ambientAliases);
            SndAlias donor = donors[SoundRules.DonorName(sound.Name, ambient)];
            var aliases = SoundRules.ExportNames(ownedPrefix, sound.Name,
                    mapOwnedAliases.Contains(sound.Name), sharedAliases.Contains(sound.Name))
                .ToDictionary(name => name, _ => new List<SndAlias>(sound.Rows.Count), StringComparer.Ordinal);
            foreach (Iw3SourceSoundRow source in sound.Rows)
            {
                byte[] payload = readPayload(source);
                if (!payload.AsSpan().StartsWith("ID3"u8))
                    throw new NotSupportedException($"COD4 sound '{sound.Name}' is not an ID3 MP3 payload.");
                StreamedSound stream = package.AddPayload(payload);
                SndAliasBits flags = SoundRules.TargetStreamFlags(donor.Flags, source.Flags, ambient);
                foreach (var (aliasName, rows) in aliases)
                {
                    rows.Add(new SndAlias
                    {
                        AliasName = aliasName,
                        SecondaryAliasName = SoundRules.ReferenceName(ownedPrefix, source.SecondaryAlias),
                        ChainAliasName = SoundRules.ReferenceName(ownedPrefix, source.ChainAlias),
                        MixerGroup = donor.MixerGroup,
                        SoundFileCount = 1,
                        SoundFiles = [new SoundFile
                        {
                            Type = SndAliasType.Streamed, Exists = 1, Payload = stream
                        }],
                        Sequence = source.Sequence,
                        VolumeMin = source.VolumeMin,
                        VolumeMax = source.VolumeMax,
                        PitchMin = source.PitchMin,
                        PitchMax = source.PitchMax,
                        DistanceMin = source.DistanceMin,
                        DistanceMax = source.DistanceMax,
                        Flags = flags,
                        SlavePercentage = source.SlavePercentage,
                        Probability = source.Probability,
                        LfePercentage = source.LfePercentage,
                        CenterPercentage = source.CenterPercentage,
                        StartDelay = source.StartDelay,
                        EnvelopMin = source.EnvelopMin,
                        EnvelopMax = source.EnvelopMax,
                        EnvelopPercentage = source.EnvelopPercentage,
                        VolumeFalloffCurve = mapCurve,
                        SpeakerMap = source.SpeakerMap ?? donor.SpeakerMap
                    });
                }
            }
            foreach (var (aliasName, rows) in aliases)
            {
                definitions.Add(new SoundAliasListAsset
                {
                    AliasName = aliasName,
                    Count = rows.Count, Aliases = rows
                });
            }
        }
        var nativeDependencies = new Dictionary<AssetKey, BaseAsset>();
        foreach (string name in SoundRules.NativeRuntimeAliases())
        {
            token.ThrowIfCancellationRequested();
            SoundAliasListAsset sound = exchange.Link(bootstrapDirectory, name, package.AddPayload);
            definitions.Add(sound);
            foreach (SndAlias alias in sound.Aliases)
            {
                if (alias.VolumeFalloffCurve is { } curve)
                    nativeDependencies.TryAdd(AssetKey.FromDefinition(curve), curve);
                foreach (LoadedSound sample in alias.SoundFiles
                             .Select(file => file.Loaded?.LoadedSound).OfType<LoadedSound>())
                    nativeDependencies.TryAdd(AssetKey.FromDefinition(sample), sample);
            }
        }
        definitions.AddRange(nativeDependencies.Values);
        return (definitions, package.ToArray());
    }

}
