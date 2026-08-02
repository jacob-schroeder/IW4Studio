using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.StructuredData;

public sealed class StructuredDataDef
{
    public const int SerializedSize = 0x34;

    public int Version { get; init; }
    public uint FormatChecksum { get; init; }
    public int EnumCount { get; init; }
    public XPointer<StructuredDataEnum[]> EnumsPointer { get; init; }
    public int StructCount { get; init; }
    public XPointer<StructuredDataStruct[]> StructsPointer { get; init; }
    public int IndexedArrayCount { get; init; }
    public XPointer<StructuredDataIndexedArray[]> IndexedArraysPointer { get; init; }
    public int EnumedArrayCount { get; init; }
    public XPointer<StructuredDataEnumedArray[]> EnumedArraysPointer { get; init; }
    public StructuredDataType RootType { get; init; } = new();
    public uint Size { get; init; }

    public IReadOnlyList<StructuredDataEnum> Enums { get; set; } = [];
    public IReadOnlyList<StructuredDataStruct> Structs { get; set; } = [];
    public IReadOnlyList<StructuredDataIndexedArray> IndexedArrays { get; set; } = [];
    public IReadOnlyList<StructuredDataEnumedArray> EnumedArrays { get; set; } = [];
}
