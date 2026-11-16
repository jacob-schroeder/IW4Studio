using System.Buffers.Binary;
using IW4.Formats.SourceFormat.Sound;
using IW4.Game.Assets;
using IW4.Game.Assets.Sound;
using IW4.Linker.Packaging;
using MapConverter.Game.IW3.PS3.Extraction;
using MapConverter.Game.IW3.PS3.Rules;

namespace MapConverter.Game.IW3.PS3.Conversion;

internal static class Iw3Ps3AudioCompiler
{
    internal static (IReadOnlyList<BaseAsset> Assets, byte[] SoundPackage) Capture(
        string sourceDirectory, string bootstrapDirectory,
        string ownedPrefix, IReadOnlySet<string> ambientAliases,
        IReadOnlySet<string> requiredAliases,
        IReadOnlyDictionary<string, Iw3Ps3SourceSound> mapSounds, CancellationToken token)
    {
        string[] sourceFiles =
        [Path.Combine(sourceDirectory, "common_mp.ff"), Path.Combine(sourceDirectory, "code_post_gfx_mp.ff"),
            Path.Combine(sourceDirectory, "ui_mp.ff")];
        var wanted = requiredAliases.Concat(ambientAliases).ToHashSet(StringComparer.Ordinal);
        var captured = new Dictionary<string, Iw3Ps3SourceSound>(StringComparer.Ordinal);
        while (true)
        {
            var missing = wanted.Except(captured.Keys).ToHashSet(StringComparer.Ordinal);
            if (missing.Count == 0) break;
            int before = captured.Count;
            foreach (string name in missing.ToArray())
                if (mapSounds.TryGetValue(name, out Iw3Ps3SourceSound? sound) && Usable(sound))
                    captured.Add(name, sound);
            missing.ExceptWith(captured.Keys);
            foreach (string source in sourceFiles.Distinct(StringComparer.Ordinal))
            {
                if (missing.Count == 0) break;
                token.ThrowIfCancellationRequested();
                if (!File.Exists(source)) continue;
                IReadOnlyDictionary<string, Iw3Ps3SourceSound> sourceSounds;
                try
                {
                    sourceSounds = Iw3Ps3FastFileCatalog.ReadSourceSounds(source, missing, token);
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"Cannot read COD4 audio from '{source}' while resolving " +
                        $"{string.Join(", ", missing.Order(StringComparer.Ordinal))}: {error.Message}", error);
                }
                foreach (var (name, sound) in sourceSounds)
                    if (Usable(sound)) captured.TryAdd(name, sound);
                missing.ExceptWith(captured.Keys);
                if (missing.Count == 0) break;
            }
            if (captured.Count == before)
                throw new InvalidDataException("Missing COD4 PS3 sound aliases: " +
                    string.Join(", ", missing.Order(StringComparer.Ordinal)));
            foreach (Iw3Ps3SourceSound sound in captured.Values)
                foreach (Iw3Ps3SourceSoundRow row in sound.Rows)
                {
                    if (!string.IsNullOrWhiteSpace(row.SecondaryAlias)) wanted.Add(row.SecondaryAlias);
                    if (!string.IsNullOrWhiteSpace(row.ChainAlias)) wanted.Add(row.ChainAlias);
                }
        }

        var missingLoaded = captured.Values.SelectMany(sound => sound.Rows)
            .Where(row => row.LoadedPayload is null && row.LoadedSoundName is not null)
            .Select(row => row.LoadedSoundName).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var loadedPayloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string source in sourceFiles)
        {
            if (missingLoaded.Count == 0) break;
            token.ThrowIfCancellationRequested();
            if (!File.Exists(source)) continue;
            IReadOnlyDictionary<string, byte[]> found;
            try
            {
                found = Iw3Ps3FastFileCatalog.ReadSourceLoadedSounds(source, missingLoaded, token);
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"Cannot read COD4 LoadedSounds from '{source}' while resolving " +
                    $"{string.Join(", ", missingLoaded.Order(StringComparer.Ordinal))}: {error.Message}", error);
            }
            foreach (var (name, payload) in found)
                loadedPayloads.TryAdd(name, payload);
            missingLoaded.ExceptWith(found.Keys);
        }
        if (missingLoaded.Count != 0)
            throw new InvalidDataException("Missing COD4 PS3 LoadedSounds: " +
                string.Join(", ", missingLoaded.Order(StringComparer.Ordinal)));

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
        foreach (Iw3Ps3SourceSound sound in captured.Values.OrderBy(sound => sound.Name, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            bool ambient = SoundRules.IsAmbientAlias(sound.Name, ambientAliases);
            SndAlias donor = donors[SoundRules.DonorName(sound.Name, ambient)];
            var rows = new List<SndAlias>(sound.Rows.Count);
            foreach (Iw3Ps3SourceSoundRow source in sound.Rows)
            {
                byte[] payload = source.LoadedPayload ??
                    (source.LoadedSoundName is { } loadedName
                        ? loadedPayloads[loadedName]
                        : ReadPackage(sourceDirectory, source));
                if (!payload.AsSpan().StartsWith("ID3"u8))
                    throw new NotSupportedException($"COD4 PS3 sound '{sound.Name}' is not an ID3 MP3 payload.");
                StreamedSound stream = package.AddPayload(payload);
                int flags = SoundRules.TargetStreamFlags(donor.Flags, source.Flags, ambient);
                rows.Add(new SndAlias
                {
                    AliasName = SoundRules.OwnedName(ownedPrefix, sound.Name),
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
                    SpeakerMap = source.SpeakerMap is { } map
                        ? ReadSpeakerMap(map, source.SpeakerMapName ?? sound.Name)
                        : donor.SpeakerMap
                });
            }
            definitions.Add(new SoundAliasListAsset
            {
                AliasName = SoundRules.OwnedName(ownedPrefix, sound.Name),
                Count = rows.Count, Aliases = rows
            });
        }
        return (definitions, package.ToArray());

        static bool Usable(Iw3Ps3SourceSound sound) => sound.Rows.Count != 0 &&
            sound.Rows.All(row => row.LoadedPayload is { Length: > 0 } ||
                row.LoadedSoundName is not null ||
                row.PackageIndex != 0 && row.PackageLength > 0);
    }

    private static byte[] ReadPackage(string sourceDirectory, Iw3Ps3SourceSoundRow source)
    {
        if (source.PackageIndex == 0 || source.PackageOffset < 0 ||
            source.PackageLength <= 0 || source.PackageLength > SoundFile.MaxInMemoryPayloadBytes)
            throw new NotSupportedException("Unsupported COD4 PS3 sound package range.");
        string path = Path.Combine(sourceDirectory, $"packfile{source.PackageIndex}.pak");
        using var file = File.OpenRead(path);
        if ((long)source.PackageOffset + source.PackageLength > file.Length)
            throw new InvalidDataException($"COD4 PS3 sound range exceeds '{path}'.");
        file.Position = source.PackageOffset;
        byte[] payload = new byte[source.PackageLength];
        file.ReadExactly(payload);
        return payload;
    }

    private static SpeakerMap ReadSpeakerMap(byte[] bytes, string name)
    {
        if (bytes.Length != SpeakerMap.SerializedSize)
            throw new InvalidDataException($"COD4 PS3 speaker map '{name}' has an invalid length.");
        var channels = new SpeakerMapChannel[2];
        for (int channel = 0; channel < channels.Length; channel++)
        {
            var outputs = new XAudioChannelMap[2];
            for (int output = 0; output < outputs.Length; output++)
            {
                int offset = 8 + channel * SpeakerMapChannel.SerializedSize +
                    output * XAudioChannelMap.SerializedSize;
                var speakers = new SpeakerLevels[6];
                for (int index = 0; index < speakers.Length; index++)
                {
                    int cell = offset + 4 + index * SpeakerLevels.SerializedSize;
                    speakers[index] = new SpeakerLevels
                    {
                        Speaker = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cell)),
                        NumLevels = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cell + 4)),
                        Level0 = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(cell + 8)),
                        Level1 = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(cell + 12))
                    };
                }
                outputs[output] = new XAudioChannelMap
                {
                    EntryCount = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset)),
                    Speakers = speakers
                };
            }
            channels[channel] = new SpeakerMapChannel { Outputs = outputs };
        }
        return new SpeakerMap
        {
            IsDefault = bytes[0], Padding = bytes.AsSpan(1, 3).ToArray(),
            Name = name, Channels = channels
        };
    }
}
