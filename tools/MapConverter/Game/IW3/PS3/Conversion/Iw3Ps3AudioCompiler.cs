using IW4.Game.Assets;
using IW4.Game.Assets.Sound;
using MapConverter.Game.IW3.Audio;
using MapConverter.Game.IW3.PS3.Extraction;

namespace MapConverter.Game.IW3.PS3.Conversion;

internal static class Iw3Ps3AudioCompiler
{
    internal static (IReadOnlyList<BaseAsset> Assets, byte[] SoundPackage) Capture(
        string sourceDirectory, string bootstrapDirectory,
        string ownedPrefix, IReadOnlySet<string> ambientAliases,
        IReadOnlySet<string> requiredAliases, IReadOnlySet<string> sharedAliases,
        IReadOnlyDictionary<string, Iw3SourceSound> mapSounds, CancellationToken token)
    {
        string[] sourceFiles =
        [Path.Combine(sourceDirectory, "common_mp.ff"), Path.Combine(sourceDirectory, "code_post_gfx_mp.ff"),
            Path.Combine(sourceDirectory, "ui_mp.ff")];
        var mapOwnedAliases = requiredAliases.Concat(ambientAliases).ToHashSet(StringComparer.Ordinal);
        var wanted = mapOwnedAliases.Concat(sharedAliases).ToHashSet(StringComparer.Ordinal);
        var captured = new Dictionary<string, Iw3SourceSound>(StringComparer.Ordinal);
        while (true)
        {
            var missing = wanted.Except(captured.Keys).ToHashSet(StringComparer.Ordinal);
            if (missing.Count == 0) break;
            int before = captured.Count;
            foreach (string name in missing.ToArray())
                if (mapSounds.TryGetValue(name, out Iw3SourceSound? sound) && Usable(sound))
                    captured.Add(name, sound);
            missing.ExceptWith(captured.Keys);
            foreach (string source in sourceFiles.Distinct(StringComparer.Ordinal))
            {
                if (missing.Count == 0) break;
                token.ThrowIfCancellationRequested();
                if (!File.Exists(source)) continue;
                IReadOnlyDictionary<string, Iw3SourceSound> sourceSounds;
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
            foreach (Iw3SourceSound sound in captured.Values)
                foreach (Iw3SourceSoundRow row in sound.Rows)
                {
                    foreach (string? reference in new[] { row.SecondaryAlias, row.ChainAlias })
                        if (!string.IsNullOrWhiteSpace(reference))
                        {
                            wanted.Add(reference);
                            mapOwnedAliases.Add(reference);
                        }
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

        return Iw3AudioEmitter.Emit(bootstrapDirectory, ownedPrefix, ambientAliases, mapOwnedAliases,
            sharedAliases, captured, source => source.LoadedPayload ??
                (source.LoadedSoundName is { } loadedName
                    ? loadedPayloads[loadedName]
                    : ReadPackage(sourceDirectory, source)), token);

        static bool Usable(Iw3SourceSound sound) => sound.Rows.Count != 0 &&
            sound.Rows.All(row => row.LoadedPayload is { Length: > 0 } ||
                row.LoadedSoundName is not null ||
                row.PackageIndex != 0 && row.PackageLength > 0);
    }

    private static byte[] ReadPackage(string sourceDirectory, Iw3SourceSoundRow source)
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
}
