using System.IO.Compression;
using System.Text.Json;
using IW4.Game.Assets;
using IW4.Game.Assets.RawFile;
using IW4.Game.Assets.Sound;
using MapConverter.Game.IW3.PC.Extraction;
using MapConverter.Game.IW3.PS3.Conversion;
using MapConverter.Game.IW3.PS3.Extraction;
using MapConverter.IW3.PC.Assets;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.Game.IW3.PC.Conversion;

internal static class Iw3PcAudioCompiler
{
    internal static async Task<(IReadOnlyList<BaseAsset> Assets, byte[] SoundPackage)> CaptureAsync(
        Iw3PcSourceLibrary? sourceLibrary, string mapFF, string? customIwd,
        string assetDirectory, string bootstrapDirectory, string ownedPrefix,
        IReadOnlySet<string> ambientAliases, IReadOnlySet<string> requiredAliases,
        IReadOnlyList<IReadOnlyDictionary<string, string>> entities, string scratch, CancellationToken token,
        bool allowMissingSounds = false)
    {
        var sharedAliases = await ReadSharedAliasesAsync(sourceLibrary, mapFF,
            customIwd, assetDirectory, entities, token).ConfigureAwait(false);
        var mapOwned = requiredAliases.Concat(ambientAliases).ToHashSet(StringComparer.Ordinal);
        var wanted = mapOwned.Concat(sharedAliases).ToHashSet(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var captures = new Dictionary<string, (JsonElement Sound, string Directory)>(StringComparer.Ordinal);
        int sequence = 0;
        while (true)
        {
            string[] missing = wanted.Except(captures.Keys).Except(skipped).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length == 0) break;
            var origins = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var selectedSoundRecordIds = new Dictionary<string, int>(StringComparer.Ordinal);
            if (sourceLibrary is not null)
            {
                foreach (string alias in missing)
                {
                    string origin;
                    try
                    {
                        var soundRecord = await sourceLibrary.ResolveSoundRecordAsync(mapFF, alias, token)
                            .ConfigureAwait(false);
                        origin = soundRecord.Path;
                        selectedSoundRecordIds.Add(alias, soundRecord.RecordId);
                    }
                    catch (InvalidDataException error) when (allowMissingSounds &&
                        error.Message.StartsWith("Missing sound:", StringComparison.Ordinal))
                    {
                        skipped.Add(alias);
                        sourceLibrary.RecordMissingSound(alias);
                        continue;
                    }
                    if (!origins.TryGetValue(origin, out List<string>? selected)) origins.Add(origin, selected = []);
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
                if (!captures.ContainsKey(name) && !skipped.Contains(name))
                {
                    if (!allowMissingSounds)
                        throw new InvalidDataException($"Missing COD4 PC sound alias '{name}'.");
                    skipped.Add(name);
                    sourceLibrary?.RecordMissingSound(name);
                }
            foreach (string name in missing)
            {
                if (skipped.Contains(name)) continue;
                foreach (JsonElement row in captures[name].Sound.GetProperty("Rows").EnumerateArray())
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
        var sourceSounds = new Dictionary<string, Iw3Ps3SourceSound>(StringComparer.Ordinal);
        foreach (string name in captures.Keys.Order(StringComparer.Ordinal))
        {
            var rows = new List<Iw3Ps3SourceSoundRow>();
            try
            {
                foreach (JsonElement source in captures[name].Sound.GetProperty("Rows").EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();
                    byte[] wav;
                    if (source.TryGetProperty("StreamedPath", out JsonElement streamed))
                    {
                        string path = streamed.GetString() ?? throw new InvalidDataException("Empty sound path.");
                        var resolved = ReadIwdSound(iwds, path);
                        wav = resolved.Bytes;
                        sourceLibrary?.RecordSoundPayloadSource(path, resolved.Archive);
                    }
                    else if (source.TryGetProperty("Loaded", out JsonElement loaded))
                        wav = WrapLoadedPcm(captures[name].Directory, loaded);
                    else throw new FileNotFoundException($"Sound '{name}' has no playable payload.", name);
                    byte[] mp3 = await EncodeMp3Async(wav, name, scratch, sequence++, token).ConfigureAwait(false);
                    rows.Add(new Iw3Ps3SourceSoundRow(
                        source.GetProperty("SecondaryAlias").GetString(), source.GetProperty("ChainAlias").GetString(),
                        source.GetProperty("Sequence").GetInt32(), Single(source, "VolumeMin"), Single(source, "VolumeMax"),
                        Single(source, "PitchMin"), Single(source, "PitchMax"), Single(source, "DistanceMin"),
                        Single(source, "DistanceMax"), source.GetProperty("Flags").GetInt32(),
                        Single(source, "SlavePercentage"), Single(source, "Probability"),
                        Single(source, "LfePercentage"), Single(source, "CenterPercentage"),
                        source.GetProperty("StartDelay").GetInt32(), Single(source, "EnvelopMin"),
                        Single(source, "EnvelopMax"), Single(source, "EnvelopPercentage"),
                        source.TryGetProperty("SpeakerMap", out JsonElement speaker) ? ReadSpeakerMapBytes(speaker) : null,
                        source.GetProperty("SpeakerMapName").GetString(), null, mp3, 0, 0, 0));
                }
            }
            catch (FileNotFoundException error) when (allowMissingSounds)
            {
                skipped.Add(name);
                sourceLibrary?.RecordMissingSound(error.FileName ?? name);
                continue;
            }
            sourceSounds.Add(name, new Iw3Ps3SourceSound(name, rows));
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
        return Iw3Ps3AudioCompiler.Emit(bootstrapDirectory, ownedPrefix, ambientAliases,
            mapOwned, sharedAliases, sourceSounds, row => row.LoadedPayload ??
                throw new InvalidDataException("PC sound row has no encoded payload."), token);
    }

    private static async Task<HashSet<string>> ReadSharedAliasesAsync(
        Iw3PcSourceLibrary? sourceLibrary, string mapFF, string? customIwd,
        string assetDirectory, IReadOnlyList<IReadOnlyDictionary<string, string>> entities,
        CancellationToken token)
    {
        if (!entities.Any(entity => entity.TryGetValue("classname", out string? value) && value == "script_struct"))
            return new HashSet<string>(StringComparer.Ordinal);
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
        return Iw3Ps3RawFileCompiler.ReadSharedPrefabSoundNames(raw, entities).ToHashSet(StringComparer.Ordinal);
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

    private static byte[] ReadSpeakerMapBytes(JsonElement source)
    {
        var bytes = new byte[SpeakerMap.SerializedSize];
        bytes[0] = (byte)source.GetProperty("IsDefault").GetInt32();
        int channelIndex = 0;
        foreach (JsonElement channel in source.GetProperty("Channels").EnumerateArray())
        {
            int outputIndex = 0;
            foreach (JsonElement output in channel.EnumerateArray())
            {
                int offset = 8 + channelIndex * SpeakerMapChannel.SerializedSize +
                    outputIndex * XAudioChannelMap.SerializedSize;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset),
                    output.GetProperty("EntryCount").GetInt32());
                int speakerIndex = 0;
                foreach (JsonElement speaker in output.GetProperty("Speakers").EnumerateArray())
                {
                    int cell = offset + 4 + speakerIndex * SpeakerLevels.SerializedSize;
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(cell),
                        speaker.GetProperty("Speaker").GetInt32());
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(cell + 4),
                        speaker.GetProperty("NumLevels").GetInt32());
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(cell + 8),
                        Single(speaker, "Level0"));
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(cell + 12),
                        Single(speaker, "Level1"));
                    speakerIndex++;
                }
                if (speakerIndex != 6) throw new InvalidDataException("PC speaker map has an invalid speaker count.");
                outputIndex++;
            }
            if (outputIndex != 2) throw new InvalidDataException("PC speaker map has an invalid output count.");
            channelIndex++;
        }
        if (channelIndex != 2) throw new InvalidDataException("PC speaker map has an invalid channel count.");
        return bytes;
    }

    private static (byte[] Bytes, string Archive) ReadIwdSound(IReadOnlyList<string> paths, string entryPath)
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
        throw new FileNotFoundException($"COD4 PC streamed sound '{entryPath}' was not found in the selected IWDs.");
    }

    private static byte[] WrapLoadedPcm(string directory, JsonElement source)
    {
        if (source.GetProperty("Format").GetInt32() != 1)
            throw new NotSupportedException("COD4 PC loaded sound is not PCM.");
        string filename = source.GetProperty("File").GetString() ?? throw new InvalidDataException("Loaded sound has no file.");
        byte[] pcm = File.ReadAllBytes(Path.Combine(directory, filename));
        int channels = source.GetProperty("Channels").GetInt32();
        int rate = source.GetProperty("Rate").GetInt32();
        int bits = source.GetProperty("Bits").GetInt32();
        if (channels is < 1 or > 2 || rate <= 0 || bits is not (8 or 16))
            throw new NotSupportedException("Unsupported COD4 PC loaded PCM format.");
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write("RIFF"u8); writer.Write(checked(pcm.Length + 36)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)channels);
        writer.Write(rate); writer.Write(checked(rate * channels * bits / 8));
        writer.Write((short)(channels * bits / 8)); writer.Write((short)bits);
        writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
        return memory.ToArray();
    }

    private static async Task<byte[]> EncodeMp3Async(byte[] source, string aliasName, string scratch, int index, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.AsSpan().StartsWith("ID3"u8)) return source;
        if (source.Length > 4 && source[0] == 0xff && (source[1] & 0xe6) == 0xe2 &&
            (source[2] & 0xf0) is not (0x00 or 0xf0) && (source[2] & 0x0c) != 0x0c)
        {
            byte[] tagged = new byte[source.Length + 10];
            "ID3"u8.CopyTo(tagged);
            tagged[3] = 3;
            source.CopyTo(tagged, 10);
            return tagged;
        }
        string input = Path.Combine(scratch, $"pc-sound-{index:D5}.wav");
        string output = Path.Combine(scratch, $"pc-sound-{index:D5}.mp3");
        string bundled = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        await File.WriteAllBytesAsync(input, source, token).ConfigureAwait(false);
        await Iw3PcExtractionBackend.RunToolAsync(
            $"FFmpeg MP3 encoding for sound alias '{aliasName}'",
            File.Exists(bundled) ? bundled : "ffmpeg", scratch,
            ["-v", "error", "-y", "-i", input, "-codec:a", "libmp3lame", "-id3v2_version", "3", output],
            token).ConfigureAwait(false);
        byte[] encoded = await File.ReadAllBytesAsync(output, token).ConfigureAwait(false);
        if (!encoded.AsSpan().StartsWith("ID3"u8))
            throw new InvalidDataException("FFmpeg did not produce an ID3 MP3 sound.");
        return encoded;
    }
}
