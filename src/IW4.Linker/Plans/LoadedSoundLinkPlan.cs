using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen LoadedSound body. Captured seek/payload occurrence identity is
/// preserved symbolically; authored fields always receive fresh storage.
/// </summary>
internal sealed class LoadedSoundLinkPlan : AssetLinkPlan
{
    private LoadedSoundLinkPlan(
        AssetKey key,
        string originalSerializedName,
        int physicalDataByteCount,
        ushort frameCount,
        ushort channelCount,
        ushort sampleRate,
        ushort pad0E,
        ushort pad10,
        ushort seekTableCount,
        LinkStorageTarget? seekTable,
        LinkStorageTarget? physicalData,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(LoadedSound.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(physicalDataByteCount);
        writer.WriteUInt16(frameCount);
        writer.WriteUInt16(channelCount);
        writer.WriteUInt16(sampleRate);
        writer.WriteUInt16(pad0E);
        writer.WriteUInt16(pad10);
        writer.WriteUInt16(seekTableCount);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root =>
            {
                var operations = new List<LinkOperation>
                {
                    NameOperation(root, 0)
                };
                if (seekTable is { } seek)
                {
                    operations.Add(DirectOperation(
                        root, 0x14, seek, "LoadedSound.SeekTable"));
                }
                if (physicalData is { } physical)
                {
                    operations.Add(DirectOperation(
                        root, 0x18, physical, "LoadedSound.PhysicalData"));
                }

                return operations;
            });
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        LoadedSound definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        byte[]? seekTable = definition.SeekTable?.ToArray();
        byte[]? physicalData = definition.PhysicalData?.ToArray();

        ValidatePayloadLengths(definition, seekTable, physicalData);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition, seekTable, physicalData);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.LoadedSound,
                originalSerializedName,
                freeze);
        }

        LinkStorageTarget? frozenSeek = seekTable is null
            ? null
            : freeze.FreezeStorage(
                definition.SeekTablePointer.Untyped,
                seekTable,
                XFileBlockType.LARGE,
                alignment: 4,
                operations: null,
                "LoadedSound.SeekTable");
        LinkStorageTarget? frozenPhysical = physicalData is null
            ? null
            : freeze.FreezeStorage(
                definition.PhysicalDataPointer.Untyped,
                physicalData,
                XFileBlockType.PHYSICAL,
                alignment: 64,
                operations: null,
                "LoadedSound.PhysicalData");

        return new LoadedSoundLinkPlan(
            key,
            originalSerializedName,
            definition.PhysicalDataByteCount,
            definition.FrameCount,
            definition.ChannelCount,
            definition.SampleRate,
            definition.Pad0E,
            definition.Pad10,
            definition.SeekTableCount,
            frozenSeek,
            frozenPhysical,
            freeze);
    }

    private static void ValidatePayloadLengths(
        LoadedSound definition,
        byte[]? seekTable,
        byte[]? physicalData)
    {
        if (definition.PhysicalDataByteCount < 0)
        {
            throw new InvalidDataException(
                "LoadedSound physical-data byte count cannot be negative.");
        }

        if (physicalData is null)
        {
            if (definition.PhysicalDataPointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    "LoadedSound retains a non-null physical payload pointer without semantic bytes.");
            }
            if (definition.PhysicalDataByteCount != 0)
            {
                throw new InvalidDataException(
                    "A null LoadedSound physical payload requires a zero byte count.");
            }
        }
        else if (physicalData.Length != definition.PhysicalDataByteCount)
        {
            throw new InvalidDataException(
                $"LoadedSound physical payload contains {physicalData.Length} byte(s), " +
                $"but its root declares {definition.PhysicalDataByteCount}.");
        }

        if (seekTable is null)
        {
            if (definition.SeekTablePointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    "LoadedSound retains a non-null seek-table pointer without semantic bytes.");
            }
            if (definition.SeekTableCount != 0)
            {
                throw new InvalidDataException(
                    "A null LoadedSound seek table requires a zero entry count.");
            }
            return;
        }

        if (seekTable.Length % sizeof(uint) != 0)
        {
            throw new InvalidDataException(
                "LoadedSound seek-table bytes must contain whole uint32 entries.");
        }
        int seekTableCount = seekTable.Length / sizeof(uint);
        if (seekTableCount != definition.SeekTableCount)
        {
            throw new InvalidDataException(
                $"LoadedSound seek table contains {seekTableCount} entries, " +
                $"but its root declares {definition.SeekTableCount}.");
        }
    }

    private static void ValidateReferenceShape(
        LoadedSound definition,
        byte[]? seekTable,
        byte[]? physicalData)
    {
        if (definition.PhysicalDataByteCount != 0 ||
            definition.FrameCount != 0 ||
            definition.ChannelCount != 0 ||
            definition.SampleRate != 0 ||
            definition.Pad0E != 0 ||
            definition.Pad10 != 0 ||
            definition.SeekTableCount != 0 ||
            definition.SeekTablePointer.Raw != 0 ||
            definition.PhysicalDataPointer.Raw != 0 ||
            seekTable is not null ||
            physicalData is not null)
        {
            throw new InvalidDataException(
                "A comma-prefixed LoadedSound provider must have a zeroed reference body.");
        }
    }

}
