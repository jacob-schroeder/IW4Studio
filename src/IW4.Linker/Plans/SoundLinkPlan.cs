using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen Sound alias graph. SoundFile arrays use their native durable alias
/// cells, SpeakerMaps use direct storage, and nested XAssets remain provider
/// dependencies selected by logical identity.
/// </summary>
internal sealed class SoundLinkPlan : AssetLinkPlan
{
    private SoundLinkPlan(
        AssetKey key,
        string originalSerializedName,
        int aliasCount,
        int? requiredLanguageCount,
        LinkStorageTarget? aliases,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        RequiredLanguageCount = requiredLanguageCount;
        var writer = new LinkTemplateWriter(SoundAliasListAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.WriteInt32(aliasCount);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => aliases is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    DirectOperation(root, 0x04, aliases.Value, "Sound.Aliases")
                ]);
    }

    internal override LinkStorageSymbol Root { get; }

    /// <summary>
    /// Native SoundFile cardinality supplied by the fastfile language
    /// envelope. A Sound with no aliases has no table to constrain.
    /// </summary>
    internal int? RequiredLanguageCount { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        SoundAliasListAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        IReadOnlyList<SndAlias> aliases = definition.Aliases ??
            throw new InvalidDataException("Sound aliases cannot be null.");
        if (definition.Count < 0)
            throw new InvalidDataException("Sound alias count cannot be negative.");
        if (definition.Count != aliases.Count)
        {
            throw new InvalidDataException(
                $"Sound root declares {definition.Count} aliases, but its semantic " +
                $"table contains {aliases.Count}.");
        }

        if (originalSerializedName.StartsWith(','))
        {
            if (definition.Count != 0 || aliases.Count != 0 ||
                definition.AliasesPointer.Raw != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed Sound provider must have a zeroed reference body.");
            }

            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Sound,
                originalSerializedName,
                freeze);
        }

        int? requiredLanguageCount = FreezeLanguageCount(aliases);
        LinkStorageTarget? aliasStorage =
            aliases.Count == 0 && definition.AliasesPointer.Type == PointerType.Null
                ? null
                : FreezeAliasTable(
                    aliases,
                    definition.AliasesPointer.Untyped,
                    freeze);
        return new SoundLinkPlan(
            key,
            originalSerializedName,
            aliases.Count,
            requiredLanguageCount,
            aliasStorage,
            freeze);
    }

    private static int? FreezeLanguageCount(IReadOnlyList<SndAlias> aliases)
    {
        int? required = null;
        for (int index = 0; index < aliases.Count; index++)
        {
            SndAlias alias = aliases[index] ?? throw new InvalidDataException(
                $"Sound.Aliases[{index}] cannot be null.");
            if (alias.SoundFileCount < 0)
            {
                throw new InvalidDataException(
                    $"Sound.Aliases[{index}].SoundFileCount cannot be negative.");
            }
            if (required is { } previous && previous != alias.SoundFileCount)
            {
                throw new InvalidDataException(
                    "Every SndAlias in one Sound provider must use the same " +
                    "fastfile language count.");
            }

            required ??= alias.SoundFileCount;
        }

        return required;
    }

    private static LinkStorageTarget FreezeAliasTable(
        IReadOnlyList<SndAlias> aliases,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        FrozenAlias[] frozen = aliases
            .Select((alias, index) => FrozenAlias.Freeze(
                alias ?? throw new InvalidDataException(
                    $"Sound.Aliases[{index}] cannot be null."),
                index,
                freeze))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * SndAlias.SerializedSize));
        foreach (FrozenAlias alias in frozen)
            writer.WriteBytes(alias.Template);

        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperations(
                        table,
                        checked(addend + index * SndAlias.SerializedSize),
                        operations);
                }
                return operations;
            },
            "Sound.Aliases");
    }

    private static LinkAliasCellSymbol? FreezeSoundFiles(
        SndAlias alias,
        int aliasIndex,
        LinkAssetFreezeScope freeze)
    {
        IReadOnlyList<SoundFile> files = alias.SoundFiles ??
            throw new InvalidDataException(
                $"Sound.Aliases[{aliasIndex}].SoundFiles cannot be null.");
        bool present = files.Count != 0 ||
            alias.SoundFilesPointer.Type != PointerType.Null;
        if (!present)
        {
            if (files.Count != 0)
                throw new InvalidDataException("A null SoundFile table cannot carry rows.");
            return null;
        }
        if (files.Count != alias.SoundFileCount)
        {
            throw new InvalidDataException(
                $"Sound.Aliases[{aliasIndex}] has {files.Count} SoundFile row(s), " +
                $"but the fastfile language count is {alias.SoundFileCount}.");
        }

        FrozenSoundFile[] frozen = files
            .Select((file, fileIndex) => FrozenSoundFile.Freeze(
                file ?? throw new InvalidDataException(
                    $"Sound.Aliases[{aliasIndex}].SoundFiles[{fileIndex}] cannot be null."),
                aliasIndex,
                fileIndex,
                freeze))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * SoundFile.SerializedSize));
        foreach (FrozenSoundFile file in frozen)
            writer.WriteBytes(file.Template);

        string path = $"Sound.Aliases[{aliasIndex}].SoundFiles";
        return freeze.FreezeAliasCellStorage(
            alias.SoundFilesPointer.Untyped,
            writer.Complete(),
            XFileBlockType.TEMP,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperations(
                        table,
                        checked(addend + index * SoundFile.SerializedSize),
                        operations);
                }
                return operations;
            },
            path);
    }

    private static LinkStorageTarget FreezeSpeakerMap(
        SpeakerMap speakerMap,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        byte[] padding = speakerMap.Padding?.ToArray() ??
            throw new InvalidDataException($"{fieldPath}.Padding cannot be null.");
        if (padding.Length != 3)
        {
            throw new InvalidDataException(
                $"{fieldPath}.Padding must contain exactly three bytes.");
        }
        IReadOnlyList<SpeakerMapChannel> channels = speakerMap.Channels ??
            throw new InvalidDataException($"{fieldPath}.Channels cannot be null.");
        if (channels.Count != 2)
        {
            throw new InvalidDataException(
                $"{fieldPath} must contain exactly two channel rows.");
        }

        LinkStorageSymbol? name = FreezeOptionalXString(
            freeze,
            speakerMap.Name,
            speakerMap.NamePointer.Untyped,
            $"{fieldPath}.Name");
        var writer = new LinkTemplateWriter(SpeakerMap.SerializedSize);
        writer.WriteByte(speakerMap.IsDefault);
        writer.WriteBytes(padding);
        writer.Skip(sizeof(int));
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            SpeakerMapChannel channel = channels[channelIndex] ??
                throw new InvalidDataException(
                    $"{fieldPath}.Channels[{channelIndex}] cannot be null.");
            IReadOnlyList<XAudioChannelMap> outputs = channel.Outputs ??
                throw new InvalidDataException(
                    $"{fieldPath}.Channels[{channelIndex}].Outputs cannot be null.");
            if (outputs.Count != 2)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.Channels[{channelIndex}] must contain exactly two outputs.");
            }

            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                XAudioChannelMap output = outputs[outputIndex] ??
                    throw new InvalidDataException(
                        $"{fieldPath}.Channels[{channelIndex}].Outputs[{outputIndex}] cannot be null.");
                IReadOnlyList<SpeakerLevels> speakers = output.Speakers ??
                    throw new InvalidDataException(
                        $"{fieldPath}.Channels[{channelIndex}].Outputs[{outputIndex}].Speakers cannot be null.");
                if (speakers.Count != 6)
                {
                    throw new InvalidDataException(
                        $"{fieldPath}.Channels[{channelIndex}].Outputs[{outputIndex}] " +
                        "must contain exactly six speaker rows.");
                }

                writer.WriteInt32(output.EntryCount);
                for (int speakerIndex = 0; speakerIndex < speakers.Count; speakerIndex++)
                {
                    SpeakerLevels speaker = speakers[speakerIndex] ??
                        throw new InvalidDataException(
                            $"{fieldPath}.Channels[{channelIndex}].Outputs[{outputIndex}]" +
                            $".Speakers[{speakerIndex}] cannot be null.");
                    writer.WriteInt32(speaker.Speaker);
                    writer.WriteInt32(speaker.NumLevels);
                    writer.WriteSingle(speaker.Level0);
                    writer.WriteSingle(speaker.Level1);
                }
            }
        }

        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (storage, addend) => name is null
                ? []
                : [XStringOperation(storage, checked(addend + 0x04), name, $"{fieldPath}.Name")],
            fieldPath);
    }

    private static LinkStorageSymbol? FreezeOptionalXString(
        LinkAssetFreezeScope freeze,
        string? value,
        XPointerReference pointer,
        string fieldPath)
    {
        if (value is null)
        {
            if (pointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains a non-null XString pointer without semantic text.");
            }
            return null;
        }

        return freeze.FreezeRequiredXString(value, pointer, fieldPath);
    }


    private sealed class FrozenAlias
    {
        private FrozenAlias(
            byte[] template,
            LinkStorageSymbol? aliasName,
            LinkStorageSymbol? subtitle,
            LinkStorageSymbol? secondaryAliasName,
            LinkStorageSymbol? chainAliasName,
            LinkStorageSymbol? mixerGroup,
            LinkAliasCellSymbol? soundFiles,
            AssetDependency? volumeFalloffCurve,
            LinkStorageTarget? speakerMap,
            int index)
        {
            Template = template;
            AliasName = aliasName;
            Subtitle = subtitle;
            SecondaryAliasName = secondaryAliasName;
            ChainAliasName = chainAliasName;
            MixerGroup = mixerGroup;
            SoundFiles = soundFiles;
            VolumeFalloffCurve = volumeFalloffCurve;
            SpeakerMap = speakerMap;
            Index = index;
        }

        public byte[] Template { get; }
        private LinkStorageSymbol? AliasName { get; }
        private LinkStorageSymbol? Subtitle { get; }
        private LinkStorageSymbol? SecondaryAliasName { get; }
        private LinkStorageSymbol? ChainAliasName { get; }
        private LinkStorageSymbol? MixerGroup { get; }
        private LinkAliasCellSymbol? SoundFiles { get; }
        private AssetDependency? VolumeFalloffCurve { get; }
        private LinkStorageTarget? SpeakerMap { get; }
        private int Index { get; }

        public static FrozenAlias Freeze(
            SndAlias alias,
            int index,
            LinkAssetFreezeScope freeze)
        {
            string path = $"Sound.Aliases[{index}]";
            LinkStorageSymbol? aliasName = FreezeOptionalXString(
                freeze,
                alias.AliasName,
                alias.AliasNamePointer.Untyped,
                $"{path}.AliasName");
            LinkStorageSymbol? subtitle = FreezeOptionalXString(
                freeze,
                alias.Subtitle,
                alias.SubtitlePointer.Untyped,
                $"{path}.Subtitle");
            LinkStorageSymbol? secondaryAliasName = FreezeOptionalXString(
                freeze,
                alias.SecondaryAliasName,
                alias.SecondaryAliasNamePointer.Untyped,
                $"{path}.SecondaryAliasName");
            LinkStorageSymbol? chainAliasName = FreezeOptionalXString(
                freeze,
                alias.ChainAliasName,
                alias.ChainAliasNamePointer.Untyped,
                $"{path}.ChainAliasName");
            LinkStorageSymbol? mixerGroup = FreezeOptionalXString(
                freeze,
                alias.MixerGroup,
                alias.MixerGroupPointer.Untyped,
                $"{path}.MixerGroup");
            LinkAliasCellSymbol? soundFiles = FreezeSoundFiles(alias, index, freeze);
            AssetDependency? volumeFalloffCurve = FreezeProviderDependency(
                alias.VolumeFalloffCurvePointer.Untyped,
                alias.VolumeFalloffCurve,
                XAssetType.SndCurve,
                $"{path}.VolumeFalloffCurve");
            LinkStorageTarget? speakerMap;
            if (alias.SpeakerMap is null)
            {
                if (alias.SpeakerMapPointer.Type != PointerType.Null)
                {
                    throw new NotSupportedException(
                        $"{path}.SpeakerMap retains direct storage without semantic data.");
                }
                speakerMap = null;
            }
            else
            {
                speakerMap = FreezeSpeakerMap(
                    alias.SpeakerMap,
                    alias.SpeakerMapPointer.Untyped,
                    $"{path}.SpeakerMap",
                    freeze);
            }

            var writer = new LinkTemplateWriter(SndAlias.SerializedSize);
            writer.Skip(sizeof(int) * 6);
            writer.WriteInt32(alias.Sequence);
            writer.WriteSingle(alias.VolumeMin);
            writer.WriteSingle(alias.VolumeMax);
            writer.WriteSingle(alias.PitchMin);
            writer.WriteSingle(alias.PitchMax);
            writer.WriteSingle(alias.DistanceMin);
            writer.WriteSingle(alias.DistanceMax);
            writer.WriteSingle(alias.VelocityMin);
            writer.WriteInt32(alias.Flags);
            writer.WriteSingle(alias.SlavePercentage);
            writer.WriteSingle(alias.Probability);
            writer.WriteSingle(alias.LfePercentage);
            writer.WriteSingle(alias.CenterPercentage);
            writer.WriteInt32(alias.StartDelay);
            writer.Skip(sizeof(int));
            writer.WriteSingle(alias.EnvelopMin);
            writer.WriteSingle(alias.EnvelopMax);
            writer.WriteSingle(alias.EnvelopPercentage);
            writer.Skip(sizeof(int));
            return new FrozenAlias(
                writer.Complete(),
                aliasName,
                subtitle,
                secondaryAliasName,
                chainAliasName,
                mixerGroup,
                soundFiles,
                volumeFalloffCurve,
                speakerMap,
                index);
        }

        public void AppendOperations(
            LinkStorageSymbol table,
            int baseOffset,
            ICollection<LinkOperation> operations)
        {
            string path = $"Sound.Aliases[{Index}]";
            AddXString(AliasName, 0x00, "AliasName");
            AddXString(Subtitle, 0x04, "Subtitle");
            AddXString(SecondaryAliasName, 0x08, "SecondaryAliasName");
            AddXString(ChainAliasName, 0x0c, "ChainAliasName");
            AddXString(MixerGroup, 0x10, "MixerGroup");
            if (SoundFiles is not null)
            {
                operations.Add(new AliasCellStorageLinkOperation(
                    new LinkStorageCell(table, checked(baseOffset + 0x14)),
                    SoundFiles,
                    $"{path}.SoundFiles",
                    LinkStorageSymbol.SourceFree(
                        XFileBlockType.VERTEX,
                        SoundFile.SerializedSize,
                        alignment: sizeof(uint),
                        LinkMaterializationKind.VertexReservation)));
            }
            if (VolumeFalloffCurve is { } curve)
            {
                operations.Add(ProviderOperation(
                    table,
                    checked(baseOffset + 0x50),
                    curve));
            }
            if (SpeakerMap is { } speakerMap)
            {
                operations.Add(DirectOperation(
                    table,
                    checked(baseOffset + 0x60),
                    speakerMap,
                    $"{path}.SpeakerMap"));
            }

            void AddXString(
                LinkStorageSymbol? value,
                int relativeOffset,
                string field)
            {
                if (value is null)
                    return;
                operations.Add(XStringOperation(
                    table,
                    checked(baseOffset + relativeOffset),
                    value,
                    $"{path}.{field}"));
            }
        }
    }

    private sealed class FrozenSoundFile
    {
        private FrozenSoundFile(
            byte[] template,
            AssetDependency? loadedSound,
            LinkStorageSymbol? directory,
            LinkStorageSymbol? filename,
            string path)
        {
            Template = template;
            LoadedSound = loadedSound;
            Directory = directory;
            Filename = filename;
            Path = path;
        }

        public byte[] Template { get; }
        private AssetDependency? LoadedSound { get; }
        private LinkStorageSymbol? Directory { get; }
        private LinkStorageSymbol? Filename { get; }
        private string Path { get; }

        public static FrozenSoundFile Freeze(
            SoundFile file,
            int aliasIndex,
            int fileIndex,
            LinkAssetFreezeScope freeze)
        {
            string path =
                $"Sound.Aliases[{aliasIndex}].SoundFiles[{fileIndex}]";
            if (!Enum.IsDefined(file.Type) || file.Type == SndAliasType.Count)
            {
                throw new InvalidDataException(
                    $"{path}.Type has unsupported value {(byte)file.Type}.");
            }

            var writer = new LinkTemplateWriter(SoundFile.SerializedSize);
            writer.WriteByte((byte)file.Type);
            writer.WriteByte(file.Exists);
            writer.WriteUInt16(file.Padding);
            AssetDependency? loadedSound = null;
            LinkStorageSymbol? directory = null;
            LinkStorageSymbol? filename = null;
            if (file.Type == SndAliasType.Loaded)
            {
                if (file.Payload is not LoadedSoundFile loaded)
                {
                    throw new InvalidDataException(
                        $"{path} is Loaded but has no LoadedSoundFile payload.");
                }
                loadedSound = FreezeProviderDependency(
                    loaded.LoadedSoundPointer.Untyped,
                    loaded.LoadedSound,
                    XAssetType.LoadedSound,
                    $"{path}.LoadedSound");
                writer.Skip(sizeof(int) * 3);
            }
            else
            {
                if (file.Payload is not StreamedSound streamed)
                {
                    throw new InvalidDataException(
                        $"{path} uses {file.Type} but has no StreamedSound payload.");
                }

                writer.WriteUInt32(streamed.FileIndex);
                if (streamed.FileIndex == 0)
                {
                    if (streamed.Source is not ExternalStreamedSoundSource external)
                    {
                        throw new InvalidDataException(
                            $"{path} has file index zero but no external streamed source.");
                    }
                    directory = FreezeOptionalXString(
                        freeze,
                        external.Directory,
                        (external.DirectoryPointer ?? default).Untyped,
                        $"{path}.Directory");
                    filename = FreezeOptionalXString(
                        freeze,
                        external.Filename,
                        (external.FilenamePointer ?? default).Untyped,
                        $"{path}.Filename");
                    writer.Skip(sizeof(int) * 2);
                }
                else
                {
                    if (streamed.Source is not StreamedSoundFileSource streamFile)
                    {
                        throw new InvalidDataException(
                            $"{path} has a nonzero file index but no stream-file range.");
                    }
                    writer.WriteInt32(streamFile.StreamFileOffset);
                    writer.WriteInt32(streamFile.StreamFileLength);
                }
            }

            return new FrozenSoundFile(
                writer.Complete(),
                loadedSound,
                directory,
                filename,
                path);
        }

        public void AppendOperations(
            LinkStorageSymbol table,
            int baseOffset,
            ICollection<LinkOperation> operations)
        {
            if (LoadedSound is { } dependency)
            {
                operations.Add(ProviderOperation(
                    table,
                    checked(baseOffset + 0x04),
                    dependency));
            }
            if (Directory is not null)
            {
                operations.Add(XStringOperation(
                    table,
                    checked(baseOffset + 0x08),
                    Directory,
                    $"{Path}.Directory"));
            }
            if (Filename is not null)
            {
                operations.Add(XStringOperation(
                    table,
                    checked(baseOffset + 0x0c),
                    Filename,
                    $"{Path}.Filename"));
            }
        }
    }
}
