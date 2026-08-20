using System.Globalization;
using IW4.Assets.Assets.StructuredData;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.StructuredData;

internal static class StructuredDataDefInspectorProjection
{
    private static readonly IReadOnlyList<InspectorChoice> TypeChoices =
        Array.AsReadOnly(Enum.GetValues<StructuredDataTypeCategory>()
            .Where(value => value != StructuredDataTypeCategory.DataCount)
            .Select(value => new InspectorChoice(
                ((int)value).ToString(CultureInfo.InvariantCulture),
                FormatTypeCategory(value)))
            .ToArray());

    internal static InspectorSelectionViewModel Create(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        StructuredDataDraft draft = viewModel.WorkingDraft;
        if (selection.DefinitionIndex < 0 ||
            selection.DefinitionIndex >= draft.Definitions.Count)
        {
            return Unavailable("Schema selection is no longer available.");
        }

        StructuredDataDefinitionDraft definition =
            draft.Definitions[selection.DefinitionIndex];
        return selection.Kind switch
        {
            StructuredDataSelectionKind.Definition => Definition(
                viewModel,
                selection.DefinitionIndex,
                definition),
            StructuredDataSelectionKind.RootType => RootType(
                viewModel,
                selection.DefinitionIndex,
                definition),
            StructuredDataSelectionKind.Enums => Group(
                $"Enums · Definition {selection.DefinitionIndex}",
                "ENUM TABLE",
                "Enums",
                $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].EnumCount",
                definition.Enums.Count),
            StructuredDataSelectionKind.Enum => EnumDefinition(
                viewModel,
                selection,
                definition),
            StructuredDataSelectionKind.EnumEntry => EnumEntry(
                viewModel,
                selection,
                definition),
            StructuredDataSelectionKind.Structs => Group(
                $"Structs · Definition {selection.DefinitionIndex}",
                "STRUCT TABLE",
                "Structs",
                $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].StructCount",
                definition.Structs.Count),
            StructuredDataSelectionKind.Struct => StructDefinition(
                viewModel,
                selection,
                definition),
            StructuredDataSelectionKind.StructProperty => StructProperty(
                viewModel,
                selection,
                definition),
            StructuredDataSelectionKind.IndexedArrays => Group(
                $"Indexed arrays · Definition {selection.DefinitionIndex}",
                "ARRAY TABLE",
                "Indexed arrays",
                $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].IndexedArrayCount",
                definition.IndexedArrays.Count),
            StructuredDataSelectionKind.IndexedArray => IndexedArray(
                viewModel,
                selection,
                definition),
            StructuredDataSelectionKind.EnumedArrays => Group(
                $"Enumed arrays · Definition {selection.DefinitionIndex}",
                "ARRAY TABLE",
                "Enumed arrays",
                $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].EnumedArrayCount",
                definition.EnumedArrays.Count),
            StructuredDataSelectionKind.EnumedArray => EnumedArray(
                viewModel,
                selection,
                definition),
            _ => Unavailable("The selected schema node is not supported.")
        };
    }

    private static InspectorSelectionViewModel Definition(
        StructuredDataDefEditorViewModel viewModel,
        int definitionIndex,
        StructuredDataDefinitionDraft definition)
    {
        string path = $"StructuredDataDefSet.Defs[{definitionIndex}]";
        return new InspectorSelectionViewModel(
            $"Definition {definitionIndex}",
            "STRUCTURED DATA DEFINITION",
            [
                new InspectorSectionViewModel(
                    "Serialized definition",
                    [
                        Integer(
                            viewModel,
                            "Version",
                            $"{path}.Version",
                            definition.Version,
                            definition.SetVersion,
                            "The serialized schema revision."),
                        HexUInt32(
                            viewModel,
                            "Format checksum",
                            $"{path}.FormatChecksum",
                            definition.FormatChecksum,
                            definition.SetFormatChecksum,
                            "Expert value stored by the engine. IW4 Studio preserves and writes it but does not calculate it."),
                        HexUInt32(
                            viewModel,
                            "Size",
                            $"{path}.Size",
                            definition.Size,
                            definition.SetSize,
                            "Serialized root data size."),
                        ReadOnly("Enums", $"{path}.EnumCount", definition.Enums.Count),
                        ReadOnly("Structs", $"{path}.StructCount", definition.Structs.Count),
                        ReadOnly(
                            "Indexed arrays",
                            $"{path}.IndexedArrayCount",
                            definition.IndexedArrays.Count),
                        ReadOnly(
                            "Enumed arrays",
                            $"{path}.EnumedArrayCount",
                            definition.EnumedArrays.Count)
                    ]),
                new InspectorSectionViewModel(
                    "Root type",
                    TypeRows(
                        viewModel,
                        definition,
                        definition.RootType,
                        $"{path}.RootType"))
            ],
            "Counts are derived from their ordered tables. The asset name and pointer cells remain locked.");
    }

    private static InspectorSelectionViewModel RootType(
        StructuredDataDefEditorViewModel viewModel,
        int definitionIndex,
        StructuredDataDefinitionDraft definition) => new(
            "Root",
            "STRUCTURED DATA TYPE",
            [
                new InspectorSectionViewModel(
                    "Type",
                    TypeRows(
                        viewModel,
                        definition,
                        definition.RootType,
                        $"StructuredDataDefSet.Defs[{definitionIndex}].RootType"))
            ],
            "Reference categories address the corresponding definition table by stable index.");

    private static InspectorSelectionViewModel EnumDefinition(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection,
        StructuredDataDefinitionDraft definition)
    {
        if (selection.Index < 0 || selection.Index >= definition.Enums.Count)
            return Unavailable("The selected enum is no longer available.");
        StructuredDataEnumDraft value = definition.Enums[selection.Index];
        string path =
            $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].Enums[{selection.Index}]";
        return new InspectorSelectionViewModel(
            StructuredDataDefEditorViewModel.ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataEnum,
                selection.Index),
            "ENUM",
            [
                new InspectorSectionViewModel(
                    "Enum",
                    [
                        ReadOnly("Index", path, selection.Index),
                        ReadOnly("Entries", $"{path}.EntryCount", value.Entries.Count),
                        Integer(
                            viewModel,
                            "Reserved entries",
                            $"{path}.ReservedEntryCount",
                            value.ReservedEntryCount,
                            value.SetReservedEntryCount,
                            "Serialized capacity cannot be lower than the number of entries.")
                    ])
            ],
            StructuredDataDefEditorViewModel.FormatReferenceDescription(
                definition,
                StructuredDataTypeCategory.DataEnum,
                selection.Index,
                $"Enum #{selection.Index}"));
    }

    private static InspectorSelectionViewModel EnumEntry(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection,
        StructuredDataDefinitionDraft definition)
    {
        if (selection.Index < 0 || selection.Index >= definition.Enums.Count)
            return Unavailable("The selected enum is no longer available.");
        StructuredDataEnumDraft owner = definition.Enums[selection.Index];
        if (selection.ChildIndex < 0 || selection.ChildIndex >= owner.Entries.Count)
            return Unavailable("The selected enum entry is no longer available.");
        StructuredDataEnumEntryDraft value = owner.Entries[selection.ChildIndex];
        string path =
            $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].Enums[{selection.Index}].Entries[{selection.ChildIndex}]";
        return new InspectorSelectionViewModel(
            value.String ?? $"Entry {selection.ChildIndex}",
            "ENUM VALUE",
            [
                new InspectorSectionViewModel(
                    "Entry",
                    [
                        Text(
                            viewModel,
                            "Value",
                            $"{path}.String",
                            value.String,
                            value.SetString,
                            "Serialized XString value. An unchanged null remains null."),
                        Unsigned(
                            viewModel,
                            "Index",
                            $"{path}.Index",
                            value.Index,
                            next => value.SetIndex(checked((ushort)next)),
                            ushort.MaxValue,
                            "Stable value stored for this entry."),
                        ReadOnly(
                            "Padding",
                            $"{path}.Padding",
                            $"0x{value.Padding:X4}",
                            "Preserved serialized padding."),
                        ReadOnly(
                            "String storage",
                            $"{path}.StringPointer",
                            value.String is null ? "NULL" : "XString")
                    ])
            ],
            $"Enum {selection.Index} · serialized row {selection.ChildIndex}");
    }

    private static InspectorSelectionViewModel StructDefinition(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection,
        StructuredDataDefinitionDraft definition)
    {
        if (selection.Index < 0 || selection.Index >= definition.Structs.Count)
            return Unavailable("The selected struct is no longer available.");
        StructuredDataStructDraft value = definition.Structs[selection.Index];
        bool isRoot = StructuredDataDefEditorViewModel.IsRootStruct(
            definition,
            selection.Index);
        string path =
            $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].Structs[{selection.Index}]";
        return new InspectorSelectionViewModel(
            StructuredDataDefEditorViewModel.ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataStruct,
                selection.Index),
            "STRUCT",
            [
                new InspectorSectionViewModel(
                    "Layout",
                    [
                        ReadOnly("Index", path, selection.Index),
                        ReadOnly(
                            "Properties",
                            $"{path}.PropertyCount",
                            value.Properties.Count),
                        Integer(
                            viewModel,
                            "Stored size",
                            $"{path}.Size",
                            value.Size,
                            value.SetSize,
                            isRoot
                                ? "Stored struct-size field. Root data size is " +
                                  $"{definition.Size:N0} bytes on the definition; " +
                                  "negative values are invalid."
                                : "Stored struct-size field; negative values are invalid."),
                        HexUInt32(
                            viewModel,
                            "Bit offset",
                            $"{path}.BitOffset",
                            value.BitOffset,
                            value.SetBitOffset,
                            "Stored bit offset for this struct.")
                    ])
            ],
            StructuredDataDefEditorViewModel.FormatReferenceDescription(
                definition,
                StructuredDataTypeCategory.DataStruct,
                selection.Index,
                $"Struct #{selection.Index}"));
    }

    private static InspectorSelectionViewModel StructProperty(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection,
        StructuredDataDefinitionDraft definition)
    {
        if (selection.Index < 0 || selection.Index >= definition.Structs.Count)
            return Unavailable("The selected struct is no longer available.");
        StructuredDataStructDraft owner = definition.Structs[selection.Index];
        if (selection.ChildIndex < 0 ||
            selection.ChildIndex >= owner.Properties.Count)
        {
            return Unavailable("The selected property is no longer available.");
        }
        StructuredDataStructPropertyDraft value =
            owner.Properties[selection.ChildIndex];
        string path =
            $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].Structs[{selection.Index}].Properties[{selection.ChildIndex}]";
        return new InspectorSelectionViewModel(
            value.Name ?? $"Property {selection.ChildIndex}",
            "STRUCT PROPERTY",
            [
                new InspectorSectionViewModel(
                    "Property",
                    [
                        Text(
                            viewModel,
                            "Name",
                            $"{path}.Name",
                            value.Name,
                            value.SetName,
                            "Serialized property XString. An unchanged null remains null."),
                        HexUInt32(
                            viewModel,
                            "Offset",
                            $"{path}.Offset",
                            value.Offset,
                            value.SetOffset,
                            "Byte offset of this field within its structure."),
                        ReadOnly(
                            "String storage",
                            $"{path}.NamePointer",
                            value.Name is null ? "NULL" : "XString")
                    ]),
                new InspectorSectionViewModel(
                    "Type",
                    TypeRows(viewModel, definition, value.Type, $"{path}.Type"))
            ],
            $"Struct {selection.Index} · serialized property {selection.ChildIndex}");
    }

    private static InspectorSelectionViewModel IndexedArray(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection,
        StructuredDataDefinitionDraft definition)
    {
        if (selection.Index < 0 ||
            selection.Index >= definition.IndexedArrays.Count)
        {
            return Unavailable("The selected indexed array is no longer available.");
        }
        StructuredDataIndexedArrayDraft value =
            definition.IndexedArrays[selection.Index];
        string path =
            $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].IndexedArrays[{selection.Index}]";
        return new InspectorSelectionViewModel(
            StructuredDataDefEditorViewModel.ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataIndexedArray,
                selection.Index),
            "INDEXED ARRAY",
            [
                new InspectorSectionViewModel(
                    "Layout",
                    [
                        ReadOnly("Index", path, selection.Index),
                        Integer(
                            viewModel,
                            "Array size",
                            $"{path}.ArraySize",
                            value.ArraySize,
                            value.SetArraySize,
                            "Number of elements; negative values are invalid."),
                        HexUInt32(
                            viewModel,
                            "Raw element size",
                            $"{path}.ElementSize",
                            value.ElementSize,
                            value.SetElementSize,
                            StructuredDataDefEditorViewModel.IsBitPackedBoolean(
                                value.ElementType,
                                value.ElementSize)
                                ? "Stored one-bit width for this bit-packed boolean array."
                                : "Raw serialized element-size field; interpretation depends on the element type.")
                    ]),
                new InspectorSectionViewModel(
                    "Element type",
                    TypeRows(
                        viewModel,
                        definition,
                        value.ElementType,
                        $"{path}.ElementType"))
            ],
            StructuredDataDefEditorViewModel.FormatReferenceDescription(
                definition,
                StructuredDataTypeCategory.DataIndexedArray,
                selection.Index,
                $"Indexed array #{selection.Index}"));
    }

    private static InspectorSelectionViewModel EnumedArray(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataSelection selection,
        StructuredDataDefinitionDraft definition)
    {
        if (selection.Index < 0 ||
            selection.Index >= definition.EnumedArrays.Count)
        {
            return Unavailable("The selected enumed array is no longer available.");
        }
        StructuredDataEnumedArrayDraft value =
            definition.EnumedArrays[selection.Index];
        string path =
            $"StructuredDataDefSet.Defs[{selection.DefinitionIndex}].EnumedArrays[{selection.Index}]";
        return new InspectorSelectionViewModel(
            StructuredDataDefEditorViewModel.ReferenceDisplayName(
                definition,
                StructuredDataTypeCategory.DataEnumArray,
                selection.Index),
            "ENUMED ARRAY",
            [
                new InspectorSectionViewModel(
                    "Layout",
                    [
                        ReadOnly("Index", path, selection.Index),
                        ReferenceChoice(
                            viewModel,
                            "Enum",
                            $"{path}.EnumIndex",
                            value.EnumIndex,
                            definition.Enums.Count,
                            index =>
                                StructuredDataDefEditorViewModel.FormatReferenceLabel(
                                    definition,
                                    StructuredDataTypeCategory.DataEnum,
                                    index,
                                    "Enum"),
                            value.SetEnumIndex,
                            "Enum table that supplies the array keys."),
                        HexUInt32(
                            viewModel,
                            "Raw element size",
                            $"{path}.ElementSize",
                            value.ElementSize,
                            value.SetElementSize,
                            StructuredDataDefEditorViewModel.IsBitPackedBoolean(
                                value.ElementType,
                                value.ElementSize)
                                ? "Stored one-bit width for this bit-packed boolean array."
                                : "Raw serialized element-size field; interpretation depends on the element type.")
                    ]),
                new InspectorSectionViewModel(
                    "Element type",
                    TypeRows(
                        viewModel,
                        definition,
                        value.ElementType,
                        $"{path}.ElementType"))
            ],
            StructuredDataDefEditorViewModel.FormatReferenceDescription(
                definition,
                StructuredDataTypeCategory.DataEnumArray,
                selection.Index,
                $"Enumed array #{selection.Index}"));
    }

    private static IReadOnlyList<InspectorPropertyRowViewModel> TypeRows(
        StructuredDataDefEditorViewModel viewModel,
        StructuredDataDefinitionDraft definition,
        StructuredDataTypeDraft value,
        string path)
    {
        var result = new List<InspectorPropertyRowViewModel>
        {
            new InspectorChoicePropertyRowViewModel(
                "Category",
                $"{path}.Type",
                TypeChoices,
                ((int)value.Type).ToString(CultureInfo.InvariantCulture),
                viewModel.IsEditable
                    ? selected => viewModel.Mutate(() => value.SetType(
                        (StructuredDataTypeCategory)int.Parse(
                            selected,
                            CultureInfo.InvariantCulture)))
                    : null,
                "Serialized type category.",
                isReadOnly: !viewModel.IsEditable)
        };

        int referenceCount = value.Type switch
        {
            StructuredDataTypeCategory.DataEnum => definition.Enums.Count,
            StructuredDataTypeCategory.DataStruct => definition.Structs.Count,
            StructuredDataTypeCategory.DataIndexedArray =>
                definition.IndexedArrays.Count,
            StructuredDataTypeCategory.DataEnumArray =>
                definition.EnumedArrays.Count,
            _ => -1
        };
        if (referenceCount > 0)
        {
            string noun = value.Type switch
            {
                StructuredDataTypeCategory.DataEnum => "Enum",
                StructuredDataTypeCategory.DataStruct => "Struct",
                StructuredDataTypeCategory.DataIndexedArray => "Indexed array",
                StructuredDataTypeCategory.DataEnumArray => "Enumed array",
                _ => "Item"
            };
            result.Add(ReferenceChoice(
                viewModel,
                "Target",
                $"{path}.UnionValue",
                value.UnionValue,
                referenceCount,
                index =>
                    StructuredDataDefEditorViewModel.FormatReferenceLabel(
                        definition,
                        value.Type,
                        index,
                        noun),
                value.SetUnionValue,
                "Stable index into the selected definition table."));
        }
        else
        {
            result.Add(Integer(
                viewModel,
                "Raw union value",
                $"{path}.UnionValue",
                value.UnionValue,
                value.SetUnionValue,
                "Its meaning is not established for this category, so the raw value is preserved."));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private static InspectorPropertyRowViewModel ReferenceChoice(
        StructuredDataDefEditorViewModel viewModel,
        string label,
        string path,
        int value,
        int count,
        Func<int, string> display,
        Action<int> apply,
        string description)
    {
        if (count <= 0)
        {
            return Integer(
                viewModel,
                label,
                path,
                value,
                apply,
                description);
        }

        InspectorChoice[] choices = Enumerable.Range(0, count)
            .Select(index => new InspectorChoice(
                index.ToString(CultureInfo.InvariantCulture),
                display(index)))
            .ToArray();
        return new InspectorChoicePropertyRowViewModel(
            label,
            path,
            choices,
            value.ToString(CultureInfo.InvariantCulture),
            viewModel.IsEditable
                ? selected => viewModel.Mutate(() => apply(int.Parse(
                    selected,
                    CultureInfo.InvariantCulture)))
                : null,
            description,
            isReadOnly: !viewModel.IsEditable);
    }

    private static InspectorTextPropertyRowViewModel Text(
        StructuredDataDefEditorViewModel viewModel,
        string label,
        string path,
        string? value,
        Action<string?> apply,
        string description) => new(
            label,
            path,
            value,
            viewModel.IsEditable
                ? next => viewModel.Mutate(() => apply(next))
                : null,
            description: description,
            isReadOnly: !viewModel.IsEditable);

    private static InspectorIntegerPropertyRowViewModel Integer(
        StructuredDataDefEditorViewModel viewModel,
        string label,
        string path,
        int value,
        Action<int> apply,
        string description) => new(
            label,
            path,
            value,
            viewModel.IsEditable
                ? next => viewModel.Mutate(() => apply(next))
                : null,
            description,
            isReadOnly: !viewModel.IsEditable);

    private static InspectorUnsignedIntegerPropertyRowViewModel Unsigned(
        StructuredDataDefEditorViewModel viewModel,
        string label,
        string path,
        uint value,
        Action<uint> apply,
        uint maximum,
        string description) => new(
            label,
            path,
            value,
            viewModel.IsEditable
                ? next => viewModel.Mutate(() => apply(next))
                : null,
            description,
            isReadOnly: !viewModel.IsEditable,
            maxValue: maximum);

    private static InspectorTextPropertyRowViewModel HexUInt32(
        StructuredDataDefEditorViewModel viewModel,
        string label,
        string path,
        uint value,
        Action<uint> apply,
        string description) => new(
            label,
            path,
            $"0x{value:X8}",
            viewModel.IsEditable
                ? input => viewModel.Mutate(() => apply(ParseUInt32(input)))
                : null,
            ValidateUInt32,
            description,
            isReadOnly: !viewModel.IsEditable);

    private static InspectorReadOnlyPropertyRowViewModel ReadOnly(
        string label,
        string path,
        int value,
        string? description = null) => new(
            label,
            path,
            value.ToString(CultureInfo.InvariantCulture),
            description);

    private static InspectorReadOnlyPropertyRowViewModel ReadOnly(
        string label,
        string path,
        string value,
        string? description = null) => new(
            label,
            path,
            value,
            description);

    private static InspectorSelectionViewModel Group(
        string title,
        string kind,
        string label,
        string path,
        int count) => new(
            title,
            kind,
            [
                new InspectorSectionViewModel(
                    "Table",
                    [
                        ReadOnly(label, path, count),
                        ReadOnly(
                            "Ordering",
                            $"{path}.Ordering",
                            "Stable serialized indices")
                    ])
            ],
            "Counts are derived from the table. Adding, deleting, and reordering rows are intentionally unavailable.");

    private static InspectorSelectionViewModel Unavailable(string message) => new(
        "Unavailable selection",
        "STRUCTURED DATA",
        [
            new InspectorSectionViewModel(
                "Selection",
                [
                    ReadOnly("Reason", "StructuredDataDefSet.Selection", message)
                ])
        ]);

    private static string? ValidateUInt32(string input) =>
        TryParseUInt32(input, out _) ? null :
        "Enter an unsigned 32-bit value in decimal or 0x hexadecimal form.";

    private static uint ParseUInt32(string input) =>
        TryParseUInt32(input, out uint value)
            ? value
            : throw new ArgumentException(
                "Enter an unsigned 32-bit value in decimal or 0x hexadecimal form.");

    private static bool TryParseUInt32(string input, out uint value)
    {
        input = input.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(
                input.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out value);
        }
        return uint.TryParse(
            input,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string FormatTypeCategory(
        StructuredDataTypeCategory value) => value switch
    {
        StructuredDataTypeCategory.DataInt => "Int",
        StructuredDataTypeCategory.DataByte => "Byte",
        StructuredDataTypeCategory.DataBool => "Bool",
        StructuredDataTypeCategory.DataString => "String",
        StructuredDataTypeCategory.DataEnum => "Enum reference",
        StructuredDataTypeCategory.DataStruct => "Struct reference",
        StructuredDataTypeCategory.DataIndexedArray => "Indexed array reference",
        StructuredDataTypeCategory.DataEnumArray => "Enumed array reference",
        StructuredDataTypeCategory.DataFloat => "Float",
        StructuredDataTypeCategory.DataShort => "Short",
        StructuredDataTypeCategory.DataCount => "Count sentinel",
        _ => value.ToString()
    };
}
