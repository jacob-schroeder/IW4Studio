using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>Immutable, detached graph consumed by the StructuredData emitter.</summary>
public sealed class StructuredDataBuildData : IXAssetBuildData
{
    public StructuredDataBuildData(string? name, IEnumerable<StructuredDataDefinitionBuildData> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        Name = name;
        Definitions = Array.AsReadOnly(definitions.Select(definition => definition ?? throw new InvalidDataException("StructuredData definitions cannot contain null.")).ToArray());
    }

    public XAssetType AssetType => XAssetType.StructuredDataDef;
    public string? Name { get; }
    public IReadOnlyList<StructuredDataDefinitionBuildData> Definitions { get; }
}

public sealed record StructuredDataTypeBuildData(StructuredDataTypeCategory Category, int UnionValue);

public sealed class StructuredDataDefinitionBuildData
{
    public StructuredDataDefinitionBuildData(
        int version,
        uint formatChecksum,
        IEnumerable<StructuredDataEnumBuildData> enums,
        IEnumerable<StructuredDataStructBuildData> structs,
        IEnumerable<StructuredDataIndexedArrayBuildData> indexedArrays,
        IEnumerable<StructuredDataEnumedArrayBuildData> enumedArrays,
        StructuredDataTypeBuildData rootType,
        uint size,
        bool indexedArraysPresent = false,
        bool enumedArraysPresent = false)
    {
        ArgumentNullException.ThrowIfNull(enums);
        ArgumentNullException.ThrowIfNull(structs);
        ArgumentNullException.ThrowIfNull(indexedArrays);
        ArgumentNullException.ThrowIfNull(enumedArrays);
        ArgumentNullException.ThrowIfNull(rootType);
        Version = version;
        FormatChecksum = formatChecksum;
        Enums = Copy(enums, "enums");
        Structs = Copy(structs, "structs");
        IndexedArrays = Copy(indexedArrays, "indexed arrays");
        EnumedArrays = Copy(enumedArrays, "enumed arrays");
        IndexedArraysPresent = indexedArraysPresent || IndexedArrays.Count != 0;
        EnumedArraysPresent = enumedArraysPresent || EnumedArrays.Count != 0;
        RootType = rootType;
        Size = size;
    }

    public int Version { get; }
    public uint FormatChecksum { get; }
    public IReadOnlyList<StructuredDataEnumBuildData> Enums { get; }
    public IReadOnlyList<StructuredDataStructBuildData> Structs { get; }
    public IReadOnlyList<StructuredDataIndexedArrayBuildData> IndexedArrays { get; }
    public IReadOnlyList<StructuredDataEnumedArrayBuildData> EnumedArrays { get; }
    public bool IndexedArraysPresent { get; }
    public bool EnumedArraysPresent { get; }
    public StructuredDataTypeBuildData RootType { get; }
    public uint Size { get; }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string path) where T : class =>
        Array.AsReadOnly(values.Select(value => value ?? throw new InvalidDataException($"StructuredData {path} cannot contain null.")).ToArray());
}

public sealed class StructuredDataEnumBuildData
{
    public StructuredDataEnumBuildData(int reservedEntryCount, IEnumerable<StructuredDataEnumEntryBuildData> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ReservedEntryCount = reservedEntryCount;
        Entries = Array.AsReadOnly(entries.Select(entry => entry ?? throw new InvalidDataException("StructuredData enum entries cannot contain null.")).ToArray());
    }

    public int ReservedEntryCount { get; }
    public IReadOnlyList<StructuredDataEnumEntryBuildData> Entries { get; }
}

public sealed record StructuredDataEnumEntryBuildData(string? Value, ushort Index, ushort Padding);

public sealed class StructuredDataStructBuildData
{
    public StructuredDataStructBuildData(int size, uint bitOffset, IEnumerable<StructuredDataPropertyBuildData> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Size = size;
        BitOffset = bitOffset;
        Properties = Array.AsReadOnly(properties.Select(property => property ?? throw new InvalidDataException("StructuredData properties cannot contain null.")).ToArray());
    }

    public int Size { get; }
    public uint BitOffset { get; }
    public IReadOnlyList<StructuredDataPropertyBuildData> Properties { get; }
}

public sealed record StructuredDataPropertyBuildData(string? Name, StructuredDataTypeBuildData Type, uint Offset);
public sealed record StructuredDataIndexedArrayBuildData(int ArraySize, StructuredDataTypeBuildData ElementType, uint ElementSize);
public sealed record StructuredDataEnumedArrayBuildData(int EnumIndex, StructuredDataTypeBuildData ElementType, uint ElementSize);
