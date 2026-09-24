using System.Globalization;
using System.Text.Json;
using IW4.Game.Assets.Sound;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Sound;

/// <summary>
/// Imports and exports the native Sound alias graph and any available sound bytes.
/// Streamed bytes are supplied by the caller because they live outside the asset graph.
/// </summary>
public sealed class SoundAliasListExchange
{
    public SoundAliasListAsset Link(string sourceDirectory, string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "Sound");
        string root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Sound source directory '{root}' does not exist.");
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            root = rootInfo.ResolveLinkTarget(true) is DirectoryInfo target
                ? Path.GetFullPath(target.FullName)
                : throw new InvalidDataException("Sound source directory link is invalid.");
        }

        string jsonPath = ResolveSourcePath(root, $"soundaliases/{name}.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
        JsonElement json = document.RootElement;
        RequireObject(json, "Sound");
        if (Integer(json, "version", "Sound") != 1 ||
            String(json, "assetType", "Sound") != "Sound")
            throw new InvalidDataException("Expected a version 1 Sound source document.");
        string? documentName = String(json, "name", "Sound");
        if (SourceOutput.NormalizeOwnedAssetName(documentName, "Sound") != name)
            throw new InvalidDataException($"Sound source name does not match '{name}'.");

        JsonElement rows = Array(json, "aliases", "Sound");
        int count = Integer(json, "count", "Sound");
        if (count < 0 || rows.GetArrayLength() != count)
            throw new InvalidDataException("Sound alias count does not match its rows.");
        var aliases = new SndAlias[count];
        for (int i = 0; i < aliases.Length; i++)
        {
            aliases[i] = ReadAlias(rows[i], root, i);
            if (i != 0 && aliases[i].SoundFileCount != aliases[0].SoundFileCount)
                throw new InvalidDataException("All Sound aliases must have the same language row count.");
        }
        return new SoundAliasListAsset { AliasName = documentName, Count = count, Aliases = aliases };
    }

    private static SndAlias ReadAlias(JsonElement json, string root, int index)
    {
        string path = $"Sound.aliases[{index}]";
        RequireObject(json, path);
        JsonElement files = Array(json, "soundFiles", path);
        int fileCount = Integer(json, "soundFileCount", path);
        if (fileCount < 0 || files.GetArrayLength() != fileCount)
            throw new InvalidDataException($"{path}.soundFileCount does not match its rows.");
        var soundFiles = new SoundFile[fileCount];
        for (int i = 0; i < fileCount; i++)
            soundFiles[i] = ReadSoundFile(files[i], root, $"{path}.soundFiles[{i}]", i);

        return new SndAlias
        {
            AliasName = String(json, "aliasName", path),
            Subtitle = String(json, "subtitle", path),
            SecondaryAliasName = String(json, "secondaryAliasName", path),
            ChainAliasName = String(json, "chainAliasName", path),
            MixerGroup = String(json, "mixerGroup", path),
            Sequence = Integer(json, "sequence", path),
            VolumeMin = Float(json, "volumeMin", path),
            VolumeMax = Float(json, "volumeMax", path),
            PitchMin = Float(json, "pitchMin", path),
            PitchMax = Float(json, "pitchMax", path),
            DistanceMin = Float(json, "distanceMin", path),
            DistanceMax = Float(json, "distanceMax", path),
            VelocityMin = Float(json, "velocityMin", path),
            Flags = Integer(json, "flags", path),
            SlavePercentage = Float(json, "slavePercentage", path),
            Probability = Float(json, "probability", path),
            LfePercentage = Float(json, "lfePercentage", path),
            CenterPercentage = Float(json, "centerPercentage", path),
            StartDelay = Integer(json, "startDelay", path),
            VolumeFalloffCurve = ReadCurve(json, path),
            EnvelopMin = Float(json, "envelopMin", path),
            EnvelopMax = Float(json, "envelopMax", path),
            EnvelopPercentage = Float(json, "envelopPercentage", path),
            SpeakerMap = ReadSpeakerMap(json, path),
            SoundFileCount = fileCount,
            SoundFiles = soundFiles
        };
    }

    private static SndCurve? ReadCurve(JsonElement alias, string path)
    {
        string status = String(alias, "volumeFalloffCurveStatus", path) ?? "";
        string? named = String(alias, "volumeFalloffCurveName", path);
        JsonElement curve = Property(alias, "volumeFalloffCurve", path);
        if (status == "absent" && curve.ValueKind == JsonValueKind.Null && named is null)
            return null;
        if (status != "materialized")
            throw new InvalidDataException($"{path}.volumeFalloffCurve is {status}; semantic curve data is required for linking.");
        RequireObject(curve, $"{path}.volumeFalloffCurve");
        string? name = String(curve, "name", path);
        if (string.IsNullOrWhiteSpace(name) || name != named)
            throw new InvalidDataException($"{path}.volumeFalloffCurve name does not match its reference.");
        JsonElement knots = Array(curve, "knots", path);
        int count = Integer(curve, "knotCount", path);
        if (count < 0 || count > SndCurve.MaxKnotCount || knots.GetArrayLength() != SndCurve.MaxKnotCount)
            throw new InvalidDataException($"{path}.volumeFalloffCurve requires 16 knot rows and a valid active count.");
        var values = new SndCurveKnot[SndCurve.MaxKnotCount];
        for (int i = 0; i < values.Length; i++)
        {
            JsonElement knot = knots[i];
            RequireObject(knot, $"{path}.volumeFalloffCurve.knots[{i}]");
            values[i] = new SndCurveKnot(Float(knot, "x", path), Float(knot, "y", path));
        }
        return new SndCurve
        {
            Filename = name,
            KnotCount = checked((ushort)count),
            Padding = UShort(curve, "padding", path),
            Knots = values
        };
    }

    private static SpeakerMap? ReadSpeakerMap(JsonElement alias, string path)
    {
        JsonElement map = Property(alias, "speakerMap", path);
        if (map.ValueKind == JsonValueKind.Null)
            return null;
        RequireObject(map, $"{path}.speakerMap");
        JsonElement channels = Array(map, "channels", path);
        if (channels.GetArrayLength() != 2)
            throw new InvalidDataException($"{path}.speakerMap requires two channels.");
        var channelValues = new SpeakerMapChannel[2];
        for (int i = 0; i < 2; i++)
        {
            JsonElement channel = channels[i];
            RequireObject(channel, $"{path}.speakerMap.channels[{i}]");
            JsonElement outputs = Array(channel, "outputs", path);
            if (outputs.GetArrayLength() != 2)
                throw new InvalidDataException($"{path}.speakerMap.channels[{i}] requires two outputs.");
            var outputValues = new XAudioChannelMap[2];
            for (int j = 0; j < 2; j++)
            {
                JsonElement output = outputs[j];
                RequireObject(output, $"{path}.speakerMap.channels[{i}].outputs[{j}]");
                JsonElement speakers = Array(output, "speakers", path);
                if (speakers.GetArrayLength() != 6)
                    throw new InvalidDataException($"{path}.speakerMap.channels[{i}].outputs[{j}] requires six speakers.");
                var speakerValues = new SpeakerLevels[6];
                for (int k = 0; k < 6; k++)
                {
                    JsonElement speaker = speakers[k];
                    RequireObject(speaker, $"{path}.speakerMap.channels[{i}].outputs[{j}].speakers[{k}]");
                    speakerValues[k] = new SpeakerLevels
                    {
                        Speaker = Integer(speaker, "speaker", path),
                        NumLevels = Integer(speaker, "numLevels", path),
                        Level0 = Float(speaker, "level0", path),
                        Level1 = Float(speaker, "level1", path)
                    };
                }
                outputValues[j] = new XAudioChannelMap
                {
                    EntryCount = Integer(output, "entryCount", path),
                    Speakers = speakerValues
                };
            }
            channelValues[i] = new SpeakerMapChannel { Outputs = outputValues };
        }
        byte[] padding;
        try { padding = Convert.FromBase64String(String(map, "padding", path) ?? ""); }
        catch (FormatException error) { throw new InvalidDataException($"{path}.speakerMap.padding is not base64.", error); }
        if (padding.Length != 3)
            throw new InvalidDataException($"{path}.speakerMap.padding requires three bytes.");
        return new SpeakerMap
        {
            IsDefault = Byte(map, "isDefault", path),
            Padding = padding,
            Name = String(map, "name", path),
            Channels = channelValues
        };
    }

    private static SoundFile ReadSoundFile(JsonElement json, string root, string path, int languageIndex)
    {
        RequireObject(json, path);
        if (Integer(json, "languageIndex", path) != languageIndex)
            throw new InvalidDataException($"{path}.languageIndex is out of order.");
        byte type = Byte(json, "type", path);
        if (!Enum.IsDefined((SndAliasType)type) || type == (byte)SndAliasType.Count)
            throw new InvalidDataException($"{path}.type {type} is unsupported.");
        SoundFilePayload payload = type == (byte)SndAliasType.Loaded
            ? ReadLoaded(json, root, path)
            : ReadStreamed(json, root, path);
        return new SoundFile
        {
            Type = (SndAliasType)type,
            Exists = Byte(json, "exists", path),
            Padding = UShort(json, "padding", path),
            Payload = payload
        };
    }

    private static LoadedSoundFile ReadLoaded(JsonElement json, string root, string path)
    {
        string? name = String(json, "loadedSoundName", path);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException($"{path} has no materialized LoadedSound name.");
        int audioCount = Integer(json, "physicalDataByteCount", path);
        ushort seekCount = UShort(json, "seekTableCount", path);
        if (audioCount < 0 || audioCount > SoundFile.MaxInMemoryPayloadBytes)
            throw new InvalidDataException($"{path}.physicalDataByteCount is outside the supported payload limit.");
        byte[]? audio = ReadPayload(json, root, path, "audioPath", "audioStatus", audioCount);
        byte[]? seek = ReadPayload(json, root, path, "seekTablePath", "seekTableStatus", checked(seekCount * sizeof(uint)));
        return new LoadedSoundFile
        {
            LoadedSound = new LoadedSound
            {
                Name = name,
                PhysicalDataByteCount = audioCount,
                FrameCount = UShort(json, "frameCount", path),
                ChannelCount = UShort(json, "channelCount", path),
                SampleRate = UShort(json, "sampleRate", path),
                Pad0E = UShort(json, "pad0E", path),
                Pad10 = UShort(json, "pad10", path),
                SeekTableCount = seekCount,
                PhysicalData = audio,
                SeekTable = seek
            }
        };
    }

    private static StreamedSound ReadStreamed(JsonElement json, string root, string path)
    {
        uint index = UInt(json, "fileIndex", path);
        StreamedSoundSource source = index == 0
            ? new ExternalStreamedSoundSource
            {
                Directory = String(json, "directory", path),
                Filename = String(json, "filename", path)
            }
            : new StreamedSoundFileSource
            {
                StreamFileOffset = Integer(json, "streamFileOffset", path),
                StreamFileLength = Integer(json, "streamFileLength", path)
            };
        string? payloadPath = String(json, "streamPayloadPath", path);
        string status = String(json, "streamPayloadStatus", path) ?? "";
        if (status == "exported" && payloadPath is not null)
        {
            string resolved = ResolveSourcePath(root, payloadPath);
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"{path} streamed sidecar is missing.", resolved);
        }
        else if (status != "unavailable" || payloadPath is not null)
            throw new InvalidDataException($"{path} has inconsistent streamed payload status and path.");
        return new StreamedSound { FileIndex = index, Source = source };
    }

    private static byte[]? ReadPayload(
        JsonElement json, string root, string path, string pathField, string statusField, int expectedLength)
    {
        string? relative = String(json, pathField, path);
        string status = String(json, statusField, path) ?? "";
        if (status == "unavailable" && relative is null)
        {
            if (expectedLength != 0)
                throw new InvalidDataException($"{path}.{pathField} is unavailable but {expectedLength} bytes are required.");
            return null;
        }
        if (status != "exported" || relative is null)
            throw new InvalidDataException($"{path}.{pathField} has inconsistent status and path.");
        string resolved = ResolveSourcePath(root, relative);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"{path}.{pathField} sidecar is missing.", resolved);
        var info = new FileInfo(resolved);
        if (info.Length != expectedLength)
            throw new InvalidDataException($"{path}.{pathField} has {info.Length} bytes; expected {expectedLength}.");
        return File.ReadAllBytes(resolved);
    }

    private static string ResolveSourcePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
            relative.Contains('\\') || relative.Any(char.IsControl))
            throw new InvalidDataException($"Sound source path '{relative}' is invalid.");
        string[] parts = relative.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException($"Sound source path '{relative}' is invalid.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string back = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(back) || back == ".." ||
            back.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidDataException($"Sound source path '{relative}' escapes the source directory.");
        string current = root;
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Sound source path '{relative}' traverses a filesystem link.");
        }
        return full;
    }

    private static JsonElement Property(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
            throw new InvalidDataException($"{path}.{name} is required.");
        return value;
    }

    private static void RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path} must be an object.");
    }

    private static JsonElement Array(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{path}.{name} must be an array.");
        return value;
    }

    private static string? String(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => throw new InvalidDataException($"{path}.{name} must be a string or null.")
        };
    }

    private static int Integer(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new InvalidDataException($"{path}.{name} must be an Int32.");
        return result;
    }

    private static uint UInt(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out uint result))
            throw new InvalidDataException($"{path}.{name} must be a UInt32.");
        return result;
    }

    private static ushort UShort(JsonElement parent, string name, string path)
    {
        int value = Integer(parent, name, path);
        if (value is < ushort.MinValue or > ushort.MaxValue)
            throw new InvalidDataException($"{path}.{name} must be a UInt16.");
        return (ushort)value;
    }

    private static byte Byte(JsonElement parent, string name, string path)
    {
        int value = Integer(parent, name, path);
        if (value is < byte.MinValue or > byte.MaxValue)
            throw new InvalidDataException($"{path}.{name} must be a byte.");
        return (byte)value;
    }

    private static float Float(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            float.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        throw new InvalidDataException($"{path}.{name} must be a Single.");
    }

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        SoundAliasListAsset asset,
        Func<StreamedSound, byte[]?>? streamedPayloadResolver = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.AliasName, "Sound");
        IReadOnlyList<SndAlias> aliases = asset.Aliases ??
            throw new InvalidDataException("Sound aliases cannot be null.");
        if (asset.Count != aliases.Count)
        {
            throw new InvalidDataException(
                $"Sound '{name}' declares {asset.Count} aliases but contains {aliases.Count}.");
        }

        string stem = $"soundaliases/{name}";
        var files = new List<(string RelativePath, Action<Stream> Write)>();
        var payloads = new Dictionary<(int Alias, int Language), PayloadPaths>();
        for (int aliasIndex = 0; aliasIndex < aliases.Count; aliasIndex++)
        {
            SndAlias alias = aliases[aliasIndex] ??
                throw new InvalidDataException($"Sound '{name}' has a null alias at {aliasIndex}.");
            IReadOnlyList<SoundFile> soundFiles = alias.SoundFiles ??
                throw new InvalidDataException($"Sound '{name}' alias {aliasIndex} has no SoundFile table.");
            if (alias.SoundFileCount != soundFiles.Count)
            {
                throw new InvalidDataException(
                    $"Sound '{name}' alias {aliasIndex} declares {alias.SoundFileCount} language rows but contains {soundFiles.Count}.");
            }

            for (int languageIndex = 0; languageIndex < soundFiles.Count; languageIndex++)
            {
                SoundFile soundFile = soundFiles[languageIndex] ??
                    throw new InvalidDataException(
                        $"Sound '{name}' alias {aliasIndex} language {languageIndex} is null.");
                string payloadStem = $"{stem}.payloads/{aliasIndex:D4}-{languageIndex:D4}";
                string? audioPath = null;
                string? seekPath = null;
                string? streamPath = null;
                if (soundFile.Type == SndAliasType.Loaded)
                {
                    LoadedSoundFile loadedFile = soundFile.Loaded ??
                        throw new InvalidDataException(
                            $"Sound '{name}' alias {aliasIndex} language {languageIndex} has no LoadedSound payload.");
                    if (loadedFile.LoadedSound?.PhysicalData is { } audio)
                    {
                        audioPath = $"{payloadStem}.audio.bin";
                        files.Add((audioPath, stream => stream.Write(audio)));
                    }
                    if (loadedFile.LoadedSound?.SeekTable is { } seek)
                    {
                        seekPath = $"{payloadStem}.seek.bin";
                        files.Add((seekPath, stream => stream.Write(seek)));
                    }
                }
                else
                {
                    StreamedSound streamed = soundFile.Streamed ??
                        throw new InvalidDataException(
                            $"Sound '{name}' alias {aliasIndex} language {languageIndex} has no streamed payload metadata.");
                    if ((streamed.FileIndex == 0 && streamed.ExternalFile is null) ||
                        (streamed.FileIndex != 0 && streamed.StreamFile is null))
                    {
                        throw new InvalidDataException(
                            $"Sound '{name}' alias {aliasIndex} language {languageIndex} has no source metadata for file index {streamed.FileIndex}.");
                    }
                    if (streamedPayloadResolver?.Invoke(streamed) is { } bytes)
                    {
                        streamPath = $"{payloadStem}.stream.bin";
                        files.Add((streamPath, stream => stream.Write(bytes)));
                    }
                }

                payloads.Add(
                    (aliasIndex, languageIndex),
                    new PayloadPaths(audioPath, seekPath, streamPath));
            }
        }

        files.Insert(0, ($"{stem}.json", stream => WriteJson(stream, asset, payloads)));
        return new SourceOutput(sourceDirectory).WriteBinaryBatch(files);
    }

    private static void WriteJson(
        Stream stream,
        SoundAliasListAsset asset,
        IReadOnlyDictionary<(int Alias, int Language), PayloadPaths> payloads)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("assetType", "Sound");
        WriteString(writer, "name", asset.AliasName);
        writer.WriteNumber("count", asset.Count);
        writer.WriteStartArray("aliases");
        for (int aliasIndex = 0; aliasIndex < asset.Aliases.Count; aliasIndex++)
        {
            SndAlias alias = asset.Aliases[aliasIndex];
            writer.WriteStartObject();
            WriteString(writer, "aliasName", alias.AliasName);
            WriteString(writer, "subtitle", alias.Subtitle);
            WriteString(writer, "secondaryAliasName", alias.SecondaryAliasName);
            WriteString(writer, "chainAliasName", alias.ChainAliasName);
            WriteString(writer, "mixerGroup", alias.MixerGroup);
            writer.WriteNumber("sequence", alias.Sequence);
            WriteFloat(writer, "volumeMin", alias.VolumeMin);
            WriteFloat(writer, "volumeMax", alias.VolumeMax);
            WriteFloat(writer, "pitchMin", alias.PitchMin);
            WriteFloat(writer, "pitchMax", alias.PitchMax);
            WriteFloat(writer, "distanceMin", alias.DistanceMin);
            WriteFloat(writer, "distanceMax", alias.DistanceMax);
            WriteFloat(writer, "velocityMin", alias.VelocityMin);
            writer.WriteNumber("flags", alias.Flags);
            WriteFloat(writer, "slavePercentage", alias.SlavePercentage);
            WriteFloat(writer, "probability", alias.Probability);
            WriteFloat(writer, "lfePercentage", alias.LfePercentage);
            WriteFloat(writer, "centerPercentage", alias.CenterPercentage);
            writer.WriteNumber("startDelay", alias.StartDelay);
            WriteString(writer, "volumeFalloffCurveName", alias.VolumeFalloffCurve?.Filename);
            WriteVolumeFalloffCurve(writer, alias);
            WriteFloat(writer, "envelopMin", alias.EnvelopMin);
            WriteFloat(writer, "envelopMax", alias.EnvelopMax);
            WriteFloat(writer, "envelopPercentage", alias.EnvelopPercentage);
            WriteSpeakerMap(writer, alias.SpeakerMap);
            writer.WriteNumber("soundFileCount", alias.SoundFileCount);
            writer.WriteStartArray("soundFiles");
            for (int languageIndex = 0; languageIndex < alias.SoundFiles.Count; languageIndex++)
            {
                SoundFile soundFile = alias.SoundFiles[languageIndex];
                PayloadPaths paths = payloads[(aliasIndex, languageIndex)];
                writer.WriteStartObject();
                writer.WriteNumber("languageIndex", languageIndex);
                writer.WriteNumber("type", (byte)soundFile.Type);
                writer.WriteNumber("exists", soundFile.Exists);
                writer.WriteNumber("padding", soundFile.Padding);
                if (soundFile.Type == SndAliasType.Loaded)
                {
                    LoadedSoundFile loadedFile = soundFile.Loaded ??
                        throw new InvalidDataException("A Loaded SoundFile has no payload.");
                    LoadedSound? loaded = loadedFile.LoadedSound;
                    WriteString(writer, "loadedSoundName", loaded?.Name);
                    if (loaded is not null)
                    {
                        writer.WriteNumber("physicalDataByteCount", loaded.PhysicalDataByteCount);
                        writer.WriteNumber("frameCount", loaded.FrameCount);
                        writer.WriteNumber("channelCount", loaded.ChannelCount);
                        writer.WriteNumber("sampleRate", loaded.SampleRate);
                        writer.WriteNumber("pad0E", loaded.Pad0E);
                        writer.WriteNumber("pad10", loaded.Pad10);
                        writer.WriteNumber("seekTableCount", loaded.SeekTableCount);
                    }
                    WriteString(writer, "audioPath", paths.Audio);
                    WriteString(writer, "seekTablePath", paths.Seek);
                    writer.WriteString("audioStatus", paths.Audio is null ? "unavailable" : "exported");
                    writer.WriteString("seekTableStatus", paths.Seek is null ? "unavailable" : "exported");
                }
                else
                {
                    StreamedSound streamed = soundFile.Streamed ??
                        throw new InvalidDataException("A streamed SoundFile has no payload metadata.");
                    writer.WriteNumber("fileIndex", streamed.FileIndex);
                    if (streamed.ExternalFile is { } external)
                    {
                        WriteString(writer, "directory", external.Directory);
                        WriteString(writer, "filename", external.Filename);
                    }
                    else if (streamed.StreamFile is { } streamFile)
                    {
                        writer.WriteNumber("streamFileOffset", streamFile.StreamFileOffset);
                        writer.WriteNumber("streamFileLength", streamFile.StreamFileLength);
                    }
                    WriteString(writer, "streamPayloadPath", paths.Stream);
                    writer.WriteString("streamPayloadStatus", paths.Stream is null ? "unavailable" : "exported");
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteVolumeFalloffCurve(Utf8JsonWriter writer, SndAlias alias)
    {
        SndCurve? curve = alias.VolumeFalloffCurve;
        if (curve is null)
        {
            writer.WriteNull("volumeFalloffCurve");
            bool unresolved = alias.VolumeFalloffCurvePointer.Type != PointerType.Null;
            writer.WriteString(
                "volumeFalloffCurveStatus",
                unresolved ? "unresolved" : "absent");
            if (unresolved)
            {
                writer.WriteString(
                    "volumeFalloffCurvePointerRaw",
                    $"0x{unchecked((uint)alias.VolumeFalloffCurvePointer.Raw):X8}");
            }
            return;
        }

        writer.WriteString("volumeFalloffCurveStatus", "materialized");
        writer.WriteStartObject("volumeFalloffCurve");
        WriteString(writer, "name", curve.Filename);
        writer.WriteNumber("knotCount", curve.KnotCount);
        writer.WriteNumber("padding", curve.Padding);
        writer.WriteStartArray("knots");
        foreach (SndCurveKnot knot in curve.Knots)
        {
            writer.WriteStartObject();
            WriteFloat(writer, "x", knot.X);
            WriteFloat(writer, "y", knot.Y);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSpeakerMap(Utf8JsonWriter writer, SpeakerMap? map)
    {
        if (map is null)
        {
            writer.WriteNull("speakerMap");
            return;
        }

        writer.WriteStartObject("speakerMap");
        writer.WriteNumber("isDefault", map.IsDefault);
        writer.WriteBase64String("padding", map.Padding);
        WriteString(writer, "name", map.Name);
        writer.WriteStartArray("channels");
        foreach (SpeakerMapChannel channel in map.Channels)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("outputs");
            foreach (XAudioChannelMap output in channel.Outputs)
            {
                writer.WriteStartObject();
                writer.WriteNumber("entryCount", output.EntryCount);
                writer.WriteStartArray("speakers");
                foreach (SpeakerLevels speaker in output.Speakers)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("speaker", speaker.Speaker);
                    writer.WriteNumber("numLevels", speaker.NumLevels);
                    WriteFloat(writer, "level0", speaker.Level0);
                    WriteFloat(writer, "level1", speaker.Level1);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteString(name, value);
    }

    private static void WriteFloat(Utf8JsonWriter writer, string name, float value)
    {
        if (float.IsFinite(value))
            writer.WriteNumber(name, value);
        else
            writer.WriteString(name, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private readonly record struct PayloadPaths(string? Audio, string? Seek, string? Stream);
}
