using System.Buffers.Binary;
using System.Text.Json;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets;

/// <summary>Exports selected IW3 PC alias lists in the format consumed by MapConverter.</summary>
public static class Iw3PcSoundExporter
{
    private const int AliasSize = 0x5c;
    private const int SpeakerMapSize = 0x198;

    public static void ExportSelected(Iw3PcZone zone, IReadOnlyCollection<string> names,
        string outputDirectory,
        IReadOnlyDictionary<string, (Iw3PcZone Zone, int RecordId)>? loadedSources = null,
        CancellationToken token = default,
        IReadOnlyCollection<int>? selectedRecordIds = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(names);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException($"Sound output path already exists: '{outputDirectory}'.");
        var selected = names.ToHashSet(StringComparer.Ordinal);
        if (selected.Count != names.Count)
            throw new InvalidDataException("Sound selection contains duplicate names.");
        HashSet<int>? recordIds = selectedRecordIds?.ToHashSet();
        if (recordIds?.Count != selectedRecordIds?.Count)
            throw new InvalidDataException("Sound selection contains duplicate record IDs.");
        Directory.CreateDirectory(outputDirectory);
        var sounds = new List<object>();
        int payloadIndex = 0;
        foreach (Iw3PcAsset asset in zone.Assets)
        {
            token.ThrowIfCancellationRequested();
            if (asset.Type != "sound" || asset.Name.StartsWith(',') || !selected.Contains(asset.Name) ||
                recordIds is not null && !recordIds.Contains(asset.Id))
                continue;
            int count = I32(zone, asset.Offset + 8);
            int head = zone.Pointer(asset.Offset + 4);
            if (count <= 0 || count >= 65536 || head == 0)
                throw new InvalidDataException($"Invalid selected sound alias rows: '{asset.Name}'.");
            var rows = new List<Dictionary<string, object?>>();
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int row = checked(head + index * AliasSize);
                var fields = new Dictionary<string, object?>
                {
                    ["SecondaryAlias"] = OptionalString(zone, row + 8),
                    ["ChainAlias"] = OptionalString(zone, row + 12),
                    ["Sequence"] = I32(zone, row + 20),
                    ["VolumeMin"] = F32(zone, row + 24),
                    ["VolumeMax"] = F32(zone, row + 28),
                    ["PitchMin"] = F32(zone, row + 32),
                    ["PitchMax"] = F32(zone, row + 36),
                    ["DistanceMin"] = F32(zone, row + 40),
                    ["DistanceMax"] = F32(zone, row + 44),
                    ["Flags"] = I32(zone, row + 48),
                    ["SlavePercentage"] = F32(zone, row + 52),
                    ["Probability"] = F32(zone, row + 56),
                    ["LfePercentage"] = F32(zone, row + 60),
                    ["CenterPercentage"] = F32(zone, row + 64),
                    ["StartDelay"] = I32(zone, row + 68),
                    ["EnvelopMin"] = F32(zone, row + 76),
                    ["EnvelopMax"] = F32(zone, row + 80),
                    ["EnvelopPercentage"] = F32(zone, row + 84),
                    ["SpeakerMapName"] = null,
                };
                int speaker = zone.Pointer(row + 88);
                if (speaker != 0)
                {
                    fields["SpeakerMapName"] = OptionalString(zone, speaker + 4);
                    fields["SpeakerMap"] = ReadSpeakerMap(zone, speaker);
                }
                int curve = zone.Pointer(row + 72);
                if (curve != 0)
                {
                    int knotCount = I32(zone, curve + 4);
                    if (knotCount is < 0 or > 8)
                        throw new InvalidDataException($"Invalid sound curve knots in '{asset.Name}'.");
                    fields["Curve"] = new { Name = OptionalString(zone, curve),
                        Knots = Enumerable.Range(0, knotCount).Select(knot =>
                            new[] { F32(zone, curve + 8 + knot * 8), F32(zone, curve + 12 + knot * 8) }).ToArray() };
                }
                int soundFile = zone.Pointer(row + 16);
                if (soundFile == 0 || zone.Bytes(soundFile + 1, 1)[0] == 0)
                {
                    fields["MissingPayload"] = true;
                    rows.Add(fields);
                    continue;
                }
                byte type = zone.Bytes(soundFile, 1)[0];
                if (type == 2)
                {
                    string? dir = OptionalString(zone, soundFile + 4);
                    string file = OptionalString(zone, soundFile + 8) ??
                        throw new InvalidDataException($"Streamed sound in '{asset.Name}' has no filename.");
                    fields["StreamedPath"] = ("sound/" + (string.IsNullOrEmpty(dir) ? "" : dir + "/") + file)
                        .Replace('\\', '/');
                }
                else if (type == 1)
                {
                    int loaded = zone.Pointer(soundFile + 4);
                    Iw3PcZone payloadZone = zone;
                    string? loadedName = loaded == 0 ? null : OptionalString(zone, loaded);
                    if ((loaded == 0 || zone.Pointer(loaded + 40) == 0 || U32(zone, loaded + 12) == 0) &&
                        loadedName is not null && loadedSources is not null &&
                        loadedSources.TryGetValue(Iw3PcAsset.CanonicalName(loadedName), out var source))
                    {
                        Iw3PcAsset provider = source.Zone.Assets[source.RecordId];
                        if (provider.Type != "loadedsound" || provider.Name.StartsWith(',') ||
                            Iw3PcAsset.CanonicalName(provider.Name) != Iw3PcAsset.CanonicalName(loadedName))
                            throw new InvalidDataException($"Loaded sound source does not match '{loadedName}'.");
                        loaded = provider.Offset;
                        payloadZone = source.Zone;
                    }
                    if (loaded == 0)
                        throw new InvalidDataException($"Loaded sound in '{asset.Name}' has no definition.");
                    int length = checked((int)U32(payloadZone, loaded + 12));
                    int data = payloadZone.Pointer(loaded + 40);
                    if (data == 0 || length is <= 0 or > 67108864)
                        throw new InvalidDataException($"Invalid loaded sound payload in '{asset.Name}'.");
                    string filename = $"loaded-{payloadIndex++}.pcm";
                    File.WriteAllBytes(Path.Combine(outputDirectory, filename), payloadZone.Bytes(data, length).ToArray());
                    fields["Loaded"] = new
                    {
                        Name = OptionalString(payloadZone, loaded), File = filename,
                        Format = I32(payloadZone, loaded + 4), Rate = U32(payloadZone, loaded + 16),
                        Bits = I32(payloadZone, loaded + 20), Channels = I32(payloadZone, loaded + 24)
                    };
                }
                else throw new InvalidDataException($"Unsupported sound file type {type} in '{asset.Name}'.");
                rows.Add(fields);
            }
            sounds.Add(new { Name = asset.Name, Rows = rows });
        }
        if (recordIds is not null && sounds.Count != recordIds.Count)
            throw new InvalidDataException("Selected IW3 sound records do not match the requested aliases.");
        File.WriteAllBytes(Path.Combine(outputDirectory, "sounds.json"), JsonSerializer.SerializeToUtf8Bytes(sounds));
    }

    private static object ReadSpeakerMap(Iw3PcZone zone, int offset)
    {
        zone.Bytes(offset, SpeakerMapSize);
        var channels = new List<object>();
        for (int channel = 0; channel < 2; channel++)
        {
            var outputs = new List<object>();
            for (int output = 0; output < 2; output++)
            {
                int cell = offset + 8 + (channel * 2 + output) * 100;
                int count = I32(zone, cell);
                if (count is < 0 or > 6)
                    throw new InvalidDataException("Invalid IW3 sound speaker count.");
                var speakers = new List<object>();
                for (int speaker = 0; speaker < 6; speaker++)
                {
                    int item = cell + 4 + speaker * 16;
                    speakers.Add(new { Speaker = I32(zone, item), NumLevels = I32(zone, item + 4),
                        Level0 = F32(zone, item + 8), Level1 = F32(zone, item + 12) });
                }
                outputs.Add(new { EntryCount = count, Speakers = speakers });
            }
            channels.Add(outputs);
        }
        return new { IsDefault = zone.Bytes(offset, 1)[0] == 0 ? 0 : 1, Channels = channels };
    }

    private static string? OptionalString(Iw3PcZone zone, int field) =>
        zone.Pointer(field) == 0 ? null : zone.String(field) is { Length: > 0 } value ? value : null;
    private static int I32(Iw3PcZone zone, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(offset, 4));
    private static uint U32(Iw3PcZone zone, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(zone.Bytes(offset, 4));
    private static float F32(Iw3PcZone zone, int offset) => BitConverter.Int32BitsToSingle(I32(zone, offset));
}
