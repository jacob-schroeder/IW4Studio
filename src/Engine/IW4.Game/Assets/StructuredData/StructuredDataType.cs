using IW4.Game.Pointers;

namespace IW4.Game.Assets.StructuredData;

public sealed class StructuredDataType
{
    public StructuredDataTypeCategory Type { get; init; }
    public int UnionValue { get; init; }

    public uint StringDataLength => unchecked((uint)ReadUnion(StructuredDataTypeCategory.DataString));
    public int EnumIndex => ReadUnion(StructuredDataTypeCategory.DataEnum);
    public int StructIndex => ReadUnion(StructuredDataTypeCategory.DataStruct);
    public int IndexedArrayIndex => ReadUnion(StructuredDataTypeCategory.DataIndexedArray);
    public int EnumedArrayIndex => ReadUnion(StructuredDataTypeCategory.DataEnumArray);

    private int ReadUnion(StructuredDataTypeCategory category) =>
        Type == category ? UnionValue : throw new InvalidOperationException(
            $"Structured data category {Type} does not contain {category}'s union value.");
}
