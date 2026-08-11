using IW4.Assets.Assets.Image;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen GfxImage wire plan. Reference definitions synthesize the canonical
/// zeroed body. Owned definitions preserve null payloads, emit inline PHYSICAL
/// bytes, reserve source-free RUNTIME pixels, or retain read-only imagefile
/// references for the package envelope.
/// </summary>
internal sealed class GfxImageLinkPlan : AssetLinkPlan
{
    private GfxImageLinkPlan(
        AssetKey key,
        string originalSerializedName,
        byte[] rootBytes,
        LinkStorageSymbol nameStorage,
        LinkStorageTarget? payloadStorage,
        IReadOnlyList<ImageFileStreamLanguageReferences> streamReferences,
        bool requireReferencePlaceholder)
        : base(
            key,
            originalSerializedName,
            nameStorage,
            requireReferencePlaceholder)
    {
        StreamReferences = streamReferences ??
            throw new ArgumentNullException(nameof(streamReferences));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            rootBytes,
            alignment: 4,
            root => payloadStorage is null
                ? [NameOperation(root, 0x4c)]
                : [
                    NameOperation(root, 0x4c),
                    new PresenceStorageLinkOperation(
                        new LinkStorageCell(root, 0x28),
                        payloadStorage.Value.View,
                        "GfxImage.Pixels")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    internal IReadOnlyList<ImageFileStreamLanguageReferences> StreamReferences { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        GfxImageAsset definition,
        IReadOnlyList<ImageFileStreamLanguageReferences> imageStreamReferences,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(imageStreamReferences);
        ArgumentNullException.ThrowIfNull(freeze);
        if (originalSerializedName.StartsWith(','))
        {
            if (imageStreamReferences.Count != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed GfxImage reference cannot carry imagefile references.");
            }
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Image,
                originalSerializedName,
                freeze);
        }

        return CreateOwned(
            key,
            originalSerializedName,
            definition,
            imageStreamReferences,
            freeze);
    }

    private static GfxImageLinkPlan CreateOwned(
        AssetKey key,
        string originalSerializedName,
        GfxImageAsset definition,
        IReadOnlyList<ImageFileStreamLanguageReferences> imageStreamReferences,
        LinkAssetFreezeScope freeze)
    {
        GfxImageStreamData[] streamData = FreezeStreamData(definition.StreamData);
        int[] streamPartByteCounts =
            GfxImageStreamData.ValidateProfileAndComputePartByteCounts(streamData);
        bool isStreamed = streamPartByteCounts.Any(byteCount => byteCount != 0);
        IReadOnlyList<ImageFileStreamLanguageReferences> frozenStreamReferences =
            FreezeStreamReferences(
                streamPartByteCounts,
                imageStreamReferences,
                isStreamed);

        // Pixel bytes are the authored presence signal. PayloadPointer is
        // loader state and is deliberately not retained by the link request.
        byte[] payload = definition.PayloadBytes?.ToArray()
            ?? throw new InvalidDataException("GfxImage payload bytes cannot be null.");
        if (isStreamed && payload.Length != 0)
        {
            throw new InvalidDataException(
                "A streamed GfxImage cannot also own an inline pixel payload.");
        }
        if (isStreamed && definition.PayloadPointer.Raw != 0)
        {
            throw new InvalidDataException(
                "A streamed GfxImage cannot retain an inline pixel-presence cell.");
        }
        if (!isStreamed && payload.Length == 0 &&
            definition.PayloadPointer.Raw != 0)
        {
            throw new NotSupportedException(
                "Canonical linking cannot preserve a present zero-byte GfxImage payload without explicit semantic presence.");
        }

        int expectedByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            definition.Format,
            definition.LevelCount,
            definition.MultiFaceControl,
            definition.TextureFlags,
            definition.Width,
            definition.Height,
            definition.Depth);
        LinkStorageTarget? payloadStorage = null;
        if (payload.Length != 0 && expectedByteCount <= 0)
        {
            throw new NotSupportedException(
                "Canonical linking requires an owned GfxImage layout with a positive, proven payload size.");
        }
        if (payload.Length != 0 && payload.Length != expectedByteCount)
        {
            throw new InvalidDataException(
                $"GfxImage inline payload is {payload.Length} byte(s); " +
                $"its serialized layout requires {expectedByteCount} byte(s).");
        }
        if (payload.Length != 0)
        {
            if (definition.TextureSemantic == 0x0b)
            {
                if (payload.Any(value => value != 0))
                {
                    throw new InvalidDataException(
                        "A source-free RUNTIME GfxImage payload can retain only zero-filled semantic bytes.");
                }
                LinkStorageSymbol runtimeStorage = LinkStorageSymbol.SourceFree(
                    XFileBlockType.RUNTIME,
                    payload.Length,
                    alignment: 128,
                    LinkMaterializationKind.RuntimeZeroFill);
                payloadStorage = new LinkStorageTarget(
                    LinkStorageView.Whole(runtimeStorage),
                    CanMaterializeRoot: true);
            }
            else
            {
                payloadStorage = freeze.FreezeStorage(
                    definition.PayloadPointer,
                    payload,
                    XFileBlockType.PHYSICAL,
                    alignment: 128,
                    operations: null,
                    "GfxImage.Pixels");
            }
        }

        return new GfxImageLinkPlan(
            key,
            originalSerializedName,
            BuildOwnedRoot(definition, streamData),
            freeze.FreezeProviderName(originalSerializedName, 0x4c, "Asset.Name"),
            payloadStorage,
            frozenStreamReferences,
            requireReferencePlaceholder: false);
    }

    private static IReadOnlyList<ImageFileStreamLanguageReferences>
        FreezeStreamReferences(
        IReadOnlyList<int> byteCounts,
        IReadOnlyList<ImageFileStreamLanguageReferences> source,
        bool isStreamed)
    {
        if (!isStreamed)
        {
            if (source.Count != 0)
            {
                throw new InvalidDataException(
                    "A non-streamed GfxImage cannot carry imagefile references.");
            }

            return Array.Empty<ImageFileStreamLanguageReferences>();
        }
        if (source.Count == 0)
        {
            throw new InvalidDataException(
                "A streamed GfxImage requires four imagefile references for every language.");
        }

        var masks = new HashSet<uint>();
        var copied = new ImageFileStreamLanguageReferences[source.Count];
        for (int languageIndex = 0; languageIndex < source.Count; languageIndex++)
        {
            ImageFileStreamLanguageReferences language = source[languageIndex] ??
                throw new InvalidDataException(
                    "GfxImage imagefile language references cannot contain null.");
            if (!masks.Add(language.LanguageMask))
            {
                throw new InvalidDataException(
                    $"GfxImage has duplicate stream contributions for language " +
                    $"0x{language.LanguageMask:X}.");
            }

            for (int partIndex = 0; partIndex < byteCounts.Count; partIndex++)
            {
                ImageFileStreamReference reference = language.References[partIndex];
                int required = byteCounts[partIndex];
                if (required == 0)
                {
                    if (!reference.IsEmpty)
                    {
                        throw new InvalidDataException(
                            $"GfxImage stream part {partIndex} is semantically empty but " +
                            $"language 0x{language.LanguageMask:X} supplies an imagefile reference.");
                    }
                }
                else if (reference.IsEmpty || reference.ByteLength != required)
                {
                    throw new InvalidDataException(
                        $"GfxImage stream part {partIndex} requires 0x{required:X} bytes " +
                        $"for language 0x{language.LanguageMask:X}.");
                }
            }

            copied[languageIndex] = language;
        }

        return Array.AsReadOnly(copied);
    }

    private static void ValidateReferenceShape(GfxImageAsset definition)
    {
        bool nonzeroStream = definition.StreamData.Any(entry =>
            entry.Width != 0 || entry.Height != 0 || entry.LevelSizeAndOffset != 0);
        if (definition.Format != 0 ||
            definition.LevelCount != 0 ||
            definition.DimensionCount != 0 ||
            definition.MultiFaceControl != 0 ||
            definition.TextureFlags != 0 ||
            definition.Width != 0 ||
            definition.Height != 0 ||
            definition.Depth != 0 ||
            definition.SerializedPixelDataBlock != 0 ||
            definition.Pad0F != 0 ||
            definition.RenderTargetPitch != 0 ||
            definition.SerializedPixelsOffset != 0 ||
            definition.MapType != 0 ||
            definition.TextureSemantic != 0 ||
            definition.Category != 0 ||
            definition.Pad1B != 0 ||
            definition.CardMemory != 0 ||
            definition.BaseWidth != 0 ||
            definition.BaseHeight != 0 ||
            definition.BaseDepth != 0 ||
            definition.BaseLevelCount != 0 ||
            definition.Cached != 0 ||
            definition.PayloadPointer.Type != IW4.FastFiles.Pointers.PointerType.Null ||
            definition.PayloadBytes.Count != 0 ||
            definition.PayloadByteCount != 0 ||
            definition.StreamImageIndex is not null ||
            definition.StreamEntries.Count != 0 ||
            nonzeroStream)
        {
            throw new InvalidDataException(
                "A comma-prefixed GfxImage provider must have a zeroed reference body.");
        }
    }

    private static GfxImageStreamData[] FreezeStreamData(
        IReadOnlyList<GfxImageStreamData> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count == 0)
        {
            return Enumerable.Range(0, GfxImageStreamData.EntryCount)
                .Select(_ => new GfxImageStreamData(0, 0, 0))
                .ToArray();
        }
        if (source.Count != GfxImageStreamData.EntryCount)
        {
            throw new InvalidDataException(
                $"GfxImage requires exactly {GfxImageStreamData.EntryCount} stream records.");
        }

        return source
            .Select(entry => entry ?? throw new InvalidDataException(
                "GfxImage stream records cannot contain null."))
            .ToArray();
    }

    private static byte[] BuildOwnedRoot(
        GfxImageAsset definition,
        IReadOnlyList<GfxImageStreamData> streamData)
    {
        var writer = new LinkTemplateWriter(GfxImageAsset.SerializedSize);
        writer.WriteByte(definition.Format);
        writer.WriteByte(definition.LevelCount);
        writer.WriteByte(definition.DimensionCount);
        writer.WriteByte(definition.MultiFaceControl);
        writer.WriteUInt32(definition.TextureFlags);
        writer.WriteUInt16(definition.Width);
        writer.WriteUInt16(definition.Height);
        writer.WriteUInt16(definition.Depth);
        writer.WriteByte(definition.SerializedPixelDataBlock);
        writer.WriteByte(definition.Pad0F);
        writer.WriteUInt32(definition.RenderTargetPitch);
        writer.WriteUInt32(definition.SerializedPixelsOffset);
        writer.WriteByte(definition.MapType);
        writer.WriteByte(definition.TextureSemantic);
        writer.WriteByte(definition.Category);
        writer.WriteByte(definition.Pad1B);
        writer.WriteUInt32(definition.CardMemory);
        writer.WriteUInt16(definition.BaseWidth);
        writer.WriteUInt16(definition.BaseHeight);
        writer.WriteUInt16(definition.BaseDepth);
        writer.WriteByte(definition.BaseLevelCount);
        writer.WriteByte(definition.Cached);
        writer.Skip(sizeof(int));
        foreach (GfxImageStreamData entry in streamData)
        {
            writer.WriteUInt16(entry.Width);
            writer.WriteUInt16(entry.Height);
            writer.WriteUInt32(entry.LevelSizeAndOffset);
        }
        writer.Skip(sizeof(int));
        return writer.Complete();
    }
}
