using System.IO.Compression;
using System.Text.Json;
using IW4.Game.Assets;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.Sound;
using MapConverter.Game.IW3.Audio;
using MapConverter.Game.IW3.PC.Extraction;
using MapConverter.Game.IW3.PS3.Conversion;
using MapConverter.Game.IW3.PC.Assets;
using MapConverter.Game.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.Conversion;

internal static class Iw3PcAudioCompiler
{
    internal static async Task<(IReadOnlyList<BaseAsset> Assets, byte[] SoundPackage,
        IReadOnlyList<string> MissingSoundReferences)> CaptureAsync(
        Iw3PcSourceLibrary? sourceLibrary, string mapFF, string? customIwd,
        string assetDirectory, string bootstrapDirectory, string ownedPrefix,
        IReadOnlySet<string> ambientAliases, IReadOnlySet<string> requiredAliases,
        IReadOnlyList<IReadOnlyDictionary<string, string>> entities, string scratch, CancellationToken token)
    {
        var sharedAliases = await ReadSharedAliasesAsync(sourceLibrary, mapFF,
            customIwd, assetDirectory, entities, token).ConfigureAwait(false);
        var mapOwned = requiredAliases.Concat(ambientAliases).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = mapOwned.Concat(sharedAliases).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingSounds = sourceLibrary?.MissingSoundReferences ?? new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var captures = new Dictionary<string, (JsonElement Sound, string Directory)>(StringComparer.OrdinalIgnoreCase);
        int sequence = 0;
        while (true)
        {
            string[] missing = wanted.Where(name => !captures.ContainsKey(name) && !skipped.Contains(name))
                .Order(StringComparer.Ordinal).ToArray();
            if (missing.Length == 0) break;
            var origins = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var selectedSoundRecordIds = new Dictionary<string, int>(StringComparer.Ordinal);
            if (sourceLibrary is not null)
            {
                foreach (string alias in missing)
                {
                    if (await sourceLibrary.ResolveSoundRecordAsync(mapFF, alias, token).ConfigureAwait(false)
                        is not { } soundRecord)
                    {
                        skipped.Add(alias);
                        continue;
                    }
                    selectedSoundRecordIds.Add(alias, soundRecord.RecordId);
                    if (!origins.TryGetValue(soundRecord.Path, out List<string>? selected)) origins.Add(soundRecord.Path, selected = []);
                    selected.Add(alias);
                }
            }
            else
            {
                string directory = Path.GetDirectoryName(mapFF) ?? throw new InvalidDataException("Map has no directory.");
                foreach (string path in new[] { mapFF }.Concat(Directory.EnumerateFiles(directory, "*.ff"))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                    origins.Add(path, missing.ToList());
            }
            foreach ((string origin, List<string> names) in origins)
            {
                token.ThrowIfCancellationRequested();
                if (sourceLibrary is null && names.All(captures.ContainsKey)) continue;
                var loadedZones = new Dictionary<string, Iw3PcZone>(
                    OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                Iw3PcZone LoadedZone(string path)
                {
                    if (!loadedZones.TryGetValue(path, out var loaded))
                        loadedZones.Add(path, loaded = Iw3PcZone.Load(path, token));
                    return loaded;
                }
                string output = Path.Combine(scratch, $"pc-sound-export-{sequence++:D4}");
                IReadOnlyList<(string Name, string FastFilePath, int RecordId)> sources = sourceLibrary is null
                    ? []
                    : await sourceLibrary.ResolveExternalLoadedSoundsAsync(mapFF, names, token).ConfigureAwait(false);
                var providers = sources.ToDictionary(source => Iw3PcAsset.CanonicalName(source.Name),
                    source => (LoadedZone(source.FastFilePath), source.RecordId), StringComparer.Ordinal);
                Iw3PcSoundExporter.ExportSelected(LoadedZone(origin), names, output, providers, token,
                    sourceLibrary is null ? null : names.Select(name => selectedSoundRecordIds[name]).ToArray());
                using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(
                    Path.Combine(output, "sounds.json"), token).ConfigureAwait(false));
                foreach (JsonElement sound in document.RootElement.EnumerateArray())
                {
                    string name = sound.GetProperty("Name").GetString() ?? throw new InvalidDataException("Sound has no name.");
                    captures.TryAdd(name, (sound.Clone(), output));
                }
            }
            foreach (string name in missing)
            {
                if (!captures.TryGetValue(name, out var capture))
                {
                    skipped.Add(name);
                    continue;
                }
                foreach (JsonElement row in capture.Sound.GetProperty("Rows").EnumerateArray())
                    foreach (string field in new[] { "SecondaryAlias", "ChainAlias" })
                        if (row.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
                            value.GetString() is { Length: > 0 } reference)
                        {
                            wanted.Add(reference);
                            mapOwned.Add(reference);
                        }
            }
        }

        string[] iwds = sourceLibrary?.IwdPaths.ToArray() ?? LegacyIwds(mapFF, customIwd);
        var sourceSounds = new Dictionary<string, Iw3SourceSound>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in captures.Keys.Order(StringComparer.Ordinal))
        {
            var rows = new List<Iw3SourceSoundRow>();
            foreach (JsonElement source in captures[name].Sound.GetProperty("Rows").EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                byte[] payload;
                string payloadName;
                JsonElement? loadedFormat = null;
                if (source.TryGetProperty("StreamedPath", out JsonElement streamed))
                {
                    string path = streamed.GetString() ?? throw new InvalidDataException("Empty sound path.");
                    if (ReadIwdSound(iwds, path) is not { } resolved)
                    {
                        missingSounds.Add(path);
                        skipped.Add(name);
                        break;
                    }
                    payload = resolved.Bytes;
                    payloadName = path;
                    sourceLibrary?.RecordSoundPayloadSource(path, resolved.Archive);
                }
                else if (source.TryGetProperty("Loaded", out JsonElement loaded))
                {
                    payloadName = loaded.GetProperty("File").GetString() ?? throw new InvalidDataException("Loaded sound has no file.");
                    payload = File.ReadAllBytes(Path.Combine(captures[name].Directory, payloadName));
                    loadedFormat = loaded;
                }
                else
                {
                    string missingPayload = source.GetProperty("MissingPayload").GetString() ?? name;
                    missingSounds.Add(missingPayload);
                    skipped.Add(name);
                    break;
                }
                byte[] mp3 = EncodeMp3(payload, loadedFormat, name, payloadName, token);
                string? speakerMapName = source.GetProperty("SpeakerMapName").GetString();
                rows.Add(new Iw3SourceSoundRow(
                    source.GetProperty("SecondaryAlias").GetString(), source.GetProperty("ChainAlias").GetString(),
                    source.GetProperty("Sequence").GetInt32(), Single(source, "VolumeMin"), Single(source, "VolumeMax"),
                    Single(source, "PitchMin"), Single(source, "PitchMax"), Single(source, "DistanceMin"),
                    Single(source, "DistanceMax"), source.GetProperty("Flags").GetInt32(),
                    Single(source, "SlavePercentage"), Single(source, "Probability"),
                    Single(source, "LfePercentage"), Single(source, "CenterPercentage"),
                    source.GetProperty("StartDelay").GetInt32(), Single(source, "EnvelopMin"),
                    Single(source, "EnvelopMax"), Single(source, "EnvelopPercentage"),
                    source.TryGetProperty("SpeakerMap", out JsonElement speaker)
                        ? ReadSpeakerMap(speaker, speakerMapName ?? name) : null,
                    null, mp3, 0, 0, 0));
            }
            if (skipped.Contains(name)) continue;
            sourceSounds.Add(name, new Iw3SourceSound(name, rows));
        }
        bool removed;
        do
        {
            removed = false;
            foreach (string name in sourceSounds.Keys.ToArray())
            {
                if (sourceSounds[name].Rows.Any(row =>
                        row.SecondaryAlias is { } secondary && skipped.Contains(secondary) ||
                        row.ChainAlias is { } chain && skipped.Contains(chain)))
                {
                    sourceSounds.Remove(name);
                    skipped.Add(name);
                    removed = true;
                }
            }
        }
        while (removed);
        var audio = Iw3AudioEmitter.Emit(bootstrapDirectory, ownedPrefix, ambientAliases,
            mapOwned, sharedAliases, sourceSounds, row => row.LoadedPayload ??
                throw new InvalidDataException("PC sound row has no encoded payload."), token);
        missingSounds.UnionWith(skipped);
        missingSounds.ExceptWith(audio.Assets.OfType<SoundAliasListAsset>()
            .Select(asset => asset.AliasName).OfType<string>());
        return (audio.Assets, audio.SoundPackage, missingSounds.ToArray());
    }

    private static async Task<HashSet<string>> ReadSharedAliasesAsync(
        Iw3PcSourceLibrary? sourceLibrary, string mapFF, string? customIwd,
        string assetDirectory, IReadOnlyList<IReadOnlyDictionary<string, string>> entities,
        CancellationToken token)
    {
        if (!entities.Any(entity => entity.TryGetValue("classname", out string? value) && value == "script_struct"))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(assetDirectory, "maps/mp/_global_fx.gsc");
        byte[]? bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, token).ConfigureAwait(false) : null;
        if (bytes is null)
        {
            string[] iwds = sourceLibrary?.IwdPaths.ToArray() ?? LegacyIwds(mapFF, customIwd);
            foreach (string iwd in iwds)
            {
                using var archive = ZipFile.OpenRead(iwd);
                ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(value =>
                    value.FullName.Equals("maps/mp/_global_fx.gsc", StringComparison.OrdinalIgnoreCase));
                if (entry is null) continue;
                using Stream stream = entry.Open();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, token).ConfigureAwait(false);
                bytes = memory.ToArray();
                break;
            }
        }
        if (bytes is null && sourceLibrary is not null)
            bytes = await sourceLibrary.ReadRawFileAsync(mapFF, "maps/mp/_global_fx.gsc", token).ConfigureAwait(false);
        if (bytes is null)
            bytes = FindSharedScript(mapFF, token);
        if (bytes is null) throw new FileNotFoundException("Shared FX script is required by script_struct entities.", path);
        var buffer = new byte[bytes.Length + 1];
        bytes.CopyTo(buffer, 0);
        var raw = new RawFileAsset { Name = "maps/mp/_global_fx.gsc", Len = bytes.Length, Buffer = buffer };
        return Iw3Ps3RawFileCompiler.ReadSharedPrefabSoundNames(raw, entities).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static byte[]? FindSharedScript(string mapFF, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(mapFF) ?? throw new InvalidDataException("Map has no directory.");
        foreach (string fastFile in new[] { mapFF }.Concat(Directory.EnumerateFiles(directory, "*.ff"))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var zone = Iw3PcZone.Load(fastFile, token);
            var asset = zone.Assets.LastOrDefault(asset => asset.Type == "rawfile" &&
                asset.Name.Equals("maps/mp/_global_fx.gsc", StringComparison.OrdinalIgnoreCase));
            if (asset != null) return Iw3PcAssetExporter.ReadRawFile(zone, asset);
        }
        return null;
    }

    private static string[] LegacyIwds(string mapFF, string? customIwd)
    {
        string directory = Path.GetDirectoryName(mapFF) ?? throw new InvalidDataException("Map has no directory.");
        return (customIwd is null ? [] : new[] { customIwd })
            .Concat(Directory.EnumerateFiles(directory, "*.iwd"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static float Single(JsonElement row, string field) => row.GetProperty(field).GetSingle();

    private static SpeakerMap ReadSpeakerMap(JsonElement source, string name)
    {
        byte isDefault = (byte)source.GetProperty("IsDefault").GetInt32();
        var channels = new SpeakerMapChannel[2];
        JsonElement channelRows = source.GetProperty("Channels");
        if (channelRows.GetArrayLength() != channels.Length)
            throw new InvalidDataException("PC speaker map has an invalid channel count.");
        for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
        {
            JsonElement channel = channelRows[channelIndex];
            var outputs = new XAudioChannelMap[2];
            if (channel.GetArrayLength() != outputs.Length)
                throw new InvalidDataException("PC speaker map has an invalid output count.");
            for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++)
            {
                JsonElement output = channel[outputIndex];
                int entryCount = output.GetProperty("EntryCount").GetInt32();
                var speakers = new SpeakerLevels[6];
                JsonElement speakerRows = output.GetProperty("Speakers");
                if (speakerRows.GetArrayLength() != speakers.Length)
                    throw new InvalidDataException("PC speaker map has an invalid speaker count.");
                for (int speakerIndex = 0; speakerIndex < speakers.Length; speakerIndex++)
                {
                    JsonElement speaker = speakerRows[speakerIndex];
                    speakers[speakerIndex] = new SpeakerLevels
                    {
                        Speaker = speaker.GetProperty("Speaker").GetInt32(),
                        NumLevels = speaker.GetProperty("NumLevels").GetInt32(),
                        Level0 = Single(speaker, "Level0"),
                        Level1 = Single(speaker, "Level1")
                    };
                }
                outputs[outputIndex] = new XAudioChannelMap
                {
                    EntryCount = entryCount,
                    Speakers = speakers
                };
            }
            channels[channelIndex] = new SpeakerMapChannel { Outputs = outputs };
        }
        return new SpeakerMap
        {
            IsDefault = isDefault,
            Padding = new byte[3], Name = name, Channels = channels
        };
    }

    private static (byte[] Bytes, string Archive)? ReadIwdSound(IReadOnlyList<string> paths, string entryPath)
    {
        foreach (string path in paths)
        {
            using var archive = ZipFile.OpenRead(path);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(value =>
                value.FullName.Equals(entryPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            if (entry.Length <= 0 || entry.Length > SoundFile.MaxInMemoryPayloadBytes)
                throw new InvalidDataException($"Invalid IWD sound length for '{entryPath}' in '{path}'.");
            using Stream stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return (memory.ToArray(), $"{path}:{entry.FullName}");
        }
        return null;
    }

    private static byte[] EncodeMp3(byte[] source, JsonElement? loadedFormat, string aliasName,
        string payloadName, CancellationToken token)
    {
        try
        {
            (short[] Samples, int Channels, int SampleRate) pcm;
            if (loadedFormat is { } loaded)
            {
                int format = loaded.GetProperty("Format").GetInt32();
                if (format != 1) throw new NotSupportedException($"Unsupported loaded sound format {format}.");
                pcm = Iw3PcWaveDecoder.DecodeRawPcm(source, loaded.GetProperty("Channels").GetInt32(),
                    loaded.GetProperty("Rate").GetInt32(), loaded.GetProperty("Bits").GetInt32());
            }
            else
            {
                if (LameMp3Encoder.ReadExistingMp3(source) is { } mp3) return mp3;
                pcm = Iw3PcWaveDecoder.Decode(source);
            }
            return LameMp3Encoder.Encode(pcm.Samples, pcm.Channels, pcm.SampleRate, token);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException($"Could not load the packaged LAME encoder for " +
                $"{System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier} while encoding sound alias '{aliasName}' ({payloadName}).", ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidDataException($"Cannot convert sound alias '{aliasName}' ({payloadName}): {ex.Message}", ex);
        }
    }
}
