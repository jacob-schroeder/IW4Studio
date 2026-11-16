using System.Buffers.Binary;

namespace MapConverter.Game.IW3.PS3.Extraction;

internal sealed record Iw3Ps3SourceSoundRow(
    string? SecondaryAlias, string? ChainAlias,
    int Sequence, float VolumeMin, float VolumeMax, float PitchMin, float PitchMax,
    float DistanceMin, float DistanceMax, int Flags,
    float SlavePercentage, float Probability, float LfePercentage,
    float CenterPercentage, int StartDelay, float EnvelopMin, float EnvelopMax,
    float EnvelopPercentage, byte[]? SpeakerMap, string? SpeakerMapName,
    string? LoadedSoundName, byte[]? LoadedPayload,
    uint PackageIndex, int PackageOffset, int PackageLength);

internal sealed record Iw3Ps3SourceSound(string Name, IReadOnlyList<Iw3Ps3SourceSoundRow> Rows);

internal static partial class Iw3Ps3FastFileCatalog
{
    internal static IReadOnlyDictionary<string, Iw3Ps3SourceSound> ReadSourceSounds(
        string path, IReadOnlySet<string> names, CancellationToken token)
    {
        var (data, end) = ReadDecoded(path, token);
        var reader = new Reader(path, data, end, token, sourceSoundNames: names);
        reader.ReadCatalog(shaderPrefix: true);
        return reader.SourceSounds;
    }

    internal static IReadOnlyDictionary<string, byte[]> ReadSourceLoadedSounds(
        string path, IReadOnlySet<string> names, CancellationToken token)
    {
        var (data, end) = ReadDecoded(path, token);
        var reader = new Reader(path, data, end, token, sourceLoadedNames: names);
        reader.ReadCatalog(shaderPrefix: true);
        return reader.SourceLoadedSounds;
    }

    private sealed partial class Reader
    {
        private sealed record SourceLoadedSound(string Name, Region? Payload);

        internal Dictionary<string, Iw3Ps3SourceSound> SourceSounds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, byte[]> SourceLoadedSounds { get; } =
            new(StringComparer.Ordinal);

        // Source-only traversal. COD4 PS3 0xccb48 loads a 12-byte sound-list
        // root, then 0xccac0 walks 0x5c-byte alias rows through 0xcc908.
        // IW3 snd_alias_t in iw3x-port Structs.hpp:2584 puts flags at +0x30,
        // startDelay at +0x44, curve at +0x48; it has no velocityMin field.
        private Pointer? SoundPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "sound", 0x0c, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    string name = ReadString(U32(start)) ??
                        throw Invalid("source sound asset has no name");
                    if (name.Length == 0) throw Invalid("source sound asset has an empty name");
                    int count = Count(U32(start + 8));
                    uint rawAliases = U32(start + 4);
                    Pointer? aliases = ReadArray(rawAliases, 4,
                        Size(count, 0x5c), "sound-alias-rows");
                    var dependencies = new HashSet<int>();
                    var rows = new List<Iw3Ps3SourceSoundRow>();
                    if (aliases is not null && rawAliases == uint.MaxValue)
                    {
                        for (int index = 0; index < count; index++)
                        {
                            int row = aliases.Region.FileOffset + index * 0x5c;
                            Address address = aliases.Region.Address.Add(index * 0x5c);
                            string? aliasName = ReadString(U32(row));
                            ReadString(U32(row + 4));
                            string? secondary = ReadString(U32(row + 8));
                            string? chain = ReadString(U32(row + 12));
                            bool capture = _sourceSoundNames is { } selected &&
                                (selected.Count == 0 || selected.Contains(name));
                            Pointer? soundFile = SoundFilePrefix(U32(row + 0x10));
                            Add(dependencies, soundFile);
                            Add(dependencies, SoundCurvePrefix(U32(row + 0x48),
                                address.Add(0x48)));
                            Pointer? speaker = SpeakerMapPrefix(U32(row + 0x58));
                            if (capture)
                            {
                                if (aliasName != name)
                                    throw Invalid($"unsupported source sound row for '{name}'");
                                int file = soundFile?.Region.FileOffset ?? -1;
                                byte kind = file < 0 ? (byte)0 : _data[file];
                                string? loadedName = null;
                                byte[]? loaded = null;
                                uint package = 0;
                                int packageOffset = 0, packageLength = 0;
                                if (file >= 0 && _data[file + 1] == 1 && kind == 1)
                                {
                                    if (soundFile?.Value is SourceLoadedSound sound)
                                    {
                                        loadedName = sound.Name;
                                        loaded = sound.Payload is { } payload
                                            ? Bytes(payload).ToArray() : null;
                                    }
                                }
                                else if (file >= 0 && _data[file + 1] == 1 && kind == 2)
                                {
                                    package = U32(file + 4);
                                    packageOffset = checked((int)U32(file + 8));
                                    packageLength = checked((int)U32(file + 12));
                                }
                                int speakerOffset = speaker?.Region.FileOffset ?? -1;
                                rows.Add(new Iw3Ps3SourceSoundRow(secondary, chain,
                                    checked((int)U32(row + 0x14)),
                                    F32(row + 0x18), F32(row + 0x1c),
                                    F32(row + 0x20), F32(row + 0x24),
                                    F32(row + 0x28), F32(row + 0x2c),
                                    checked((int)U32(row + 0x30)),
                                    F32(row + 0x34), F32(row + 0x38),
                                    F32(row + 0x3c), F32(row + 0x40),
                                    checked((int)U32(row + 0x44)),
                                    F32(row + 0x4c), F32(row + 0x50),
                                    F32(row + 0x54), speakerOffset < 0 ? null :
                                        _data.AsSpan(speakerOffset, 0x198).ToArray(),
                                    speaker?.Value as string, loadedName, loaded,
                                    package, packageOffset, packageLength));
                            }
                        }
                    }
                    else if (count != 0)
                        throw Unsupported("sound alias list has no inline alias rows");
                    if (_sourceSoundNames is { } wanted &&
                        (wanted.Count == 0 || wanted.Contains(name)) &&
                        !SourceSounds.TryAdd(name, new Iw3Ps3SourceSound(name, rows)))
                        throw Invalid($"duplicate source sound '{name}'");
                    return Named("sound", name, root, dependencies);
                }
                finally { Pop(); }
            });

        // 0xcc068 loads the 0x48-byte curve and its name XString. Some stock
        // curves point at an empty source string; a source-only traversal can
        // retain that pointer without inventing an asset identity.
        private Pointer? SoundCurvePrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "soundcurve", 0x48, root =>
            {
                Push(4);
                try
                {
                    string? name = ReadString(U32(root.FileOffset));
                    return string.IsNullOrEmpty(name)
                        ? new Pointer("soundcurve", root, [])
                        : Named("soundcurve", name, root);
                }
                finally { Pop(); }
            });

        // 0xcc8c0: SoundFile is 0x10 bytes. Tag 1 owns a LoadedSound asset;
        // other tags use an embedded streamed-file union. No codec is read.
        private Pointer? SoundFilePrefix(uint raw)
        {
            Pointer? file = ReadArray(raw, 4, 0x10, "sound-file");
            if (file is null || raw != uint.MaxValue) return file;
            int start = file.Region.FileOffset;
            var dependencies = new HashSet<int>();
            SourceLoadedSound? payload = null;
            if (_data[start] == 1)
            {
                Pointer? loaded = LoadedSoundPrefix(U32(start + 4),
                    file.Region.Address.Add(4));
                Add(dependencies, loaded);
                payload = loaded?.Value as SourceLoadedSound;
            }
            else if (U32(start + 4) == 0)
            {
                ReadString(U32(start + 8));
                ReadString(U32(start + 0x0c));
            }
            return Remember(file with { Dependencies = dependencies.Order().ToArray(), Value = payload });
        }

        // 0xcc3d8/0xbe0b0: root 0x1c, name XString, 0x18-byte format header
        // at +4, and a length-prefixed payload aligned to 0x80 in block 4.
        private Pointer? LoadedSoundPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "loadedsound", 0x1c, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    string? name = ReadString(U32(start));
                    Pointer? payload = PresenceArray(U32(start + 0x18), 0x80,
                        Count(U32(start + 4)), "loaded-sound-data");
                    Pointer result = Named("loadedsound", name, root,
                        value: name is not { Length: > 0 } ? null : new SourceLoadedSound(
                            name[0] == ',' ? name[1..] : name,
                            payload?.Region));
                    if (name is { Length: > 0 } && name[0] != ',' &&
                        payload is not null && _sourceLoadedNames?.Contains(name) == true)
                        SourceLoadedSounds.TryAdd(name, Bytes(payload.Region).ToArray());
                    return result;
                }
                finally { Pop(); }
            });

        // 0xcc908: SpeakerMap is inline 0x198 bytes; its sole child is the
        // name XString at +4.
        private Pointer? SpeakerMapPrefix(uint raw)
        {
            Pointer? map = ReadArray(raw, 4, 0x198, "speaker-map");
            if (map is not null && raw == uint.MaxValue)
                map = Remember(map with { Value = ReadString(U32(map.Region.FileOffset + 4)) });
            return map;
        }

        private float F32(int offset) => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(offset, 4));
    }
}
