using IW4.Assets.Assets.StructuredData;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class StructuredDataBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.StructuredDataDef;

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(buildData);
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        if (buildData is not StructuredDataBuildData data)
        {
            diagnostics.Add(new EmissionError("body", "StructuredDataDef requires StructuredDataBuildData.", rowIndex, AssetType));
            return diagnostics;
        }

        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name))
            diagnostics.Add(Diagnostic("name", "StructuredData name contains an embedded null or non-Latin-1 character.", rowIndex));
        for (int defIndex = 0; defIndex < data.Definitions.Count; defIndex++)
            ValidateDefinition(data.Definitions[defIndex], $"defs[{defIndex}]", diagnostics, rowIndex);
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        var data = (StructuredDataBuildData)buildData;
        var segments = new List<EmissionBlockSegment>();
        IDictionary<string, EmissionAddress> aliases = plan.StringAliases;
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x0c, alignment: 4);
        plan.Push(XFileBlockType.LARGE);
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, aliases);
        EmissionAddress? defsAddress = data.Definitions.Count == 0
            ? null
            : plan.Allocate(checked(data.Definitions.Count * 0x34), alignment: 4);
        var definitions = new List<DefinitionPlan>(data.Definitions.Count);
        for (int index = 0; index < data.Definitions.Count; index++)
            definitions.Add(PlanDefinition(data.Definitions[index], plan, segments, aliases));
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        if (defsAddress is { } defTable)
        {
            var writer = new XSourceWriter();
            foreach (DefinitionPlan definition in definitions)
                definition.Write(writer);
            segments.Add(new EmissionBlockSegment(defTable, writer.ToArray()));
        }

        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name));
        rootWriter.WriteInt32(data.Definitions.Count);
        rootWriter.WriteInt32(defsAddress is null ? 0 : -1);
        segments.Add(new EmissionBlockSegment(root, rootWriter.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }

    private static DefinitionPlan PlanDefinition(
        StructuredDataDefinitionBuildData data,
        EmissionPlan plan,
        List<EmissionBlockSegment> segments,
        IDictionary<string, EmissionAddress> aliases)
    {
        EmissionAddress? enumsAddress = data.Enums.Count == 0 ? null : plan.Allocate(checked(data.Enums.Count * 0x0c), alignment: 4);
        var enumPlans = data.Enums.Select(value => PlanEnum(value, plan, segments, aliases)).ToArray();
        if (enumsAddress is { } enumTable)
        {
            var writer = new XSourceWriter();
            foreach (EnumPlan enumPlan in enumPlans)
                enumPlan.Write(writer);
            segments.Add(new EmissionBlockSegment(enumTable, writer.ToArray()));
        }

        EmissionAddress? structsAddress = data.Structs.Count == 0 ? null : plan.Allocate(checked(data.Structs.Count * 0x10), alignment: 4);
        var structPlans = data.Structs.Select(value => PlanStruct(value, plan, segments, aliases)).ToArray();
        if (structsAddress is { } structTable)
        {
            var writer = new XSourceWriter();
            foreach (StructPlan structPlan in structPlans)
                structPlan.Write(writer);
            segments.Add(new EmissionBlockSegment(structTable, writer.ToArray()));
        }

        EmissionAddress? indexedAddress = data.IndexedArrays.Count == 0 ? null : plan.Allocate(checked(data.IndexedArrays.Count * 0x10), alignment: 4);
        if (indexedAddress is null && data.IndexedArraysPresent)
            plan.Align(4);
        if (indexedAddress is { } indexedTable)
        {
            var writer = new XSourceWriter();
            foreach (StructuredDataIndexedArrayBuildData value in data.IndexedArrays)
            {
                writer.WriteInt32(value.ArraySize);
                WriteType(writer, value.ElementType);
                writer.WriteUInt32(value.ElementSize);
            }
            segments.Add(new EmissionBlockSegment(indexedTable, writer.ToArray()));
        }

        EmissionAddress? enumedAddress = data.EnumedArrays.Count == 0 ? null : plan.Allocate(checked(data.EnumedArrays.Count * 0x10), alignment: 4);
        if (enumedAddress is null && data.EnumedArraysPresent)
            plan.Align(4);
        if (enumedAddress is { } enumedTable)
        {
            var writer = new XSourceWriter();
            foreach (StructuredDataEnumedArrayBuildData value in data.EnumedArrays)
            {
                writer.WriteInt32(value.EnumIndex);
                WriteType(writer, value.ElementType);
                writer.WriteUInt32(value.ElementSize);
            }
            segments.Add(new EmissionBlockSegment(enumedTable, writer.ToArray()));
        }

        return new DefinitionPlan(data, enumsAddress, structsAddress, indexedAddress, enumedAddress);
    }

    private static EnumPlan PlanEnum(
        StructuredDataEnumBuildData data,
        EmissionPlan plan,
        List<EmissionBlockSegment> segments,
        IDictionary<string, EmissionAddress> aliases)
    {
        EmissionAddress? entriesAddress = data.Entries.Count == 0 ? null : plan.Allocate(checked(data.Entries.Count * 0x08), alignment: 4);
        var pointers = new PlannedString?[data.Entries.Count];
        for (int index = 0; index < data.Entries.Count; index++)
            pointers[index] = AssetBodyEmitterHelpers.PlanString(data.Entries[index].Value, plan, segments, aliases);
        if (entriesAddress is { } entryTable)
        {
            var writer = new XSourceWriter();
            for (int index = 0; index < data.Entries.Count; index++)
            {
                writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(pointers[index]));
                writer.WriteUInt16(data.Entries[index].Index);
                writer.WriteUInt16(data.Entries[index].Padding);
            }
            segments.Add(new EmissionBlockSegment(entryTable, writer.ToArray()));
        }
        return new EnumPlan(data, entriesAddress);
    }

    private static StructPlan PlanStruct(
        StructuredDataStructBuildData data,
        EmissionPlan plan,
        List<EmissionBlockSegment> segments,
        IDictionary<string, EmissionAddress> aliases)
    {
        EmissionAddress? propertiesAddress = data.Properties.Count == 0 ? null : plan.Allocate(checked(data.Properties.Count * 0x10), alignment: 4);
        var pointers = new PlannedString?[data.Properties.Count];
        for (int index = 0; index < data.Properties.Count; index++)
            pointers[index] = AssetBodyEmitterHelpers.PlanString(data.Properties[index].Name, plan, segments, aliases);
        if (propertiesAddress is { } propertyTable)
        {
            var writer = new XSourceWriter();
            for (int index = 0; index < data.Properties.Count; index++)
            {
                StructuredDataPropertyBuildData value = data.Properties[index];
                writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(pointers[index]));
                WriteType(writer, value.Type);
                writer.WriteUInt32(value.Offset);
            }
            segments.Add(new EmissionBlockSegment(propertyTable, writer.ToArray()));
        }
        return new StructPlan(data, propertiesAddress);
    }

    private static void ValidateDefinition(
        StructuredDataDefinitionBuildData data,
        string path,
        List<EmissionError> diagnostics,
        int? rowIndex)
    {
        ValidateType(data.RootType, $"{path}.rootType", data, diagnostics, rowIndex);
        for (int enumIndex = 0; enumIndex < data.Enums.Count; enumIndex++)
        {
            StructuredDataEnumBuildData value = data.Enums[enumIndex];
            if (value.ReservedEntryCount < value.Entries.Count)
                diagnostics.Add(Diagnostic($"{path}.enums[{enumIndex}].reservedEntryCount", "Reserved entry count cannot be below entry count.", rowIndex));
            if (value.Entries.Select(entry => entry.Index).Distinct().Count() != value.Entries.Count)
                diagnostics.Add(Diagnostic($"{path}.enums[{enumIndex}].entries", "Enum entry indices must be unique.", rowIndex));
            for (int entryIndex = 0; entryIndex < value.Entries.Count; entryIndex++)
            {
                string? text = value.Entries[entryIndex].Value;
                if (text is { } stringValue && !AssetBodyEmitterHelpers.IsLatin1CString(stringValue))
                    diagnostics.Add(Diagnostic($"{path}.enums[{enumIndex}].entries[{entryIndex}].value", "Enum value contains an embedded null or non-Latin-1 character.", rowIndex));
            }
        }
        for (int structIndex = 0; structIndex < data.Structs.Count; structIndex++)
        {
            StructuredDataStructBuildData value = data.Structs[structIndex];
            if (value.Size < 0)
                diagnostics.Add(Diagnostic($"{path}.structs[{structIndex}].size", "Struct size cannot be negative.", rowIndex));
            for (int propertyIndex = 0; propertyIndex < value.Properties.Count; propertyIndex++)
            {
                StructuredDataPropertyBuildData property = value.Properties[propertyIndex];
                if (property.Name is { } propertyName && !AssetBodyEmitterHelpers.IsLatin1CString(propertyName))
                    diagnostics.Add(Diagnostic($"{path}.structs[{structIndex}].properties[{propertyIndex}].name", "Property name contains an embedded null or non-Latin-1 character.", rowIndex));
                // Property offsets are serialized metadata rather than managed
                // bounds. Valid definitions can contain offsets beyond this
                // struct's Size field, so preserve the uint32 value verbatim.
                ValidateType(property.Type, $"{path}.structs[{structIndex}].properties[{propertyIndex}].type", data, diagnostics, rowIndex);
            }
        }
        for (int index = 0; index < data.IndexedArrays.Count; index++)
        {
            StructuredDataIndexedArrayBuildData value = data.IndexedArrays[index];
            if (value.ArraySize < 0)
                diagnostics.Add(Diagnostic($"{path}.indexedArrays[{index}].arraySize", "Array size cannot be negative.", rowIndex));
            ValidateType(value.ElementType, $"{path}.indexedArrays[{index}].elementType", data, diagnostics, rowIndex);
        }
        for (int index = 0; index < data.EnumedArrays.Count; index++)
        {
            StructuredDataEnumedArrayBuildData value = data.EnumedArrays[index];
            if (value.EnumIndex < 0 || value.EnumIndex >= data.Enums.Count)
                diagnostics.Add(Diagnostic($"{path}.enumedArrays[{index}].enumIndex", "Enumed-array enumIndex is outside this definition's enum table.", rowIndex));
            ValidateType(value.ElementType, $"{path}.enumedArrays[{index}].elementType", data, diagnostics, rowIndex);
        }
    }

    private static void ValidateType(StructuredDataTypeBuildData type, string path, StructuredDataDefinitionBuildData definition, List<EmissionError> diagnostics, int? rowIndex)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(type.Category))
        {
            diagnostics.Add(Diagnostic(path, "StructuredData type category is not defined.", rowIndex));
            return;
        }
        int limit = type.Category switch
        {
            StructuredDataTypeCategory.DataEnum => definition.Enums.Count,
            StructuredDataTypeCategory.DataStruct => definition.Structs.Count,
            StructuredDataTypeCategory.DataIndexedArray => definition.IndexedArrays.Count,
            StructuredDataTypeCategory.DataEnumArray => definition.EnumedArrays.Count,
            _ => -1
        };
        if (limit >= 0 && (type.UnionValue < 0 || type.UnionValue >= limit))
            diagnostics.Add(Diagnostic($"{path}.unionValue", "Type index is outside the referenced definition table.", rowIndex));
    }

    private static void WriteType(XSourceWriter writer, StructuredDataTypeBuildData type)
    {
        writer.WriteInt32((int)type.Category);
        writer.WriteInt32(type.UnionValue);
    }

    private static EmissionError Diagnostic(string path, string message, int? rowIndex) =>
        new(path, message, rowIndex, XAssetType.StructuredDataDef);

    private sealed record DefinitionPlan(
        StructuredDataDefinitionBuildData Data,
        EmissionAddress? Enums,
        EmissionAddress? Structs,
        EmissionAddress? IndexedArrays,
        EmissionAddress? EnumedArrays)
    {
        public void Write(XSourceWriter writer)
        {
            writer.WriteInt32(Data.Version);
            writer.WriteUInt32(Data.FormatChecksum);
            writer.WriteInt32(Data.Enums.Count);
            writer.WriteInt32(Enums is null ? 0 : -1);
            writer.WriteInt32(Data.Structs.Count);
            writer.WriteInt32(Structs is null ? 0 : -1);
            writer.WriteInt32(Data.IndexedArrays.Count);
            writer.WriteInt32(IndexedArrays is null && !Data.IndexedArraysPresent ? 0 : -1);
            writer.WriteInt32(Data.EnumedArrays.Count);
            writer.WriteInt32(EnumedArrays is null && !Data.EnumedArraysPresent ? 0 : -1);
            WriteType(writer, Data.RootType);
            writer.WriteUInt32(Data.Size);
        }
    }

    private sealed record EnumPlan(StructuredDataEnumBuildData Data, EmissionAddress? Entries)
    {
        public void Write(XSourceWriter writer)
        {
            writer.WriteInt32(Data.Entries.Count);
            writer.WriteInt32(Data.ReservedEntryCount);
            writer.WriteInt32(Entries is null ? 0 : -1);
        }
    }

    private sealed record StructPlan(StructuredDataStructBuildData Data, EmissionAddress? Properties)
    {
        public void Write(XSourceWriter writer)
        {
            writer.WriteInt32(Data.Properties.Count);
            writer.WriteInt32(Properties is null ? 0 : -1);
            writer.WriteInt32(Data.Size);
            writer.WriteUInt32(Data.BitOffset);
        }
    }
}
