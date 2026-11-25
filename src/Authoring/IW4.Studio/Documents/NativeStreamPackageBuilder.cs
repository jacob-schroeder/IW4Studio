using IW4.Game.Assets;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Sound;
using IW4.Game.Database;
using IW4.Game.Database.Streaming;
using IW4.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Packaging;
using IW4.Runtime.Assets.Images;
using IW4.Runtime.Assets.Sound;

namespace IW4.Studio.Documents;

/// <summary>One selected provider and the physical zone that owns its stream packages.</summary>
public sealed record NativeStreamProviderInput(
    AssetKey Key,
    BaseAsset Definition,
    LoadedXZone? PhysicalOwner,
    LinkAssetProviderSource Source);

/// <summary>Rebased provider sources and any named native sidecar bytes they require.</summary>
public sealed record NativeStreamPackageBuildResult(
    IReadOnlyList<LinkAssetProviderSource> Sources,
    byte[]? ImagePackage,
    byte[]? SoundPackage);

/// <summary>Current named package bytes captured for an editing revision.</summary>
public sealed record NativeStreamPackageBase(byte[]? ImagePackage, byte[]? SoundPackage)
{
    public NativeStreamPackageBase Copy() => new(
        ImagePackage?.ToArray(),
        SoundPackage?.ToArray());
}

/// <summary>Repackages streamed provider data into named packages for a destination zone.</summary>
public static class NativeStreamPackageBuilder
{
    private const int ImagePackagePrefixLength = DbHeader.UnsignedPrefixLength;
    private const int SoundAlignment = 2048;

    public static NativeStreamPackageBuildResult Repackage(
        IReadOnlyList<NativeStreamProviderInput> providers,
        uint languageMask,
        byte[]? existingImages = null,
        byte[]? existingSounds = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (!DbLanguageMask.IsSupported(languageMask))
            throw new ArgumentOutOfRangeException(nameof(languageMask));

        var imageRequests = new List<ImageRequest>();
        var soundRequests = new List<SoundRequest>();
        foreach (NativeStreamProviderInput input in providers)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Definition);
            ArgumentNullException.ThrowIfNull(input.Source);
            if (input.Definition is GfxImageAsset image && image.StreamData.Any(data => data.HasStreamingData))
                imageRequests.Add(new ImageRequest(input, image));
            else if (input.Definition is SoundAliasListAsset sound && HasStreamedSounds(sound))
                soundRequests.Add(new SoundRequest(input, sound));
        }

        ImageBuild imageBuild = RepackageImages(imageRequests, languageMask, existingImages);
        SoundBuild soundBuild = RepackageSounds(soundRequests, existingSounds);
        LinkAssetProviderSource[] changed = imageBuild.Sources
            .Concat(soundBuild.Sources)
            .ToArray();
        return new NativeStreamPackageBuildResult(
            Array.AsReadOnly(changed),
            imageBuild.Package,
            soundBuild.Package);
    }

    private static ImageBuild RepackageImages(
        IReadOnlyList<ImageRequest> requests,
        uint languageMask,
        byte[]? existingImages)
    {
        if (requests.Count == 0)
            return new ImageBuild([], null);

        var payloads = new List<ReadOnlyMemory<byte>>();
        var ranges = new List<ImageRange>();
        foreach (ImageRequest request in requests)
        {
            if (request.Input.PhysicalOwner is not { } owner)
                throw new InvalidDataException($"Streamed image '{request.Image.Name}' has no physical package owner.");
            int[] lengths = GfxImageStreamData.ValidateProfileAndComputePartByteCounts(request.Image.StreamData);
            var resolver = owner.ImagePayloadResolver;
            for (int languageIndex = 0; languageIndex < DbLanguageMask.BitCount; languageIndex++)
            {
                uint language = 1u << languageIndex;
                if ((languageMask & language) == 0)
                    continue;
                IReadOnlyList<byte[]> parts;
                string reason;
                if (resolver is IGfxImageLanguagePayloadResolver languageResolver)
                {
                    if (!languageResolver.TryResolveStreamParts(request.Image, language, out parts, out reason))
                        throw new InvalidDataException($"Cannot resolve image '{request.Image.Name}' language 0x{language:X}: {reason}");
                }
                else if (language == owner.Header.SelectedLanguageMask &&
                         resolver.TryResolveStreamParts(request.Image, out parts, out reason))
                {
                    // Legacy resolvers expose only their selected table.
                }
                else
                {
                    throw new InvalidDataException(
                        $"Image '{request.Image.Name}' requires language 0x{language:X}, but its resolver cannot read language-specific streams.");
                }

                if (parts.Count != GfxImageStreamData.EntryCount)
                    throw new InvalidDataException($"Image '{request.Image.Name}' language 0x{language:X} did not resolve four stream parts.");
                int first = payloads.Count;
                for (int part = 0; part < parts.Count; part++)
                {
                    if (parts[part].Length != lengths[part])
                        throw new InvalidDataException(
                            $"Image '{request.Image.Name}' part {part} resolved {parts[part].Length} bytes; expected {lengths[part]}.");
                    payloads.Add(parts[part]);
                }
                ranges.Add(new ImageRange(request, language, first));
            }
        }

        if (payloads.All(payload => payload.Length == 0))
            return new ImageBuild([], null);
        ImageFilePackage packed = new ImageFilePackager().Package(
            DbHeaderImageStreamEntry.NamedFileIndex,
            payloads);
        byte[] packageBytes = AppendImagePackage(existingImages, packed.Bytes.Span);
        int imageOffset = existingImages is null ? 0 : existingImages.Length - ImagePackagePrefixLength;

        var refsByRequest = new Dictionary<ImageRequest, Dictionary<uint, IReadOnlyList<ImageFileStreamReference>>>();
        foreach (ImageRange range in ranges)
        {
            ImageFileStreamReference[] refs = packed.References
                .Skip(range.FirstReference)
                .Take(GfxImageStreamData.EntryCount)
                .Select(reference => RebaseImageReference(reference, imageOffset))
                .ToArray();
            if (!refsByRequest.TryGetValue(range.Request, out Dictionary<uint, IReadOnlyList<ImageFileStreamReference>>? languageRefs))
                refsByRequest.Add(range.Request, languageRefs = new Dictionary<uint, IReadOnlyList<ImageFileStreamReference>>());
            languageRefs[range.Language] = Array.AsReadOnly(refs);
        }

        var updatedSources = new List<LinkAssetProviderSource>();
        foreach ((ImageRequest request, Dictionary<uint, IReadOnlyList<ImageFileStreamReference>> languageRefs) in refsByRequest)
        {
            var merged = request.Input.Source.ImageStreamReferences
                .Where(reference => (languageMask & reference.LanguageMask) == 0)
                .ToDictionary(reference => reference.LanguageMask);
            foreach ((uint language, IReadOnlyList<ImageFileStreamReference> references) in languageRefs)
                merged[language] = new ImageFileStreamLanguageReferences(language, references);
            updatedSources.Add(CloneSource(request.Input.Source, request.Image, merged.Values));
        }

        return new ImageBuild(Array.AsReadOnly(updatedSources.ToArray()), packageBytes);
    }

    private static SoundBuild RepackageSounds(
        IReadOnlyList<SoundRequest> requests,
        byte[]? existingSounds)
    {
        if (requests.Count == 0)
            return new SoundBuild([], null);
        var packager = new SoundFilePackager();
        var definitions = new List<(SoundRequest Request, SoundAliasListAsset Definition)>();
        foreach (SoundRequest request in requests)
        {
            if (request.Input.PhysicalOwner is not { } owner)
                throw new InvalidDataException($"Sound '{request.Sound.AliasName}' has no physical package owner.");
            SndAlias[] aliases = request.Sound.Aliases.Select(alias =>
            {
                SoundFile[] files = alias.SoundFiles.Select(file =>
                {
                    if (file.Streamed is not { Source: StreamedSoundFileSource } streamed)
                        return file;
                    if (!owner.SoundPayloadResolver.TryResolvePayload(streamed, out byte[] payload, out string reason))
                        throw new InvalidDataException($"Cannot resolve streamed sound '{request.Sound.AliasName}/{alias.AliasName}': {reason}");
                    StreamedSound reference = packager.AddPayload(payload);
                    if (existingSounds is not null)
                    {
                        int appendBase = Align(existingSounds.Length, SoundAlignment);
                        reference = new StreamedSound
                        {
                            FileIndex = StreamedSound.NamedFileIndex,
                            Source = new StreamedSoundFileSource
                            {
                                StreamFileOffset = checked(appendBase + reference.StreamFile!.StreamFileOffset),
                                StreamFileLength = reference.StreamFile!.StreamFileLength
                            }
                        };
                    }
                    return new SoundFile
                    {
                        Offset = file.Offset,
                        Type = file.Type,
                        Exists = file.Exists,
                        Padding = file.Padding,
                        Payload = reference
                    };
                }).ToArray();
                return new SndAlias
                {
                    Offset = alias.Offset,
                    AliasNamePointer = alias.AliasNamePointer,
                    AliasName = alias.AliasName,
                    SubtitlePointer = alias.SubtitlePointer,
                    Subtitle = alias.Subtitle,
                    SecondaryAliasNamePointer = alias.SecondaryAliasNamePointer,
                    SecondaryAliasName = alias.SecondaryAliasName,
                    ChainAliasNamePointer = alias.ChainAliasNamePointer,
                    ChainAliasName = alias.ChainAliasName,
                    MixerGroupPointer = alias.MixerGroupPointer,
                    MixerGroup = alias.MixerGroup,
                    SoundFilesPointer = alias.SoundFilesPointer,
                    SoundFileCount = files.Length,
                    SoundFiles = files,
                    Sequence = alias.Sequence,
                    VolumeMin = alias.VolumeMin,
                    VolumeMax = alias.VolumeMax,
                    PitchMin = alias.PitchMin,
                    PitchMax = alias.PitchMax,
                    DistanceMin = alias.DistanceMin,
                    DistanceMax = alias.DistanceMax,
                    VelocityMin = alias.VelocityMin,
                    Flags = alias.Flags,
                    SlavePercentage = alias.SlavePercentage,
                    Probability = alias.Probability,
                    LfePercentage = alias.LfePercentage,
                    CenterPercentage = alias.CenterPercentage,
                    StartDelay = alias.StartDelay,
                    VolumeFalloffCurvePointer = alias.VolumeFalloffCurvePointer,
                    VolumeFalloffCurve = alias.VolumeFalloffCurve,
                    EnvelopMin = alias.EnvelopMin,
                    EnvelopMax = alias.EnvelopMax,
                    EnvelopPercentage = alias.EnvelopPercentage,
                    SpeakerMapPointer = alias.SpeakerMapPointer,
                    SpeakerMap = alias.SpeakerMap
                };
            }).ToArray();
            definitions.Add((request, new SoundAliasListAsset
            {
                AliasNamePointer = request.Sound.AliasNamePointer,
                AliasName = request.Sound.AliasName,
                AliasesPointer = request.Sound.AliasesPointer,
                Count = aliases.Length,
                Aliases = aliases
            }));
        }

        if (packager.PayloadCount == 0)
            return new SoundBuild([], null);
        byte[] fresh = packager.ToArray();
        byte[] output;
        if (existingSounds is null)
        {
            output = fresh;
        }
        else
        {
            int appendBase = Align(existingSounds.Length, SoundAlignment);
            output = new byte[checked(appendBase + fresh.Length)];
            existingSounds.CopyTo(output, 0);
            fresh.CopyTo(output, appendBase);
        }
        LinkAssetProviderSource[] sources = definitions
            .Select(pair => CloneSource(pair.Request.Input.Source, pair.Definition,
                pair.Request.Input.Source.ImageStreamReferences,
                LinkAssetProviderSourceDisposition.AuthoredDetached))
            .ToArray();
        return new SoundBuild(Array.AsReadOnly(sources), output);
    }

    private static byte[] AppendImagePackage(byte[]? existing, ReadOnlySpan<byte> fresh)
    {
        if (existing is null)
            return fresh.ToArray();
        if (existing.Length < ImagePackagePrefixLength)
            throw new InvalidDataException("Existing named image package is shorter than its native prefix.");
        var output = new byte[checked(existing.Length + fresh.Length - ImagePackagePrefixLength)];
        existing.CopyTo(output, 0);
        fresh[ImagePackagePrefixLength..].CopyTo(output.AsSpan(existing.Length));
        return output;
    }

    private static ImageFileStreamReference RebaseImageReference(
        ImageFileStreamReference reference,
        int offset)
    {
        if (reference.IsEmpty)
            return reference;
        DbHeaderImageStreamEntry entry = reference.Entry with
        {
            SourceStart = checked(reference.Entry.SourceStart + (uint)offset),
            SourceEnd = checked(reference.Entry.SourceEnd + (uint)offset)
        };
        return new ImageFileStreamReference(entry, reference.ByteLength);
    }

    private static LinkAssetProviderSource CloneSource(
        LinkAssetProviderSource source,
        BaseAsset definition,
        IEnumerable<ImageFileStreamLanguageReferences> imageReferences,
        LinkAssetProviderSourceDisposition? disposition = null) =>
        new(definition, source.ImportResolver, imageReferences, disposition ?? source.Disposition,
            source.ImportedDefinition, source.SerializedName);

    private static bool HasStreamedSounds(SoundAliasListAsset sound) =>
        sound.Aliases.SelectMany(alias => alias.SoundFiles).Any(file =>
            file.Streamed?.Source is StreamedSoundFileSource);

    private static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);

    private sealed record ImageRequest(NativeStreamProviderInput Input, GfxImageAsset Image);
    private sealed record SoundRequest(NativeStreamProviderInput Input, SoundAliasListAsset Sound);
    private sealed record ImageRange(ImageRequest Request, uint Language, int FirstReference);
    private sealed record ImageBuild(IReadOnlyList<LinkAssetProviderSource> Sources, byte[]? Package);
    private sealed record SoundBuild(IReadOnlyList<LinkAssetProviderSource> Sources, byte[]? Package);
}
