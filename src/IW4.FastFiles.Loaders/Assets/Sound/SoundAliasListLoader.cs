using IW4.FastFiles.Loaders.Database;
using System.Buffers.Binary;
using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Sound;

public sealed class SoundAliasListLoader
{
    private static readonly SndCurveLoader SndCurveLoader = new();
    private static readonly LoadedSoundLoader LoadedSoundLoader = new();

    public SoundAliasListAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true)
            ?? throw new InvalidDataException("Top-level Sound pointer resolved to null.");
    }

    public SoundAliasListAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false);
    }

    private static SoundAliasListAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level Sound pointer is null.");

            return null;
        }

        if (pointer.Type == PointerType.Offset)
        {
            XPointerNullability nullability = requireAsset
                ? XPointerNullability.Required
                : XPointerNullability.Nullable;
            context.PointerReader.ValidateOffsetPointerRange<SoundAliasListAsset>(
                pointer,
                SoundAliasListAsset.SerializedSize,
                nullability,
                "Sound");
            SoundAliasListAsset? canonical = context.ResolveCanonicalAsset<SoundAliasListAsset>(
                pointer,
                XAssetType.Sound);
            if (canonical is null)
            {
                if (!requireAsset)
                    return null;

                throw new InvalidDataException(
                    $"Top-level Sound pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical Sound asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Sound pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            SoundAliasListAsset sound = ReadSoundAliasList(cursor, rootAddress, context);
            SoundAliasListAsset canonical = context.DB_AddXAsset(
                XAssetType.Sound,
                sound.AliasName,
                sound,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static SoundAliasListAsset ReadSoundAliasList(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, SoundAliasListAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"Sound pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XString aliasNamePointer = ReadXStringPointer(rootCursor, context);
        XPointer<SndAlias[]> aliasesPointer = ReadPointer<SndAlias[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int count = rootCursor.ReadInt32();

        if (rootCursor.Offset != SoundAliasListAsset.SerializedSize)
            throw new InvalidDataException($"snd_alias_list_t consumed 0x{rootCursor.Offset:X} bytes instead of 0x{SoundAliasListAsset.SerializedSize:X}.");


        string? aliasName;
        IReadOnlyList<SndAlias> aliases;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            aliasName = LoadSoundXString(cursor, aliasNamePointer, context);
            aliases = ReadAliasArray(cursor, aliasesPointer.Untyped, count, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new SoundAliasListAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            AliasNamePointer = aliasNamePointer,
            AliasName = aliasName,
            AliasesPointer = aliasesPointer,
            Count = count,
            Aliases = aliases
        };
    }

    private static IReadOnlyList<SndAlias> ReadAliasArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative snd_alias_t count {count}.");

        int byteCount = checked(count * SndAlias.SerializedSize);
        if (pointer.Type == PointerType.Null || count == 0)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<SndAlias[]>(pointer, byteCount, "snd_alias_t[]");
            return [];
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            return [];

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress aliasesAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] aliasBytes = context.Blocks.Load(cursor, byteCount);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(aliasesAddress));

        var aliasCursor = new FastFileCursor(aliasBytes, aliasesAddress);
        var roots = new SndAliasRoot[count];
        for (int i = 0; i < roots.Length; i++)
            roots[i] = ReadAliasRoot(aliasCursor, context);

        var aliases = new SndAlias[count];
        for (int i = 0; i < aliases.Length; i++)
            aliases[i] = ReadAliasChildren(cursor, roots[i], context);


        return aliases;
    }

    private static SndAliasRoot ReadAliasRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int offset = cursor.AddressAt(cursor.Offset)?.Offset ?? cursor.Offset;
        int start = cursor.Offset;
        var root = new SndAliasRoot(
            offset,
            ReadXStringPointer(cursor, context),
            ReadXStringPointer(cursor, context),
            ReadXStringPointer(cursor, context),
            ReadXStringPointer(cursor, context),
            ReadXStringPointer(cursor, context),
            ReadPointerCellNoValidation<SoundFile[]>(cursor, context, XPointerResolutionMode.AliasCell),
            cursor.ReadInt32(),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            cursor.ReadInt32(),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            cursor.ReadInt32(),
            ReadPointerCellNoValidation<SndCurve>(cursor, context, XPointerResolutionMode.AliasCell),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadPointerCellNoValidation<SpeakerMap>(cursor, context, XPointerResolutionMode.Direct));

        if (cursor.Offset - start != SndAlias.SerializedSize)
            throw new InvalidDataException($"snd_alias_t consumed 0x{cursor.Offset - start:X} bytes instead of 0x{SndAlias.SerializedSize:X}.");

        return root;
    }

    private static SndAlias ReadAliasChildren(
        FastFileCursor cursor,
        SndAliasRoot root,
        DbLoadExecutionContext context)
    {
        string? aliasName = LoadSoundXString(cursor, root.AliasNamePointer, context);
        string? subtitle = LoadSoundXString(cursor, root.SubtitlePointer, context);
        string? secondaryAliasName = LoadSoundXString(cursor, root.SecondaryAliasNamePointer, context);
        string? chainAliasName = LoadSoundXString(cursor, root.ChainAliasNamePointer, context);
        string? mixerGroup = LoadSoundXString(cursor, root.MixerGroupPointer, context);

        int soundFileCount = context.SoundFileCount;
        IReadOnlyList<SoundFile> soundFiles;
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            soundFiles = ReadSoundFileArray(
                cursor,
                root.SoundFilesPointer.Untyped,
                soundFileCount,
                context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        SndCurve? volumeFalloffCurve = ReadSndCurvePointer(
            cursor,
            root.VolumeFalloffCurvePointer.Untyped,
            context);
        SpeakerMap? speakerMap = ReadSpeakerMapPointer(cursor, root.SpeakerMapPointer.Untyped, context);

        return new SndAlias
        {
            Offset = root.Offset,
            AliasNamePointer = root.AliasNamePointer,
            AliasName = aliasName,
            SubtitlePointer = root.SubtitlePointer,
            Subtitle = subtitle,
            SecondaryAliasNamePointer = root.SecondaryAliasNamePointer,
            SecondaryAliasName = secondaryAliasName,
            ChainAliasNamePointer = root.ChainAliasNamePointer,
            ChainAliasName = chainAliasName,
            MixerGroupPointer = root.MixerGroupPointer,
            MixerGroup = mixerGroup,
            SoundFilesPointer = root.SoundFilesPointer,
            SoundFileCount = soundFileCount,
            SoundFiles = soundFiles,
            Sequence = root.Sequence,
            VolumeMin = root.VolumeMin,
            VolumeMax = root.VolumeMax,
            PitchMin = root.PitchMin,
            PitchMax = root.PitchMax,
            DistanceMin = root.DistanceMin,
            DistanceMax = root.DistanceMax,
            VelocityMin = root.VelocityMin,
            Flags = root.Flags,
            SlavePercentage = root.SlavePercentage,
            Probability = root.Probability,
            LfePercentage = root.LfePercentage,
            CenterPercentage = root.CenterPercentage,
            StartDelay = root.StartDelay,
            VolumeFalloffCurvePointer = root.VolumeFalloffCurvePointer,
            VolumeFalloffCurve = volumeFalloffCurve,
            EnvelopMin = root.EnvelopMin,
            EnvelopMax = root.EnvelopMax,
            EnvelopPercentage = root.EnvelopPercentage,
            SpeakerMapPointer = root.SpeakerMapPointer,
            SpeakerMap = speakerMap
        };
    }

    private static IReadOnlyList<SoundFile> ReadSoundFileArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int soundFileCount,
        DbLoadExecutionContext context)
    {
        if (soundFileCount < 0)
            throw new InvalidDataException($"Invalid negative SoundFile count {soundFileCount}.");

        _ = checked(soundFileCount * SoundFile.SerializedSize);
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            if (pointer.ResolutionMode != XPointerResolutionMode.AliasCell ||
                pointer.PackedAddress is not { } packedAddress)
            {
                throw new InvalidDataException(
                    $"SoundFile[] pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "is not a packed alias-cell pointer.");
            }

            string view = SoundFileArrayView(soundFileCount);
            if (context.TryGetMaterializedView<SoundFile[]>(
                    packedAddress,
                    view,
                    out SoundFile[]? existing) &&
                existing is not null)
            {
                return existing;
            }

            // Stock packed SoundFile pointers target a previous persistent
            // snd_alias_t +0x14 owner cell, rather than the TEMP table body.
            // Only a cell registered while that earlier table was decoded is
            // accepted here; the raw address alone never establishes sharing.
            context.Blocks.ValidateMaterializedRange(
                packedAddress,
                sizeof(int),
                "SoundFile[] owner cell",
                pointer.Raw);
            throw new InvalidDataException(
                $"Packed SoundFile[] target {packedAddress} has no earlier " +
                "materialized semantic owner.");
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"SoundFile[] pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                $"has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress soundFilesAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        SoundFile[] soundFiles = ReadInlineSoundFileArray(
            cursor,
            soundFileCount,
            context,
            soundFilesAddress,
            insertCell);
        return RegisterSoundFileArray(
            soundFiles,
            pointer.CellAddress,
            insertCell,
            context);
    }

    private static SoundFile[] ReadInlineSoundFileArray(
        FastFileCursor cursor,
        int soundFileCount,
        DbLoadExecutionContext context,
        XBlockAddress? soundFilesAddressOverride = null,
        XBlockAddress? insertCell = null)
    {
        int byteCount = checked(soundFileCount * SoundFile.SerializedSize);
        XBlockAddress soundFilesAddress = soundFilesAddressOverride ?? context.Blocks.CurrentAddress;
        byte[] soundFileBytes = context.Blocks.Load(cursor, byteCount);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(soundFilesAddress));

        var soundFileCursor = new FastFileCursor(soundFileBytes, soundFilesAddress);
        var roots = new SoundFileRoot[soundFileCount];
        for (int i = 0; i < roots.Length; i++)
            roots[i] = ReadSoundFileRoot(soundFileCursor, context);

        var soundFiles = new SoundFile[soundFileCount];
        for (int i = 0; i < soundFiles.Length; i++)
            soundFiles[i] = ReadSoundFileChildren(cursor, roots[i], context);

        return soundFiles;
    }

    private static SoundFile[] RegisterSoundFileArray(
        SoundFile[] soundFiles,
        XBlockAddress? ownerCell,
        XBlockAddress? insertCell,
        DbLoadExecutionContext context)
    {
        string view = SoundFileArrayView(soundFiles.Length);
        XBlockAddress persistentOwner = ownerCell
            ?? throw new InvalidDataException(
                "A present SoundFile[] pointer has no serialized owner cell.");
        if (persistentOwner.BlockType == XFileBlockType.TEMP)
        {
            throw new InvalidDataException(
                "A SoundFile[] semantic owner cell cannot use rewound TEMP storage.");
        }

        SoundFile[] registered = context.RegisterMaterializedView(
            persistentOwner,
            view,
            soundFiles,
            "SoundFile[] owner cell");

        if (insertCell is { } insertedOwner)
        {
            context.RegisterMaterializedView(
                insertedOwner,
                view,
                registered,
                "SoundFile[] insert cell");
        }

        return registered;
    }

    private static string SoundFileArrayView(int count) =>
        $"SoundFile[{count}]";

    private static SoundFileRoot ReadSoundFileRoot(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        int offset = cursor.AddressAt(cursor.Offset)?.Offset ?? cursor.Offset;
        int start = cursor.Offset;
        var type = (SndAliasType)cursor.ReadByte();
        byte exists = cursor.ReadByte();
        ushort padding = cursor.ReadUInt16();
        int unionCellOffset = cursor.Offset;
        int unionRaw0;
        int unionRaw1;
        int unionRaw2;
        byte[] unionBytes = new byte[12];
        if (type == SndAliasType.Loaded)
        {
            XPointer<LoadedSound> loadedSoundPointer = context.PointerReader.ReadDeferredPointer<LoadedSound>(cursor, XPointerResolutionMode.AliasCell);
            unionRaw0 = loadedSoundPointer.Raw;
            byte[] tail = cursor.ReadBytes(8);
            unionRaw1 = BinaryPrimitives.ReadInt32BigEndian(tail.AsSpan(0, sizeof(int)));
            unionRaw2 = BinaryPrimitives.ReadInt32BigEndian(tail.AsSpan(sizeof(int), sizeof(int)));
            BinaryPrimitives.WriteInt32BigEndian(unionBytes.AsSpan(0, sizeof(int)), unionRaw0);
            tail.CopyTo(unionBytes, sizeof(int));
        }
        else
        {
            unionRaw0 = cursor.ReadInt32();
            // Native dispatch treats every non-Loaded tag as the streamed
            // union. FileIndex zero selects its directory/filename XStrings.
            if (unionRaw0 == 0)
            {
                XPointer<string> directoryPointer = context.PointerReader.ReadDeferredPointer<string>(cursor, XPointerResolutionMode.Direct);
                XPointer<string> filenamePointer = context.PointerReader.ReadDeferredPointer<string>(cursor, XPointerResolutionMode.Direct);
                unionRaw1 = directoryPointer.Raw;
                unionRaw2 = filenamePointer.Raw;
            }
            else
            {
                unionRaw1 = cursor.ReadInt32();
                unionRaw2 = cursor.ReadInt32();
            }

            BinaryPrimitives.WriteInt32BigEndian(unionBytes.AsSpan(0, sizeof(int)), unionRaw0);
            BinaryPrimitives.WriteInt32BigEndian(unionBytes.AsSpan(sizeof(int), sizeof(int)), unionRaw1);
            BinaryPrimitives.WriteInt32BigEndian(unionBytes.AsSpan(sizeof(int) * 2, sizeof(int)), unionRaw2);
        }

        if (cursor.Offset - start != SoundFile.SerializedSize)
            throw new InvalidDataException($"SoundFile consumed 0x{cursor.Offset - start:X} bytes instead of 0x{SoundFile.SerializedSize:X}.");

        return new SoundFileRoot(
            offset,
            type,
            exists,
            padding,
            unionBytes,
            unionRaw0,
            unionRaw1,
            unionRaw2,
            cursor.AddressAt(unionCellOffset) ?? throw new InvalidDataException("SoundFile union cell has no runtime destination address."),
            cursor.AddressAt(unionCellOffset + sizeof(int)) ?? throw new InvalidDataException("StreamedSound directory cell has no runtime destination address."),
            cursor.AddressAt(unionCellOffset + (sizeof(int) * 2)) ?? throw new InvalidDataException("StreamedSound filename cell has no runtime destination address."));
    }

    private static SoundFile ReadSoundFileChildren(
        FastFileCursor cursor,
        SoundFileRoot root,
        DbLoadExecutionContext context)
    {
        SoundFilePayload? payload = null;

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            if (root.Type == SndAliasType.Loaded)
            {
                XPointer<LoadedSound> loadedSoundPointer = context.PointerReader.FromRaw<LoadedSound>(
                    root.UnionRaw0,
                    XPointerResolutionMode.AliasCell,
                    root.UnionCellAddress);
                LoadedSound? loadedSound = LoadedSoundLoader.LoadFromPointer(
                    cursor,
                    loadedSoundPointer.Untyped,
                    context);
                payload = new LoadedSoundFile
                {
                    LoadedSoundPointer = loadedSoundPointer,
                    LoadedSound = loadedSound
                };
            }
            else
            {
                payload = ReadStreamedSound(cursor, root, context);
            }
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new SoundFile
        {
            Offset = root.Offset,
            Type = root.Type,
            Exists = root.Exists,
            Padding = root.Padding,
            Payload = payload
        };
    }

    private static StreamedSound ReadStreamedSound(
        FastFileCursor cursor,
        SoundFileRoot root,
        DbLoadExecutionContext context)
    {
        uint fileIndex = unchecked((uint)root.UnionRaw0);
        StreamedSoundSource source;

        if (fileIndex == 0)
        {
            XString directoryPointer = new(
                root.UnionRaw1,
                XPointerResolutionMode.Direct,
                root.StreamedDirectoryCellAddress);
            XString filenamePointer = new(
                root.UnionRaw2,
                XPointerResolutionMode.Direct,
                root.StreamedFilenameCellAddress);
            string? directory = LoadSoundXString(cursor, directoryPointer, context);
            string? filename = LoadSoundXString(cursor, filenamePointer, context);
            source = new ExternalStreamedSoundSource
            {
                DirectoryPointer = directoryPointer,
                Directory = directory,
                FilenamePointer = filenamePointer,
                Filename = filename
            };
        }
        else
        {
            source = new StreamedSoundFileSource
            {
                StreamFileOffset = root.UnionRaw1,
                StreamFileLength = root.UnionRaw2
            };
        }

        return new StreamedSound
        {
            FileIndex = fileIndex,
            Source = source
        };
    }

    private static SndCurve? ReadSndCurvePointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return SndCurveLoader.LoadFromPointer(
            cursor,
            pointer,
            context);
    }

    private static SpeakerMap? ReadSpeakerMapPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            if (pointer.PackedAddress == context.Blocks.CurrentAddress)
                return ReadSpeakerMap(cursor, context.Blocks.CurrentAddress, context);

            context.PointerReader.ValidateOffsetPointerRange<SpeakerMap>(pointer, SpeakerMap.SerializedSize, "SpeakerMap");
            return context.ResolveMaterializedDirect<SpeakerMap>(
                pointer,
                "SpeakerMap");
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            return null;

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress speakerMapAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        SpeakerMap speakerMap = ReadSpeakerMap(cursor, speakerMapAddress, context);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(speakerMapAddress));

        return speakerMap;
    }

    private static SpeakerMap ReadSpeakerMap(
        FastFileCursor cursor,
        XBlockAddress speakerMapAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, SpeakerMap.SerializedSize);
        var rootCursor = new FastFileCursor(rootBytes, speakerMapAddress);

        byte isDefault = rootCursor.ReadByte();
        byte[] padding = rootCursor.ReadBytes(3);
        XString namePointer = ReadXStringPointer(rootCursor, context);
        IReadOnlyList<SpeakerMapChannel> channels = ReadSpeakerMapChannels(rootCursor);

        if (rootCursor.Offset != SpeakerMap.SerializedSize)
            throw new InvalidDataException($"SpeakerMap consumed 0x{rootCursor.Offset:X} bytes instead of 0x{SpeakerMap.SerializedSize:X}.");

        string? name = LoadSoundXString(cursor, namePointer, context);


        SpeakerMap speakerMap = new()
        {
            Offset = speakerMapAddress.Offset,
            IsDefault = isDefault,
            Padding = padding,
            NamePointer = namePointer,
            Name = name,
            Channels = channels
        };

        return context.RegisterMaterialized(
            speakerMapAddress,
            speakerMap,
            "SpeakerMap");
    }

    private static IReadOnlyList<SpeakerMapChannel> ReadSpeakerMapChannels(FastFileCursor cursor)
    {
        var channels = new SpeakerMapChannel[2];
        for (int i = 0; i < channels.Length; i++)
        {
            var outputs = new XAudioChannelMap[2];
            for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++)
            {
                int entryCount = cursor.ReadInt32();
                var speakers = new SpeakerLevels[6];
                for (int speakerIndex = 0; speakerIndex < speakers.Length; speakerIndex++)
                {
                    speakers[speakerIndex] = new SpeakerLevels
                    {
                        Speaker = cursor.ReadInt32(),
                        NumLevels = cursor.ReadInt32(),
                        Level0 = ReadSingle(cursor),
                        Level1 = ReadSingle(cursor)
                    };
                }

                outputs[outputIndex] = new XAudioChannelMap
                {
                    EntryCount = entryCount,
                    Speakers = speakers
                };
            }

            channels[i] = new SpeakerMapChannel
            {
                Outputs = outputs
            };
        }

        return channels;
    }

    private static XString ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadDeferredPointer<string>(cursor, XPointerResolutionMode.Direct);
    }

    private static string? LoadSoundXString(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        XPointerReference untyped = pointer.Untyped;
        if (untyped.Type == PointerType.Null)
            return null;

        if (untyped.Type == PointerType.Offset)
        {
            if (untyped.PackedAddress == context.Blocks.CurrentAddress)
                return context.PointerReader.LoadXStringPayload(cursor);

            return context.PointerReader.LoadXString(cursor, pointer);
        }

        if (untyped.Type is not (PointerType.Inline or PointerType.Insert))
            return null;

        XBlockAddress? insertCell = untyped.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(untyped, alignment: 0);
        string value = context.PointerReader.LoadXStringPayload(cursor);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(targetAddress));

        return value;
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        SoundAliasListAsset canonical,
        DbLoadExecutionContext context)
    {
        if (pointer.CellAddress is not { } pointerCellAddress)
            return;

        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical Sound has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode resolutionMode)
    {
        return context.PointerReader.ReadPointer<T>(cursor, resolutionMode);
    }

    private static XPointer<T> ReadPointerCellNoValidation<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode resolutionMode) => context.PointerReader.ReadDeferredPointer<T>(cursor, resolutionMode);

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

}
